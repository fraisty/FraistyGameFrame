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
        [SerializeField] private string _scope;
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
        private Vector2 _tagDetailScroll;

        [MenuItem("FRFrame/Gameplay/References")]
        public static void Open()
        {
            var source = (focusedWindow as SkillAuthoring.GameplaySkillWorkbench)?.Document ?? Selection.activeObject;
            OpenForScope(GameplayReferenceCatalogProvider.Scope(source));
        }

        internal static void OpenForScope(string scope)
        {
            var window = GetWindow<GameplayReferenceEditorWindow>("Gameplay References");
            window.minSize = new Vector2(580,320);
            if(scope!=null)window.SelectScope(scope);
            window.Focus();
        }

        private void OnEnable()
        {
            EditorApplication.projectChanged += OnProjectChanged;
            Undo.undoRedoPerformed += OnProjectChanged;
        }
        private void OnDisable()
        {
            EditorApplication.projectChanged -= OnProjectChanged;
            Undo.undoRedoPerformed -= OnProjectChanged;
        }
        private void OnProjectChanged()
        {
            _report = null; _preview = null;
            Repaint();
        }

        private void SelectScope(string scope)
        {
            _scope = scope; _selectedTag = null; _preview = null;
            _expanded.Clear(); _scroll = _tagDetailScroll = Vector2.zero;
        }

        private void Refresh()
        {
            var assets = GameplayReferenceScanner.FindDefinitionAssets();
            _references = GameplayReferenceScanner.ScanAssets(assets);
            _literals = GameplayReferenceScanner.ScanProductionCode();
            _report = GameplayReferenceDiagnostics.Scan(null, assets, _literals);
            _preview = null;
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            _tab = (Tab)GUILayout.Toolbar((int)_tab, new[] { "Tags", "Attributes", "Diagnostics" }, EditorStyles.toolbarButton);
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(65))) Refresh();
            EditorGUILayout.EndHorizontal();
            _catalog = GameplayReferenceCatalogProvider.Find(out string error);
            if (_catalog == null)
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
                return;
            }
            if(_tab==Tab.Tags)
            {
                var scopes = GameplayReferenceCatalogProvider.Catalogs.Select(GameplayReferenceCatalogProvider.OwnerScope)
                    .Distinct(StringComparer.Ordinal).Prepend("").ToArray();
                int selected = Math.Max(0,Array.IndexOf(scopes,_scope??""));
                int next = EditorGUILayout.Popup("Tag Scope",selected,scopes.Select(scope=>scope==""?"全工程":scope).ToArray());
                if(next!=selected)SelectScope(scopes[next]);
            }
            _search = EditorGUILayout.TextField("Search", _search);
            if(_tab==Tab.Tags){DrawTags();return;}
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if(_tab==Tab.Attributes)DrawAttributes();else DrawDiagnostics();
            EditorGUILayout.EndScrollView();
        }

        private void DrawTags()
        {
            var catalogs = GameplayReferenceCatalogProvider.Catalogs;
            var visible = string.IsNullOrEmpty(_scope) ? catalogs : GameplayReferenceCatalogProvider.Visible(_scope);
            var owners = visible.SelectMany(catalog=>catalog.Tags.Select(tag=>(tag,catalog)))
                .Where(entry=>!string.IsNullOrEmpty(entry.tag))
                .ToLookup(entry=>entry.tag,entry=>entry.catalog,StringComparer.Ordinal);
            var tags = owners.Select(group=>group.Key).ToArray();
            EditorGUILayout.HelpBox(string.IsNullOrEmpty(_scope)?"全工程浏览。选择具体 Scope 后可登记。":
                $"显示 {_scope} 和可引用的共享 Tag。新 Tag 登记到 {_scope}。", MessageType.Info);
            float detailWidth=Mathf.Clamp(position.width*.38f,290,440);
            using(new EditorGUILayout.HorizontalScope())
            {
                using(var list=new EditorGUILayout.ScrollViewScope(_scroll,GUILayout.Width(position.width-detailWidth-8)))
                {
                    _scroll=list.scrollPosition;
                    if (string.IsNullOrEmpty(_search))
                        foreach (var child in GameplayReferenceTree.Project(tags).Children.Values) DrawTreeNode(child, 0,owners);
                    else foreach (string tag in tags.Where(x => x.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
                                 .OrderBy(x => x, StringComparer.Ordinal)) DrawTagRow(tag,owners[tag]);
                }
                using(var details=new EditorGUILayout.ScrollViewScope(_tagDetailScroll,GUILayout.Width(detailWidth)))
                {
                    _tagDetailScroll=details.scrollPosition;
                    DrawTagDetails(owners);
                }
            }
        }

        private void DrawTagDetails(ILookup<string,GameplayReferenceCatalogAsset> owners)
        {
            _newTag = EditorGUILayout.TextField("New Tag / Child", _newTag);
            var target = string.IsNullOrEmpty(_scope)?null:GameplayReferenceCatalogProvider.Find(_scope,out _);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(target==null))
            {
                if (GUILayout.Button("Add Tag")) AddTag(target,_newTag);
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_selectedTag)))
                    if (GUILayout.Button("Add Child")) AddTag(target,_selectedTag + "." + _newTag);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Selected", _selectedTag ?? "(none)");
            if (string.IsNullOrEmpty(_selectedTag)) return;
            var selectedOwners=owners[_selectedTag].ToArray();
            if(selectedOwners.Length==1)
            {
                var owner=selectedOwners[0];
                using(new EditorGUI.DisabledScope(true))EditorGUILayout.ObjectField("所属 Catalog",owner,typeof(GameplayReferenceCatalogAsset),false);
                string ownerScope=GameplayReferenceCatalogProvider.OwnerScope(owner);
                if(ownerScope!=GameplayReferenceCatalogProvider.Shared&&ownerScope!=GameplayReferenceCatalogProvider.Framework&&
                    GUILayout.Button(new GUIContent("提升此 Tag 到 Shared","移动登记归属，各游戏可引用同一个原字符串。")))
                    Promote(owner,_selectedTag);
            }
            if(_report!=null)
            {
                EditorGUILayout.LabelField("Serialized references (subtree)",
                    _references.Count(x => x.Kind == GameplayReferenceKind.Tag && GameplayTagRefactorService.Matches(x.Value, _selectedTag)).ToString());
                EditorGUILayout.LabelField("Code literals (subtree)",
                    _literals.Count(x => x.Kind == GameplayReferenceKind.Tag && GameplayTagRefactorService.Matches(x.Value, _selectedTag)).ToString());
            }
            _renameTarget = EditorGUILayout.TextField("Rename to", _renameTarget);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("预览全工程重命名"))
                _preview = GameplayTagRefactorService.PreviewProjectRename(_selectedTag, _renameTarget);
            if (GUILayout.Button("预览全工程删除"))
                _preview = GameplayTagRefactorService.PreviewProjectDelete(_selectedTag);
            EditorGUILayout.EndHorizontal();
            DrawPreview();
        }

        private void DrawTreeNode(GameplayReferenceTreeNode node, int depth,ILookup<string,GameplayReferenceCatalogAsset> owners)
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
            if (node.IsDefined) DrawOwners(owners[node.FullPath]);
            EditorGUILayout.EndHorizontal();
            if (node.Children.Count > 0 && _expanded[node.FullPath])
                foreach (var child in node.Children.Values) DrawTreeNode(child, depth + 1,owners);
        }
        private void DrawTagRow(string path,IEnumerable<GameplayReferenceCatalogAsset> owners)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent(path, path), EditorStyles.linkLabel))
            { _selectedTag = path; _renameTarget = path; _preview = null; }
            DrawOwners(owners);
            EditorGUILayout.EndHorizontal();
        }
        private static void DrawOwners(IEnumerable<GameplayReferenceCatalogAsset> owners) =>
            GUILayout.Label(string.Join(" / ",owners.Select(GameplayReferenceCatalogProvider.OwnerScope)),EditorStyles.miniLabel);
        private void DrawPreview()
        {
            if (_preview == null) return;
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(_preview.IsDelete ? "Delete preview" : "Rename preview", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Catalog entries", _preview.AffectedCatalogTags.Count.ToString());
            foreach(var pair in _preview.CatalogChanges)
                EditorGUILayout.LabelField(GameplayReferenceCatalogProvider.OwnerScope(pair.Key),pair.Value.Length.ToString());
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
                if(_report!=null){
                    GUILayout.Label("assets " + CountSerialized(GameplayReferenceKind.Attribute, value), GUILayout.Width(75));
                    GUILayout.Label("code " + CountCode(GameplayReferenceKind.Attribute, value), GUILayout.Width(70));
                }
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
                $"missing Tag {_report.MissingTagReferences}, out of scope {_report.OutOfScopeTagReferences}, missing Attribute {_report.MissingAttributeReferences}, " +
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

        private void AddTag(GameplayReferenceCatalogAsset catalog,string value)
        {
            try
            {
                var next = GameplayReferenceCatalogProvider.WithRegistrations(catalog,new[]{value});
                if(next.SequenceEqual(catalog.Tags,StringComparer.Ordinal))return;
                Undo.RecordObject(catalog,"登记 Gameplay Tag");
                catalog.Tags=next;
                EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssetIfDirty(catalog);
                _selectedTag=value;_renameTarget=value;OnProjectChanged();
            }
            catch(InvalidOperationException error){EditorUtility.DisplayDialog("登记 Gameplay Tag",error.Message,"关闭");}
        }
        private void Promote(GameplayReferenceCatalogAsset owner,string tag)
        {
            var shared=GameplayReferenceCatalogProvider.Find(GameplayReferenceCatalogProvider.Shared,out string error);
            if(shared==null||GameplayReferenceCatalogProvider.Catalogs.Count(catalog=>catalog.Tags.Contains(tag,StringComparer.Ordinal))!=1)
            {EditorUtility.DisplayDialog("提升到 Shared",error??"Tag 归属存在冲突。","关闭");return;}
            Undo.RecordObjects(new UnityEngine.Object[]{owner,shared},"提升 Gameplay Tag 到 Shared");
            owner.Tags=owner.Tags.Where(value=>value!=tag).ToArray();
            shared.Tags=shared.Tags.Append(tag).ToArray();
            EditorUtility.SetDirty(owner);EditorUtility.SetDirty(shared);
            AssetDatabase.SaveAssetIfDirty(owner);AssetDatabase.SaveAssetIfDirty(shared);OnProjectChanged();
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
