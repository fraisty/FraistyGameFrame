#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Live2D.Cubism.Core;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Live2D.Editor
{
    public class Live2DNewMotionLibraryBuilderWindow : EditorWindow
    {
        private DefaultAsset _modelFolder;
        private DefaultAsset _batchRootFolder;
        private TextAsset _model3Json;
        private bool _createInteractionProfile = true;
        private bool _createDefaultTouchArea = true;
        private bool _overwriteDefaultTouchArea;
        private bool _overwriteClips = true;
        private bool _overwriteDefaultGroups = true;
        private bool _autoAttachControllerToPrefab = true;
        private bool _addSimpleAudioHandler = true;
        private string _librarySuffix = "_Live2DMotionLibrary";
        private string _profileSuffix = "_Live2DInteractionProfile";

        [MenuItem("Tools/Live2D/New Runtime/Build Library From Selected Folder")]
        public static void BuildFromSelectedFolder()
        {
            var window = GetWindow<Live2DNewMotionLibraryBuilderWindow>("Live2D Library Builder");
            if (Selection.activeObject is DefaultAsset folder) window._modelFolder = folder;
            window.Show();
        }

        [MenuItem("Tools/Live2D/New Runtime/Library Builder")]
        public static void Open()
        {
            GetWindow<Live2DNewMotionLibraryBuilderWindow>("Live2D Library Builder");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Build New Live2D Runtime Assets", EditorStyles.boldLabel);
            _modelFolder = (DefaultAsset)EditorGUILayout.ObjectField("Model Folder", _modelFolder, typeof(DefaultAsset), false);
            _model3Json = (TextAsset)EditorGUILayout.ObjectField("Model3 Json", _model3Json, typeof(TextAsset), false);
            _librarySuffix = EditorGUILayout.TextField("Library Suffix", _librarySuffix);
            _profileSuffix = EditorGUILayout.TextField("Profile Suffix", _profileSuffix);
            _createInteractionProfile = EditorGUILayout.Toggle("Create Interaction Profile", _createInteractionProfile);
            using (new EditorGUI.DisabledScope(!_createInteractionProfile))
            {
                _createDefaultTouchArea = EditorGUILayout.Toggle("Create Default Touch Area", _createDefaultTouchArea);
                _overwriteDefaultTouchArea = EditorGUILayout.Toggle("Overwrite Default Touch Area", _overwriteDefaultTouchArea);
            }
            _overwriteClips = EditorGUILayout.Toggle("Overwrite Clips", _overwriteClips);
            _overwriteDefaultGroups = EditorGUILayout.Toggle("Overwrite Default Groups", _overwriteDefaultGroups);
            _autoAttachControllerToPrefab = EditorGUILayout.Toggle("Auto Attach Controller To Prefab", _autoAttachControllerToPrefab);
            _addSimpleAudioHandler = EditorGUILayout.Toggle("Add Simple Audio Handler", _addSimpleAudioHandler);

            using (new EditorGUI.DisabledScope(_modelFolder == null && _model3Json == null))
            {
                if (GUILayout.Button("Build")) Build();
                if (GUILayout.Button("Delete Generated Assets And Prefab Components")) DeleteCurrentFolderGeneratedData();
            }

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Batch Build", EditorStyles.boldLabel);
            _batchRootFolder = (DefaultAsset)EditorGUILayout.ObjectField("Root Folder", _batchRootFolder, typeof(DefaultAsset), false);
            using (new EditorGUI.DisabledScope(_batchRootFolder == null))
            {
                if (GUILayout.Button("Build All Child Model Folders")) BuildBatch();
                if (GUILayout.Button("Delete All Child Generated Assets And Prefab Components")) DeleteBatchGeneratedData();
            }
        }

        private void Build()
        {
            string folderPath = ResolveFolderPath();
            BuildFolder(folderPath, true);
        }

        private bool BuildFolder(string folderPath, bool selectLibrary)
        {
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
            {
                Debug.LogError("Live2D model folder is invalid.");
                return false;
            }

            var model3Json = ResolveModel3Json(folderPath);
            if (model3Json == null)
            {
                Debug.LogError($"model3.json not found under {folderPath}");
                return false;
            }

            string modelName = Path.GetFileName(folderPath);
            string libraryPath = $"{folderPath}/{modelName}{_librarySuffix}.asset";
            var library = AssetDatabase.LoadAssetAtPath<Live2DMotionLibrary>(libraryPath) ?? FindExistingAsset<Live2DMotionLibrary>(folderPath, _librarySuffix);
            if (library == null)
            {
                library = CreateInstance<Live2DMotionLibrary>();
                AssetDatabase.CreateAsset(library, libraryPath);
            }

            library.modelName = modelName;
            library.model3Json = model3Json;
            library.motionFolderPath = $"{folderPath}/motions";
            if (_overwriteClips) BuildClips(library, folderPath, model3Json);
            if (_overwriteDefaultGroups) BuildDefaultGroups(library);
            EnsureAllMotionsGroup(library);
            library.BuildRuntimeCache();
            EditorUtility.SetDirty(library);

            Live2DInteractionProfile profile = null;
            if (_createInteractionProfile) profile = CreateOrUpdateProfile(folderPath, modelName, library);
            if (_autoAttachControllerToPrefab) AttachControllerToPrefab(folderPath, modelName, library, profile);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (selectLibrary) Selection.activeObject = library;
            Debug.Log($"Live2D new runtime library built: {AssetDatabase.GetAssetPath(library)}");
            return true;
        }

        private void BuildBatch()
        {
            string rootPath = _batchRootFolder != null ? AssetDatabase.GetAssetPath(_batchRootFolder) : null;
            if (string.IsNullOrEmpty(rootPath) || !AssetDatabase.IsValidFolder(rootPath))
            {
                Debug.LogError("Live2D batch root folder is invalid.");
                return;
            }

            int success = 0;
            int skipped = 0;
            foreach (string folderPath in EnumerateModelFolders(rootPath))
            {
                try
                {
                    if (BuildFolder(folderPath, false)) success++;
                    else skipped++;
                }
                catch (Exception e)
                {
                    skipped++;
                    Debug.LogError($"Live2D batch build failed: {folderPath}\n{e}");
                }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Live2D batch build completed. success={success}, skipped={skipped}, root={rootPath}");
        }

        private void DeleteCurrentFolderGeneratedData()
        {
            string folderPath = ResolveFolderPath();
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
            {
                Debug.LogError("Live2D model folder is invalid.");
                return;
            }
            DeleteGeneratedData(folderPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private void DeleteBatchGeneratedData()
        {
            string rootPath = _batchRootFolder != null ? AssetDatabase.GetAssetPath(_batchRootFolder) : null;
            if (string.IsNullOrEmpty(rootPath) || !AssetDatabase.IsValidFolder(rootPath))
            {
                Debug.LogError("Live2D batch root folder is invalid.");
                return;
            }

            int count = 0;
            foreach (string folderPath in EnumerateModelFolders(rootPath))
            {
                DeleteGeneratedData(folderPath);
                count++;
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Live2D batch delete completed. count={count}, root={rootPath}");
        }

        private void DeleteGeneratedData(string folderPath)
        {
            string modelName = Path.GetFileName(folderPath);
            RemoveControllerFromPrefab(folderPath, modelName);
            DeleteAssetIfExists<Live2DInteractionProfile>(folderPath, _profileSuffix, $"{folderPath}/{modelName}{_profileSuffix}.asset");
            DeleteAssetIfExists<Live2DMotionLibrary>(folderPath, _librarySuffix, $"{folderPath}/{modelName}{_librarySuffix}.asset");
            Debug.Log($"Live2D generated data deleted under {folderPath}");
        }

        private static void DeleteAssetIfExists<T>(string folderPath, string suffix, string preferredPath) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(preferredPath) ?? FindExistingAsset<T>(folderPath, suffix);
            if (asset == null) return;
            string path = AssetDatabase.GetAssetPath(asset);
            if (!string.IsNullOrEmpty(path)) AssetDatabase.DeleteAsset(path);
        }

        private void RemoveControllerFromPrefab(string folderPath, string modelName)
        {
            string prefabPath = FindPrefabPath(folderPath, modelName);
            if (string.IsNullOrEmpty(prefabPath)) return;
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                bool changed = false;
                foreach (var controller in root.GetComponentsInChildren<Live2DMotionController>(true))
                {
                    DestroyImmediate(controller, true);
                    changed = true;
                }
                foreach (var audioHandler in root.GetComponentsInChildren<Live2DSimpleAudioEventHandler>(true))
                {
                    DestroyImmediate(audioHandler, true);
                    changed = true;
                }
                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    Debug.Log($"Live2D components removed from prefab: {prefabPath}");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static IEnumerable<string> EnumerateModelFolders(string rootPath)
        {
            var result = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] guids = AssetDatabase.FindAssets("t:TextAsset", new[] { rootPath });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".model3.json", StringComparison.OrdinalIgnoreCase)) continue;
                string folder = Path.GetDirectoryName(path)?.Replace("\\", "/");
                if (!string.IsNullOrEmpty(folder)) result.Add(folder);
            }
            return result;
        }

        private string ResolveFolderPath()
        {
            if (_modelFolder != null) return AssetDatabase.GetAssetPath(_modelFolder);
            if (_model3Json == null) return null;
            string jsonPath = AssetDatabase.GetAssetPath(_model3Json);
            return Path.GetDirectoryName(jsonPath)?.Replace("\\", "/");
        }

        private TextAsset ResolveModel3Json(string folderPath)
        {
            if (_model3Json != null) return _model3Json;
            string[] guids = AssetDatabase.FindAssets("t:TextAsset", new[] { folderPath });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".model3.json", StringComparison.OrdinalIgnoreCase))
                    return AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            }
            return null;
        }

        private static T FindExistingAsset<T>(string folderPath, string suffix) where T : UnityEngine.Object
        {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { folderPath });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path).EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return AssetDatabase.LoadAssetAtPath<T>(path);
            }
            return null;
        }

        private static void BuildClips(Live2DMotionLibrary library, string folderPath, TextAsset model3Json)
        {
            library.clips.Clear();
            var paths = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            string modelPath = AssetDatabase.GetAssetPath(model3Json);
            string modelText = File.ReadAllText(modelPath);
            foreach (var path in ExtractMotionPaths(modelText)) paths.Add(path);

            string motionsFolder = $"{folderPath}/motions";
            if (AssetDatabase.IsValidFolder(motionsFolder))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:TextAsset", new[] { motionsFolder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.EndsWith(".motion3.json", StringComparison.OrdinalIgnoreCase)) paths.Add(path.Substring(folderPath.Length + 1));
                }
            }

            foreach (string relativePath in paths)
            {
                string assetPath = relativePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ? relativePath : $"{folderPath}/{relativePath}";
                var motionAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
                if (motionAsset == null) continue;
                string motionId = Live2DMotionLibrary.NormalizeMotionId(Path.GetFileName(relativePath));
                var category = Live2DMotionLibrary.GuessCategory(motionId);
                library.clips.Add(new Live2DMotionClip
                {
                    motionId = motionId,
                    displayName = motionId,
                    motionAsset = motionAsset,
                    sourceFilePath = relativePath,
                    category = category,
                    defaultLayer = Live2DMotionLibrary.GuessDefaultLayer(category),
                    loopMode = GuessLoopMode(motionId, category),
                    interruptPolicy = GuessInterruptPolicy(category)
                });
            }
        }

        private static IEnumerable<string> ExtractMotionPaths(string json)
        {
            const string key = "\"File\"";
            int index = 0;
            while ((index = json.IndexOf(key, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int colon = json.IndexOf(':', index + key.Length);
                if (colon < 0) yield break;
                int start = json.IndexOf('"', colon + 1);
                if (start < 0) yield break;
                int end = json.IndexOf('"', start + 1);
                if (end < 0) yield break;
                string path = json.Substring(start + 1, end - start - 1).Replace("\\", "/");
                if (path.EndsWith(".motion3.json", StringComparison.OrdinalIgnoreCase)) yield return path;
                index = end + 1;
            }
        }

        private static Live2DLoopMode GuessLoopMode(string motionId, Live2DMotionCategory category)
        {
            string n = motionId.ToLowerInvariant();
            if (n.StartsWith("idle") || n.StartsWith("main")) return Live2DLoopMode.ForceLoop;
            if (n == "login") return Live2DLoopMode.ForceOnce;
            return category == Live2DMotionCategory.Effect ? Live2DLoopMode.UseSource : Live2DLoopMode.ForceOnce;
        }

        private static Live2DInterruptPolicy GuessInterruptPolicy(Live2DMotionCategory category)
        {
            return category == Live2DMotionCategory.Idle || category == Live2DMotionCategory.Base
                ? Live2DInterruptPolicy.QueueUntilFinish
                : Live2DInterruptPolicy.ImmediateCrossFade;
        }

        private static void BuildDefaultGroups(Live2DMotionLibrary library)
        {
            library.groups.Clear();
            AddGroup(library, "idle_base", Live2DSelectionMode.ShuffleBag, c => c.category == Live2DMotionCategory.Idle);
            AddGroup(library, "base_once", Live2DSelectionMode.ShuffleBag, c => c.category == Live2DMotionCategory.Base || c.category == Live2DMotionCategory.Transition);
            AddGroup(library, "touch", Live2DSelectionMode.ShuffleBag, c => c.category == Live2DMotionCategory.Touch || c.category == Live2DMotionCategory.TouchIdle);
            AddGroup(library, "drag", Live2DSelectionMode.ShuffleBag, c => c.category == Live2DMotionCategory.Drag);
            AddGroup(library, "effect", Live2DSelectionMode.First, c => c.category == Live2DMotionCategory.Effect);
        }

        private static void EnsureAllMotionsGroup(Live2DMotionLibrary library)
        {
            if (library == null) return;
            library.groups.RemoveAll(g => g != null && string.Equals(g.groupId, "all_motions", StringComparison.OrdinalIgnoreCase));
            AddGroup(library, "all_motions", Live2DSelectionMode.ShuffleBag, c => true);
        }

        private static void AddGroup(Live2DMotionLibrary library, string groupId, Live2DSelectionMode mode, Predicate<Live2DMotionClip> match)
        {
            var group = new Live2DMotionGroup { groupId = groupId, displayName = groupId, selectionMode = mode };
            foreach (var clip in library.clips)
            {
                if (clip == null || !match(clip)) continue;
                group.candidates.Add(new Live2DMotionCandidate
                {
                    motion = new Live2DMotionReference { library = library, motionId = clip.motionId },
                    weight = 1f
                });
            }
            if (group.candidates.Count > 0) library.groups.Add(group);
        }

        private void AttachControllerToPrefab(string folderPath, string modelName, Live2DMotionLibrary library, Live2DInteractionProfile profile)
        {
            string prefabPath = FindPrefabPath(folderPath, modelName);
            if (string.IsNullOrEmpty(prefabPath))
            {
                Debug.LogWarning($"Live2D prefab not found under {folderPath}");
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var model = root.GetComponentInChildren<CubismModel>(true);
                if (model == null)
                {
                    Debug.LogWarning($"CubismModel not found in prefab: {prefabPath}");
                    return;
                }

                var controller = model.GetComponent<Live2DMotionController>();
                if (controller == null) controller = model.gameObject.AddComponent<Live2DMotionController>();
                controller.cubismModel = model;
                controller.motionLibrary = library;
                controller.interactionProfile = profile;

                if (_addSimpleAudioHandler)
                {
                    var audioHandler = model.GetComponent<Live2DSimpleAudioEventHandler>();
                    if (audioHandler == null) audioHandler = model.gameObject.AddComponent<Live2DSimpleAudioEventHandler>();
                    controller.audioEventHandler = audioHandler;
                }

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log($"Live2D controller configured on prefab: {prefabPath}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static string FindPrefabPath(string folderPath, string modelName)
        {
            string preferredPath = $"{folderPath}/{modelName}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(preferredPath) != null) return preferredPath;

            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { folderPath });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetDirectoryName(path)?.Replace("\\", "/") == folderPath) return path;
            }
            return null;
        }

        private Live2DInteractionProfile CreateOrUpdateProfile(string folderPath, string modelName, Live2DMotionLibrary library)
        {
            string profilePath = $"{folderPath}/{modelName}{_profileSuffix}.asset";
            var profile = AssetDatabase.LoadAssetAtPath<Live2DInteractionProfile>(profilePath) ?? FindExistingAsset<Live2DInteractionProfile>(folderPath, _profileSuffix);
            if (profile == null)
            {
                profile = CreateInstance<Live2DInteractionProfile>();
                AssetDatabase.CreateAsset(profile, profilePath);
            }
            profile.motionLibrary = library;
            if (profile.layouts.Count == 0)
            {
                profile.defaultLayoutId = "Default";
                profile.layouts.Add(new Live2DInteractionLayout { layoutId = "Default", displayName = "Default" });
            }
            if (_createDefaultTouchArea) EnsureDefaultTouchArea(profile);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private void EnsureDefaultTouchArea(Live2DInteractionProfile profile)
        {
            var layout = profile.GetDefaultLayout();
            if (layout == null)
            {
                layout = new Live2DInteractionLayout { layoutId = "Default", displayName = "Default" };
                profile.defaultLayoutId = layout.layoutId;
                profile.layouts.Add(layout);
            }
            if (layout.areas == null) layout.areas = new List<Live2DTouchArea>();

            var area = layout.areas.Find(a => a != null && a.areaId == "default_touch");
            if (area == null)
            {
                area = new Live2DTouchArea { areaId = "default_touch", displayName = "Default Touch", priority = 0 };
                layout.areas.Add(area);
            }
            else if (!_overwriteDefaultTouchArea)
            {
                return;
            }

            area.enabled = true;
            area.shapeType = Live2DTouchShapeType.Rect;
            area.normalizedRect = new Rect(0f, 0f, 1f, 1f);
            area.bindings = new List<Live2DInteractionBinding>
            {
                new Live2DInteractionBinding
                {
                    trigger = Live2DTriggerType.Tap,
                    cooldown = 0.3f,
                    actions = new List<Live2DInteractionAction>
                    {
                        new Live2DInteractionAction
                        {
                            actionType = Live2DInteractionActionType.PlayMotionGroup,
                            groupId = "all_motions",
                            layerId = Live2DLayerId.Overlay,
                            interruptPolicy = Live2DInterruptPolicy.ImmediateCrossFade
                        }
                    }
                }
            };
        }
    }
}
#endif
