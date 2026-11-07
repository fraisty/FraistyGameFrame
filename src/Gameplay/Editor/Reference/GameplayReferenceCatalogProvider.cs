using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FRFrame.Gameplay.Editor
{
    /// <summary>Editor ownership and visibility for canonical Tag registrations.</summary>
    [InitializeOnLoad]
    public static class GameplayReferenceCatalogProvider
    {
        public const string Framework = "FRFrame";
        public const string Shared = "Shared";
        private static GameplayReferenceCatalogAsset[] _catalogs;

        static GameplayReferenceCatalogProvider() => EditorApplication.projectChanged += () => _catalogs = null;

        public static IReadOnlyList<GameplayReferenceCatalogAsset> Catalogs =>
            _catalogs ??= AssetDatabase.FindAssets("t:GameplayReferenceCatalogAsset", new[] { "Assets" })
                .Select(guid => AssetDatabase.LoadAssetAtPath<GameplayReferenceCatalogAsset>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(asset => asset != null)
                .OrderBy(OwnerScope, StringComparer.Ordinal).ToArray();

        public static string OwnerScope(GameplayReferenceCatalogAsset catalog) =>
            string.IsNullOrEmpty(catalog.Scope) ? Framework : catalog.Scope;

        /// <summary>Retains the framework registry entry point for project-wide Attributes.</summary>
        public static GameplayReferenceCatalogAsset Find(out string error) => Find(Framework, out error);

        public static GameplayReferenceCatalogAsset Find(string scope, out string error)
        {
            var matches = Catalogs.Where(catalog => OwnerScope(catalog) == scope || catalog.ScopeAliases.Contains(scope)).ToArray();
            // A single pre-split registry has no serialized scope and still serves its project.
            if (matches.Length == 0 && Catalogs.Count == 1 && string.IsNullOrEmpty(Catalogs[0].Scope))
            { error = null; return Catalogs[0]; }
            error = matches.Length == 1 ? null : matches.Length == 0
                ? $"Scope「{scope}」没有 Reference Catalog。"
                : $"Scope「{scope}」有多个 Reference Catalog。";
            return matches.Length == 1 ? matches[0] : null;
        }

        public static string Scope(Object asset)
        {
            if (asset == null) return null;
            var path = AssetDatabase.GetAssetPath(asset);
            var document = asset as GameplaySkillDocumentAsset ?? AssetDatabase.LoadMainAssetAtPath(path) as GameplaySkillDocumentAsset;
            return Scope(path, document != null && document.AuthoringSchemaVersion == 2 ? document.AuthoringGraph?.Scope : null);
        }

        internal static string Scope(string assetPath, string declaredScope)
        {
            if (!string.IsNullOrEmpty(declaredScope) && declaredScope != Framework)
                return Find(declaredScope, out _) is GameplayReferenceCatalogAsset declared ? OwnerScope(declared) : declaredScope;
            // Legacy framework sample documents use FRFrame for node availability and live in the Lab.
            var folderOwner = Catalogs.Where(catalog => !string.IsNullOrWhiteSpace(catalog.AssetRoot) &&
                    assetPath.StartsWith(catalog.AssetRoot.TrimEnd('/') + "/", StringComparison.Ordinal))
                .OrderByDescending(catalog => catalog.AssetRoot.Length).FirstOrDefault();
            return folderOwner != null ? OwnerScope(folderOwner) : declaredScope;
        }

        internal static bool CanRead(string sourceScope, string ownerScope) =>
            ownerScope == Framework || ownerScope == sourceScope || (ownerScope == Shared && sourceScope != Framework);

        internal static IEnumerable<GameplayReferenceCatalogAsset> Visible(string scope) =>
            Catalogs.Where(catalog => CanRead(scope, OwnerScope(catalog)));

        /// <summary>Preflights one write against every owner before changing any catalog or document.</summary>
        internal static string[] WithRegistrations(GameplayReferenceCatalogAsset catalog, IEnumerable<string> tags)
        {
            var additions = tags.Distinct(StringComparer.Ordinal).ToArray();
            foreach (string tag in additions)
            {
                if (!GameplayReferenceDiagnostics.IsValid(GameplayReferenceKind.Tag, tag))
                    throw new InvalidOperationException("无效的 Gameplay Tag · " + tag);
                var other = Catalogs.FirstOrDefault(candidate => candidate != catalog && candidate.Tags.Contains(tag, StringComparer.Ordinal));
                if (other != null)
                    throw new InvalidOperationException($"{tag} 已由「{OwnerScope(other)}」登记。");
            }
            return catalog.Tags.Concat(additions).Distinct(StringComparer.Ordinal).ToArray();
        }

        /// <summary>Registers game-owned Tags from standalone content builders.</summary>
        public static void RegisterTags(string scope, IEnumerable<string> tags)
        {
            var catalog = Find(scope, out string error);
            if (catalog == null) throw new InvalidOperationException(error);
            var visible = new HashSet<string>(Visible(OwnerScope(catalog)).SelectMany(owner => owner.Tags), StringComparer.Ordinal);
            var next = WithRegistrations(catalog, tags.Where(tag => !visible.Contains(tag)));
            if (next.SequenceEqual(catalog.Tags, StringComparer.Ordinal)) return;
            Undo.RecordObject(catalog, "登记 Gameplay Tag");
            catalog.Tags = next;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
        }
    }
}
