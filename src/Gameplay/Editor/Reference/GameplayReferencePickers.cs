using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Editor.SkillAuthoring;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Gameplay.Editor
{
    internal sealed class GameplayReferencePickerPopup : PopupWindowContent
    {
        private readonly GameplayReferenceCatalogAsset _catalog;
        private readonly string _scope;
        private readonly GameplayReferenceKind _kind;
        private readonly Action<string> _choose;
        private readonly Dictionary<string, bool> _expanded = new Dictionary<string, bool>(StringComparer.Ordinal);
        private string _search = string.Empty;
        private Vector2 _scroll;

        internal GameplayReferencePickerPopup(GameplayReferenceCatalogAsset catalog, GameplayReferenceKind kind, string scope, Action<string> choose)
        { _catalog = catalog; _kind = kind; _scope = scope; _choose = choose; }
        public override Vector2 GetWindowSize() => new Vector2(390f, 370f);
        public override void OnGUI(Rect rect)
        {
            GUILayout.Label(_kind == GameplayReferenceKind.Tag ? "标签 · "+(_scope ?? "共享范围") : "已注册属性", EditorStyles.boldLabel);
            _search = EditorGUILayout.TextField("搜索", _search);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (_kind == GameplayReferenceKind.Tag)
            {
                var tags = GameplayReferenceCatalogProvider.Visible(_scope).SelectMany(catalog=>catalog.Tags).Distinct(StringComparer.Ordinal).ToArray();
                if (string.IsNullOrEmpty(_search))
                    foreach (var child in GameplayReferenceTree.Project(tags).Children.Values) DrawNode(child, 0);
                else foreach (string tag in tags.Where(x => x != null && x.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
                             .OrderBy(x => x, StringComparer.Ordinal)) DrawChoice(tag, tag, 0);
            }
            else foreach (string attribute in (_catalog.Attributes ?? Array.Empty<string>())
                         .Where(x => x != null && x.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
                         .OrderBy(x => x, StringComparer.Ordinal)) DrawChoice(attribute, attribute, 0);
            EditorGUILayout.EndScrollView();
            if (_kind == GameplayReferenceKind.Tag && GUILayout.Button("打开 Reference 管理"))
            { GameplayReferenceEditorWindow.OpenForScope(_scope); editorWindow.Close(); }
        }
        private void DrawNode(GameplayReferenceTreeNode node, int depth)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(depth * 13f);
            if (node.Children.Count > 0)
            {
                bool expanded = !_expanded.TryGetValue(node.FullPath, out bool value) || value;
                bool next = GUILayout.Toggle(expanded, expanded ? "▾" : "▸", EditorStyles.label, GUILayout.Width(14f));
                _expanded[node.FullPath] = next;
            }
            else GUILayout.Space(14f);
            if (node.IsDefined)
            {
                if (GUILayout.Button(new GUIContent(node.Segment, node.FullPath), EditorStyles.linkLabel)) Choose(node.FullPath);
            }
            else GUILayout.Label(new GUIContent(node.Segment + "（分组）", node.FullPath), EditorStyles.label);
            EditorGUILayout.EndHorizontal();
            if (node.Children.Count > 0 && _expanded[node.FullPath])
                foreach (var child in node.Children.Values) DrawNode(child, depth + 1);
        }
        private void DrawChoice(string label, string value, int depth)
        {
            EditorGUILayout.BeginHorizontal(); GUILayout.Space(depth * 13f);
            if (GUILayout.Button(new GUIContent(label, value), EditorStyles.linkLabel)) Choose(value);
            EditorGUILayout.EndHorizontal();
        }
        private void Choose(string value) { _choose(value); editorWindow.Close(); }
    }

    internal static class GameplayReferenceDrawerGUI
    {
        private const string TagFormatError = "标签名称格式错误";
        private const string TagCatalogWarning = "目录提示 · 未登记或当前 Scope 不可用";

        private static bool TagAvailable(IEnumerable<string> scopes, string tag) =>
            scopes.All(source => GameplayReferenceCatalogProvider.Visible(source).Any(owner => owner.Tags.Contains(tag,StringComparer.Ordinal)));

        private static string TagIssue(string value, IEnumerable<string> scopes)
        {
            if (string.IsNullOrEmpty(value)) return null;
            if (!GameplayReferenceDiagnostics.IsValid(GameplayReferenceKind.Tag, value)) return TagFormatError;
            return TagAvailable(scopes, value) ? null : TagCatalogWarning;
        }

        internal static bool HasTagIssue(SerializedProperty property)
        {
            if (!GameplayAuthoringInspector.InWorkbenchField) return false;
            var value = property.FindPropertyRelative("Name").stringValue;
            return TagIssue(value, property.serializedObject.targetObjects.Select(GameplayReferenceCatalogProvider.Scope).Distinct()) != null;
        }

        internal static void Draw(Rect position, SerializedProperty property, GUIContent label,
            GameplayReferenceKind kind, string childName)
        {
            SerializedProperty child = property.FindPropertyRelative(childName);
            if (child == null) { EditorGUI.LabelField(position, label.text, "缺少引用字段"); return; }
            EditorGUI.BeginProperty(position, label, property);
            Rect content = EditorGUI.PrefixLabel(position, label);
            float height = EditorGUIUtility.singleLineHeight;
            var raw = new Rect(content.x, content.y, Mathf.Max(50f, content.width - 83f), height);
            var pick = new Rect(raw.xMax + 3f, content.y, 48f, height);
            var clear = new Rect(pick.xMax + 3f, content.y, 29f, height);
            var catalog = GameplayReferenceCatalogProvider.Find(out string discoveryError);
            var targets = property.serializedObject.targetObjects;
            var scopes = kind == GameplayReferenceKind.Tag ? targets.Select(GameplayReferenceCatalogProvider.Scope).Distinct().ToArray() : Array.Empty<string>();
            string scope = scopes.Length == 1 ? scopes[0] : null;
            string value = child.stringValue;
            string issue = kind == GameplayReferenceKind.Tag ? TagIssue(value, scopes) :
                !string.IsNullOrEmpty(value) && (!GameplayReferenceDiagnostics.IsValid(kind, value) || catalog == null ||
                    !(catalog.Attributes ?? Array.Empty<string>()).Contains(value, StringComparer.Ordinal))
                    ? "当前 Scope 不可用或未登记。可在 Reference 检查归属。" : null;
            Color old = GUI.color;
            if (issue != null && !child.hasMultipleDifferentValues)
                GUI.color = old * (issue == TagCatalogWarning ? new Color(1f, .84f, .50f) : new Color(1f, .65f, .55f));
            EditorGUI.showMixedValue = child.hasMultipleDifferentValues;
            EditorGUI.PropertyField(raw, child, GUIContent.none);
            EditorGUI.showMixedValue = false;
            GUI.color = old;
            if (catalog == null) GUI.Label(pick, new GUIContent("!", discoveryError));
            else if (GUI.Button(pick, new GUIContent("选择", issue ?? value)))
            {
                string path=child.propertyPath;
                if (targets.Length == 1 && targets[0] is GameplaySkillDocumentAsset document)
                {
                    if (!GameplayInspectorOperations.Flush(property.serializedObject, document))
                    { EditorGUI.EndProperty(); return; }
                }
                else property.serializedObject.ApplyModifiedProperties();
                var handles=targets.Select(target=>GameplaySourceHandle.Capture(target,path)).ToArray();
                var sourceGuard=GameplaySkillFieldSchema.CurrentSourceGuard;
                PopupWindow.Show(pick, new GameplayReferencePickerPopup(catalog, kind, scope,
                    selected => {try{
                        GameplayInspectorOperations.ChooseReference(handles,kind,selected,sourceGuard);
                    }catch(InvalidOperationException e){EditorUtility.DisplayDialog("引用位置已变化",e.Message,"关闭");}}));
            }
            if (GUI.Button(clear, new GUIContent("×", "清空此引用"))) child.stringValue = string.Empty;
            if (issue != null && kind == GameplayReferenceKind.Tag && GameplayAuthoringInspector.InWorkbenchField)
                EditorGUI.LabelField(new Rect(position.x,position.y+height+EditorGUIUtility.standardVerticalSpacing,position.width,height),
                    issue,EditorStyles.miniBoldLabel);
            EditorGUI.EndProperty();
        }

    }

    [CustomPropertyDrawer(typeof(GameplayTagReference))]
    public sealed class GameplayTagReferenceDrawer : PropertyDrawer
    {
        private const int VisibleChoices = 6;
        private GameplaySkillDocumentAsset _openDocument;
        private string _openPath, _search = "";

        private static bool IsContextKey(SerializedProperty property, out bool producer)
        {
            var owner = GameplaySkillContextVariables.FieldOwner(property);
            producer = GameplaySkillContextVariables.IsProducer(owner);
            return producer || GameplaySkillContextVariables.IsReader(owner);
        }

        private bool IsOpen(GameplaySkillDocumentAsset document, string path) => _openDocument == document && _openPath == path;

        private (string Key, string Label)[] Filter((string Key, string Label)[] choices) => choices.Where(choice =>
            choice.Key.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0 ||
            choice.Label.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0).Take(VisibleChoices).ToArray();

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!IsContextKey(property, out bool producer))
                return base.GetPropertyHeight(property, label) + (GameplayReferenceDrawerGUI.HasTagIssue(property) ?
                    EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing : 0);
            int rows = 2;
            var document = (GameplaySkillDocumentAsset)property.serializedObject.targetObject;
            string path = property.propertyPath;
            if (!producer && IsOpen(document, path))
                rows += 1 + Math.Max(1, Filter(GameplaySkillContextVariables.For(document).Choices(path)).Length);
            return rows * EditorGUIUtility.singleLineHeight + (rows - 1) * EditorGUIUtility.standardVerticalSpacing;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (!IsContextKey(property, out bool producer))
            {
                GameplayReferenceDrawerGUI.Draw(position, property, label, GameplayReferenceKind.Tag, "Name");
                return;
            }
            var document = (GameplaySkillDocumentAsset)property.serializedObject.targetObject;
            var child = property.FindPropertyRelative("Name");
            string path = property.propertyPath;
            var variables = GameplaySkillContextVariables.For(document);
            string warning = producer
                ? (GameplayReferenceDiagnostics.IsValid(GameplayReferenceKind.Tag, child.stringValue) ? null : "请输入有效的 Context 键。")
                : variables.Warning(path, child.stringValue);
            float height = EditorGUIUtility.singleLineHeight;
            float step = height + EditorGUIUtility.standardVerticalSpacing;
            var row = new Rect(position.x, position.y, position.width, height);
            label = new GUIContent(producer ? "定义局部变量" : "Context 变量", "局部变量无需登记全局 Tag。");
            EditorGUI.BeginProperty(position, label, property);
            var input = EditorGUI.PrefixLabel(row, label);
            if (!producer) input.width -= 49f;
            child.stringValue = EditorGUI.DelayedTextField(input, child.stringValue);
            var choices = producer ? Array.Empty<(string Key, string Label)>() : variables.Choices(path);
            bool open = IsOpen(document, path);
            if (!producer)
            {
                var button = new Rect(input.xMax + 3f, input.y, 46f, height);
                bool changed = GUI.changed;
                using (new EditorGUI.DisabledScope(choices.Length == 0 && !open))
                    if (GUI.Button(button, new GUIContent(open ? "收起" : "选择", "搜索当前路径可读取的技能变量")))
                    {
                        _openDocument = open ? null : document;
                        _openPath = open ? null : path;
                        _search = "";
                    }
                GUI.changed = changed;
            }
            string caption = warning ?? (producer ? "技能局部定义 · 仅沿实际执行链传递" :
                choices.FirstOrDefault(choice => choice.Key == child.stringValue).Label);
            row.y += step;
            EditorGUI.LabelField(row,
                new GUIContent(caption, caption), warning == null ? EditorStyles.miniLabel : EditorStyles.boldLabel);
            if (open && !producer)
            {
                row.y += step;
                var visible = Filter(choices);
                bool changed = GUI.changed;
                _search = EditorGUI.TextField(row, new GUIContent("搜索变量", "输入名称或完整键过滤当前路径的变量"), _search);
                GUI.changed = changed;
                if (visible.Length == 0)
                {
                    row.y += step;
                    EditorGUI.LabelField(row, "没有匹配的可读变量", EditorStyles.miniLabel);
                }
                foreach (var choice in visible)
                {
                    row.y += step;
                    if (!GUI.Button(row, new GUIContent(choice.Label, choice.Key), EditorStyles.miniButton)) continue;
                    child.stringValue = choice.Key;
                    _openDocument = null;
                    _openPath = null;
                }
            }
            EditorGUI.EndProperty();
        }
    }

    [CustomPropertyDrawer(typeof(GameplayAttributeReference))]
    public sealed class GameplayAttributeReferenceDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) =>
            GameplayReferenceDrawerGUI.Draw(position, property, label, GameplayReferenceKind.Attribute, "Value");
    }
}
