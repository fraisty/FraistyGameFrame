using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>A read-only lease for explicit physical asset operations. Any project, scene,
    /// Undo or on-disk input change invalidates the audit. It loads no artwork and never saves
    /// the user's pending edits. Revalidation happens immediately before synchronous mutation.</summary>
    [InitializeOnLoad]
    public sealed class GameplaySkillAssetAuditLease
    {
        private static long _revision;
        private readonly long _start;
        private readonly Dictionary<string,string> _files;
        private readonly bool _physical;
        static GameplaySkillAssetAuditLease()
        {
            EditorApplication.projectChanged+=()=>_revision++;
            EditorApplication.hierarchyChanged+=()=>_revision++;
            Undo.undoRedoPerformed+=()=>_revision++;
            Undo.postprocessModifications+=changes=>{_revision++;return changes;};
        }
        private GameplaySkillAssetAuditLease(bool physical)
        {
            _physical=physical;RequireSaved(physical);_start=_revision;
            _files=Paths().ToDictionary(p=>p,Stamp,StringComparer.Ordinal);
        }
        public static GameplaySkillAssetAuditLease Capture(bool physical=true)=>new GameplaySkillAssetAuditLease(physical);
        private static string[] Paths()=>AssetDatabase.GetAllAssetPaths().Where(p=>p.StartsWith("Assets/",StringComparison.Ordinal)&&File.Exists(p)).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
        private static string Stamp(string path)
        {
            var file=new FileInfo(path);var meta=new FileInfo(path+".meta");
            return file.Length+":"+file.LastWriteTimeUtc.Ticks+":"+(meta.Exists?meta.Length+":"+meta.LastWriteTimeUtc.Ticks:"missing");
        }
        public static void RequireSaved(bool physical=true)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)
                throw new InvalidOperationException("退出 Play 并等待导入/编译完成后重试资产审计。");
            for(int i=0;physical&&i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("存在未保存的 Scene；审计没有保存或删除任何内容。");
            if(Resources.FindObjectsOfTypeAll<ScriptableObject>().Any(a=>AssetDatabase.Contains(a)&&EditorUtility.IsDirty(a)))
                throw new InvalidOperationException("存在未保存的资源；请先处理自己的修改，再重新审计。");
        }
        public void Revalidate()
        {
            RequireSaved(_physical);
            if(_revision!=_start)throw new InvalidOperationException("审计期间项目、Scene 或编辑状态发生变化；请重新审计。");
            var paths=Paths();
            if(paths.Length!=_files.Count||paths.Any(p=>!_files.TryGetValue(p,out var stamp)||stamp!=Stamp(p)))
                throw new InvalidOperationException("审计输入文件或 meta 已变更；没有提交物理操作。");
        }
    }
}
