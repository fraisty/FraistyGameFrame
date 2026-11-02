#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Editor
{
    public sealed class FRFrameBuildWindow : EditorWindow
    {
        private enum FRFrameBuildPage
        {
            MainPackage,
            GamePackage,
            PublishPackage
        }

        private const string MenuPath = "FRFrame/构建/构建窗口";
        private const string DefaultProfileDirectory = "Assets/Settings/FRFrameBuild";

        private readonly FRFrameBuildLogger _logger = new FRFrameBuildLogger();
        private Vector2 _scroll;
        private FRFrameBuildProfile _buildProfile;
        private FRFrameBuildPage _activePage;
        private FRFrameGamePackageBuildWindow _gamePackagePanel;

        private bool _showOverview = true;
        private bool _showBuildSettings = true;
        private bool _showHybridCLR = true;
        private bool _showDefaultYooAsset = true;
        private bool _showBootstrap = true;
        private bool _showMainPackageBuild = true;
        private bool _showPlayerBuild = true;
        private bool _showBuildRecords = true;
        private bool _showPublishPackage = true;
        private bool _showPublishRecords = true;
        private bool _showLogs = true;

        [MenuItem(MenuPath)]
        public static void Open()
        {
            FRFrameBuildWindow window = GetWindow<FRFrameBuildWindow>("FRFrame 构建");
            window.minSize = new Vector2(980f, 700f);
            window.Show();
        }

        private void OnEnable()
        {
            AutoSelectFirstBuildProfile();
        }

        private void OnGUI()
        {
            DrawHeader();
            DrawPageTabs();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            try
            {
                if (_activePage == FRFrameBuildPage.MainPackage)
                {
                    DrawOverviewGroup();
                    DrawBuildSettingsGroup();
                    DrawHybridCLRGroup();
                    DrawDefaultYooAssetGroup();
                    DrawBootstrapGroup();
                    DrawMainPackageBuildGroup();
                    DrawPlayerBuildGroup();
                    DrawBuildRecordsGroup();
                    DrawLogsGroup();
                }
                else if (_activePage == FRFrameBuildPage.GamePackage)
                {
                    GetGamePackagePanel().DrawEmbeddedGUI();
                }
                else
                {
                    DrawPublishPackageGroup();
                    DrawPublishRecordsGroup();
                    DrawLogsGroup();
                }
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawHeader()
        {
            Rect rect = GUILayoutUtility.GetRect(0f, 58f, GUILayout.ExpandWidth(true));
            DrawColorBlock(rect, new Color(0.13f, 0.20f, 0.27f), new Color(0.20f, 0.36f, 0.42f));
            GUILayout.Space(-52f);
            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.Space(7f);
                GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 18,
                    normal = { textColor = Color.white }
                };
                GUIStyle subTitleStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(0.82f, 0.92f, 0.95f) }
                };
                EditorGUILayout.LabelField("FRFrame 构建中心", titleStyle);
                EditorGUILayout.LabelField("在同一个窗口内切换主包构建、游戏包构建与发布聚合包，各自保留独立流程、参数、输出记录和日志。", subTitleStyle);
            }
            EditorGUILayout.Space(10f);
        }

        private void DrawPageTabs()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawPageTab(FRFrameBuildPage.MainPackage, "主包构建", new Color(0.18f, 0.42f, 0.58f));
                DrawPageTab(FRFrameBuildPage.GamePackage, "游戏包构建", new Color(0.45f, 0.30f, 0.62f));
                DrawPageTab(FRFrameBuildPage.PublishPackage, "发布聚合包", new Color(0.62f, 0.42f, 0.18f));
            }

            EditorGUILayout.Space(8f);
        }

        private void DrawPageTab(FRFrameBuildPage page, string label, Color color)
        {
            bool active = _activePage == page;
            Color oldBackgroundColor = GUI.backgroundColor;
            GUI.backgroundColor = active ? color : new Color(color.r * 0.65f, color.g * 0.65f, color.b * 0.65f);
            if (GUILayout.Button(label, GUILayout.Height(active ? 34f : 30f)))
            {
                _activePage = page;
                _scroll = Vector2.zero;
                Repaint();
            }
            GUI.backgroundColor = oldBackgroundColor;
        }

        private void DrawOverviewGroup()
        {
            _showOverview = EditorGUILayout.BeginFoldoutHeaderGroup(_showOverview, "主包构建状态");
            if (_showOverview)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("当前 Unity 平台", EditorUserBuildSettings.activeBuildTarget.ToString());
                    EditorGUILayout.LabelField("构建配置", _buildProfile == null ? "未选择" : _buildProfile.name);
                    EditorGUILayout.LabelField("目标平台", _buildProfile == null ? "未选择" : _buildProfile.buildTarget.ToString());
                    EditorGUILayout.LabelField("构建模式", _buildProfile == null ? "未选择" : GetBuildModeLabel(_buildProfile.buildMode));
                    EditorGUILayout.LabelField("输出根目录", _buildProfile == null || string.IsNullOrEmpty(_buildProfile.outputRoot) ? "BuildOutput" : _buildProfile.outputRoot);
                    EditorGUILayout.LabelField("DefaultPackage", _buildProfile == null || string.IsNullOrEmpty(_buildProfile.defaultYooAssetPackageName) ? "未填写" : _buildProfile.defaultYooAssetPackageName);
                    EditorGUILayout.LabelField("关联游戏配置", _buildProfile == null || _buildProfile.selectedGameProfiles == null ? "0" : _buildProfile.selectedGameProfiles.Count.ToString());
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawBuildSettingsGroup()
        {
            _showBuildSettings = EditorGUILayout.BeginFoldoutHeaderGroup(_showBuildSettings, "主包构建配置");
            if (_showBuildSettings)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        _buildProfile = (FRFrameBuildProfile)EditorGUILayout.ObjectField("构建配置资产", _buildProfile, typeof(FRFrameBuildProfile), false);
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
                        EditorGUILayout.HelpBox("请先选择或新建主包构建配置资产。", MessageType.Info);
                    }
                    else
                    {
                        EditorGUI.BeginChangeCheck();
                        _buildProfile.buildTarget = (BuildTarget)EditorGUILayout.EnumPopup("目标平台", _buildProfile.buildTarget);
                        _buildProfile.buildTargetGroup = (BuildTargetGroup)EditorGUILayout.EnumPopup("目标平台组", _buildProfile.buildTargetGroup);
                        _buildProfile.outputRoot = DrawFolderField("主包输出目录", _buildProfile.outputRoot);
                        _buildProfile.packageVersion = DrawMainVersionFields("主包版本", _buildProfile.packageVersion);
                        EditorGUILayout.HelpBox("版本格式为 大版本.整包版本.补丁版本。大版本只手动调整；构建主包整包会自动递增整包版本并重置补丁号；构建主包补丁包会自动递增补丁版本。", MessageType.Info);
                        _buildProfile.buildMode = (FRFrameBuildMode)EditorGUILayout.EnumPopup("构建模式", _buildProfile.buildMode);
                        EditorGUILayout.HelpBox($"Player 构建选项：{GetBuildModeLabel(_buildProfile.buildMode)}。建议保持跟随 Unity Build Settings，避免窗口构建和手动 Build 的 Development Build 开关不一致。", MessageType.Info);
                        _buildProfile.scriptingDefineSymbols = EditorGUILayout.TextField("脚本宏定义", _buildProfile.scriptingDefineSymbols);
                        _buildProfile.logDefineMode = (FRBuildLogDefineMode)EditorGUILayout.EnumPopup("日志宏策略", _buildProfile.logDefineMode);
                        _buildProfile.enableRuntimeConsoleInDevelopment = EditorGUILayout.Toggle("开发包运行时 Console", _buildProfile.enableRuntimeConsoleInDevelopment);
                        _buildProfile.enableFileLogInDevelopment = EditorGUILayout.Toggle("开发包文件日志", _buildProfile.enableFileLogInDevelopment);
                        _buildProfile.enableFileLogInRelease = EditorGUILayout.Toggle("发布包文件日志", _buildProfile.enableFileLogInRelease);
                        _buildProfile.developmentMinimumLogLevel = (FRLogLevel)EditorGUILayout.EnumPopup("开发包最低日志等级", _buildProfile.developmentMinimumLogLevel);
                        _buildProfile.releaseMinimumLogLevel = (FRLogLevel)EditorGUILayout.EnumPopup("发布包最低日志等级", _buildProfile.releaseMinimumLogLevel);
                        _buildProfile.playerOutputName = EditorGUILayout.TextField("Player 输出名称", _buildProfile.playerOutputName);
                        _buildProfile.switchBuildTarget = EditorGUILayout.Toggle("自动切换平台", _buildProfile.switchBuildTarget);
                        _buildProfile.enableHybridCLR = EditorGUILayout.Toggle("启用 HybridCLR", _buildProfile.enableHybridCLR);
                        _buildProfile.buildHotUpdateDll = EditorGUILayout.Toggle("生成热更 DLL", _buildProfile.buildHotUpdateDll);
                        _buildProfile.buildDefaultYooAsset = EditorGUILayout.Toggle("Player 前构建 DefaultPackage", _buildProfile.buildDefaultYooAsset);
                        _buildProfile.exportBootstrap = EditorGUILayout.Toggle("导出启动配置", _buildProfile.exportBootstrap);
                        _buildProfile.buildPlayer = EditorGUILayout.Toggle("完整流水线构建 Player", _buildProfile.buildPlayer);
                        if (EditorGUI.EndChangeCheck())
                        {
                            EditorUtility.SetDirty(_buildProfile);
                        }
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawHybridCLRGroup()
        {
            _showHybridCLR = EditorGUILayout.BeginFoldoutHeaderGroup(_showHybridCLR, "HybridCLR 流程");
            if (_showHybridCLR)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.HelpBox("生成 HybridCLR DLL 会执行 Generate All，并按当前构建目标 Compile HotUpdate DLL；复制按钮只处理已生成产物。", MessageType.Info);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("生成 HybridCLR DLL"))
                        {
                            ScheduleBuildAction(() => RunStep(new FRFrameActionBuildStep("生成 HybridCLR DLL", context => HybridCLRBuildHelper.ExecuteGenerateAllOnly(context.BuildTarget))));
                        }

                        if (GUILayout.Button("生成并复制到 StreamingAssets"))
                        {
                            ScheduleBuildAction(() => RunStep(new FRFrameActionBuildStep("生成并复制主包 HybridCLR 到 StreamingAssets", context => HybridCLRBuildHelper.ExecuteGenerateAndCopyMainHotFixToStreamingAssetsOnly(context.BuildTarget, GetGameHotUpdateAssemblies(context.BuildProfile)))));
                        }

                        if (GUILayout.Button("生成并复制到 StreamingAssets 和 GameRes"))
                        {
                            ScheduleBuildAction(() => RunStep(new FRFrameActionBuildStep("生成并复制主包 HybridCLR 到 StreamingAssets 和 GameRes", context => HybridCLRBuildHelper.ExecuteGenerateAndCopyMainHotFixToStreamingAssetsAndGameRes(context.BuildTarget, GetGameHotUpdateAssemblies(context.BuildProfile)))));
                        }
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("仅复制到 StreamingAssets"))
                        {
                            ScheduleBuildAction(() => RunStep(new FRFrameActionBuildStep("仅复制主包 HybridCLR 到 StreamingAssets", context => HybridCLRBuildHelper.ExecuteCopyMainHotFixToStreamingAssetsOnly(context.BuildTarget, GetGameHotUpdateAssemblies(context.BuildProfile)))));
                        }

                        if (GUILayout.Button("仅同步到 GameRes"))
                        {
                            ScheduleBuildAction(() => RunStep(new FRFrameActionBuildStep("同步 StreamingAssets 热更文件到 GameRes", _ => HybridCLRBuildHelper.ExecuteSyncStreamingHotFixToGameResOnly())));
                        }

                        if (GUILayout.Button("清理所有 HotFix 输出"))
                        {
                            ScheduleBuildAction(() => RunStep(new FRFrameActionBuildStep("清理所有 HotFix 输出", _ => HybridCLRBuildHelper.ClearAllHotFixOutput())));
                        }
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawDefaultYooAssetGroup()
        {
            _showDefaultYooAsset = EditorGUILayout.BeginFoldoutHeaderGroup(_showDefaultYooAsset, "DefaultPackage YooAsset 配置");
            if (_showDefaultYooAsset)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (_buildProfile == null)
                    {
                        EditorGUILayout.HelpBox("请先选择或新建主包构建配置资产。", MessageType.Info);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("DefaultPackage 是主包内置资源包，会在 Player 构建前按这里的参数构建并复制到 StreamingAssets。", MessageType.Info);
                        EditorGUI.BeginChangeCheck();
                        _buildProfile.defaultYooAssetPackageName = EditorGUILayout.TextField("默认包名", _buildProfile.defaultYooAssetPackageName);
                        _buildProfile.defaultYooAssetPackageVersion = EditorGUILayout.TextField("默认包版本", _buildProfile.defaultYooAssetPackageVersion);
                        _buildProfile.defaultYooAssetBuildPipeline = (YooAsset.Editor.EBuildPipeline)EditorGUILayout.EnumPopup("构建管线", _buildProfile.defaultYooAssetBuildPipeline);
                        _buildProfile.defaultYooAssetCompressOption = (YooAsset.Editor.ECompressOption)EditorGUILayout.EnumPopup("压缩方式", _buildProfile.defaultYooAssetCompressOption);
                        _buildProfile.defaultYooAssetFileNameStyle = (YooAsset.EFileNameStyle)EditorGUILayout.EnumPopup("文件命名方式", _buildProfile.defaultYooAssetFileNameStyle);
                        _buildProfile.defaultYooAssetBuildinFileCopyOption = (YooAsset.Editor.EBundledCopyOption)EditorGUILayout.EnumPopup("内置文件复制", _buildProfile.defaultYooAssetBuildinFileCopyOption);
                        _buildProfile.defaultYooAssetBuildOutputRoot = DrawFolderField("资源输出目录", _buildProfile.defaultYooAssetBuildOutputRoot);
                        _buildProfile.defaultYooAssetBuildinFileRoot = DrawFolderField("内置文件目录", _buildProfile.defaultYooAssetBuildinFileRoot);
                        _buildProfile.defaultYooAssetBuildinFileCopyParams = EditorGUILayout.TextField("内置复制参数", _buildProfile.defaultYooAssetBuildinFileCopyParams);
                        _buildProfile.defaultYooAssetEnableSharePackRule = EditorGUILayout.Toggle("启用共享打包规则", _buildProfile.defaultYooAssetEnableSharePackRule);
                        _buildProfile.defaultYooAssetSingleReferencedPackAlone = EditorGUILayout.Toggle("单引用资源单独打包", _buildProfile.defaultYooAssetSingleReferencedPackAlone);
                        _buildProfile.defaultYooAssetVerifyBuildingResult = EditorGUILayout.Toggle("验证构建结果", _buildProfile.defaultYooAssetVerifyBuildingResult);
                        _buildProfile.defaultYooAssetClearBuildCacheFiles = EditorGUILayout.Toggle("清理构建缓存", _buildProfile.defaultYooAssetClearBuildCacheFiles);
                        _buildProfile.defaultYooAssetUseAssetDependencyDB = EditorGUILayout.Toggle("使用依赖数据库", _buildProfile.defaultYooAssetUseAssetDependencyDB);
                        _buildProfile.defaultYooAssetStripUnityVersion = EditorGUILayout.Toggle("剥离 Unity 版本", _buildProfile.defaultYooAssetStripUnityVersion);
                        _buildProfile.defaultYooAssetDisableWriteTypeTree = EditorGUILayout.Toggle("禁写 TypeTree", _buildProfile.defaultYooAssetDisableWriteTypeTree);
                        _buildProfile.defaultYooAssetOverwriteOutput = EditorGUILayout.Toggle("覆盖已有版本输出", _buildProfile.defaultYooAssetOverwriteOutput);
                        _buildProfile.defaultYooAssetBuiltinShadersBundleName = EditorGUILayout.TextField("内置 Shader Bundle", _buildProfile.defaultYooAssetBuiltinShadersBundleName);
                        _buildProfile.defaultYooAssetMonoScriptsBundleName = EditorGUILayout.TextField("MonoScripts Bundle", _buildProfile.defaultYooAssetMonoScriptsBundleName);
                        _buildProfile.defaultYooAssetPackageNote = EditorGUILayout.TextField("资源包备注", _buildProfile.defaultYooAssetPackageNote);
                        if (EditorGUI.EndChangeCheck())
                        {
                            EditorUtility.SetDirty(_buildProfile);
                        }

                        if (GUILayout.Button("仅构建 DefaultPackage", GUILayout.Height(28f)))
                        {
                            ScheduleBuildAction(() => RunStep(new DefaultYooAssetPackageBuildStep()));
                        }
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawBootstrapGroup()
        {
            _showBootstrap = EditorGUILayout.BeginFoldoutHeaderGroup(_showBootstrap, "启动配置导出");
            if (_showBootstrap)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.HelpBox("启动配置会导出主包初始化所需的 YooAsset、UI、游戏目录种子和 HotFix 清单。", MessageType.Info);
                    if (GUILayout.Button("导出完整 Bootstrap 启动配置", GUILayout.Height(28f)))
                    {
                        ScheduleBuildAction(() => RunStep(new BootstrapConfigExportBuildStep()));
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawMainPackageBuildGroup()
        {
            _showMainPackageBuild = EditorGUILayout.BeginFoldoutHeaderGroup(_showMainPackageBuild, "主包补丁包/整包输出配置");
            if (_showMainPackageBuild)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (_buildProfile == null)
                    {
                        EditorGUILayout.HelpBox("请先选择或新建主包构建配置资产。", MessageType.Info);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("主包整包 zip 会随主包完整流水线生成并递增 Y；这里保留整包输出命名、历史清理和补丁包构建。补丁包只替换变更文件并删除废弃文件。", MessageType.Info);
                        EditorGUI.BeginChangeCheck();
                        _buildProfile.clearHistoryMainFullPackages = EditorGUILayout.Toggle("整包前清理历史整包", _buildProfile.clearHistoryMainFullPackages);
                        _buildProfile.mainFullPackageNamePattern = EditorGUILayout.TextField("整包命名", _buildProfile.mainFullPackageNamePattern);
                        _buildProfile.mainPatchPackageNamePattern = EditorGUILayout.TextField("补丁包命名", _buildProfile.mainPatchPackageNamePattern);
                        _buildProfile.mainOldFileManifestPath = DrawFileField("旧文件清单", _buildProfile.mainOldFileManifestPath, "json");
                        if (EditorGUI.EndChangeCheck())
                        {
                            EditorUtility.SetDirty(_buildProfile);
                        }

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button("自动关联旧清单"))
                            {
                                ScheduleBuildAction(() =>
                                {
                                    _buildProfile.mainOldFileManifestPath = MainPackageBuildUtility.FindDefaultOldFileManifest(CreateContext());
                                    EditorUtility.SetDirty(_buildProfile);
                                    _logger.Info(string.IsNullOrEmpty(_buildProfile.mainOldFileManifestPath) ? "[主包补丁] 未找到可用旧清单。" : $"[主包补丁] 已关联旧清单：{_buildProfile.mainOldFileManifestPath}");
                                });
                            }

                            if (GUILayout.Button("构建主包补丁包"))
                            {
                                ScheduleBuildAction(() => RunStep(new MainPatchPackageBuildStep(_buildProfile.mainOldFileManifestPath)));
                            }
                        }
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawPublishPackageGroup()
        {
            _showPublishPackage = EditorGUILayout.BeginFoldoutHeaderGroup(_showPublishPackage, "发布聚合包构建");
            if (_showPublishPackage)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (_buildProfile == null)
                    {
                        EditorGUILayout.HelpBox("请先选择或新建主包构建配置资产。", MessageType.Info);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("发布聚合包是外层 zip，内部保留独立主包 zip 和游戏包 zip，运行时会先分发主包再分发游戏包。", MessageType.Info);
                        EditorGUI.BeginChangeCheck();
                        _buildProfile.publishOutputRoot = DrawFolderField("发布输出目录", _buildProfile.publishOutputRoot);
                        _buildProfile.publishPackageNamePattern = EditorGUILayout.TextField("发布包命名", _buildProfile.publishPackageNamePattern);
                        _buildProfile.publishMainPackageZipPath = DrawFileField("主包 zip", _buildProfile.publishMainPackageZipPath, "zip");
                        DrawPublishGameZipList();
                        if (EditorGUI.EndChangeCheck())
                        {
                            EditorUtility.SetDirty(_buildProfile);
                        }

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button("自动关联最新 zip"))
                            {
                                ScheduleBuildAction(() =>
                                {
                                    FRFrameBuildContext context = CreateContext();
                                    _buildProfile.publishMainPackageZipPath = MainPackageBuildUtility.FindLatestMainPackageZip(context);
                                    _buildProfile.publishGamePackageZipPaths = MainPackageBuildUtility.FindLatestGamePackageZips(context);
                                    EditorUtility.SetDirty(_buildProfile);
                                    _logger.Info("[发布聚合包] 已自动关联最新主包和游戏包 zip。");
                                });
                            }

                            if (GUILayout.Button("构建发布聚合包"))
                            {
                                ScheduleBuildAction(() => RunStep(new FRFrameActionBuildStep("构建发布聚合包", context => MainPackageBuildUtility.BuildPublishPackage(context))));
                            }
                        }
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawPublishGameZipList()
        {
            _buildProfile.publishGamePackageZipPaths ??= new List<string>();
            EditorGUILayout.LabelField($"游戏包 zip：{_buildProfile.publishGamePackageZipPaths.Count} 个", EditorStyles.boldLabel);
            for (int i = 0; i < _buildProfile.publishGamePackageZipPaths.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    _buildProfile.publishGamePackageZipPaths[i] = EditorGUILayout.TextField($"游戏包 {i + 1}", _buildProfile.publishGamePackageZipPaths[i]);
                    if (GUILayout.Button("选择", GUILayout.Width(80f)))
                    {
                        string selected = EditorUtility.OpenFilePanel("选择游戏包 zip", Application.dataPath, "zip");
                        if (!string.IsNullOrEmpty(selected))
                        {
                            _buildProfile.publishGamePackageZipPaths[i] = selected;
                        }
                    }

                    if (GUILayout.Button("移除", GUILayout.Width(70f)))
                    {
                        _buildProfile.publishGamePackageZipPaths.RemoveAt(i);
                        EditorUtility.SetDirty(_buildProfile);
                        return;
                    }
                }
            }

            if (GUILayout.Button("添加游戏包 zip", GUILayout.Height(26f)))
            {
                _buildProfile.publishGamePackageZipPaths.Add(string.Empty);
                EditorUtility.SetDirty(_buildProfile);
            }
        }

        private void DrawPublishRecordsGroup()
        {
            _showPublishRecords = EditorGUILayout.BeginFoldoutHeaderGroup(_showPublishRecords, "发布聚合包输出记录");
            if (_showPublishRecords)
            {
                string root = GetPublishRecordsRoot();
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("记录根目录", root);
                    if (GUILayout.Button("打开根目录", GUILayout.Width(120f)) && Directory.Exists(root))
                    {
                        EditorUtility.RevealInFinder(root);
                    }
                }

                if (!Directory.Exists(root))
                {
                    EditorGUILayout.HelpBox("未找到发布聚合包输出目录。", MessageType.Info);
                }
                else
                {
                    string[] files = Directory.GetFiles(root, "*.zip", SearchOption.AllDirectories)
                        .OrderByDescending(File.GetLastWriteTime)
                        .Take(60)
                        .ToArray();
                    foreach (string file in files)
                    {
                        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                        {
                            EditorGUILayout.LabelField(ToProjectRelativePath(file), GUILayout.MinWidth(0f));
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

        private void DrawPlayerBuildGroup()
        {
            _showPlayerBuild = EditorGUILayout.BeginFoldoutHeaderGroup(_showPlayerBuild, "Player 构建流程");
            if (_showPlayerBuild)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    BuildTarget target = _buildProfile == null ? EditorUserBuildSettings.activeBuildTarget : _buildProfile.buildTarget;
                    string outputRoot = _buildProfile == null || string.IsNullOrEmpty(_buildProfile.outputRoot) ? "BuildOutput" : _buildProfile.outputRoot;
                    EditorGUILayout.LabelField("目标平台", target.ToString());
                    EditorGUILayout.LabelField("输出目录", outputRoot);
                    EditorGUILayout.HelpBox("构建 Player 是 Unity 可执行程序构建：Android 输出 APK，Windows 输出 exe，iOS 输出 Xcode 工程。", MessageType.Info);
                    EditorGUILayout.HelpBox("完整主包流水线会按配置执行：准备目标平台和宏定义、HybridCLR、启动配置、DefaultPackage、生成主包整包 zip，并可选构建 Player。", MessageType.Info);

                    if (GUILayout.Button("构建 DefaultPackage 并构建 Player", GUILayout.Height(30f)))
                    {
                        ScheduleBuildAction(RunPlayerBuildPipeline);
                    }

                    if (GUILayout.Button("执行主包完整流水线", GUILayout.Height(30f)))
                    {
                        ScheduleBuildAction(RunFullPipeline);
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawBuildRecordsGroup()
        {
            _showBuildRecords = EditorGUILayout.BeginFoldoutHeaderGroup(_showBuildRecords, "主包输出记录");
            if (_showBuildRecords)
            {
                string root = GetRecordsRoot();
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("记录根目录", root);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("刷新", GUILayout.Width(120f)))
                        {
                            Repaint();
                        }

                        if (GUILayout.Button("打开根目录", GUILayout.Width(120f)) && Directory.Exists(root))
                        {
                            EditorUtility.RevealInFinder(root);
                        }
                    }
                }

                if (!Directory.Exists(root))
                {
                    EditorGUILayout.HelpBox("未找到主包输出目录。", MessageType.Info);
                    EditorGUILayout.EndFoldoutHeaderGroup();
                    return;
                }

                string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                    .Where(path => Path.GetExtension(path).Equals(".apk", System.StringComparison.OrdinalIgnoreCase)
                        || Path.GetExtension(path).Equals(".exe", System.StringComparison.OrdinalIgnoreCase)
                        || Path.GetExtension(path).Equals(".zip", System.StringComparison.OrdinalIgnoreCase)
                        || Path.GetFileName(path).Equals("build_history.json", System.StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(File.GetLastWriteTime)
                    .Take(60)
                    .ToArray();

                foreach (string file in files)
                {
                    using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField(ToProjectRelativePath(file), GUILayout.MinWidth(0f));
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
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawLogsGroup()
        {
            _showLogs = EditorGUILayout.BeginFoldoutHeaderGroup(_showLogs, "主包构建日志");
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

                    EditorGUILayout.TextArea(_logger.ToText(), GUILayout.MinHeight(160f), GUILayout.ExpandHeight(true));
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

                Repaint();
            };
        }

        private void RunStep(IFRFrameBuildStep step)
        {
            FRFrameBuildContext context = CreateContext();
            FRFrameBuildPipeline pipeline = new FRFrameBuildPipeline().Add(step);
            FRFrameBuildStepResult result = pipeline.Execute(context);
            if (result.Success)
            {
                _logger.Info($"[主包构建] 流程完成，{result.ElapsedSeconds:F2}s");
            }
        }

        private void RunPlayerBuildPipeline()
        {
            FRFrameBuildContext context = CreateContext();
            FRFrameBuildProfile profile = context.BuildProfile;
            FRFrameBuildPipeline pipeline = new FRFrameBuildPipeline();
            pipeline.Add(new FRFramePrepareBuildSettingsStep());
            pipeline.Add(new FRFrameLogBuildStep());
            AddDefaultYooAssetBuildStepIfNeeded(pipeline, profile);
            pipeline.Add(new FRFrameActionBuildStep("刷新构建输入资源", _ => AssetDatabase.Refresh()));
            pipeline.Add(new FRFramePlayerBuildStep());

            FRFrameBuildStepResult result = pipeline.Execute(context);
            if (result.Success)
            {
                _logger.Info($"[主包构建] Player 构建完成，{result.ElapsedSeconds:F2}s");
            }
        }

        private void RunFullPipeline()
        {
            FRFrameBuildContext context = CreateContext();
            FRFrameBuildProfile profile = context.BuildProfile;
            FRFrameBuildPipeline pipeline = new FRFrameBuildPipeline();
            pipeline.Add(new FRFramePrepareBuildSettingsStep());
            pipeline.Add(new FRFrameLogBuildStep());

            if (profile.enableHybridCLR)
            {
                if (profile.buildHotUpdateDll)
                {
                    pipeline.Add(new FRFrameActionBuildStep("生成并复制主包 HybridCLR 到 StreamingAssets 和 GameRes", stepContext => HybridCLRBuildHelper.ExecuteGenerateAndCopyMainHotFixToStreamingAssetsAndGameRes(stepContext.BuildTarget, GetGameHotUpdateAssemblies(stepContext.BuildProfile))));
                }
                else
                {
                    pipeline.Add(new FRFrameActionBuildStep("复制主包 HybridCLR 到 StreamingAssets 和 GameRes", stepContext => HybridCLRBuildHelper.ExecuteCopyMainHotFixToStreamingAssetsAndGameRes(stepContext.BuildTarget, GetGameHotUpdateAssemblies(stepContext.BuildProfile))));
                }
            }

            if (profile.exportBootstrap)
            {
                pipeline.Add(new BootstrapConfigExportBuildStep());
            }

            pipeline.Add(new MainFullPackageBuildStep());

            if (profile.buildPlayer)
            {
                AddDefaultYooAssetBuildStepIfNeeded(pipeline, profile);
                pipeline.Add(new FRFrameActionBuildStep("刷新构建输入资源", _ => AssetDatabase.Refresh()));
                pipeline.Add(new FRFramePlayerBuildStep());
            }

            FRFrameBuildStepResult result = pipeline.Execute(context);
            if (result.Success)
            {
                _logger.Info($"[主包构建] 完整流水线完成，{result.ElapsedSeconds:F2}s");
            }
        }

        private static void AddDefaultYooAssetBuildStepIfNeeded(FRFrameBuildPipeline pipeline, FRFrameBuildProfile profile)
        {
            if (profile.buildDefaultYooAsset)
            {
                pipeline.Add(new DefaultYooAssetPackageBuildStep());
            }
        }

        private static IEnumerable<string> GetGameHotUpdateAssemblies(FRFrameBuildProfile profile)
        {
            if (profile?.selectedGameProfiles == null)
            {
                yield break;
            }

            foreach (GameBuildProfile gameProfile in profile.selectedGameProfiles)
            {
                if (gameProfile == null || !gameProfile.includeHotUpdateAssembly)
                {
                    continue;
                }

                string assemblyName = string.IsNullOrEmpty(gameProfile.hotUpdateAssemblyName) ? gameProfile.gameId : gameProfile.hotUpdateAssemblyName;
                if (!string.IsNullOrEmpty(assemblyName))
                {
                    yield return assemblyName;
                }
            }
        }

        private string DrawFolderField(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                value = EditorGUILayout.TextField(label, value);
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

        private string DrawMainVersionFields(string label, string value)
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

        private string DrawFileField(string label, string value, string extension)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                value = EditorGUILayout.TextField(label, value);
                if (GUILayout.Button("选择", GUILayout.Width(80f)))
                {
                    string selected = EditorUtility.OpenFilePanel($"选择{label}", string.IsNullOrEmpty(value) ? Application.dataPath : Path.GetDirectoryName(value), extension);
                    if (!string.IsNullOrEmpty(selected))
                    {
                        value = selected;
                    }
                }
            }

            return value;
        }

        private string GetRecordsRoot()
        {
            string outputRoot = _buildProfile == null || string.IsNullOrEmpty(_buildProfile.outputRoot) ? "BuildOutput" : _buildProfile.outputRoot;
            return Path.GetFullPath(outputRoot);
        }

        private string GetPublishRecordsRoot()
        {
            if (_buildProfile == null)
            {
                return Path.GetFullPath(Path.Combine("BuildOutput", "PublishPackage"));
            }

            string outputRoot = string.IsNullOrEmpty(_buildProfile.publishOutputRoot) ? GetRecordsRoot() : _buildProfile.publishOutputRoot;
            return Path.GetFullPath(Path.Combine(outputRoot, "PublishPackage"));
        }

        private FRFrameGamePackageBuildWindow GetGamePackagePanel()
        {
            return _gamePackagePanel ??= new FRFrameGamePackageBuildWindow(Repaint);
        }

        private static void DrawColorBlock(Rect rect, Color leftColor, Color rightColor)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            EditorGUI.DrawRect(rect, leftColor);
            Rect accent = new Rect(rect.x + rect.width * 0.68f, rect.y, rect.width * 0.32f, rect.height);
            EditorGUI.DrawRect(accent, rightColor);
        }

        private static string GetBuildModeLabel(FRFrameBuildMode buildMode)
        {
            switch (buildMode)
            {
                case FRFrameBuildMode.FollowEditorBuildSettings:
                    return $"跟随 Unity Build Settings ({(EditorUserBuildSettings.development ? "Development" : "Release")})";
                case FRFrameBuildMode.DevelopmentBuild:
                    return "强制 Development Build";
                case FRFrameBuildMode.ReleaseBuild:
                    return "强制 Release Build";
                case FRFrameBuildMode.OfflineFullApk:
                    return "离线完整包";
                case FRFrameBuildMode.MinimalApk:
                    return "最小母包";
                case FRFrameBuildMode.HotFixOnly:
                    return "仅热更";
                case FRFrameBuildMode.BootstrapOnly:
                    return "仅启动配置";
                default:
                    return buildMode.ToString();
            }
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
                throw new System.InvalidOperationException("请先选择或新建主包构建配置资产。");
            }

            return new FRFrameBuildContext(_buildProfile, null, _buildProfile.buildTarget, string.IsNullOrEmpty(_buildProfile.outputRoot) ? "BuildOutput" : _buildProfile.outputRoot, _logger);
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
        }

        private void CreateBuildProfileAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject("新建主包构建配置", "FRFrameBuildProfile", "asset", "选择主包构建配置资产保存位置", DefaultProfileDirectory);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            EnsureAssetDirectory(Path.GetDirectoryName(path));
            FRFrameBuildProfile profile = CreateInstance<FRFrameBuildProfile>();
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();
            _buildProfile = profile;
            Selection.activeObject = profile;
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
    }
}
#endif
