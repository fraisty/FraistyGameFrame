using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Editor.SkillAuthoring;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>Explicit physical cleanup only. Uses indexed dependencies and streamed exact
    /// GUID/fileID references; never loads project artwork to discover consumers. Ordinary node
    /// Delete does not call this audit.</summary>
    public sealed class GameplaySkillLegacyCleanup : IDisposable
    {
        private static readonly Regex Header=new Regex(@"^--- !u!\d+ &(-?\d+)",RegexOptions.Compiled);
        private static readonly Regex Reference=new Regex(@"\{fileID:\s*(-?\d+)(?:,\s*guid:\s*([a-fA-F0-9]{32}))?[^}]*\}",RegexOptions.Compiled);
        private readonly Dictionary<string,Object> _candidates=new Dictionary<string,Object>();
        private readonly Dictionary<string,HashSet<string>> _incoming=new Dictionary<string,HashSet<string>>();
        private readonly Dictionary<string,HashSet<string>> _outgoing=new Dictionary<string,HashSet<string>>();
        private readonly Dictionary<string,byte[]> _backups=new Dictionary<string,byte[]>();
        private readonly HashSet<string> _paths=new HashSet<string>();
        private readonly HashSet<string> _kept=new HashSet<string>();
        private readonly HashSet<string> _formal=new HashSet<string>();
        private readonly HashSet<string> _touched=new HashSet<string>();
        private GameplaySkillAssetAuditLease _lease;
        private readonly Dictionary<GameplaySkillDocumentAsset,string> _semantics=new Dictionary<GameplaySkillDocumentAsset,string>();
        private string[] _scan;private int _cursor;
        public bool Complete {get;private set;}
        public string Failure {get;private set;}
        public int Examined => _cursor;
        public int CandidateCount=>_candidates.Count;
        public int RemovableCount=>Complete?_candidates.Keys.Count(id=>!_kept.Contains(id)):0;
        public int KeptCount=>_kept.Count;
        public int DeletedObjects {get;private set;}
        public int DeletedFiles {get;private set;}
        /// <summary>Stops a read-only audit when its owner closes; a cancelled lease cannot commit.</summary>
        public void Dispose(){EditorApplication.update-=Advance;if(!Complete){Complete=true;Failure="审计已取消，没有修改资产。";}}
        public static GameplaySkillLegacyCleanup Begin(IEnumerable<string> originalSourcePaths)
        {
            var audit=new GameplaySkillLegacyCleanup();
            audit._lease=GameplaySkillAssetAuditLease.Capture();
            var documents=GameplaySkillWorkbench.Documents().Where(d=>d.AuthoringSchemaVersion==2).ToArray();
            var live=new HashSet<Object>(documents.SelectMany(d=>GameplayAuthoringDependencyGraph.BuildDefinition(d).Nodes).Select(n=>n.Asset));
            foreach(var document in documents)audit._semantics.Add(document,GameplaySkillDefinitionSemantics.Capture(document.Build(new GameplayAuthoringBuildContext())));
            foreach(string path in originalSourcePaths.Distinct())
            {
                var objects=AssetDatabase.LoadAllAssetsAtPath(path);
                foreach(var obj in objects)
                {
                    bool obsolete=obj is GameplayDefinitionAsset&&!(obj is GameplaySkillDocumentAsset)&&!live.Contains(obj);
                    if(!obsolete)continue;
                    string id=Identity(obj);audit._candidates[id]=obj;audit._incoming[id]=new HashSet<string>();audit._paths.Add(path);
                }
            }
            audit._scan=AssetDatabase.GetAllAssetPaths().Where(p=>p.StartsWith("Assets/",StringComparison.Ordinal)&&File.Exists(p)).ToArray();
            EditorApplication.update+=audit.Advance;return audit;
        }
        private void Advance()
        {
            try
            {
                double stop=EditorApplication.timeSinceStartup+.008;int work=0;
                while(_cursor<_scan.Length&&work++<128&&EditorApplication.timeSinceStartup<stop)
                {
                    string path=_scan[_cursor++];
                    if(!_paths.Contains(path)&&!AssetDatabase.GetDependencies(path,false).Any(_paths.Contains))continue;
                    ReadReferences(path);
                }
                if(_cursor<_scan.Length)return;
                EditorApplication.update-=Advance;
                foreach(var pair in _incoming)if(pair.Value.Any(source=>!_candidates.ContainsKey(source)))_kept.Add(pair.Key);
                // A file containing a genuine live/shared child must retain its main object.
                foreach(string path in _paths)
                {
                    var objects=AssetDatabase.LoadAllAssetsAtPath(path);
                    if(objects.Any(o=>!_candidates.ContainsKey(Identity(o))||_kept.Contains(Identity(o))))
                    {string main=Identity(AssetDatabase.LoadMainAssetAtPath(path));if(_candidates.ContainsKey(main))_kept.Add(main);}
                }
                bool changed;do{changed=false;foreach(string source in _kept.ToArray())if(_outgoing.TryGetValue(source,out var targets))foreach(string id in targets)if(_candidates.ContainsKey(id)){
                    if(_kept.Add(id))changed=true;if(_formal.Contains(source)&&_formal.Add(id))changed=true;
                }}while(changed);
                Complete=true;Debug.Log("A2 exact-reference cleanup audit: "+CandidateCount+" candidates, "+RemovableCount+" removable; "+KeptCount+" retained for real references/history/container identity. No assets changed.");
            }
            catch(Exception error){EditorApplication.update-=Advance;Failure=error.ToString();Complete=true;Debug.LogException(error);}
        }
        private bool IsHistoryReference(string path,string source,string target)
        {
            // Only an exact V2 archive slot can classify an incoming use as editor history.
            // Other hosts/assets, including unknown editor consumers, remain formal and editable.
            foreach(var doc in GameplaySkillWorkbench.Documents().Where(d=>AssetDatabase.GetAssetPath(d)==path&&Identity(d)==source)){
                if(!doc.AuthoringGraph.LegacyResources.Any(l=>Identity(l.Resource)==target))continue;
                return !GameplayAuthoringDependencyGraph.BuildDefinition(doc).Nodes.Any(n=>Identity(n.Asset)==target);
            }return false;
        }
        private void ReadReferences(string path)
        {
            using(var reader=new StreamReader(path))
            {
                var headerBytes=new char[5];int headerLength=reader.Read(headerBytes,0,5);if(headerLength<5||new string(headerBytes)!="%YAML"){
                    // Indexed native/binary dependencies cannot prove a particular fileID unused.
                    // Conservatively protect all candidates in the referenced asset paths.
                    var dependencies=new HashSet<string>(AssetDatabase.GetDependencies(path,false));
                    foreach(var candidate in _candidates)if(dependencies.Contains(AssetDatabase.GetAssetPath(candidate.Value))){_kept.Add(candidate.Key);_formal.Add(candidate.Key);}
                    return;
                }
                string guid=AssetDatabase.AssetPathToGUID(path),source=guid+":0",line;
                while((line=reader.ReadLine())!=null)
                {
                    var header=Header.Match(line);if(header.Success){source=guid+":"+header.Groups[1].Value;continue;}
                    foreach(Match match in Reference.Matches(line))
                    {
                        string target=(match.Groups[2].Success?match.Groups[2].Value:guid)+":"+match.Groups[1].Value;
                        if(!_candidates.ContainsKey(target))continue;
                        _incoming[target].Add(source);if(!_outgoing.TryGetValue(source,out var edges))_outgoing[source]=edges=new HashSet<string>();edges.Add(target);
                        if(!_candidates.ContainsKey(source)&&!IsHistoryReference(path,source,target))_formal.Add(target);
                    }
                }
            }
        }
        public void Commit()
        {
            if(!Complete||Failure!=null)throw new InvalidOperationException("Exact reference audit has not completed successfully.");
            _lease.Revalidate();
            foreach(string path in _paths){_backups[path]=File.ReadAllBytes(path);if(File.Exists(path+".meta"))_backups[path+".meta"]=File.ReadAllBytes(path+".meta");}
            try
            {
                foreach(string path in _paths)
                {
                    var main=AssetDatabase.LoadMainAssetAtPath(path);var objects=AssetDatabase.LoadAllAssetsAtPath(path);
                    if(objects.All(o=>_candidates.ContainsKey(Identity(o))&&!_kept.Contains(Identity(o))))
                    {_touched.Add(path);if(!AssetDatabase.DeleteAsset(path))throw new IOException("Cannot delete audited unused source file: "+path);DeletedObjects+=objects.Length;DeletedFiles++;continue;}
                    foreach(var obj in objects.Where(o=>o!=main&&_candidates.ContainsKey(Identity(o))&&!_kept.Contains(Identity(o))).ToArray())
                    {_touched.Add(path);AssetDatabase.RemoveObjectFromAsset(obj);Object.DestroyImmediate(obj,true);EditorUtility.SetDirty(main);DeletedObjects++;}
                    foreach(var source in objects.OfType<GameplayDefinitionAsset>().Where(a=>a&&_kept.Contains(Identity(a)))){bool history=!_formal.Contains(Identity(source));if(source.MigrationHistoryOnly==history)continue;_touched.Add(path);source.MarkMigrationHistoryOnly(history);EditorUtility.SetDirty(source);}
                    if(main&&_touched.Contains(path))AssetDatabase.SaveAssetIfDirty(main);
                }
                foreach(var pair in _semantics)GameplaySkillDefinitionSemantics.RequireEqual(pair.Value,pair.Key.Build(new GameplayAuthoringBuildContext()),pair.Key.name);
                Debug.Log("A2 cleanup: deleted "+DeletedObjects+" old objects / "+DeletedFiles+" unused files. Runtime definitions remain equivalent.");
            }
            catch
            {
                foreach(var backup in _backups.Where(p=>_touched.Contains(p.Key.EndsWith(".meta",StringComparison.Ordinal)?p.Key.Substring(0,p.Key.Length-5):p.Key)))File.WriteAllBytes(backup.Key,backup.Value);
                AssetDatabase.Refresh();throw;
            }
        }
        private static string Identity(Object value)
        {if(!value)return "null";AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value,out string guid,out long id);return guid+":"+id;}
    }
}
