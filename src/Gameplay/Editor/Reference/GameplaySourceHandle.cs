using System;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Editor.SkillAuthoring;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FRFrame.Gameplay.Editor
{
    /// <summary>Editor identity of a real SO or an embedded serialized location. Array indexes are
    /// lookup hints only: an embedded object is anchored to its owner's managedReferenceId.
    /// Deleted/replaced objects fail resolution instead of falling back to an occupied old index.</summary>
    [Serializable]
    public sealed class GameplaySourceHandle
    {
        [SerializeField] private Object _owner;
        [SerializeField] private string _guid, _originalPath, _anchorPath, _suffix;
        [SerializeField] private string _guardArray, _guardSignature;
        [SerializeField] private string _managedType;
        [SerializeField] private long _fileId, _managedId = -1;
        [SerializeField] private bool _valid, _guardNullSlot;

        public Object Owner
        {
            get
            {
                if (!_owner && !string.IsNullOrEmpty(_guid))
                {
                    string path = AssetDatabase.GUIDToAssetPath(_guid);
                    if (!string.IsNullOrEmpty(path))
                        _owner = AssetDatabase.LoadAllAssetsAtPath(path).FirstOrDefault(o =>
                            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string _, out long id) && id == _fileId);
                }
                return _owner;
            }
        }
        public long ManagedReferenceId => _managedId;
        public string OriginalPath => _originalPath;
        public string AssetKey => !string.IsNullOrEmpty(_guid) ? _guid + "@" + _fileId + ":" : "scratch@" + (_owner ? _owner.GetInstanceID() : 0) + ":";
        public string Key => AssetKey + (_managedId >= 0 ? "mr@" + _managedId + _suffix : "field@" + _originalPath);

        public static GameplaySourceHandle Capture(Object owner, string path = "")
        {
            var handle = new GameplaySourceHandle { _owner = owner, _originalPath = path ?? "", _suffix = "" };
            if (!owner) return handle;
            if(!EditorUtility.IsPersistent(owner)||!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(owner,out handle._guid,out handle._fileId))
            {handle._guid=null;handle._fileId=0;}
            if (handle._originalPath.Length == 0) { handle._valid = true; return handle; }
            using (var so = new SerializedObject(owner))
            {
                if (Find(so,handle._originalPath) == null) return handle;
                handle._valid = true;
                string candidate = handle._originalPath;
                while (candidate.Length > 0)
                {
                    var property = Find(so,candidate);
                    if (property?.propertyType == SerializedPropertyType.ManagedReference && property.managedReferenceId >= 0)
                    {
                        handle._managedId = property.managedReferenceId;
                        handle._managedType = property.managedReferenceFullTypename;
                        handle._anchorPath = candidate;
                        handle._suffix = handle._originalPath.Substring(candidate.Length);
                        break;
                    }
                    int dot = candidate.LastIndexOf('.');
                    candidate = dot >= 0 ? candidate.Substring(0, dot) : "";
                }
                var original=Find(so,handle._originalPath);
                if(original.propertyType==SerializedPropertyType.ManagedReference&&original.managedReferenceId<0){
                    int at=handle._originalPath.LastIndexOf(".Array.data[",StringComparison.Ordinal);
                    if(at>=0&&handle._originalPath.EndsWith("]",StringComparison.Ordinal)){
                        string arrayPath=handle._originalPath.Substring(0,at);
                        handle._guardArray=handle._managedId>=0?arrayPath.Substring(handle._anchorPath.Length):arrayPath;
                        handle._guardSignature=ArraySignature(Find(so,arrayPath));
                        handle._guardNullSlot=true;
                    }
                }
            }
            return handle;
        }
        public bool TryResolve(out string path)
        {
            path = null;
            var owner = Owner;
            if (!owner || !_valid) return false;
            if (_originalPath.Length == 0) { path = ""; return true; }
            using (var so = new SerializedObject(owner))
            {
                if (_managedId >= 0)
                {
                    var anchor = Find(so,_anchorPath);
                    if (anchor?.propertyType != SerializedPropertyType.ManagedReference || anchor.managedReferenceId != _managedId)
                        anchor = GameplayAuthoringProperties.Walk(so).FirstOrDefault(p =>
                            p.propertyType == SerializedPropertyType.ManagedReference && p.managedReferenceId == _managedId);
                    if (anchor == null) return false;
                    if(!string.IsNullOrEmpty(_managedType)&&anchor.managedReferenceFullTypename!=_managedType)return false;
                    _anchorPath = anchor.propertyPath;
                    path = _anchorPath + _suffix;
                }
                else path = _originalPath;
                if(_guardNullSlot){
                    string guardPath=_managedId>=0?_anchorPath+_guardArray:_guardArray;
                    if(ArraySignature(Find(so,guardPath))!=_guardSignature)return false;
                }
                return Find(so,path) != null;
            }
        }
        // Unity's FindProperty can emit a native Error for a once-valid out-of-bounds path.
        // Bounds-check each array before resolving the cached lookup hint.
        private static SerializedProperty Find(SerializedObject so,string path){
            if(string.IsNullOrEmpty(path))return null;
            var parts=path.Split('.');var property=so.FindProperty(parts[0]);
            for(int i=1;i<parts.Length&&property!=null;i++){
                if(parts[i]=="Array"&&i+1<parts.Length&&parts[i+1].StartsWith("data[",StringComparison.Ordinal)){
                    string token=parts[++i];
                    if(!property.isArray||!int.TryParse(token.Substring(5,token.Length-6),out int index)||index<0||index>=property.arraySize)return null;
                    property=property.GetArrayElementAtIndex(index);
                }else property=property.FindPropertyRelative(parts[i]);
            }
            return property;
        }
        private static string ArraySignature(SerializedProperty array)=>array==null||!array.isArray?null:
            string.Join(",",Enumerable.Range(0,array.arraySize).Select(i=>array.GetArrayElementAtIndex(i).managedReferenceId.ToString()));
        public string ResolvePath()
        {
            if (TryResolve(out string path)) return path;
            throw new InvalidOperationException("原配置已删除或身份已改变，请重新选择；不会写入旧数组位置。");
        }
        /// <summary>Read-only compatibility key for pre-U6 layouts; never written to a formal source.</summary>
        public string LegacyKey
        {
            get
            {
                var owner = Owner;
                string guid = _guid;
                if (string.IsNullOrEmpty(guid)) guid = "scratch@" + (owner ? owner.GetInstanceID() : 0);
                if (owner && !AssetDatabase.IsMainAsset(owner)) guid += "@" + (_fileId != 0 ? _fileId : owner.GetInstanceID());
                return guid + ":" + _originalPath;
            }
        }
    }
}
