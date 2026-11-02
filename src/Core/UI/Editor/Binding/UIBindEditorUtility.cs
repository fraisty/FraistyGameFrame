#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FRFrame.Editor
{
    public static class UIBindEditorUtility
    {
        public static readonly Dictionary<Type, string> TypePrefixMap = new Dictionary<Type, string>
        {
            { typeof(GameObject), "_go" },
            { typeof(Transform), "_tf" },
            { typeof(RectTransform), "_rect" },
            { typeof(Button), "_btn" },
            { typeof(Image), "_img" },
            { typeof(RawImage), "_raw" },
            { typeof(ScrollRect), "_scroll" },
            { typeof(Scrollbar), "_scrollbar" },
            { typeof(Toggle), "_toggle" },
            { typeof(Slider), "_slider" },
            { typeof(InputField), "_input" },
            { typeof(TMP_InputField), "_input" },
            { typeof(TextMeshProUGUI), "_txt" },
            { typeof(Text), "_txt" },
            { typeof(CanvasGroup), "_canvasGroup" },
            { typeof(UnityEngine.UI.Outline), "_outline" },
        };

        public static readonly Type[] DefaultBindableTypes =
        {
            typeof(GameObject),
            typeof(Transform),
            typeof(RectTransform),
            typeof(Button),
            typeof(Image),
            typeof(RawImage),
            typeof(ScrollRect),
            typeof(Scrollbar),
            typeof(Toggle),
            typeof(Slider),
            typeof(InputField),
            typeof(TMP_InputField),
            typeof(TextMeshProUGUI),
            typeof(Text),
            typeof(CanvasGroup),
            typeof(UnityEngine.UI.Outline),
        };

        private static readonly HashSet<Type> IgnoredComponentTypes = new HashSet<Type>
        {
            typeof(CanvasRenderer),
        };

        private static readonly Dictionary<Type, string[]> NameAffixMap = new Dictionary<Type, string[]>
        {
            { typeof(GameObject), new[] { "go", "gameobject", "obj" } },
            { typeof(Transform), new[] { "tf", "transform" } },
            { typeof(RectTransform), new[] { "rect", "recttransform" } },
            { typeof(Button), new[] { "btn", "button" } },
            { typeof(Image), new[] { "img", "image" } },
            { typeof(RawImage), new[] { "raw", "rawimage" } },
            { typeof(ScrollRect), new[] { "scroll", "scrollrect" } },
            { typeof(Scrollbar), new[] { "scrollbar" } },
            { typeof(Toggle), new[] { "toggle", "tog" } },
            { typeof(Slider), new[] { "slider" } },
            { typeof(InputField), new[] { "input", "inputfield" } },
            { typeof(TMP_InputField), new[] { "input", "inputfield", "tmpinput", "tmpinputfield" } },
            { typeof(TextMeshProUGUI), new[] { "txt", "text", "tmp", "label" } },
            { typeof(Text), new[] { "txt", "text", "label" } },
            { typeof(CanvasGroup), new[] { "canvasgroup", "group" } },
        };

        /// <summary>
        /// 自动命名时向上关联的层级数量。
        /// 例如 2 表示语义名由"父节点名 + 自身节点名"组成，
        /// GridFrame/main_2/shadow 会生成 Main_2Shadow，而不是只有 Shadow，降低重名概率、语义更清晰。
        /// </summary>
        private const int DefaultNameLevelCount = 2;

        public static string BuildDefaultFieldName(GameObject target, Type type)
        {
            return BuildUniqueFieldName(target, type, null);
        }

        public static string BuildUniqueFieldName(GameObject target, Type type, IReadOnlyList<UIBindInfo> existingInfos)
        {
            return BuildUniqueFieldName(target, type, existingInfos, null);
        }

        /// <summary>
        /// 生成唯一字段名。带 root 时会关联多层节点名生成更具辨识度的语义名。
        /// root 为空时退化为仅使用自身节点名（兼容旧调用）。
        /// </summary>
        public static string BuildUniqueFieldName(GameObject target, Type type, IReadOnlyList<UIBindInfo> existingInfos, Transform root)
        {
            string prefix = "_com";
            if (type != null && TypePrefixMap.TryGetValue(type, out var p))
                prefix = p;

            string pureName = BuildMultiLevelPureName(target, type, root);

            if (string.IsNullOrEmpty(pureName))
                pureName = "Node";

            string baseName = prefix + pureName;
            if (existingInfos == null)
                return baseName;

            HashSet<string> used = new HashSet<string>();
            for (int i = 0; i < existingInfos.Count; i++)
            {
                UIBindInfo info = existingInfos[i];
                if (info != null && !string.IsNullOrEmpty(info.fieldName))
                    used.Add(info.fieldName);
            }

            return MakeUniqueAgainst(baseName, used);
        }

        /// <summary>
        /// 关联多层节点名生成语义名。
        ///
        /// 规则：
        /// 1. 从 target 自身向上取最多 DefaultNameLevelCount 层节点名（不含 root 本身）；
        /// 2. 只有最靠近控件的一层（自身）会去掉类型词缀（如 Shadow 上的 Image 不会重复带 Img），
        ///    上层节点名保持原样，保留层级语义（如 Main_2）；
        /// 3. 各层 Sanitize 后 UpperFirst，从上到下拼接：父 + 自身 → Main_2Shadow。
        /// </summary>
        private static string BuildMultiLevelPureName(GameObject target, Type type, Transform root)
        {
            if (target == null)
                return "Node";

            // 未显式传 root 时，自动向上找所属的 UIBindData 作为边界，
            // 避免把窗体根节点名也拼进每个字段。
            if (root == null)
            {
                UIBindData owner = target.GetComponentInParent<UIBindData>();
                if (owner != null)
                    root = owner.transform;
            }

            // 从自身向上收集节点名（自身在前）。
            List<string> segments = new List<string>();
            Transform current = target.transform;

            while (current != null && segments.Count < DefaultNameLevelCount)
            {
                // 到达根节点则停止：根节点名一般是窗体名，不需要带进每个字段。
                if (root != null && current == root)
                    break;

                bool isSelf = segments.Count == 0;

                string seg = SanitizeName(current.name);
                // 只有自身层去类型词缀，上层保留原名以保留层级语义。
                if (isSelf)
                    seg = StripTypeAffix(seg, type);

                if (!string.IsNullOrEmpty(seg))
                    segments.Add(UpperFirst(seg));

                current = current.parent;
            }

            if (segments.Count == 0)
                return "Node";

            // segments 是"自身 → 父"顺序，拼接时反转成"父 → 自身"。
            StringBuilder sb = new StringBuilder();
            for (int i = segments.Count - 1; i >= 0; i--)
                sb.Append(segments[i]);

            return sb.ToString();
        }

        public static string SanitizeName(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "Node";

            string s = input.Replace(" ", string.Empty)
                .Replace("-", string.Empty)
                .Replace(".", string.Empty)
                .Replace("(", string.Empty)
                .Replace(")", string.Empty)
                .Replace("/", string.Empty)
                .Replace("\\", string.Empty)
                .Replace("[", string.Empty)
                .Replace("]", string.Empty)
                .Replace("{", string.Empty)
                .Replace("}", string.Empty);

            if (string.IsNullOrEmpty(s))
                return "Node";

            if (char.IsDigit(s[0]))
                s = "Node" + s;

            return s;
        }

        private static string StripTypeAffix(string value, Type type)
        {
            if (string.IsNullOrEmpty(value) || type == null)
                return value;

            if (!NameAffixMap.TryGetValue(type, out string[] affixes))
                return value;

            for (int i = 0; i < affixes.Length; i++)
            {
                string affix = affixes[i];

                if (StartsWithIgnoreCase(value, affix) && value.Length > affix.Length)
                {
                    string stripped = value.Substring(affix.Length);
                    if (!string.IsNullOrEmpty(stripped))
                        return UpperFirst(stripped);
                }

                if (EndsWithIgnoreCase(value, affix) && value.Length > affix.Length)
                {
                    string stripped = value.Substring(0, value.Length - affix.Length);
                    if (!string.IsNullOrEmpty(stripped))
                        return UpperFirst(stripped);
                }
            }

            return value;
        }

        private static bool StartsWithIgnoreCase(string value, string prefix)
        {
            return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool EndsWithIgnoreCase(string value, string suffix)
        {
            return value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        private static string UpperFirst(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            if (value.Length == 1)
                return value.ToUpperInvariant();

            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        public static Type GuessBindableType(GameObject target)
        {
            if (target == null)
                return typeof(GameObject);

            if (target.GetComponent<Button>() != null) return typeof(Button);
            if (target.GetComponent<TMP_InputField>() != null) return typeof(TMP_InputField);
            if (target.GetComponent<InputField>() != null) return typeof(InputField);
            if (target.GetComponent<TextMeshProUGUI>() != null) return typeof(TextMeshProUGUI);
            if (target.GetComponent<Text>() != null) return typeof(Text);
            if (target.GetComponent<Image>() != null) return typeof(Image);
            if (target.GetComponent<RawImage>() != null) return typeof(RawImage);
            if (target.GetComponent<ScrollRect>() != null) return typeof(ScrollRect);
            if (target.GetComponent<Scrollbar>() != null) return typeof(Scrollbar);
            if (target.GetComponent<Toggle>() != null) return typeof(Toggle);
            if (target.GetComponent<Slider>() != null) return typeof(Slider);
            if (target.GetComponent<CanvasGroup>() != null) return typeof(CanvasGroup);

            if (target.transform is RectTransform) return typeof(RectTransform);
            return typeof(GameObject);
        }

        public static Component ResolveComponent(GameObject target, Type bindType)
        {
            if (target == null || bindType == null)
                return null;

            if (bindType == typeof(GameObject))
                return null;

            if (bindType == typeof(Transform) || bindType == typeof(RectTransform))
                return target.transform;

            return target.GetComponent(bindType);
        }

        public static bool CanBindType(Type type)
        {
            if (type == null)
                return false;

            if (type == typeof(GameObject))
                return true;

            if (!typeof(Component).IsAssignableFrom(type))
                return false;

            return !IgnoredComponentTypes.Contains(type);
        }

        public static List<Type> GetBindableTypes(GameObject go)
        {
            List<Type> result = new List<Type>();
            if (go == null)
                return result;

            AddUnique(result, typeof(GameObject));

            if (go.transform is RectTransform)
                AddUnique(result, typeof(RectTransform));
            else
                AddUnique(result, typeof(Transform));

            Component[] components = go.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null)
                    continue;

                Type type = component.GetType();
                if (!CanBindType(type))
                    continue;

                if (type == typeof(Transform) || type == typeof(RectTransform))
                    continue;

                AddUnique(result, type);
            }

            return result;
        }

        private static void AddUnique(List<Type> list, Type type)
        {
            if (type == null || list.Contains(type))
                return;

            list.Add(type);
        }

        public static bool IsAlreadyBound(UIBindData bindData, GameObject target, Type type)
        {
            if (bindData == null || target == null || type == null || bindData.BindInfos == null)
                return false;

            string typeName = type.FullName;
            for (int i = 0; i < bindData.BindInfos.Count; i++)
            {
                UIBindInfo info = bindData.BindInfos[i];
                if (info == null || info.isWidget)
                    continue;

                if (info.target == target && info.typeName == typeName)
                    return true;
            }

            return false;
        }

        public static List<UIBindInfo> GetBindInfosOnTarget(UIBindData bindData, GameObject target)
        {
            List<UIBindInfo> result = new List<UIBindInfo>();
            if (bindData == null || target == null || bindData.BindInfos == null)
                return result;

            for (int i = 0; i < bindData.BindInfos.Count; i++)
            {
                UIBindInfo info = bindData.BindInfos[i];
                if (info != null && info.target == target)
                    result.Add(info);
            }

            return result;
        }

        public static string GetTransformPath(Transform root, Transform target)
        {
            if (root == null || target == null)
                return string.Empty;

            if (root == target)
                return string.Empty;

            List<string> names = new List<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                names.Add(current.name);
                current = current.parent;
            }

            names.Reverse();
            return string.Join("/", names);
        }

        public static string GetCompactTypeName(Type type)
        {
            if (type == null)
                return "Missing";

            if (type == typeof(GameObject)) return "GameObject";
            if (type == typeof(RectTransform)) return "Rect";
            if (type == typeof(Transform)) return "Transform";
            if (type == typeof(TextMeshProUGUI)) return "TMP_Text";
            if (type == typeof(TMP_InputField)) return "TMP_Input";

            return type.Name;
        }

        public static int GetTypeSortOrder(Type type)
        {
            if (type == null)
                return 9999;

            for (int i = 0; i < DefaultBindableTypes.Length; i++)
            {
                if (DefaultBindableTypes[i] == type)
                    return i;
            }

            return 1000;
        }

        public static void SortBindInfos(List<UIBindInfo> bindInfos, UIBindData bindData)
        {
            if (bindInfos == null)
                return;

            UIBindSortMode sortMode = bindData != null ? bindData.SortMode : UIBindSortMode.Path;
            Transform root = bindData != null ? bindData.transform : null;
            bindInfos.Sort((a, b) => CompareBindInfo(a, b, sortMode, root));
        }

        public static int CompareBindInfo(UIBindInfo a, UIBindInfo b, UIBindSortMode sortMode, Transform root)
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;

            int targetCompare = sortMode == UIBindSortMode.Hierarchy
                ? CompareHierarchyOrder(root, a.target != null ? a.target.transform : null, b.target != null ? b.target.transform : null)
                : string.Compare(a.path, b.path, StringComparison.Ordinal);
            if (targetCompare != 0)
                return targetCompare;

            Type typeA = a.isWidget ? FindType(a.widgetTypeName) : FindType(a.typeName);
            Type typeB = b.isWidget ? FindType(b.widgetTypeName) : FindType(b.typeName);
            int orderCompare = GetTypeSortOrder(typeA).CompareTo(GetTypeSortOrder(typeB));
            if (orderCompare != 0)
                return orderCompare;

            return string.Compare(a.fieldName, b.fieldName, StringComparison.Ordinal);
        }

        private static int CompareHierarchyOrder(Transform root, Transform a, Transform b)
        {
            if (a == b) return 0;
            if (a == null) return 1;
            if (b == null) return -1;

            List<int> orderA = GetHierarchyOrder(root, a);
            List<int> orderB = GetHierarchyOrder(root, b);
            int count = Math.Min(orderA.Count, orderB.Count);
            for (int i = 0; i < count; i++)
            {
                int compare = orderA[i].CompareTo(orderB[i]);
                if (compare != 0)
                    return compare;
            }

            return orderA.Count.CompareTo(orderB.Count);
        }

        private static List<int> GetHierarchyOrder(Transform root, Transform target)
        {
            List<int> order = new List<int>();
            Transform current = target;
            while (current != null && current != root)
            {
                order.Add(current.GetSiblingIndex());
                current = current.parent;
            }

            if (current != root)
                return new List<int> { int.MaxValue };

            order.Reverse();
            return order;
        }

        /// <summary>
        /// 类型查找缓存。
        ///
        /// FindType 在重建列表、排序、校验、代码生成时被高频调用，
        /// 100+ 绑定时如果每次都遍历所有已加载程序集会明显拖慢编辑器。
        /// 这里用字典缓存 typeName -> Type 的结果（包含找不到的 null 结果，避免重复失败查找）。
        /// </summary>
        private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>();

        public static Type FindType(string fullTypeName)
        {
            if (string.IsNullOrEmpty(fullTypeName))
                return null;

            // 命中缓存直接返回（null 也会被缓存，代表确实找不到）。
            if (TypeCache.TryGetValue(fullTypeName, out Type cached))
                return cached;

            Type type = Type.GetType(fullTypeName);
            if (type == null)
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length; i++)
                {
                    type = assemblies[i].GetType(fullTypeName);
                    if (type != null)
                        break;
                }
            }

            TypeCache[fullTypeName] = type;
            return type;
        }

        /// <summary>
        /// 清空类型缓存。
        /// 脚本重编译后类型可能变化，交由 InitializeOnLoad 时机调用一次即可。
        /// </summary>
        public static void ClearTypeCache()
        {
            TypeCache.Clear();
        }

        /// <summary>
        /// 按"节点名 + 类型规则"为单条绑定重新生成字段名，忽略当前已有的名字。
        /// used 用于跨多条绑定去重，调用方需要把已确定的新名字加入其中。
        /// </summary>
        public static string BuildRuleFieldName(UIBindInfo info, HashSet<string> used)
        {
            return BuildRuleFieldName(info, used, null);
        }

        /// <summary>
        /// 按"节点名 + 类型规则"为单条绑定重新生成字段名（关联多层节点名），带 root 时限定层级边界。
        /// </summary>
        public static string BuildRuleFieldName(UIBindInfo info, HashSet<string> used, Transform root)
        {
            if (info == null)
                return string.Empty;

            Type type = info.isWidget ? FindType(info.widgetTypeName) : FindType(info.typeName);

            string prefix = "_com";
            if (!info.isWidget && type != null && TypePrefixMap.TryGetValue(type, out var p))
                prefix = p;
            else if (info.isWidget)
                prefix = "_widget";

            // Widget 传 null 类型，避免把 Widget 类名当作 UGUI 类型词缀去剥离。
            string pureName = BuildMultiLevelPureName(info.target, info.isWidget ? null : type, root);

            if (string.IsNullOrEmpty(pureName))
                pureName = "Node";

            string baseName = prefix + pureName;
            return MakeUniqueAgainst(baseName, used);
        }

        /// <summary>
        /// 在 used 集合内让 baseName 唯一：已存在则依次追加 2、3、4……
        /// 不会把返回值写入 used，是否登记由调用方决定。
        /// </summary>
        public static string MakeUniqueAgainst(string baseName, HashSet<string> used)
        {
            if (string.IsNullOrEmpty(baseName))
                baseName = "_field";

            if (used == null || !used.Contains(baseName))
                return baseName;

            int index = 2;
            string candidate;
            do
            {
                candidate = baseName + index;
                index++;
            } while (used.Contains(candidate));

            return candidate;
        }

        #region 批量重命名

        /// <summary>
        /// 批量重命名的操作类型。
        /// </summary>
        public enum BatchRenameMode
        {
            /// <summary>加前缀。</summary>
            AddPrefix = 0,
            /// <summary>加后缀。</summary>
            AddSuffix = 1,
            /// <summary>普通文本查找替换。</summary>
            Replace = 2,
            /// <summary>正则查找替换。</summary>
            RegexReplace = 3,
            /// <summary>按"节点名 + 类型规则"重新生成。</summary>
            RebuildByRule = 4
        }

        /// <summary>
        /// 计算单个原名在给定模式下的目标名（不含去重）。
        /// info 仅在 RebuildByRule 模式下需要，其余模式可传 null。
        /// </summary>
        public static string EvaluateBatchRename(
            string original,
            BatchRenameMode mode,
            string argA,
            string argB,
            UIBindInfo info,
            HashSet<string> usedForRule)
        {
            original ??= string.Empty;
            argA ??= string.Empty;
            argB ??= string.Empty;

            switch (mode)
            {
                case BatchRenameMode.AddPrefix:
                    return argA + original;

                case BatchRenameMode.AddSuffix:
                    return original + argA;

                case BatchRenameMode.Replace:
                    if (string.IsNullOrEmpty(argA))
                        return original;
                    return original.Replace(argA, argB);

                case BatchRenameMode.RegexReplace:
                    if (string.IsNullOrEmpty(argA))
                        return original;
                    try
                    {
                        return System.Text.RegularExpressions.Regex.Replace(original, argA, argB);
                    }
                    catch (Exception)
                    {
                        // 正则不合法时保持原名，交由 UI 层提示。
                        return original;
                    }

                case BatchRenameMode.RebuildByRule:
                    return BuildRuleFieldName(info, usedForRule);

                default:
                    return original;
            }
        }

        /// <summary>
        /// 校验正则是否可编译，供 UI 层在应用前提示。
        /// </summary>
        public static bool IsValidRegex(string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return false;

            try
            {
                _ = System.Text.RegularExpressions.Regex.Match(string.Empty, pattern);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        #endregion

        #region 字段名前缀拆分

        /// <summary>
        /// 获取字段名里"类型前缀"部分。
        ///
        /// 自动命名规则为 前缀 + 语义名，例如 _goBg_fleet 中：
        /// _go 是 GameObject 的类型前缀（由 TypePrefixMap 决定），Bg_fleet 是根据节点关系生成的语义名。
        /// 大多数改名场景只需要改语义名，前缀应保持不动。
        ///
        /// 返回值：
        /// 若字段名确实以该绑定对应类型的前缀开头，则返回该前缀；
        /// 否则返回空串，表示识别不到前缀，应对整个字段名进行编辑。
        /// </summary>
        public static string GetFieldNamePrefix(UIBindInfo info)
        {
            if (info == null)
                return string.Empty;

            return GetFieldNamePrefix(info.fieldName, info.isWidget, info.typeName);
        }

        /// <summary>
        /// 基于字段名与类型信息识别前缀。供只有 SerializedProperty、拿不到 UIBindInfo 实例的场景使用。
        /// </summary>
        public static string GetFieldNamePrefix(string fieldName, bool isWidget, string typeName)
        {
            if (string.IsNullOrEmpty(fieldName))
                return string.Empty;

            // 收集该绑定可能使用的前缀候选。
            List<string> candidates = new List<string>();

            if (isWidget)
            {
                candidates.Add("_widget");
                candidates.Add("_com");
            }
            else
            {
                Type type = FindType(typeName);
                if (type != null && TypePrefixMap.TryGetValue(type, out var p))
                    candidates.Add(p);

                candidates.Add("_com");
            }

            // 长前缀优先匹配，避免 _scroll 抢占 _scrollbar 这类包含关系。
            candidates.Sort((a, b) => b.Length.CompareTo(a.Length));

            for (int i = 0; i < candidates.Count; i++)
            {
                string prefix = candidates[i];
                if (fieldName.Length > prefix.Length &&
                    fieldName.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return prefix;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// 获取字段名去掉类型前缀后的语义部分。识别不到前缀时返回整个字段名。
        /// </summary>
        public static string GetFieldNameRemainder(UIBindInfo info)
        {
            if (info == null || string.IsNullOrEmpty(info.fieldName))
                return string.Empty;

            string prefix = GetFieldNamePrefix(info);
            if (string.IsNullOrEmpty(prefix))
                return info.fieldName;

            return info.fieldName.Substring(prefix.Length);
        }

        /// <summary>
        /// 字符串版本：获取去掉前缀后的语义部分。
        /// </summary>
        public static string GetFieldNameRemainder(string fieldName, bool isWidget, string typeName)
        {
            if (string.IsNullOrEmpty(fieldName))
                return string.Empty;

            string prefix = GetFieldNamePrefix(fieldName, isWidget, typeName);
            if (string.IsNullOrEmpty(prefix))
                return fieldName;

            return fieldName.Substring(prefix.Length);
        }

        /// <summary>
        /// 将前缀与语义名组合成完整字段名。
        /// </summary>
        public static string ComposeFieldName(string prefix, string remainder)
        {
            return (prefix ?? string.Empty) + (remainder ?? string.Empty);
        }

        #endregion

        #region 按规则重生成名称

        /// <summary>
        /// 对指定的一组绑定按"多层节点名 + 类型规则"重新生成字段名。
        ///
        /// targets 为要重命名的绑定；allInfos 为整份列表（用于把未选中项已占用的名字纳入去重）。
        /// root 为绑定根 transform（决定层级边界），可为空（内部会自动向上找 UIBindData）。
        /// 返回实际改名的数量。
        /// </summary>
        public static int RegenerateFieldNames(
            IReadOnlyList<UIBindInfo> targets,
            IReadOnlyList<UIBindInfo> allInfos,
            Transform root)
        {
            if (targets == null || targets.Count == 0)
                return 0;

            // used 先纳入"未被重命名的其它绑定"已占用的名字，避免重建结果与它们冲突。
            HashSet<string> targetSet = new HashSet<string>();
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] != null)
                    targetSet.Add(targets[i].fieldName ?? string.Empty);
            }

            HashSet<string> used = new HashSet<string>();
            if (allInfos != null)
            {
                for (int i = 0; i < allInfos.Count; i++)
                {
                    UIBindInfo info = allInfos[i];
                    if (info == null || string.IsNullOrEmpty(info.fieldName))
                        continue;

                    // 跳过将要被重命名的目标本身，它们的旧名字不占用。
                    bool isTarget = false;
                    for (int t = 0; t < targets.Count; t++)
                    {
                        if (ReferenceEquals(targets[t], info))
                        {
                            isTarget = true;
                            break;
                        }
                    }

                    if (!isTarget)
                        used.Add(info.fieldName);
                }
            }

            int renamed = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                UIBindInfo info = targets[i];
                if (info == null)
                    continue;

                string newName = BuildRuleFieldName(info, used, root);
                if (string.IsNullOrEmpty(newName))
                    continue;

                used.Add(newName);

                if (info.fieldName != newName)
                {
                    info.fieldName = newName;
                    renamed++;
                }
            }

            return renamed;
        }

        #endregion

        #region 重名消歧

        /// <summary>
        /// 为整份绑定列表消除重复字段名：保留首次出现，后续重复项依次追加序号。
        /// 空名字段跳过（交由校验流程单独提示）。返回被改名的数量。
        /// </summary>
        public static int DeduplicateFieldNames(IReadOnlyList<UIBindInfo> infos)
        {
            if (infos == null)
                return 0;

            HashSet<string> used = new HashSet<string>();
            int renamed = 0;

            for (int i = 0; i < infos.Count; i++)
            {
                UIBindInfo info = infos[i];
                if (info == null || string.IsNullOrEmpty(info.fieldName))
                    continue;

                if (used.Add(info.fieldName))
                    continue;

                string unique = MakeUniqueAgainst(info.fieldName, used);
                info.fieldName = unique;
                used.Add(unique);
                renamed++;
            }

            return renamed;
        }

        #endregion

        public static bool IsWidgetType(Type type)
        {
            return type != null && typeof(UIWidget).IsAssignableFrom(type);
        }

        public static bool IsFormType(Type type)
        {
            return type != null && typeof(UIForm).IsAssignableFrom(type);
        }
    }
}
#endif