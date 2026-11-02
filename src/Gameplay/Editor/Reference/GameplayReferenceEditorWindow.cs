using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay;
using FRFrame.Gameplay.Authoring;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Gameplay.Editor
{
    /// <summary>Project authoring registry, typed reference diagnostics and conservative Tag refactor UI.</summary>
    public sealed class GameplayReferenceEditorWindow : EditorWindow
    {
        private enum Tab { Tags, Attributes, Diagnostics }
        private Tab _tab;
        private GameplayReferenceCatalogAsset _catalog;
        private string _discoveryError;
        private string _search = string.Empty;
        private string _selectedTag;
        private string _newTag = string.Empty;
        private string _newAttribute = string.Empty;
        private string _renameTarget = string.Empty;
        private GameplayTagRefactorPreview _preview;
        private GameplayReferenceDiagnosticReport _report;
        private IReadOnlyList<GameplaySerializedReference> _references = Array.Empty<GameplaySerializedReference>();
        private IReadOnlyList<GameplayCodeLiteralReference> _literals = Array.Empty<GameplayCodeLiteralReference>();
        private readonly Dictionary<string, bool> _expanded = new Dictionary<string, bool>(StringComparer.Ordinal);
        private Vector2 _scroll;

        [MenuItem("FRFrame/Gameplay/References")]
        public static void Open() => GetWindow<GameplayReferenceEditorWindow>("Gameplay References");

        private void OnEnable()
        {
            EditorApplication.projectChanged += OnProjectChanged;
            Refresh();
        }
        private void OnDisable() => EditorApplication.projectChanged -= OnProjectChanged;
        private void OnProjectChanged()
        {
            _catalog = null; _report = null;
            EditorApplication.delayCall += () => { if (this != null) Refresh(); };
        }

        private void Refresh()
        {
            _catalog = GameplayReferenceCatalogProvider.Find(out _discoveryError);
            var assets = GameplayReferenceScanner.FindDefinitionAssets();
            _references = GameplayReferenceScanner.ScanAssets(assets);
            _literals = GameplayReferenceScanner.ScanProductionCode();
            _report = GameplayReferenceDiagnostics.Scan(_catalog, assets, _literals);
            _preview = null;
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            _tab = (Tab)GUILayout.Toolbar((int)_tab, new[] { "Tags", "Attributes", "Diagnostics" }, EditorStyles.toolbarButton);
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(65))) Refresh();
            EditorGUILayout.EndHorizontal();
            if (_catalog == null) _catalog = GameplayReferenceCatalogProvider.Find(out _discoveryError);
            if (_catalog == null)
            {
                EditorGUILayout.HelpBox(_discoveryError ?? "Catalog unavailable.", MessageType.Error);
                return;
            }
            EditorGUILayout.ObjectField("Catalog", _catalog, typeof(GameplayReferenceCatalogAsset), false);
            _search = EditorGUILayout.TextField("Search", _search);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            switch (_tab)
            {
                case Tab.Tags: DrawTags(); break;
                case Tab.Attributes: DrawAttributes(); break;
                default: DrawDiagnostics(); break;
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawTags()
        {
            EditorGUILayout.HelpBox("Full Tag paths are canonical identities. Indented groups are derived, not stored.", MessageType.Info);
            var tags = _catalog.Tags ?? Array.Empty<string>();
            if (string.IsNullOrEmpty(_search))
                foreach (var child in GameplayReferenceTree.Project(tags).Children.Values) DrawTreeNode(child, 0);
            else foreach (string tag in tags.Where(x => x != null && x.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
                         .OrderBy(x => x, StringComparer.Ordinal)) DrawTagRow(tag, tag, 0);
            EditorGUILayout.Space(8);
            _newTag = EditorGUILayout.TextField("New Tag / Child", _newTag);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Tag")) AddTag(_newTag);
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_selectedTag)))
                if (GUILayout.Button("Add Child")) AddTag(_selectedTag + "." + _newTag);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Selected", _selectedTag ?? "(none)");
            if (string.IsNullOrEmpty(_selectedTag)) return;
            EditorGUILayout.LabelField("Serialized references (subtree)",
                _references.Count(x => x.Kind == GameplayReferenceKind.Tag && GameplayTagRefactorService.Matches(x.Value, _selectedTag)).ToString());
            EditorGUILayout.LabelField("Code literals (subtree)",
                _literals.Count(x => x.Kind == GameplayReferenceKind.Tag && GameplayTagRefactorService.Matches(x.Value, _selectedTag)).ToString());
            _renameTarget = EditorGUILayout.TextField("Rename to", _renameTarget);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Preview Rename"))
                _preview = GameplayTagRefactorService.PreviewRename(_catalog, _selectedTag, _renameTarget);
            if (GUILayout.Button("Preview Delete"))
                _preview = GameplayTagRefactorService.PreviewDelete(_catalog, _selectedTag);
            EditorGUILayout.EndHorizontal();
            DrawPreview();
        }

        private void DrawTreeNode(GameplayReferenceTreeNode node, int depth)
        {
            EditorGUILayout.BeginHorizontal(); GUILayout.Space(depth * 14f);
            if (node.Children.Count > 0)
            {
                bool expanded = !_expanded.TryGetValue(node.FullPath, out bool value) || value;
                _expanded[node.FullPath] = GUILayout.Toggle(expanded, expanded ? "▾" : "▸", EditorStyles.label, GUILayout.Width(15f));
            }
            else GUILayout.Space(15f);
            if (GUILayout.Button(new GUIContent(node.Segment + (node.IsDefined ? "" : " (group)"), node.FullPath),
                    _selectedTag == node.FullPath ? EditorStyles.toolbarButton : EditorStyles.linkLabel))
            { _selectedTag = node.FullPath; _renameTarget = node.FullPath; _preview = null; }
            if (node.IsDefined) GUILayout.Label("defined", EditorStyles.miniLabel, GUILayout.Width(45f));
            EditorGUILayout.EndHorizontal();
            if (node.Children.Count > 0 && _expanded[node.FullPath])
                foreach (var child in node.Children.Values) DrawTreeNode(child, depth + 1);
        }
        private void DrawTagRow(string label, string path, int depth)
        {
            EditorGUILayout.BeginHorizontal(); GUILayout.Space(depth * 14f);
            if (GUILayout.Button(new GUIContent(label, path), EditorStyles.linkLabel))
            { _selectedTag = path; _renameTarget = path; _preview = null; }
            EditorGUILayout.EndHorizontal();
        }
        private void DrawPreview()
        {
            if (_preview == null) return;
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(_preview.IsDelete ? "Delete preview" : "Rename preview", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Catalog entries", _preview.AffectedCatalogTags.Count.ToString());
            EditorGUILayout.LabelField("Serialized references", _preview.AffectedReferences.Count.ToString());
            EditorGUILayout.LabelField("C# literals", _preview.AffectedCode.Count.ToString());
            foreach (string error in _preview.Errors) EditorGUILayout.HelpBox(error, MessageType.Error);
            foreach (var literal in _preview.AffectedCode)
                if (GUILayout.Button(literal.SourcePath + ":" + literal.Line + "  " + literal.Value, EditorStyles.linkLabel))
                    Ping(literal.SourcePath, literal.Line);
            foreach (var reference in _preview.AffectedReferences)
                if (GUILayout.Button(reference.AssetPath + "  " + reference.PropertyPath, EditorStyles.linkLabel))
                    Ping(reference.AssetPath);
            using (new EditorGUI.DisabledScope(!_preview.CanApply))
                if (GUILayout.Button("Apply with Undo"))
                {
                    try { GameplayTagRefactorService.Apply(_preview); Refresh(); }
                    catch (Exception ex) { EditorUtility.DisplayDialog("Tag refactor failed", ex.Message, "OK"); Refresh(); }
                }
            if (GUILayout.Button("Cancel Preview")) _preview = null;
        }

        private void DrawAttributes()
        {
            EditorGUILayout.HelpBox("Catalog entries register existing attribute identities for authoring; they do not create Runtime Attributes.", MessageType.Info);
            foreach (string value in (_catalog.Attributes ?? Array.Empty<string>())
                         .Where(x => x != null && x.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
                         .OrderBy(x => x, StringComparer.Ordinal))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(value, GUILayout.MinWidth(150));
                GUILayout.Label("assets " + CountSerialized(GameplayReferenceKind.Attribute, value), GUILayout.Width(75));
                GUILayout.Label("code " + CountCode(GameplayReferenceKind.Attribute, value), GUILayout.Width(70));
                if (GUILayout.Button("Remove", GUILayout.Width(65))) RemoveAttribute(value);
                EditorGUILayout.EndHorizontal();
            }
            _newAttribute = EditorGUILayout.TextField("New identity", _newAttribute);
            if (GUILayout.Button("Add Attribute Entry")) AddAttribute(_newAttribute);
        }

        private void DrawDiagnostics()
        {
            if (_report == null) Refresh();
            EditorGUILayout.LabelField($"Assets {_report.AssetCount}, serialized references {_report.SerializedReferenceCount}, " +
                $"source files {_report.SourceFileCount}, code literals {_report.SourceLiteralCount}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Errors {_report.Errors.Count}: 12A {_report.AuthoringValidationErrors}, " +
                $"missing Tag {_report.MissingTagReferences}, missing Attribute {_report.MissingAttributeReferences}, " +
                $"code literal {_report.MissingCodeLiterals}");
            if (_report.Errors.Count == 0) EditorGUILayout.HelpBox("All project references are valid.", MessageType.Info);
            foreach (GameplayReferenceDiagnostic warning in _report.Warnings)
                EditorGUILayout.HelpBox(warning.ToString(), MessageType.Warning);
            foreach (GameplayReferenceDiagnostic error in _report.Errors)
                if (GUILayout.Button(error.ToString(), EditorStyles.linkLabel))
                {
                    string path = error.Location;
                    int cs = path.IndexOf(".cs:", StringComparison.Ordinal);
                    if (cs >= 0)
                    { int.TryParse(path.Substring(cs + 4), out int line); Ping(path.Substring(0, cs + 3), line); }
                    else
                    { int asset = path.IndexOf(".asset", StringComparison.Ordinal); if (asset >= 0) Ping(path.Substring(0, asset + 6)); }
                }
        }

        private int CountSerialized(GameplayReferenceKind kind, string value) =>
            _references.Count(x => x.Kind == kind && StringComparer.Ordinal.Equals(x.Value, value));
        private int CountCode(GameplayReferenceKind kind, string value) =>
            _literals.Count(x => x.Kind == kind && StringComparer.Ordinal.Equals(x.Value, value));

        private void AddTag(string value)
        {
            if (!GameplayReferenceDiagnostics.IsValid(GameplayReferenceKind.Tag, value))
            { EditorUtility.DisplayDialog("Invalid Tag", "Enter a valid dot-separated Tag path.", "OK"); return; }
            if ((_catalog.Tags ?? Array.Empty<string>()).Contains(value, StringComparer.Ordinal)) return;
            Undo.RecordObject(_catalog, "Add Gameplay Tag");
            _catalog.Tags = (_catalog.Tags ?? Array.Empty<string>()).Concat(new[] { value }).ToArray();
            EditorUtility.SetDirty(_catalog); AssetDatabase.SaveAssets(); Refresh();
        }
        private void AddAttribute(string value)
        {
            if (!GameplayReferenceDiagnostics.IsValid(GameplayReferenceKind.Attribute, value))
            { EditorUtility.DisplayDialog("Invalid Attribute", "Enter a valid attribute identity.", "OK"); return; }
            if ((_catalog.Attributes ?? Array.Empty<string>()).Contains(value, StringComparer.Ordinal)) return;
            Undo.RecordObject(_catalog, "Add Gameplay Attribute entry");
            _catalog.Attributes = (_catalog.Attributes ?? Array.Empty<string>()).Concat(new[] { value }).ToArray();
            EditorUtility.SetDirty(_catalog); AssetDatabase.SaveAssets(); Refresh();
        }
        private void RemoveAttribute(string value)
        {
            var liveReferences = GameplayReferenceScanner.ScanAssets(GameplayReferenceScanner.FindDefinitionAssets());
            var liveLiterals = GameplayReferenceScanner.ScanProductionCode();
            if (liveReferences.Any(x => x.Kind == GameplayReferenceKind.Attribute && StringComparer.Ordinal.Equals(x.Value, value)) ||
                liveLiterals.Any(x => x.Kind == GameplayReferenceKind.Attribute && StringComparer.Ordinal.Equals(x.Value, value)))
            { EditorUtility.DisplayDialog("Referenced Attribute", "Remove serialized and C# references first. Attribute rename is not supported in 12B.", "OK"); return; }
            Undo.RecordObject(_catalog, "Remove Gameplay Attribute entry");
            _catalog.Attributes = _catalog.Attributes.Where(x => !StringComparer.Ordinal.Equals(x, value)).ToArray();
            EditorUtility.SetDirty(_catalog); AssetDatabase.SaveAssets(); Refresh();
        }
        private static void Ping(string path, int line = 0)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null) return;
            EditorGUIUtility.PingObject(asset);
            if (line > 0) AssetDatabase.OpenAsset(asset, line);
            else Selection.activeObject = asset;
        }
    }
}
