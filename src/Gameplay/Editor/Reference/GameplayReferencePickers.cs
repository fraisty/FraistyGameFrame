using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Gameplay.Editor
{
    internal sealed class GameplayReferencePickerPopup : PopupWindowContent
    {
        private readonly GameplayReferenceCatalogAsset _catalog;
        private readonly GameplayReferenceKind _kind;
        private readonly Action<string> _choose;
        private readonly Dictionary<string, bool> _expanded = new Dictionary<string, bool>(StringComparer.Ordinal);
        private string _search = string.Empty;
        private Vector2 _scroll;

        internal GameplayReferencePickerPopup(GameplayReferenceCatalogAsset catalog, GameplayReferenceKind kind, Action<string> choose)
        { _catalog = catalog; _kind = kind; _choose = choose; }
        public override Vector2 GetWindowSize() => new Vector2(390f, 370f);
        public override void OnGUI(Rect rect)
        {
            GUILayout.Label(_kind == GameplayReferenceKind.Tag ? "已定义标签" : "已注册属性", EditorStyles.boldLabel);
            _search = EditorGUILayout.TextField("搜索", _search);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (_kind == GameplayReferenceKind.Tag)
            {
                string[] tags = _catalog.Tags ?? Array.Empty<string>();
                if (string.IsNullOrEmpty(_search))
                    foreach (var child in GameplayReferenceTree.Project(tags).Children.Values) DrawNode(child, 0);
                else foreach (string tag in tags.Where(x => x != null && x.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
                             .OrderBy(x => x, StringComparer.Ordinal)) DrawChoice(tag, tag, 0);
            }
            else foreach (string attribute in (_catalog.Attributes ?? Array.Empty<string>())
                         .Where(x => x != null && x.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
                         .OrderBy(x => x, StringComparer.Ordinal)) DrawChoice(attribute, attribute, 0);
            EditorGUILayout.EndScrollView();
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
            string value = child.stringValue;
            bool known = catalog != null && (kind == GameplayReferenceKind.Tag
                ? (catalog.Tags ?? Array.Empty<string>()).Contains(value, StringComparer.Ordinal)
                : (catalog.Attributes ?? Array.Empty<string>()).Contains(value, StringComparer.Ordinal));
            bool invalid = !string.IsNullOrEmpty(value) && (!GameplayReferenceDiagnostics.IsValid(kind, value) || !known);
            Color old = GUI.color;
            if (invalid && !child.hasMultipleDifferentValues) GUI.color = new Color(1f, .65f, .55f);
            EditorGUI.showMixedValue = child.hasMultipleDifferentValues;
            EditorGUI.PropertyField(raw, child, GUIContent.none);
            EditorGUI.showMixedValue = false;
            GUI.color = old;
            if (catalog == null) GUI.Label(pick, new GUIContent("!", discoveryError));
            else if (GUI.Button(pick, new GUIContent("选择", invalid ? "未知或无效引用；选择已定义值或修正文本。" : value)))
            {
                string path=child.propertyPath;var targets=property.serializedObject.targetObjects;
                property.serializedObject.ApplyModifiedProperties();
                var handles=targets.Select(target=>GameplaySourceHandle.Capture(target,path)).ToArray();
                PopupWindow.Show(pick, new GameplayReferencePickerPopup(catalog, kind,
                    selected => {try{ApplyChoice(handles,selected);}catch(InvalidOperationException e){EditorUtility.DisplayDialog("引用位置已变化",e.Message,"关闭");}}));
            }
            if (GUI.Button(clear, new GUIContent("×", "清空此引用"))) child.stringValue = string.Empty;
            EditorGUI.EndProperty();
        }

        internal static void ApplyChoice(GameplaySourceHandle[] handles,string value)
        {
            // The popup may outlive a reorder in the other Inspector. Resolve every native
            // managed identity before recording Undo; never write its old array index.
            var paths=new string[handles.Length];
            for(int i=0;i<handles.Length;i++){
                if(!handles[i].TryResolve(out paths[i]))throw new InvalidOperationException("原配置已删除或替换，请重新打开选择器。");
                using(var source=new SerializedObject(handles[i].Owner))if(source.FindProperty(paths[i])?.propertyType!=SerializedPropertyType.String)throw new InvalidOperationException("原引用字段已改变，请重新选择。");
            }
            GameplaySharedAuthoringCommands.Transaction("选择正式 Gameplay 引用",handles.Select(h=>h.Owner).ToArray(),()=>{
                for(int i=0;i<handles.Length;i++)using(var source=new SerializedObject(handles[i].Owner)){
                    source.FindProperty(paths[i]).stringValue=value;source.ApplyModifiedPropertiesWithoutUndo();
                }
            });
        }
    }

    [CustomPropertyDrawer(typeof(GameplayTagReference))]
    public sealed class GameplayTagReferenceDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) =>
            GameplayReferenceDrawerGUI.Draw(position, property, label, GameplayReferenceKind.Tag, "Name");
    }

    [CustomPropertyDrawer(typeof(GameplayAttributeReference))]
    public sealed class GameplayAttributeReferenceDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) =>
            GameplayReferenceDrawerGUI.Draw(position, property, label, GameplayReferenceKind.Attribute, "Value");
    }
}
