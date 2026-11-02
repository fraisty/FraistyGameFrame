#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Live2D.Editor
{
    public sealed class Live2DBatchReimportWindow : EditorWindow
    {
        private DefaultAsset _rootFolder;
        private bool _removeMissingScripts = true;
        private Vector2 _scrollPosition;
        private readonly List<string> _modelPaths = new List<string>();

        [MenuItem("Tools/Live2D/Migration/Batch Reimport Models (Cubism r5)")]
        public static void Open()
        {
            var window = GetWindow<Live2DBatchReimportWindow>("Live2D r5 Reimport");
            if (Selection.activeObject is DefaultAsset selectedFolder
                && AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(selectedFolder)))
            {
                window._rootFolder = selectedFolder;
                window.ScanModels();
            }
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Cubism r5 Batch Reimport", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Recursively finds .model3.json files and forces Cubism to rebuild their imported assets and prefabs.",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            _rootFolder = (DefaultAsset)EditorGUILayout.ObjectField("Root Folder", _rootFolder, typeof(DefaultAsset), false);
            if (EditorGUI.EndChangeCheck())
                ScanModels();

            _removeMissingScripts = EditorGUILayout.ToggleLeft(
                "Remove missing scripts from existing model prefabs before reimport",
                _removeMissingScripts);

            if (_removeMissingScripts)
            {
                EditorGUILayout.HelpBox(
                    "Cubism r5 removed CubismMaskController. Cleanup prevents the r5 importer from failing while copying old prefab components. " +
                    "This removes every missing MonoBehaviour on each matching model prefab.",
                    MessageType.Warning);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Scan"))
                    ScanModels();

                using (new EditorGUI.DisabledScope(_modelPaths.Count == 0))
                {
                    if (GUILayout.Button($"Reimport {_modelPaths.Count} Models"))
                        ConfirmAndReimport();
                }
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField($"Models found: {_modelPaths.Count}", EditorStyles.boldLabel);
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            foreach (string modelPath in _modelPaths)
                EditorGUILayout.LabelField(modelPath, EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();
        }

        private void ScanModels()
        {
            _modelPaths.Clear();
            string rootPath = GetRootPath();
            if (string.IsNullOrEmpty(rootPath))
            {
                Repaint();
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:TextAsset", new[] { rootPath });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".model3.json", StringComparison.OrdinalIgnoreCase))
                    _modelPaths.Add(path);
            }

            _modelPaths.Sort(StringComparer.OrdinalIgnoreCase);
            Repaint();
        }

        private string GetRootPath()
        {
            if (_rootFolder == null) return null;
            string path = AssetDatabase.GetAssetPath(_rootFolder);
            return AssetDatabase.IsValidFolder(path) ? path : null;
        }

        private void ConfirmAndReimport()
        {
            string rootPath = GetRootPath();
            if (string.IsNullOrEmpty(rootPath) || _modelPaths.Count == 0) return;

            string cleanupNotice = _removeMissingScripts
                ? "\n\nMissing MonoBehaviours on matching existing prefabs will be removed first."
                : string.Empty;
            bool confirmed = EditorUtility.DisplayDialog(
                "Batch Reimport Live2D Models",
                $"Force reimport {_modelPaths.Count} Live2D models under:\n{rootPath}{cleanupNotice}",
                "Reimport",
                "Cancel");
            if (!confirmed) return;

            ReimportModels(rootPath);
        }

        private void ReimportModels(string rootPath)
        {
            int succeeded = 0;
            int failed = 0;
            int removedMissingScripts = 0;
            bool canceled = false;

            try
            {
                for (int i = 0; i < _modelPaths.Count; i++)
                {
                    string modelPath = _modelPaths[i];
                    canceled = EditorUtility.DisplayCancelableProgressBar(
                        "Reimporting Live2D Models",
                        modelPath,
                        (float)i / _modelPaths.Count);
                    if (canceled) break;

                    try
                    {
                        string prefabPath = GetModelPrefabPath(modelPath);
                        if (_removeMissingScripts && AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
                            removedMissingScripts += RemoveMissingScripts(prefabPath);

                        AssetDatabase.ImportAsset(
                            modelPath,
                            ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                        succeeded++;
                    }
                    catch (Exception exception)
                    {
                        failed++;
                        Debug.LogError($"Live2D r5 reimport failed: {modelPath}\n{exception}");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            string status = canceled ? "canceled" : "completed";
            Debug.Log(
                $"Live2D r5 batch reimport {status}. succeeded={succeeded}, failed={failed}, " +
                $"removedMissingScripts={removedMissingScripts}, root={rootPath}");
            EditorUtility.DisplayDialog(
                "Live2D Batch Reimport",
                $"Status: {status}\nSucceeded: {succeeded}\nFailed: {failed}\nRemoved missing scripts: {removedMissingScripts}",
                "OK");
        }

        private static string GetModelPrefabPath(string modelPath)
        {
            const string modelSuffix = ".model3.json";
            return modelPath.Substring(0, modelPath.Length - modelSuffix.Length) + ".prefab";
        }

        private static int RemoveMissingScripts(string prefabPath)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null) return 0;

            try
            {
                int removed = 0;
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                    removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);

                if (removed > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    Debug.Log($"Removed {removed} missing script(s) before Live2D r5 reimport: {prefabPath}");
                }
                return removed;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
#endif
