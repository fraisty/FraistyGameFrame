using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay;
using FRFrame.Gameplay.Authoring;
using UnityEditor;

namespace FRFrame.Gameplay.Editor
{
    public readonly struct GameplayReferenceDiagnostic
    {
        public readonly string Location;
        public readonly string Message;
        public GameplayReferenceDiagnostic(string location, string message)
        { Location = location; Message = message; }
        public override string ToString() => Location + ": " + Message;
    }

    public sealed class GameplayReferenceDiagnosticReport
    {
        public readonly List<GameplayReferenceDiagnostic> Errors = new List<GameplayReferenceDiagnostic>();
        /// <summary>Non-blocking author guidance; retained separately from invalid content/catalog references.</summary>
        public readonly List<GameplayReferenceDiagnostic> Warnings = new List<GameplayReferenceDiagnostic>();
        public int AssetCount;
        public int SerializedReferenceCount;
        public int SourceFileCount;
        public int SourceLiteralCount;
        public int AuthoringValidationErrors;
        public int MissingTagReferences;
        public int OutOfScopeTagReferences;
        public int MissingAttributeReferences;
        public int MissingCodeLiterals;
    }

    /// <summary>Combines 12A recursive validation with Editor-only catalog membership checks.</summary>
    public static class GameplayReferenceDiagnostics
    {
        public static GameplayReferenceDiagnosticReport Scan(GameplayReferenceCatalogAsset catalog = null,
            IEnumerable<GameplayDefinitionAsset> selectedAssets = null,
            IReadOnlyList<GameplayCodeLiteralReference> selectedLiterals = null)
        {
            var report = new GameplayReferenceDiagnosticReport();
            bool project = catalog == null;
            var catalogs = project ? GameplayReferenceCatalogProvider.Catalogs : new[] { catalog };
            if (project) catalog = GameplayReferenceCatalogProvider.Find(out _);
            if (catalog == null) report.Errors.Add(new GameplayReferenceDiagnostic("Catalog", "需要唯一的 FRFrame Catalog。"));
            var assets = (selectedAssets ?? GameplayReferenceScanner.FindDefinitionAssets()).ToArray();
            var references = GameplayReferenceScanner.ScanAssets(assets);
            var literals = selectedLiterals ?? GameplayReferenceScanner.ScanProductionCode();
            report.AssetCount = assets.Length;
            report.SerializedReferenceCount = references.Count;
            report.SourceLiteralCount = literals.Count;
            report.SourceFileCount = GameplayReferenceScanner.CountProductionSourceFiles();

            var owners = new Dictionary<string, string>(StringComparer.Ordinal);
            var scopes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in catalogs)
            {
                string owner = GameplayReferenceCatalogProvider.OwnerScope(entry);
                string path = AssetDatabase.GetAssetPath(entry);
                foreach (string scope in entry.ScopeAliases.Prepend(owner))
                    if (!IsValid(GameplayReferenceKind.Tag, scope) || scope.Contains(".") || !scopes.Add(scope))
                        report.Errors.Add(new GameplayReferenceDiagnostic(path + ".Scope", "无效或重复的 Scope · " + scope));
                foreach (var issue in entry.Validate())
                    report.Errors.Add(new GameplayReferenceDiagnostic(path + "." + issue.Path, issue.Message));
                foreach (string tag in entry.Tags ?? Array.Empty<string>())
                    if (tag != null && !owners.TryAdd(tag, owner) && owners[tag] != owner)
                        report.Errors.Add(new GameplayReferenceDiagnostic(path + ".Tags", $"重复登记 {tag} · {owners[tag]} / {owner}"));
            }
            var attributes = new HashSet<string>(catalog?.Attributes ?? Array.Empty<string>(), StringComparer.Ordinal);
            var sourceScopes = project ? references.Select(reference=>reference.Source).Concat(assets).Distinct()
                .ToDictionary(source=>source,source=>GameplayReferenceCatalogProvider.Scope(source)) : null;
            foreach (GameplayDefinitionAsset asset in assets)
            {
                var validation = new GameplayAuthoringValidationContext();
                validation.Asset(asset, AssetDatabase.GetAssetPath(asset));
                foreach (var issue in validation.Issues)
                {
                    var diagnostic=new GameplayReferenceDiagnostic(issue.Path, issue.Message);
                    if(issue.Severity==GameplayAuthoringIssueSeverity.Warning)report.Warnings.Add(diagnostic);
                    else {report.Errors.Add(diagnostic);report.AuthoringValidationErrors++;}
                }
            }
            foreach(var issue in SkillAuthoring.GameplayActionOrderValidation.Scan(assets)){
                var diagnostic=new GameplayReferenceDiagnostic(AssetDatabase.GetAssetPath(issue.Source)+"."+issue.Path,issue.Message);
                if(issue.Severity==GameplayAuthoringIssueSeverity.Warning)report.Warnings.Add(diagnostic);
                else{report.Errors.Add(diagnostic);report.AuthoringValidationErrors++;}
            }
            foreach (var reference in references)
            {
                if (!IsValid(reference.Kind, reference.Value)) continue; // 12A already reports invalid authoring values.
                if (reference.Kind == GameplayReferenceKind.Tag)
                {
                    string location = reference.AssetPath + "." + reference.PropertyPath;
                    if (!owners.TryGetValue(reference.Value, out string owner))
                    { report.Errors.Add(new GameplayReferenceDiagnostic(location, "Unregistered Tag: " + reference.Value)); report.MissingTagReferences++; }
                    else if (project && sourceScopes[reference.Source] is string scope && !GameplayReferenceCatalogProvider.CanRead(scope,owner))
                    { report.Errors.Add(new GameplayReferenceDiagnostic(location,$"{scope} 无法引用 {owner} 的私有 Tag · {reference.Value}")); report.OutOfScopeTagReferences++; }
                }
                if (reference.Kind == GameplayReferenceKind.Attribute && !attributes.Contains(reference.Value))
                { report.Errors.Add(new GameplayReferenceDiagnostic(reference.AssetPath + "." + reference.PropertyPath, "Unregistered Attribute: " + reference.Value)); report.MissingAttributeReferences++; }
            }
            foreach (var literal in literals)
            {
                bool valid = IsValid(literal.Kind, literal.Value);
                bool present = literal.Kind == GameplayReferenceKind.Tag ? owners.ContainsKey(literal.Value) : attributes.Contains(literal.Value);
                if (valid && present) continue;
                report.Errors.Add(new GameplayReferenceDiagnostic(literal.SourcePath + ":" + literal.Line,
                    (valid ? "Unregistered" : "Invalid") + " production " + literal.Kind + " literal: " + literal.Value));
                report.MissingCodeLiterals++;
            }
            if (project)
                foreach (string sourcePath in sourceScopes.Where(pair=>pair.Value==GameplayReferenceCatalogProvider.Shared)
                    .Select(pair=>AssetDatabase.GetAssetPath(pair.Key)).Where(path=>path.Length>0).Distinct(StringComparer.Ordinal))
                    foreach (string dependency in AssetDatabase.GetDependencies(sourcePath,false))
                    {
                        string scope=GameplayReferenceCatalogProvider.Scope(AssetDatabase.LoadMainAssetAtPath(dependency));
                        if(scope!=null&&!GameplayReferenceCatalogProvider.CanRead(GameplayReferenceCatalogProvider.Shared,scope))
                            report.Errors.Add(new GameplayReferenceDiagnostic(sourcePath,$"Shared 内容引用了 {scope} 的私有资产 · {dependency}"));
                    }
            return report;
        }

        public static bool IsValid(GameplayReferenceKind kind, string value)
        {
            try
            {
                if (kind == GameplayReferenceKind.Tag) new GameplayTag(value);
                else new GameplayAttributeId(value);
                return true;
            }
            catch (ArgumentException) { return false; }
        }
    }
}
