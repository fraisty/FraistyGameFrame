using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using UnityEditor;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>Whole-file deletion is an explicit asset operation. Audit indexed dependency
    /// paths in bounded editor slices, without loading their art or running during ordinary Delete.</summary>
    public static class GameplaySkillAssetDeletion
    {
        public static void Begin(GameplaySkillDocumentAsset document, Action<bool,string> completed)
        {
            if (!document || !AssetDatabase.Contains(document) || AssetDatabase.IsSubAsset(document))
                throw new InvalidOperationException("只能物理删除独立的主技能文件；嵌入技能需明确处理容器归属。");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("退出 Play 后才能删除正式资源。");
            for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
                if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("存在未保存的 Scene；请先保存或还原，以便按真实引用审计物理删除。");
            if(UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.ScriptableObject>().Any(a=>a!=document&&AssetDatabase.Contains(a)&&EditorUtility.IsDirty(a)))
                throw new InvalidOperationException("存在未保存的资源；请先保存或还原，以便审计其真实引用。");
            string target = AssetDatabase.GetAssetPath(document);
            var lease=GameplaySkillAssetAuditLease.Capture();
            string guid = AssetDatabase.AssetPathToGUID(target);
            string[] paths = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/",StringComparison.Ordinal) && p != target && File.Exists(p)).ToArray();
            var users = new List<string>(); int cursor = 0; bool invalidated=false;
            Action changed=()=>invalidated=true;
            EditorApplication.projectChanged+=changed;
            EditorApplication.CallbackFunction step = null;
            step = () => {
                try {
                    double stop = EditorApplication.timeSinceStartup + .008; int count = 0;
                    while (cursor < paths.Length && count++ < 128 && EditorApplication.timeSinceStartup < stop) {
                        string path = paths[cursor++];
                        if (AssetDatabase.GetDependencies(path,false).Contains(target)) users.Add(path);
                    }
                    if (cursor < paths.Length) return;
                    EditorApplication.update -= step;EditorApplication.projectChanged-=changed;
                    if (users.Count != 0) { completed(false,"保留主技能：仍被 " + users.Count + " 个文件引用。\n" + string.Join("\n",users.Take(8))); return; }
                    if (invalidated || !document || EditorApplication.isPlayingOrWillChangePlaymode || AssetDatabase.AssetPathToGUID(target) != guid)
                        throw new InvalidOperationException("审计期间目标或 Editor 状态已改变；没有删除文件。");
                    lease.Revalidate();
                    if (!AssetDatabase.MoveAssetToTrash(target)) throw new IOException("Unity 未能将主技能移入回收站："+target);
                    completed(true,"引用审计完成，主技能已移入回收站。");
                } catch (Exception error) { EditorApplication.update -= step;EditorApplication.projectChanged-=changed; completed(false,error.Message); }
            };
            EditorApplication.update += step;
        }
    }
}
