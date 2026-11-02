#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace FRFrame.Editor
{
    /// <summary>
    /// UIForm、UIWidget、UIItem 初始化菜单工具。
    ///
    /// 用途：
    /// 对一个新的 UI 预制体或 UI 根节点，一键完成：
    /// 1. 根据菜单类型创建同名 UIForm、UIWidget 或 UIItem 脚本；
    /// 2. 脚本继承对应的 UI 运行时基类；
    /// 3. 脚本声明为 partial，供 UI Bind 生成同名绑定分部类；
    /// 4. 根节点添加 UIBindData；
    /// 5. 根节点添加 UIBindScriptHolder；
    /// 6. 自动关联刚创建的脚本；
    /// 7. 脚本编译完成后同步真实命名空间与程序集信息。
    ///
    /// 支持：
    /// - Hierarchy 中的普通场景对象；
    /// - Prefab Stage 中打开的预制体对象；
    /// - Project 面板中的 Prefab Asset。
    ///
    /// 类型约定：
    /// - UIForm 用作由 UIManager 管理的完整窗口；
    /// - UIWidget 用作窗口内部具备独立生命周期的复合区域；
    /// - UIItem 用作列表、网格或对象池中的可复用条目。
    /// </summary>
    public static class UIFormSetupMenu
    {
        /// <summary>
        /// 初始化工具支持的运行时 UI 根类型。
        /// 该类型同时决定菜单语义、默认脚本名和生成脚本的基类。
        /// </summary>
        private enum UIType
        {
            Form,
            Widget,
            Item
        }

        private const string MenuRoot = "UI Bind";

        /*
         * 这里默认认为 UIForm、UIWidget、UIItem 都在 FRFrame 命名空间下。
         *
         * 如果这些运行时基类不在 FRFrame 命名空间：
         * 1. 可以把 GeneratedScriptUsingNamespace 改成空字符串；
         * 2. 或者改成实际存放这些基类的运行时命名空间。
         */
        private const string GeneratedScriptUsingNamespace = "FRFrame";

        /*
         * 这里默认生成的 UI 脚本不包 namespace。
         *
         * 如果你希望所有自动生成的 UIForm、UIWidget、UIItem 脚本都进入固定 namespace，
         * 例如 Game.UI，可以改成：
         *
         * private const string GeneratedScriptNamespace = "Game.UI";
         */
        private const string GeneratedScriptNamespace = "";

        /*
         * 新脚本默认程序集名。
         *
         * 如果脚本放在普通 Assets 目录且没有 asmdef，通常是 Assembly-CSharp。
         * 如果脚本放在 asmdef 下，后续脚本编译完成后，本工具会尝试通过 MonoScript.GetClass()
         * 再同步一次真实程序集名。
         */
        private const string DefaultAssemblyName = "Assembly-CSharp";

        /*
         * pending 信息跨脚本编译和域重载暂存。
         * GlobalObjectId 用于重新定位 UIBindData，ScriptPath 用于取得编译后的 MonoScript 类型。
         */
        private const string PendingGlobalObjectIdKey = "FRFrame.UIFormSetup.Pending.GlobalObjectId";
        private const string PendingScriptPathKey = "FRFrame.UIFormSetup.Pending.ScriptPath";

        #region Hierarchy 菜单

        /// <summary>
        /// 为 Hierarchy 选中的根节点创建 UIForm 脚本并初始化绑定。
        /// </summary>
        [MenuItem("GameObject/" + MenuRoot + "/创建 UIForm 脚本并初始化绑定", false, 10)]
        private static void CreateForHierarchyObject(MenuCommand command)
        {
            CreateForHierarchyObject(command, UIType.Form);
        }

        /// <summary>
        /// 为 Hierarchy 选中的根节点创建 UIWidget 脚本并初始化绑定。
        /// </summary>
        [MenuItem("GameObject/" + MenuRoot + "/创建 UIWidget 脚本并初始化绑定", false, 11)]
        private static void CreateWidgetForHierarchyObject(MenuCommand command)
        {
            CreateForHierarchyObject(command, UIType.Widget);
        }

        /// <summary>
        /// 为 Hierarchy 选中的根节点创建 UIItem 脚本并初始化绑定。
        /// </summary>
        [MenuItem("GameObject/" + MenuRoot + "/创建 UIItem 脚本并初始化绑定", false, 12)]
        private static void CreateItemForHierarchyObject(MenuCommand command)
        {
            CreateForHierarchyObject(command, UIType.Item);
        }

        /// <summary>
        /// 解析右键菜单上下文或当前 Selection，并进入场景对象初始化主流程。
        /// MenuCommand.context 优先，可确保 Hierarchy 右键对象与当前激活对象不一致时仍操作正确目标。
        /// </summary>
        private static void CreateForHierarchyObject(MenuCommand command, UIType uiType)
        {
            GameObject target = command.context as GameObject;
            if (target == null)
                target = Selection.activeGameObject;

            if (target == null)
            {
                EditorUtility.DisplayDialog("提示", "请先选择一个 UI 根节点。", "确定");
                return;
            }

            CreateScriptAndSetupSceneObject(target, uiType);
        }

        /// <summary>
        /// 仅在 Hierarchy 中存在激活对象时启用三类初始化菜单。
        /// </summary>
        [MenuItem("GameObject/" + MenuRoot + "/创建 UIForm 脚本并初始化绑定", true)]
        [MenuItem("GameObject/" + MenuRoot + "/创建 UIWidget 脚本并初始化绑定", true)]
        [MenuItem("GameObject/" + MenuRoot + "/创建 UIItem 脚本并初始化绑定", true)]
        private static bool ValidateCreateForHierarchyObject()
        {
            return Selection.activeGameObject != null;
        }

        #endregion

        #region Project Prefab 菜单

        /// <summary>
        /// 为 Project 面板选中的 Prefab Asset 创建 UIForm 脚本并初始化绑定。
        /// </summary>
        [MenuItem("Assets/" + MenuRoot + "/创建 UIForm 脚本并初始化绑定", false, 10)]
        private static void CreateForPrefabAsset()
        {
            CreateForPrefabAsset(UIType.Form);
        }

        /// <summary>
        /// 为 Project 面板选中的 Prefab Asset 创建 UIWidget 脚本并初始化绑定。
        /// </summary>
        [MenuItem("Assets/" + MenuRoot + "/创建 UIWidget 脚本并初始化绑定", false, 11)]
        private static void CreateWidgetForPrefabAsset()
        {
            CreateForPrefabAsset(UIType.Widget);
        }

        /// <summary>
        /// 为 Project 面板选中的 Prefab Asset 创建 UIItem 脚本并初始化绑定。
        /// </summary>
        [MenuItem("Assets/" + MenuRoot + "/创建 UIItem 脚本并初始化绑定", false, 12)]
        private static void CreateItemForPrefabAsset()
        {
            CreateForPrefabAsset(UIType.Item);
        }

        /// <summary>
        /// 校验 Project 面板选择并进入 Prefab Asset 初始化主流程。
        /// 这里只接受真正的 Prefab Asset，不处理普通 GameObject 或其他资源类型。
        /// </summary>
        private static void CreateForPrefabAsset(UIType uiType)
        {
            GameObject prefabAsset = Selection.activeObject as GameObject;
            if (prefabAsset == null)
            {
                EditorUtility.DisplayDialog("提示", "请在 Project 面板中选择一个 Prefab。", "确定");
                return;
            }

            string prefabPath = AssetDatabase.GetAssetPath(prefabAsset);
            if (string.IsNullOrEmpty(prefabPath) || PrefabUtility.GetPrefabAssetType(prefabAsset) == PrefabAssetType.NotAPrefab)
            {
                EditorUtility.DisplayDialog("提示", "当前选择的对象不是 Prefab Asset。", "确定");
                return;
            }

            CreateScriptAndSetupPrefabAsset(prefabPath, uiType);
        }

        /// <summary>
        /// 仅在 Project 面板选中 Prefab Asset 时启用三类初始化菜单。
        /// </summary>
        [MenuItem("Assets/" + MenuRoot + "/创建 UIForm 脚本并初始化绑定", true)]
        [MenuItem("Assets/" + MenuRoot + "/创建 UIWidget 脚本并初始化绑定", true)]
        [MenuItem("Assets/" + MenuRoot + "/创建 UIItem 脚本并初始化绑定", true)]
        private static bool ValidateCreateForPrefabAsset()
        {
            GameObject prefabAsset = Selection.activeObject as GameObject;
            if (prefabAsset == null)
                return false;

            return PrefabUtility.GetPrefabAssetType(prefabAsset) != PrefabAssetType.NotAPrefab;
        }

        #endregion

        #region 主流程：场景对象 / Prefab Stage 对象

        /// <summary>
        /// 对 Hierarchy 中的对象执行对应 UI 类型的脚本创建和组件初始化。
        /// 包括普通场景对象，以及 Prefab Stage 中打开的预制体对象。
        ///
        /// 场景对象通过 Undo API 添加和修改组件，使整次初始化可以撤销；
        /// Prefab 实例上的组件变更还会记录为实例覆盖。
        /// </summary>
        private static void CreateScriptAndSetupSceneObject(GameObject root, UIType uiType)
        {
            if (root == null)
                return;

            string defaultClassName = MakeSafeClassName(root.name, uiType);
            string scriptPath = AskScriptSavePath(defaultClassName, uiType);
            if (string.IsNullOrEmpty(scriptPath))
                return;

            string className = MakeSafeClassName(Path.GetFileNameWithoutExtension(scriptPath), uiType);

            if (!CreateScriptFile(scriptPath, className, uiType))
                return;

            MonoScript monoScript = LoadMonoScript(scriptPath);

            string undoTypeName = GetTypeName(uiType);
            Undo.RegisterFullObjectHierarchyUndo(root, $"Setup {undoTypeName} Binding");

            UIBindData bindData = GetOrAddComponent<UIBindData>(root);
            UIBindScriptHolder scriptHolder = GetOrAddComponent<UIBindScriptHolder>(root);

            if (scriptHolder != null)
            {
                Undo.RecordObject(scriptHolder, $"Set {undoTypeName} Script");
                scriptHolder.targetScript = monoScript;
                EditorUtility.SetDirty(scriptHolder);
            }

            if (bindData != null)
            {
                Undo.RecordObject(bindData, $"Set {undoTypeName} Script Reference");
                SetRuntimeScriptReference(bindData, monoScript, className, GeneratedScriptNamespace, DefaultAssemblyName);
                EditorUtility.SetDirty(bindData);
            }

            EditorUtility.SetDirty(root);

            /*
             * 如果当前对象属于 Prefab 实例或 Prefab Stage，尽量保存修改。
             */
            TryRecordPrefabInstanceModification(bindData);
            TryRecordPrefabInstanceModification(scriptHolder);

            RegisterPendingRuntimeScriptSync(bindData, scriptPath);

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[UIBind] {GetTypeName(uiType)} 初始化完成: {root.name}, Script: {scriptPath}");
        }

        #endregion

        #region 主流程：Project Prefab Asset

        /// <summary>
        /// 对 Project 面板中的 Prefab Asset 执行对应 UI 类型的初始化。
        ///
        /// 注意：
        /// Prefab Asset 不能像场景对象一样直接通过 Undo.AddComponent 修改，
        /// 需要先用 LoadPrefabContents 打开隔离内容，完成组件配置后再保存并卸载。
        /// </summary>
        private static void CreateScriptAndSetupPrefabAsset(string prefabPath, UIType uiType)
        {
            if (string.IsNullOrEmpty(prefabPath))
                return;

            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabAsset == null)
            {
                EditorUtility.DisplayDialog("错误", "无法加载 Prefab Asset。", "确定");
                return;
            }

            string defaultClassName = MakeSafeClassName(prefabAsset.name, uiType);
            string scriptPath = AskScriptSavePath(defaultClassName, uiType);
            if (string.IsNullOrEmpty(scriptPath))
                return;

            string className = MakeSafeClassName(Path.GetFileNameWithoutExtension(scriptPath), uiType);

            if (!CreateScriptFile(scriptPath, className, uiType))
                return;

            MonoScript monoScript = LoadMonoScript(scriptPath);

            GameObject prefabRoot = null;

            try
            {
                prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
                if (prefabRoot == null)
                {
                    EditorUtility.DisplayDialog("错误", "无法加载 Prefab 内容。", "确定");
                    return;
                }

                UIBindData bindData = prefabRoot.GetComponent<UIBindData>();
                if (bindData == null)
                    bindData = prefabRoot.AddComponent<UIBindData>();

                UIBindScriptHolder scriptHolder = prefabRoot.GetComponent<UIBindScriptHolder>();
                if (scriptHolder == null)
                    scriptHolder = prefabRoot.AddComponent<UIBindScriptHolder>();

                if (scriptHolder != null)
                {
                    scriptHolder.targetScript = monoScript;
                    EditorUtility.SetDirty(scriptHolder);
                }

                if (bindData != null)
                {
                    SetRuntimeScriptReference(bindData, monoScript, className, GeneratedScriptNamespace, DefaultAssemblyName);
                    EditorUtility.SetDirty(bindData);
                }

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);

                /*
                 * 保存后重新拿 Prefab Asset 上的 UIBindData，用于注册编译后的二次同步。
                 */
                GameObject savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                UIBindData savedBindData = savedPrefab != null ? savedPrefab.GetComponent<UIBindData>() : null;
                RegisterPendingRuntimeScriptSync(savedBindData, scriptPath);

                Selection.activeObject = savedPrefab;
                EditorGUIUtility.PingObject(savedPrefab);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log($"[UIBind] Prefab {GetTypeName(uiType)} 初始化完成: {prefabPath}, Script: {scriptPath}");
            }
            finally
            {
                if (prefabRoot != null)
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        #endregion

        #region 创建脚本

        /// <summary>
        /// 弹出脚本保存面板，让用户选择对应 UI 类型的脚本保存目录。
        /// 保存文件名将再次经过类名清理，因此用户可以在面板中修改默认名称。
        /// </summary>
        private static string AskScriptSavePath(string defaultClassName, UIType uiType)
        {
            string typeName = GetTypeName(uiType);
            string path = EditorUtility.SaveFilePanelInProject(
                $"创建 {typeName} 脚本",
                defaultClassName + ".cs",
                "cs",
                $"请选择 {typeName} 脚本保存位置。"
            );

            if (string.IsNullOrEmpty(path))
                return string.Empty;

            if (!path.StartsWith("Assets/", StringComparison.Ordinal))
            {
                EditorUtility.DisplayDialog("错误", "脚本必须保存到 Assets 目录下。", "确定");
                return string.Empty;
            }

            if (File.Exists(path))
            {
                bool overwrite = EditorUtility.DisplayDialog(
                    "文件已存在",
                    $"脚本已存在：\n{path}\n\n是否覆盖？",
                    "覆盖",
                    "取消"
                );

                if (!overwrite)
                    return string.Empty;
            }

            return path;
        }

        /// <summary>
        /// 创建 UIForm、UIWidget 或 UIItem 脚本文件并立即导入 AssetDatabase。
        /// 文件采用无 BOM UTF-8，保持生成代码编码稳定。
        /// </summary>
        private static bool CreateScriptFile(string assetPath, string className, UIType uiType)
        {
            try
            {
                string fullPath = AssetPathToFullPath(assetPath);
                string directory = Path.GetDirectoryName(fullPath);

                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                string content = GenerateScriptContent(className, uiType);
                File.WriteAllText(fullPath, content, new UTF8Encoding(false));

                AssetDatabase.ImportAsset(assetPath);
                AssetDatabase.Refresh();

                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("错误", "创建脚本失败，请查看 Console。", "确定");
                return false;
            }
        }

        /// <summary>
        /// 生成对应 UI 类型的最小业务脚本内容。
        /// 仅声明 partial 类和正确基类，绑定字段由后续 UI Bind 代码生成器负责。
        /// </summary>
        private static string GenerateScriptContent(string className, UIType uiType)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("using UnityEngine;");

            if (!string.IsNullOrEmpty(GeneratedScriptUsingNamespace))
                sb.AppendLine("using " + GeneratedScriptUsingNamespace + ";");

            sb.AppendLine();

            string typeName = GetTypeName(uiType);
            if (!string.IsNullOrEmpty(GeneratedScriptNamespace))
            {
                sb.AppendLine("namespace " + GeneratedScriptNamespace);
                sb.AppendLine("{");
                sb.AppendLine("    public partial class " + className + " : " + typeName);
                sb.AppendLine("    {");
                sb.AppendLine("    }");
                sb.AppendLine("}");
            }
            else
            {
                sb.AppendLine("public partial class " + className + " : " + typeName);
                sb.AppendLine("{");
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 返回生成脚本使用的运行时基类名称。
        /// 该名称也用于菜单提示、日志、默认类名和 Undo 操作名称。
        /// </summary>
        private static string GetTypeName(UIType uiType)
        {
            switch (uiType)
            {
                case UIType.Widget:
                    return "UIWidget";
                case UIType.Item:
                    return "UIItem";
                default:
                    return "UIForm";
            }
        }

        /// <summary>
        /// 加载刚创建并导入的 MonoScript。
        /// 首次加载时脚本可能尚未完成编译，因此允许 GetClass() 暂时返回 null，
        /// 后续由 pending sync 在域重载后补全真实类型信息。
        /// </summary>
        private static MonoScript LoadMonoScript(string scriptPath)
        {
            AssetDatabase.ImportAsset(scriptPath);
            AssetDatabase.Refresh();

            MonoScript monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
            if (monoScript == null)
            {
                Debug.LogWarning("[UIBind] MonoScript 暂时加载失败，可能需要等待 Unity 编译完成: " + scriptPath);
            }

            return monoScript;
        }

        #endregion

        #region 组件初始化

        /// <summary>
        /// 获取或添加组件。
        /// 场景对象用 Undo.AddComponent，方便撤销。
        /// Prefab Asset LoadPrefabContents 出来的对象不走这里。
        /// </summary>
        private static T GetOrAddComponent<T>(GameObject gameObject) where T : Component
        {
            if (gameObject == null)
                return null;

            T component = gameObject.GetComponent<T>();
            if (component != null)
                return component;

            return Undo.AddComponent<T>(gameObject);
        }

        /// <summary>
        /// 设置 UIBindData 中运行时脚本引用信息。
        ///
        /// 优先使用 MonoScript.GetClass() 的真实类型信息。
        /// 如果刚创建脚本后 Unity 还没编译完成，则使用预估信息先写入。
        /// 编译完成后会通过 pending sync 再尝试同步一次真实信息。
        /// </summary>
        private static void SetRuntimeScriptReference(
            UIBindData bindData,
            MonoScript monoScript,
            string fallbackClassName,
            string fallbackNamespace,
            string fallbackAssemblyName)
        {
            if (bindData == null)
                return;

            string className = fallbackClassName;
            string namespaceName = fallbackNamespace;
            string assemblyName = fallbackAssemblyName;

            if (monoScript != null)
            {
                Type type = monoScript.GetClass();
                if (type != null)
                {
                    className = type.Name;
                    namespaceName = type.Namespace;
                    assemblyName = type.Assembly.GetName().Name;
                }
            }

            MonoScriptReference scriptReference = new MonoScriptReference
            {
                className = className,
                namespaceName = namespaceName,
                assemblyName = assemblyName
            };

            bindData.SetTargetScript(scriptReference);
        }

        /// <summary>
        /// 记录 Prefab 实例上的组件属性修改，使绑定信息能够作为实例覆盖被 Unity 保存。
        /// 对普通场景对象或 Prefab Stage 对象调用不会改变初始化语义。
        /// </summary>
        private static void TryRecordPrefabInstanceModification(UnityEngine.Object obj)
        {
            if (obj == null)
                return;

            PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
        }

        #endregion

        #region 编译后再次同步脚本真实程序集信息

        /// <summary>
        /// 注册一个待同步任务。
        ///
        /// 原因：
        /// 新脚本创建后，Unity 通常会触发编译。
        /// 在编译完成前，MonoScript.GetClass() 可能返回 null。
        /// 所以这里先把目标 UIBindData 和脚本路径记到 SessionState。
        /// 编译完成 / 域重载后，再尝试用真实 Type 信息同步一次。
        /// </summary>
        private static void RegisterPendingRuntimeScriptSync(UIBindData bindData, string scriptPath)
        {
            if (bindData == null || string.IsNullOrEmpty(scriptPath))
                return;

            string globalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(bindData).ToString();

            SessionState.SetString(PendingGlobalObjectIdKey, globalObjectId);
            SessionState.SetString(PendingScriptPathKey, scriptPath);
        }

        [DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            EditorApplication.delayCall += TryConsumePendingRuntimeScriptSync;
        }

        [InitializeOnLoadMethod]
        private static void OnEditorLoaded()
        {
            EditorApplication.delayCall += TryConsumePendingRuntimeScriptSync;
        }

        /// <summary>
        /// 脚本编译完成后，尝试重新读取 MonoScript.GetClass()，
        /// 并把真实 namespace / assemblyName 写入 UIBindData。
        /// </summary>
        private static void TryConsumePendingRuntimeScriptSync()
        {
            string globalObjectIdString = SessionState.GetString(PendingGlobalObjectIdKey, string.Empty);
            string scriptPath = SessionState.GetString(PendingScriptPathKey, string.Empty);

            if (string.IsNullOrEmpty(globalObjectIdString) || string.IsNullOrEmpty(scriptPath))
                return;

            MonoScript monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
            if (monoScript == null)
                return;

            Type type = monoScript.GetClass();

            /*
             * 如果脚本还没编译好，保留 pending，下次再试。
             */
            if (type == null)
                return;

            if (!GlobalObjectId.TryParse(globalObjectIdString, out GlobalObjectId globalObjectId))
            {
                ClearPendingRuntimeScriptSync();
                return;
            }

            UnityEngine.Object targetObject = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalObjectId);
            UIBindData bindData = targetObject as UIBindData;

            if (bindData == null)
            {
                ClearPendingRuntimeScriptSync();
                return;
            }

            Undo.RecordObject(bindData, "Sync UI Script Reference After Compile");

            MonoScriptReference scriptReference = new MonoScriptReference
            {
                className = type.Name,
                namespaceName = type.Namespace,
                assemblyName = type.Assembly.GetName().Name
            };

            bindData.SetTargetScript(scriptReference);
            EditorUtility.SetDirty(bindData);

            UIBindScriptHolder scriptHolder = bindData.GetComponent<UIBindScriptHolder>();
            if (scriptHolder != null && scriptHolder.targetScript != monoScript)
            {
                Undo.RecordObject(scriptHolder, "Sync UI MonoScript After Compile");
                scriptHolder.targetScript = monoScript;
                EditorUtility.SetDirty(scriptHolder);
            }

            AssetDatabase.SaveAssets();
            ClearPendingRuntimeScriptSync();

            Debug.Log($"[UIBind] 已在脚本编译后同步 UI 脚本信息: {type.FullName}, Assembly: {type.Assembly.GetName().Name}");
        }

        /// <summary>
        /// 清除已经完成或无法继续处理的编译后同步任务。
        /// </summary>
        private static void ClearPendingRuntimeScriptSync()
        {
            SessionState.EraseString(PendingGlobalObjectIdKey);
            SessionState.EraseString(PendingScriptPathKey);
        }

        #endregion

        #region 路径和类名辅助

        /// <summary>
        /// 把 Assets 相对路径转换为当前 Unity 工程中的绝对文件系统路径。
        /// </summary>
        private static string AssetPathToFullPath(string assetPath)
        {
            string projectPath = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectPath, assetPath);
        }

        /// <summary>
        /// 把 GameObject 或文件名转换成合法 C# 类名。
        /// 空名称按目标 UI 类型回退为 NewUIForm、NewUIWidget 或 NewUIItem。
        /// </summary>
        private static string MakeSafeClassName(string rawName, UIType uiType)
        {
            string fallbackName = "New" + GetTypeName(uiType);
            if (string.IsNullOrEmpty(rawName))
                return fallbackName;

            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < rawName.Length; i++)
            {
                char c = rawName[i];

                if (char.IsLetterOrDigit(c) || c == '_')
                    sb.Append(c);
                else
                    sb.Append('_');
            }

            string className = sb.ToString();

            while (className.Contains("__"))
                className = className.Replace("__", "_");

            className = className.Trim('_');

            if (string.IsNullOrEmpty(className))
                className = fallbackName;

            if (char.IsDigit(className[0]))
                className = "UI" + className;

            if (IsCSharpKeyword(className))
                className = className + "UI";

            return className;
        }

        /// <summary>
        /// 判断清理后的类名是否仍与 C# 保留关键字冲突。
        /// </summary>
        private static bool IsCSharpKeyword(string value)
        {
            return CSharpKeywords.Contains(value);
        }

        private static readonly HashSet<string> CSharpKeywords = new HashSet<string>
        {
            "abstract",
            "as",
            "base",
            "bool",
            "break",
            "byte",
            "case",
            "catch",
            "char",
            "checked",
            "class",
            "const",
            "continue",
            "decimal",
            "default",
            "delegate",
            "do",
            "double",
            "else",
            "enum",
            "event",
            "explicit",
            "extern",
            "false",
            "finally",
            "fixed",
            "float",
            "for",
            "foreach",
            "goto",
            "if",
            "implicit",
            "in",
            "int",
            "interface",
            "internal",
            "is",
            "lock",
            "long",
            "namespace",
            "new",
            "null",
            "object",
            "operator",
            "out",
            "override",
            "params",
            "private",
            "protected",
            "public",
            "readonly",
            "ref",
            "return",
            "sbyte",
            "sealed",
            "short",
            "sizeof",
            "stackalloc",
            "static",
            "string",
            "struct",
            "switch",
            "this",
            "throw",
            "true",
            "try",
            "typeof",
            "uint",
            "ulong",
            "unchecked",
            "unsafe",
            "ushort",
            "using",
            "virtual",
            "void",
            "volatile",
            "while"
        };

        #endregion
    }
}
#endif