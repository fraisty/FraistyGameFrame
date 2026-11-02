#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Editor
{
    public sealed class FRFrameGamePackageBuildWindow
    {
        private const string DefaultProfileDirectory = "Assets/Settings/FRFrameBuild";

        private readonly FRFrameBuildLogger _logger = new FRFrameBuildLogger();
        private readonly System.Action _requestRepaint;
        private FRFrameBuildProfile _buildProfile;
        private GameBuildProfile _pendingGameProfile;
        private string _oldFileManifestPath = string.Empty;
        private int _activeGameIndex = -1;

        private bool _showOverview = true;
        private bool _showBuildProfile = true;
        private bool _showGameList = true;
        private bool _showGameSettings = true;
        private bool _showYooAsset = true;
        private bool _showPackageBuild = true;
        private bool _showBuildRecords = true;
        private bool _showLogs = true;

        private GameBuildProfile ActiveGameProfile
        {
            get
            {
                if (_buildProfile == null || _buildProfile.selectedGameProfiles == null || _activeGameIndex < 0 || _activeGameIndex >= _buildProfile.selectedGameProfiles.Count)
                {
                    return null;
                }

                return _buildProfile.selectedGameProfiles[_activeGameIndex];
            }
        }

        public FRFrameGamePackageBuildWindow(System.Action requestRepaint)
        {
            _requestRepaint = requestRepaint;
            AutoSelectFirstBuildProfile();
        }

        public void DrawEmbeddedGUI()
        {
            DrawHeader();
            DrawOverviewGroup();
            DrawBuildProfileGroup();
            DrawGameListGroup();
            DrawGameSettingsGroup();
            DrawYooAssetGroup();
            DrawPackageBuildGroup();
            DrawBuildRecordsGroup();
            DrawLogsGroup();
        }

        private void DrawHeader()
        {
            Rect rect = GUILayoutUtility.GetRect(0f, 48f, GUILayout.ExpandWidth(true));
            DrawColorBlock(rect, new Color(0.24f, 0.17f, 0.34f), new Color(0.40f, 0.28f, 0.55f));
            GUILayout.Space(-43f);
            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.Space(5f);
                GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    normal = { textColor = Color.white }
                };
                GUIStyle subTitleStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(0.92f, 0.86f, 0.98f) }
                };
                EditorGUILayout.LabelField("FRFrame 游戏包构建", titleStyle);
                EditorGUILayout.LabelField("单个游戏的配置、YooAsset 构建、运行时文件、整包 zip、补丁 zip 和独立输出记录。", subTitleStyle);
            }
            EditorGUILayout.Space(8f);
        }

        private void DrawOverviewGroup()
        {
            _showOverview = EditorGUILayout.BeginFoldoutHeaderGroup(_showOverview, "游戏包构建状态");
            if (_showOverview)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    GameBuildProfile gameProfile = ActiveGameProfile;
                    EditorGUILayout.LabelField("当前 Unity 平台", EditorUserBuildSettings.activeBuildTarget.ToString());
                    EditorGUILayout.LabelField("构建配置", _buildProfile == null ? "未选择" : _buildProfile.name);
                    EditorGUILayout.LabelField("目标平台", _buildProfile == null ? "未选择" : _buildProfile.buildTarget.ToString());
                    EditorGUILayout.LabelField("游戏配置数量", _buildProfile == null || _buildProfile.selectedGameProfiles == null ? "0" : _buildProfile.selectedGameProfiles.Count.ToString());
                    EditorGUILayout.LabelField("当前游戏", gameProfile == null || string.IsNullOrEmpty(gameProfile.gameId) ? "未选择" : gameProfile.gameId);
                    EditorGUILayout.LabelField("游戏版本", gameProfile == null || string.IsNullOrEmpty(gameProfile.packageVersion) ? "未填写" : gameProfile.packageVersion);
                    EditorGUILayout.LabelField("YooAsset 包", gameProfile == null || string.IsNullOrEmpty(GetSelectedYooAssetPackageName()) ? "未填写" : GetSelectedYooAssetPackageName());
                    EditorGUILayout.LabelField("游戏输出目录", gameProfile == null || string.IsNullOrEmpty(gameProfile.outputRoot) ? GetOutputRoot() : gameProfile.outputRoot);
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawBuildProfileGroup()
        {
            _showBuildProfile = EditorGUILayout.BeginFoldoutHeaderGroup(_showBuildProfile, "游戏包构建参数");
            if (_showBuildProfile)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUI.BeginChangeCheck();
                        _buildProfile = (FRFrameBuildProfile)EditorGUILayout.ObjectField("构建配置资产", _buildProfile, typeof(FRFrameBuildProfile), false);
                        if (EditorGUI.EndChangeCheck())
                        {
                            ClampActiveGameIndex();
                            RequestRepaint();
                        }

                        if (GUILayout.Button("新建", GUILayout.Width(80f)))
                        {
                            CreateBuildProfileAsset();
                        }

                        if (GUILayout.Button("保存", GUILayout.Width(80f)))
                        {
                            SaveAsset(_buildProfile);
                        }
                    }

                    if (_buildProfile == null)
                    {
                        EditorGUILayout.HelpBox("请先选择或新建构建配置资产。游戏包窗口会使用这里的目标平台，并把游戏配置列表挂在该资产上。", MessageType.Info);
                    }
                    else
                    {
                        EditorGUI.BeginChangeCheck();
                        _buildProfile.buildTarget = (BuildTarget)EditorGUILayout.EnumPopup("游戏包目标平台", _buildProfile.buildTarget);
                        _buildProfile.buildTargetGroup = (BuildTargetGroup)EditorGUILayout.EnumPopup("目标平台组", _buildProfile.buildTargetGroup);
                        _buildProfile.outputRoot = DrawFolderField("默认输出目录", _buildProfile.outputRoot);
                        if (EditorGUI.EndChangeCheck())
                        {
                            EditorUtility.SetDirty(_buildProfile);
                        }
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawGameListGroup()
        {
            _showGameList = EditorGUILayout.BeginFoldoutHeaderGroup(_showGameList, "游戏配置列表");
            if (_showGameList)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (_buildProfile == null)
                    {
                        EditorGUILayout.HelpBox("游戏配置挂在构建配置资产上，请先选择或新建构建配置。", MessageType.Info);
                    }
                    else
                    {
                        EnsureGameProfileList();
                        EditorGUILayout.LabelField($"当前关联游戏配置：{_buildProfile.selectedGameProfiles.Count} 个", EditorStyles.boldLabel);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            _pendingGameProfile = (GameBuildProfile)EditorGUILayout.ObjectField("添加已有游戏配置", _pendingGameProfile, typeof(GameBuildProfile), false);
                            if (GUILayout.Button("添加", GUILayout.Width(80f)))
                            {
                                AddGameProfile(_pendingGameProfile);
                                _pendingGameProfile = null;
                            }

                            if (GUILayout.Button("新建游戏配置", GUILayout.Width(120f)))
                            {
                                CreateGameProfileAsset();
                            }
                        }

                        DrawGameProfileList();
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawGameProfileList()
        {
            if (_buildProfile.selectedGameProfiles.Count == 0)
            {
                EditorGUILayout.HelpBox("当前没有游戏配置。新建或添加已有 GameBuildProfile 后，会在这里显示列表。", MessageType.Info);
                return;
            }

            EditorGUILayout.Space(4f);
            for (int i = 0; i < _buildProfile.selectedGameProfiles.Count; i++)
            {
                GameBuildProfile profile = _buildProfile.selectedGameProfiles[i];
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    bool active = i == _activeGameIndex;
                    string title = profile == null ? "空引用" : $"{profile.displayName} ({profile.gameId})";
                    if (GUILayout.Toggle(active, title, "Button", GUILayout.Height(24f)))
                    {
                        _activeGameIndex = i;
                    }

                    _buildProfile.selectedGameProfiles[i] = (GameBuildProfile)EditorGUILayout.ObjectField(profile, typeof(GameBuildProfile), false, GUILayout.Width(220f));
                    if (GUILayout.Button("移除引用", GUILayout.Width(90f)))
                    {
                        _buildProfile.selectedGameProfiles.RemoveAt(i);
                        EditorUtility.SetDirty(_buildProfile);
                        ClampActiveGameIndex();
                        return;
                    }
                }
            }
        }

        private void DrawGameSettingsGroup()
        {
            _showGameSettings = EditorGUILayout.BeginFoldoutHeaderGroup(_showGameSettings, "当前游戏配置");
            if (_showGameSettings)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    GameBuildProfile gameProfile = ActiveGameProfile;
                    if (gameProfile == null)
                    {
                        EditorGUILayout.HelpBox("请先在游戏配置列表中选择一个游戏配置。", MessageType.Info);
                    }
                    else
                    {
                        EditorGUI.BeginChangeCheck();
                        gameProfile.gameId = EditorGUILayout.TextField("游戏标识", gameProfile.gameId);
                        gameProfile.displayName = EditorGUILayout.TextField("显示名称", gameProfile.displayName);
                        gameProfile.packageName = EditorGUILayout.TextField("运行时包名", gameProfile.packageName);
                        gameProfile.packageVersion = DrawGameVersionFields("游戏版本", gameProfile.packageVersion);
                        EditorGUILayout.HelpBox("版本格式为 大版本.整包版本.补丁版本。大版本只手动调整；构建当前游戏整包会自动递增整包版本；构建当前补丁包会自动递增补丁版本。", MessageType.Info);
                        gameProfile.gameConfigAsset = (GamePackageConfig)EditorGUILayout.ObjectField("游戏运行时配置", gameProfile.gameConfigAsset, typeof(GamePackageConfig), false);
                        gameProfile.gameConfigLocation = DrawAssetLocationField("配置资源地址", gameProfile.gameConfigLocation, gameProfile.gameConfigAsset);
                        if (string.IsNullOrEmpty(gameProfile.gameConfigLocation))
                        {
                            EditorGUILayout.HelpBox("游戏包必须设置 GamePackageConfig 资产，入口场景、UI 配置、图标和背景视频都从该资产读取。", MessageType.Warning);
                        }
                        gameProfile.runtimeFilesRoot = DrawFolderField("运行时文件目录", gameProfile.runtimeFilesRoot);
                        gameProfile.includeHotUpdateAssembly = EditorGUILayout.Toggle("包含游戏热更 DLL", gameProfile.includeHotUpdateAssembly);
                        gameProfile.hotUpdateAssemblyName = EditorGUILayout.TextField("游戏热更程序集", gameProfile.hotUpdateAssemblyName);
                        gameProfile.outputRoot = DrawFolderField("游戏输出目录", gameProfile.outputRoot);
                        gameProfile.clearHistoryFullPackages = EditorGUILayout.Toggle("整包前清理历史整包", gameProfile.clearHistoryFullPackages);
                        gameProfile.fullPackageNamePattern = EditorGUILayout.TextField("整包命名格式", gameProfile.fullPackageNamePattern);
                        gameProfile.patchPackageNamePattern = EditorGUILayout.TextField("补丁包命名格式", gameProfile.patchPackageNamePattern);
                        if (EditorGUI.EndChangeCheck())
                        {
                            EditorUtility.SetDirty(gameProfile);
                            EditorUtility.SetDirty(_buildProfile);
                        }

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button("保存当前游戏配置", GUILayout.Width(140f)))
                            {
                                SaveAsset(gameProfile);
                                SaveAsset(_buildProfile);
                            }

                            if (GUILayout.Button("定位资产", GUILayout.Width(100f)))
                            {
                                Selection.activeObject = gameProfile;
                                EditorGUIUtility.PingObject(gameProfile);
                            }
                        }
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawYooAssetGroup()
        {
            _showYooAsset = EditorGUILayout.BeginFoldoutHeaderGroup(_showYooAsset, "当前游戏 YooAsset 配置");
            if (_showYooAsset)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    GameBuildProfile gameProfile = ActiveGameProfile;
                    if (gameProfile == null)
                    {
                        EditorGUILayout.HelpBox("请先选择当前游戏配置。", MessageType.Info);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("构建整包或补丁包时，会按这里的参数构建当前游戏 YooAsset Package，并复制真实构建输出到游戏包的 YooAsset 目录。", MessageType.Info);
                        EditorGUI.BeginChangeCheck();
                        gameProfile.yooAssetPackageName = EditorGUILayout.TextField("YooAsset 包名", gameProfile.yooAssetPackageName);
                        gameProfile.packagePlayMode = (FRFrameGamePackagePlayMode)EditorGUILayout.EnumPopup("资源运行模式", gameProfile.packagePlayMode);
                        gameProfile.buildYooAsset = EditorGUILayout.Toggle("构建时生成 YooAsset", gameProfile.buildYooAsset);
                        gameProfile.yooAssetBuildPipeline = (YooAsset.Editor.EBuildPipeline)EditorGUILayout.EnumPopup("构建管线", gameProfile.yooAssetBuildPipeline);
                        gameProfile.yooAssetCompressOption = (YooAsset.Editor.ECompressOption)EditorGUILayout.EnumPopup("压缩方式", gameProfile.yooAssetCompressOption);
                        gameProfile.yooAssetFileNameStyle = (YooAsset.EFileNameStyle)EditorGUILayout.EnumPopup("文件命名方式", gameProfile.yooAssetFileNameStyle);
                        gameProfile.yooAssetBuildinFileCopyOption = (YooAsset.Editor.EBundledCopyOption)EditorGUILayout.EnumPopup("内置文件复制", gameProfile.yooAssetBuildinFileCopyOption);
                        gameProfile.yooAssetBuildOutputRoot = DrawFolderField("资源输出目录", gameProfile.yooAssetBuildOutputRoot);
                        gameProfile.yooAssetBuildinFileRoot = DrawFolderField("内置文件目录", gameProfile.yooAssetBuildinFileRoot);
                        gameProfile.yooAssetBuildinFileCopyParams = EditorGUILayout.TextField("内置复制参数", gameProfile.yooAssetBuildinFileCopyParams);
                        gameProfile.yooAssetEnableSharePackRule = EditorGUILayout.Toggle("启用共享打包规则", gameProfile.yooAssetEnableSharePackRule);
                        gameProfile.yooAssetSingleReferencedPackAlone = EditorGUILayout.Toggle("单引用资源单独打包", gameProfile.yooAssetSingleReferencedPackAlone);
                        gameProfile.yooAssetVerifyBuildingResult = EditorGUILayout.Toggle("验证构建结果", gameProfile.yooAssetVerifyBuildingResult);
                        gameProfile.yooAssetClearBuildCacheFiles = EditorGUILayout.Toggle("清理构建缓存", gameProfile.yooAssetClearBuildCacheFiles);
                        gameProfile.yooAssetUseAssetDependencyDB = EditorGUILayout.Toggle("使用依赖数据库", gameProfile.yooAssetUseAssetDependencyDB);
                        gameProfile.yooAssetStripUnityVersion = EditorGUILayout.Toggle("剥离 Unity 版本", gameProfile.yooAssetStripUnityVersion);
                        gameProfile.yooAssetDisableWriteTypeTree = EditorGUILayout.Toggle("禁写 TypeTree", gameProfile.yooAssetDisableWriteTypeTree);
                        gameProfile.yooAssetOverwriteOutput = EditorGUILayout.Toggle("覆盖已有版本输出", gameProfile.yooAssetOverwriteOutput);
                        gameProfile.yooAssetClearBuildOutputAfterPackage = EditorGUILayout.Toggle("游戏包成功后清理 YooAsset 输出", gameProfile.yooAssetClearBuildOutputAfterPackage);
                        gameProfile.yooAssetBuiltinShadersBundleName = EditorGUILayout.TextField("内置 Shader Bundle", gameProfile.yooAssetBuiltinShadersBundleName);
                        gameProfile.yooAssetMonoScriptsBundleName = EditorGUILayout.TextField("MonoScripts Bundle", gameProfile.yooAssetMonoScriptsBundleName);
                        gameProfile.yooAssetPackageNote = EditorGUILayout.TextField("资源包备注", gameProfile.yooAssetPackageNote);
                        if (EditorGUI.EndChangeCheck())
                        {
                            EditorUtility.SetDirty(gameProfile);
                        }
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawPackageBuildGroup()
        {
            _showPackageBuild = EditorGUILayout.BeginFoldoutHeaderGroup(_showPackageBuild, "游戏包构建流程");
            if (_showPackageBuild)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    GameBuildProfile gameProfile = ActiveGameProfile;
                    EditorGUILayout.LabelField("当前游戏", gameProfile == null || string.IsNullOrEmpty(gameProfile.gameId) ? "未选择" : gameProfile.gameId);
                    EditorGUILayout.LabelField("当前版本", gameProfile == null || string.IsNullOrEmpty(gameProfile.packageVersion) ? "未填写" : gameProfile.packageVersion);
                    EditorGUILayout.LabelField("YooAsset 包", gameProfile == null || string.IsNullOrEmpty(GetSelectedYooAssetPackageName()) ? "未填写" : GetSelectedYooAssetPackageName());
                    EditorGUILayout.HelpBox("整包用于新增游戏或完整重装，会自动递增第二段版本号并重置补丁号；补丁包会优先使用手动选择的旧 file_manifest.json，未选择时自动关联当前游戏已构建的最新版本清单。", MessageType.Info);

                    if (GUILayout.Button("构建当前游戏整包", GUILayout.Height(30f)))
                    {
                        ScheduleBuildAction(() => RunStep(new GameFullPackageBuildStep()));
                    }

                    EditorGUILayout.Space(6f);
                    string autoManifestPath = GetLatestBuiltFileManifestPath(gameProfile);
                    if (ShouldUseAutoManifestPath(autoManifestPath))
                    {
                        _oldFileManifestPath = autoManifestPath;
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        _oldFileManifestPath = EditorGUILayout.TextField("旧文件清单", _oldFileManifestPath, GUILayout.MaxWidth(GetSafeContentWidth() - 90f));
                        if (GUILayout.Button("选择", GUILayout.Width(80f)))
                        {
                            string selected = EditorUtility.OpenFilePanel("选择旧 file_manifest.json", string.Empty, "json");
                            if (!string.IsNullOrEmpty(selected))
                            {
                                _oldFileManifestPath = selected;
                            }
                        }
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(string.IsNullOrEmpty(autoManifestPath) ? "自动清单：未找到已构建版本" : $"自动清单：{ToProjectRelativePath(autoManifestPath)}", EditorStyles.miniLabel, GUILayout.MaxWidth(GetSafeContentWidth() - 90f));
                        if (GUILayout.Button("使用最新", GUILayout.Width(80f)))
                        {
                            _oldFileManifestPath = autoManifestPath;
                        }
                    }

                    if (GUILayout.Button("构建当前游戏补丁包", GUILayout.Height(30f)))
                    {
                        ScheduleBuildAction(() => RunStep(new GamePatchPackageBuildStep(ResolveOldFileManifestPath())));
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawBuildRecordsGroup()
        {
            _showBuildRecords = EditorGUILayout.BeginFoldoutHeaderGroup(_showBuildRecords, "游戏包输出记录");
            if (_showBuildRecords)
            {
                GameBuildProfile gameProfile = ActiveGameProfile;
                string root = GetGameOutputDirectory(gameProfile);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("记录根目录", root);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("刷新", GUILayout.Width(120f)))
                        {
                            RequestRepaint();
                        }

                        if (GUILayout.Button("打开根目录", GUILayout.Width(120f)) && Directory.Exists(root))
                        {
                            EditorUtility.RevealInFinder(root);
                        }
                    }
                }

                if (!Directory.Exists(root))
                {
                    EditorGUILayout.HelpBox("未找到游戏包输出记录。", MessageType.Info);
                }
                else
                {
                    string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                        .Where(path => Path.GetFileName(path) == GamePackageBuildUtility.GameManifestFileName
                            || Path.GetFileName(path) == GamePackageBuildUtility.FileManifestFileName
                            || Path.GetFileName(path) == GamePackageBuildUtility.PatchManifestFileName
                            || Path.GetExtension(path).Equals(".zip", System.StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(File.GetLastWriteTime)
                        .Take(80)
                        .ToArray();

                    foreach (string file in files)
                    {
                        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                        {
                            EditorGUILayout.LabelField(ToProjectRelativePath(file), GUILayout.Width(GetSafeContentWidth() - 150f));
                            if (GUILayout.Button("打开", GUILayout.Width(70f)))
                            {
                                EditorUtility.OpenWithDefaultApp(file);
                            }

                            if (GUILayout.Button("定位", GUILayout.Width(70f)))
                            {
                                EditorUtility.RevealInFinder(file);
                            }
                        }
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawLogsGroup()
        {
            _showLogs = EditorGUILayout.BeginFoldoutHeaderGroup(_showLogs, "游戏包构建日志");
            if (_showLogs)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("清空日志", GUILayout.Width(120f)))
                        {
                            _logger.Clear();
                        }

                        if (GUILayout.Button("复制日志", GUILayout.Width(120f)))
                        {
                            EditorGUIUtility.systemCopyBuffer = _logger.ToText();
                        }
                    }

                    GUIStyle logStyle = new GUIStyle(EditorStyles.textArea)
                    {
                        wordWrap = true,
                        stretchWidth = false
                    };
                    EditorGUILayout.TextArea(_logger.ToText(), logStyle, GUILayout.Width(GetSafeContentWidth()), GUILayout.MinHeight(160f), GUILayout.ExpandHeight(true));
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void ScheduleBuildAction(System.Action action)
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    action();
                }
                catch (System.Exception e)
                {
                    Debug.LogException(e);
                    _logger.Error(e.ToString());
                }

                RequestRepaint();
            };
        }

        private void RequestRepaint()
        {
            _requestRepaint?.Invoke();
        }

        private void RunStep(IFRFrameBuildStep step)
        {
            FRFrameBuildContext context = CreateContext();
            FRFrameBuildPipeline pipeline = new FRFrameBuildPipeline()
                .Add(new FRFramePrepareBuildSettingsStep())
                .Add(step);
            FRFrameBuildStepResult result = pipeline.Execute(context);
            if (result.Success)
            {
                _logger.Info($"[游戏包构建] 流程完成，{result.ElapsedSeconds:F2}s");
            }
        }

        private string DrawAssetLocationField(string label, string value, Object asset)
        {
            string assetPath = asset == null ? string.Empty : AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(assetPath))
            {
                value = assetPath;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                value = EditorGUILayout.TextField(label, value, GUILayout.MaxWidth(GetSafeContentWidth() - 90f));
                GUI.enabled = !string.IsNullOrEmpty(assetPath);
                if (GUILayout.Button("使用资产", GUILayout.Width(80f)))
                {
                    value = assetPath;
                }
                GUI.enabled = true;
            }

            return value;
        }

        private string DrawFolderField(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                value = EditorGUILayout.TextField(label, value, GUILayout.MaxWidth(GetSafeContentWidth() - 90f));
                if (GUILayout.Button("选择", GUILayout.Width(80f)))
                {
                    string selected = EditorUtility.OpenFolderPanel($"选择{label}", string.IsNullOrEmpty(value) ? Application.dataPath : value, string.Empty);
                    if (!string.IsNullOrEmpty(selected))
                    {
                        value = selected;
                    }
                }
            }

            return value;
        }

        private string DrawGameVersionFields(string label, string value)
        {
            GamePackageVersion version;
            if (!GamePackageVersion.TryParse(value, out version))
            {
                version = GamePackageVersion.Default;
                EditorGUILayout.HelpBox($"{label}格式无效，已按 1.0.0 显示，请保存后继续构建。", MessageType.Warning);
            }

            EditorGUILayout.LabelField(label, version.ToString());
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("大版本", GUILayout.Width(48f));
                version.major = Mathf.Max(0, EditorGUILayout.IntField(version.major, GUILayout.MinWidth(48f)));
                EditorGUILayout.LabelField("整包", GUILayout.Width(36f));
                version.full = Mathf.Max(0, EditorGUILayout.IntField(version.full, GUILayout.MinWidth(48f)));
                EditorGUILayout.LabelField("补丁", GUILayout.Width(36f));
                version.patch = Mathf.Max(0, EditorGUILayout.IntField(version.patch, GUILayout.MinWidth(48f)));
            }

            return version.ToString();
        }

        private string GetRecordsRoot()
        {
            return Path.GetFullPath(GetOutputRoot());
        }

        private string GetGameOutputDirectory(GameBuildProfile gameProfile)
        {
            string outputRoot = gameProfile == null || string.IsNullOrEmpty(gameProfile.outputRoot) ? GetOutputRoot() : gameProfile.outputRoot;
            string gameDirectoryName = gameProfile == null || string.IsNullOrEmpty(gameProfile.gameId) ? GetSelectedYooAssetPackageName() : gameProfile.gameId;
            return Path.GetFullPath(Path.Combine(outputRoot, gameDirectoryName));
        }

        private string ResolveOldFileManifestPath()
        {
            if (!string.IsNullOrEmpty(_oldFileManifestPath) && File.Exists(_oldFileManifestPath))
            {
                return _oldFileManifestPath;
            }

            string latestManifestPath = GetLatestBuiltFileManifestPath(ActiveGameProfile);
            if (!string.IsNullOrEmpty(latestManifestPath))
            {
                _oldFileManifestPath = latestManifestPath;
            }

            return _oldFileManifestPath;
        }

        private string GetLatestBuiltFileManifestPath(GameBuildProfile gameProfile)
        {
            if (gameProfile == null)
            {
                return string.Empty;
            }

            string stageRoot = Path.Combine(GetGameOutputDirectory(gameProfile), "Stage");
            if (!Directory.Exists(stageRoot))
            {
                return string.Empty;
            }

            if (!GamePackageVersion.TryParse(gameProfile.packageVersion, out GamePackageVersion currentVersion))
            {
                return string.Empty;
            }

            string currentManifestPath = Path.Combine(stageRoot, currentVersion.ToString(), GamePackageBuildUtility.FileManifestFileName);
            if (File.Exists(currentManifestPath))
            {
                return currentManifestPath;
            }

            return Directory.GetFiles(stageRoot, GamePackageBuildUtility.FileManifestFileName, SearchOption.AllDirectories)
                .Where(path => IsValidOldManifest(path, currentVersion))
                .OrderByDescending(path => GetManifestVersion(path).full)
                .ThenByDescending(path => GetManifestVersion(path).patch)
                .ThenByDescending(File.GetLastWriteTime)
                .FirstOrDefault() ?? string.Empty;
        }

        private static bool IsValidOldManifest(string path, GamePackageVersion currentVersion)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            GamePackageVersion manifestVersion = GetManifestVersion(path);
            return manifestVersion.IsSameMajor(currentVersion);
        }

        private bool ShouldUseAutoManifestPath(string autoManifestPath)
        {
            if (string.IsNullOrEmpty(autoManifestPath))
            {
                return false;
            }

            if (string.IsNullOrEmpty(_oldFileManifestPath) || !File.Exists(_oldFileManifestPath))
            {
                return true;
            }

            string stageRoot = Path.GetFullPath(Path.Combine(GetGameOutputDirectory(ActiveGameProfile), "Stage"));
            string oldManifestPath = Path.GetFullPath(_oldFileManifestPath);
            return oldManifestPath.StartsWith(stageRoot, System.StringComparison.OrdinalIgnoreCase) && GetManifestVersion(oldManifestPath).ToString() != ActiveGameProfile.packageVersion;
        }

        private static GamePackageVersion GetManifestVersion(string path)
        {
            string versionDirectory = new DirectoryInfo(Path.GetDirectoryName(path)).Name;
            return GamePackageVersion.TryParse(versionDirectory, out GamePackageVersion version) ? version : GamePackageVersion.Default;
        }

        private float GetSafeContentWidth()
        {
            return Mathf.Max(260f, EditorGUIUtility.currentViewWidth - 48f);
        }

        private string GetOutputRoot()
        {
            return _buildProfile == null || string.IsNullOrEmpty(_buildProfile.outputRoot) ? "BuildOutput" : _buildProfile.outputRoot;
        }

        private string GetSelectedYooAssetPackageName()
        {
            GameBuildProfile gameProfile = ActiveGameProfile;
            if (gameProfile == null)
            {
                return string.Empty;
            }

            return string.IsNullOrEmpty(gameProfile.yooAssetPackageName) ? gameProfile.packageName : gameProfile.yooAssetPackageName;
        }

        private static string ToProjectRelativePath(string path)
        {
            string fullPath = Path.GetFullPath(path).Replace('\\', '/');
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            if (fullPath.StartsWith(projectRoot))
            {
                return fullPath.Substring(projectRoot.Length).TrimStart('/');
            }

            return fullPath;
        }

        private FRFrameBuildContext CreateContext()
        {
            if (_buildProfile == null)
            {
                throw new System.InvalidOperationException("请先选择或新建构建配置资产。");
            }

            return new FRFrameBuildContext(_buildProfile, ActiveGameProfile, _buildProfile.buildTarget, GetOutputRoot(), _logger);
        }

        private void AutoSelectFirstBuildProfile()
        {
            if (_buildProfile != null)
            {
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:FRFrameBuildProfile");
            if (guids.Length == 0)
            {
                return;
            }

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            _buildProfile = AssetDatabase.LoadAssetAtPath<FRFrameBuildProfile>(path);
            ClampActiveGameIndex();
        }

        private void CreateBuildProfileAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject("新建构建配置", "FRFrameBuildProfile", "asset", "选择构建配置资产保存位置", DefaultProfileDirectory);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            EnsureAssetDirectory(Path.GetDirectoryName(path));
            FRFrameBuildProfile profile = ScriptableObject.CreateInstance<FRFrameBuildProfile>();
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();
            _buildProfile = profile;
            _activeGameIndex = -1;
            Selection.activeObject = profile;
        }

        private void CreateGameProfileAsset()
        {
            if (_buildProfile == null)
            {
                return;
            }

            string path = EditorUtility.SaveFilePanelInProject("新建游戏配置", "GameBuildProfile", "asset", "选择游戏配置资产保存位置", DefaultProfileDirectory);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            EnsureAssetDirectory(Path.GetDirectoryName(path));
            GameBuildProfile profile = ScriptableObject.CreateInstance<GameBuildProfile>();
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();
            AddGameProfile(profile);
            Selection.activeObject = profile;
        }

        private void AddGameProfile(GameBuildProfile profile)
        {
            if (_buildProfile == null || profile == null)
            {
                return;
            }

            EnsureGameProfileList();
            if (!_buildProfile.selectedGameProfiles.Contains(profile))
            {
                _buildProfile.selectedGameProfiles.Add(profile);
                EditorUtility.SetDirty(_buildProfile);
            }

            _activeGameIndex = _buildProfile.selectedGameProfiles.IndexOf(profile);
        }

        private void EnsureGameProfileList()
        {
            if (_buildProfile.selectedGameProfiles == null)
            {
                _buildProfile.selectedGameProfiles = new System.Collections.Generic.List<GameBuildProfile>();
                EditorUtility.SetDirty(_buildProfile);
            }

            ClampActiveGameIndex();
        }

        private void ClampActiveGameIndex()
        {
            if (_buildProfile == null || _buildProfile.selectedGameProfiles == null || _buildProfile.selectedGameProfiles.Count == 0)
            {
                _activeGameIndex = -1;
                return;
            }

            if (_activeGameIndex < 0 || _activeGameIndex >= _buildProfile.selectedGameProfiles.Count)
            {
                _activeGameIndex = 0;
            }
        }

        private static void SaveAsset(Object asset)
        {
            if (asset == null)
            {
                return;
            }

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void EnsureAssetDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory) || AssetDatabase.IsValidFolder(directory))
            {
                return;
            }

            string[] parts = directory.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        private static void DrawColorBlock(Rect rect, Color leftColor, Color rightColor)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            EditorGUI.DrawRect(rect, leftColor);
            Rect accent = new Rect(rect.x + rect.width * 0.70f, rect.y, rect.width * 0.30f, rect.height);
            EditorGUI.DrawRect(accent, rightColor);
        }
    }
}
#endif
