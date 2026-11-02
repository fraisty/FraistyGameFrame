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
            if (catalog == null)
            {
                catalog = GameplayReferenceCatalogProvider.Find(out string discoveryError);
                if (catalog == null) report.Errors.Add(new GameplayReferenceDiagnostic("Catalog", discoveryError));
            }
            var assets = (selectedAssets ?? GameplayReferenceScanner.FindDefinitionAssets()).ToArray();
            var references = GameplayReferenceScanner.ScanAssets(assets);
            var literals = selectedLiterals ?? GameplayReferenceScanner.ScanProductionCode();
            report.AssetCount = assets.Length;
            report.SerializedReferenceCount = references.Count;
            report.SourceLiteralCount = literals.Count;
            report.SourceFileCount = GameplayReferenceScanner.CountProductionSourceFiles();

            if (catalog != null)
                foreach (var issue in catalog.Validate())
                    report.Errors.Add(new GameplayReferenceDiagnostic(AssetDatabase.GetAssetPath(catalog) + "." + issue.Path, issue.Message));
            var tags = new HashSet<string>(catalog?.Tags ?? Array.Empty<string>(), StringComparer.Ordinal);
            var attributes = new HashSet<string>(catalog?.Attributes ?? Array.Empty<string>(), StringComparer.Ordinal);
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
                if (reference.Kind == GameplayReferenceKind.Tag && !tags.Contains(reference.Value))
                { report.Errors.Add(new GameplayReferenceDiagnostic(reference.AssetPath + "." + reference.PropertyPath, "Unregistered Tag: " + reference.Value)); report.MissingTagReferences++; }
                if (reference.Kind == GameplayReferenceKind.Attribute && !attributes.Contains(reference.Value))
                { report.Errors.Add(new GameplayReferenceDiagnostic(reference.AssetPath + "." + reference.PropertyPath, "Unregistered Attribute: " + reference.Value)); report.MissingAttributeReferences++; }
            }
            foreach (var literal in literals)
            {
                bool valid = IsValid(literal.Kind, literal.Value);
                bool present = literal.Kind == GameplayReferenceKind.Tag ? tags.Contains(literal.Value) : attributes.Contains(literal.Value);
                if (valid && present) continue;
                report.Errors.Add(new GameplayReferenceDiagnostic(literal.SourcePath + ":" + literal.Line,
                    (valid ? "Unregistered" : "Invalid") + " production " + literal.Kind + " literal: " + literal.Value));
                report.MissingCodeLiterals++;
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
