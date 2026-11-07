using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay;
using FRFrame.Gameplay.Authoring;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Gameplay.Editor
{
    /// <summary>Immutable preview of one exact or dot-boundary subtree operation.</summary>
    public sealed class GameplayTagRefactorPreview
    {
        public GameplayReferenceCatalogAsset Catalog { get; internal set; }
        public string OldPath { get; internal set; }
        public string NewPath { get; internal set; }
        public bool IsDelete { get; internal set; }
        public IReadOnlyList<GameplayDefinitionAsset> Assets { get; internal set; }
        public IReadOnlyList<GameplayCodeLiteralReference> CodeLiterals { get; internal set; }
        public IReadOnlyList<GameplaySerializedReference> AffectedReferences { get; internal set; }
        public IReadOnlyList<GameplayCodeLiteralReference> AffectedCode { get; internal set; }
        public IReadOnlyList<string> AffectedCatalogTags { get; internal set; }
        internal IReadOnlyDictionary<GameplayReferenceCatalogAsset,string[]> CatalogChanges { get; set; }
        public IReadOnlyList<string> Errors { get; internal set; }
        internal bool UseProjectAssets { get; set; }
        internal bool UseProductionCode { get; set; }
        public bool CanApply => Errors.Count == 0;
    }

    /// <summary>Typed SerializedProperty refactor with code-literal blockers and one Undo transaction.</summary>
    public static class GameplayTagRefactorService
    {
        public static GameplayTagRefactorPreview PreviewRename(GameplayReferenceCatalogAsset catalog,
            string oldPath, string newPath, IEnumerable<GameplayDefinitionAsset> assets = null,
            IReadOnlyList<GameplayCodeLiteralReference> codeLiterals = null) =>
            Preview(catalog, oldPath, newPath, false, assets, codeLiterals);

        public static GameplayTagRefactorPreview PreviewDelete(GameplayReferenceCatalogAsset catalog,
            string path, IEnumerable<GameplayDefinitionAsset> assets = null,
            IReadOnlyList<GameplayCodeLiteralReference> codeLiterals = null) =>
            Preview(catalog, path, null, true, assets, codeLiterals);

        public static GameplayTagRefactorPreview PreviewProjectRename(string oldPath, string newPath) =>
            Preview(null, oldPath, newPath, false, null, null);

        public static GameplayTagRefactorPreview PreviewProjectDelete(string path) =>
            Preview(null, path, null, true, null, null);

        private static GameplayTagRefactorPreview Preview(GameplayReferenceCatalogAsset catalog,
            string oldPath, string newPath, bool delete, IEnumerable<GameplayDefinitionAsset> selectedAssets,
            IReadOnlyList<GameplayCodeLiteralReference> selectedLiterals)
        {
            var assets = (selectedAssets ?? GameplayReferenceScanner.FindDefinitionAssets()).ToArray();
            var literals = selectedLiterals ?? GameplayReferenceScanner.ScanProductionCode();
            var catalogs = catalog != null ? new[] { catalog } : GameplayReferenceCatalogProvider.Catalogs;
            var changes = catalogs.ToDictionary(entry=>entry,entry=>entry.Tags.Where(tag=>Matches(tag,oldPath)).ToArray())
                .Where(pair=>pair.Value.Length>0).ToDictionary(pair=>pair.Key,pair=>pair.Value);
            var catalogTags = changes.Values.SelectMany(tags=>tags).ToArray();
            var references = GameplayReferenceScanner.ScanAssets(assets)
                .Where(reference => reference.Kind == GameplayReferenceKind.Tag && Matches(reference.Value, oldPath)).ToArray();
            var code = literals.Where(literal => literal.Kind == GameplayReferenceKind.Tag && Matches(literal.Value, oldPath)).ToArray();
            var errors = new List<string>();
            if (!GameplayReferenceDiagnostics.IsValid(GameplayReferenceKind.Tag, oldPath)) errors.Add("Old Tag path is invalid.");
            if (!delete && !GameplayReferenceDiagnostics.IsValid(GameplayReferenceKind.Tag, newPath)) errors.Add("New Tag path is invalid.");
            if (!delete && StringComparer.Ordinal.Equals(oldPath, newPath)) errors.Add("New Tag path equals old path.");
            if (catalogTags.Length == 0) errors.Add("No exact or descendant catalog Tag exists.");
            if (code.Length > 0) errors.Add($"{code.Length} production C# literal(s) still use the old Tag; update code manually first.");
            if (delete && references.Length > 0) errors.Add($"{references.Length} serialized reference(s) still use the Tag.");
            if (!delete)
            {
                var final = catalogs.SelectMany(entry=>entry.Tags).Select(tag => Matches(tag, oldPath) ? Map(tag, oldPath, newPath) : tag);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (string tag in final)
                {
                    if (!GameplayReferenceDiagnostics.IsValid(GameplayReferenceKind.Tag, tag)) errors.Add("Invalid target Tag: " + tag);
                    if (tag != null && !seen.Add(tag)) errors.Add("Target collision: " + tag);
                }
            }
            return new GameplayTagRefactorPreview
            {
                Catalog = catalog, OldPath = oldPath, NewPath = newPath, IsDelete = delete,
                Assets = assets, CodeLiterals = literals, AffectedReferences = references,
                AffectedCode = code, AffectedCatalogTags = catalogTags, CatalogChanges = changes, Errors = errors,
                UseProjectAssets = selectedAssets == null,
                UseProductionCode = selectedLiterals == null
            };
        }

        public static void Apply(GameplayTagRefactorPreview preview)
        {
            if (preview == null) throw new ArgumentNullException(nameof(preview));
            if (!preview.CanApply) throw new InvalidOperationException(string.Join(" | ", preview.Errors));
            // Re-preview immediately: asset references or code may have changed since the UI displayed this preview.
            IEnumerable<GameplayDefinitionAsset> freshAssets =
                preview.UseProjectAssets ? GameplayReferenceScanner.FindDefinitionAssets() : preview.Assets;
            IReadOnlyList<GameplayCodeLiteralReference> freshLiterals =
                preview.UseProductionCode ? GameplayReferenceScanner.ScanProductionCode() : preview.CodeLiterals;
            var fresh = Preview(preview.Catalog, preview.OldPath, preview.NewPath, preview.IsDelete,
                freshAssets, freshLiterals);
            if (!fresh.CanApply) throw new InvalidOperationException(string.Join(" | ", fresh.Errors));
            if (preview.CatalogChanges.Count != fresh.CatalogChanges.Count ||
                preview.CatalogChanges.Any(pair=>!fresh.CatalogChanges.TryGetValue(pair.Key,out var tags)||!pair.Value.SequenceEqual(tags,StringComparer.Ordinal)) ||
                !preview.AffectedReferences.Select(x => (x.Source,x.PropertyPath,x.Value))
                    .SequenceEqual(fresh.AffectedReferences.Select(x => (x.Source,x.PropertyPath,x.Value))))
                throw new InvalidOperationException("Catalog or serialized references changed since Preview; preview again.");
            int group = Undo.GetCurrentGroup();
            Undo.IncrementCurrentGroup();
            group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(preview.IsDelete ? "Delete Gameplay Tag" : "Rename Gameplay Tag");
            try
            {
                if (!fresh.IsDelete)
                {
                    foreach (var byAsset in fresh.AffectedReferences.GroupBy(x => x.Source))
                    {
                        var asset = byAsset.Key;
                        Undo.RecordObject(asset, "Rename Gameplay Tag references");
                        using var serialized = new SerializedObject(asset);
                        serialized.Update();
                        foreach (GameplaySerializedReference reference in byAsset)
                        {
                            var property = serialized.FindProperty(reference.PropertyPath);
                            if (property == null || property.propertyType != SerializedPropertyType.String ||
                                !StringComparer.Ordinal.Equals(property.stringValue, reference.Value))
                                throw new InvalidOperationException("Reference changed: " + byAsset.Key + "." + reference.PropertyPath);
                            property.stringValue = Map(reference.Value, fresh.OldPath, fresh.NewPath);
                        }
                        serialized.ApplyModifiedProperties();
                        EditorUtility.SetDirty(asset);
                    }
                }
                foreach(var entry in fresh.CatalogChanges.Keys)
                {
                    Undo.RecordObject(entry, "Update Gameplay Tag catalog");
                    entry.Tags = fresh.IsDelete
                        ? entry.Tags.Where(tag => !Matches(tag, fresh.OldPath)).ToArray()
                        : entry.Tags.Select(tag => Matches(tag, fresh.OldPath) ? Map(tag, fresh.OldPath, fresh.NewPath) : tag).ToArray();
                    EditorUtility.SetDirty(entry);
                }
                var post = GameplayReferenceDiagnostics.Scan(fresh.Catalog, fresh.Assets,
                    preview.UseProductionCode ? fresh.CodeLiterals : preview.CodeLiterals);
                if (post.Errors.Count > 0)
                    throw new InvalidOperationException("Post-refactor diagnostics: " + string.Join(" | ", post.Errors.Take(5)));
                foreach(var entry in fresh.CatalogChanges.Keys)AssetDatabase.SaveAssetIfDirty(entry);
                foreach(var asset in fresh.AffectedReferences.Select(reference=>reference.Source).Distinct())
                    AssetDatabase.SaveAssetIfDirty(asset);
                Undo.CollapseUndoOperations(group);
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                throw;
            }
        }

        public static bool Matches(string value, string prefix) => value != null && prefix != null &&
            (StringComparer.Ordinal.Equals(value, prefix) || value.StartsWith(prefix + ".", StringComparison.Ordinal));

        private static string Map(string value, string oldPath, string newPath) =>
            newPath + value.Substring(oldPath.Length);
    }
}
