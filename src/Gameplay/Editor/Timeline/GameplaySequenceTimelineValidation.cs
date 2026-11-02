using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using UnityEditor;

namespace FRFrame.Gameplay.Editor.Timeline
{
    /// <summary>Combines 12A asset validation with 12B reference-catalog membership.</summary>
    public static class GameplaySequenceTimelineValidation
    {
        public static IReadOnlyList<string> Check(GameplaySequenceDefinitionAsset sequence)
        {
            var issues = new List<string>();
            if (sequence == null) return issues;
            var validation = new GameplayAuthoringValidationContext();
            validation.Asset(sequence, AssetDatabase.GetAssetPath(sequence));
            issues.AddRange(validation.Issues.Select(issue => issue.ToString()));
            var catalog = GameplayReferenceCatalogProvider.Find(out string error);
            if (catalog == null) { issues.Add(error); return issues; }
            var tags = new HashSet<string>(catalog.Tags, StringComparer.Ordinal);
            var attributes = new HashSet<string>(catalog.Attributes, StringComparer.Ordinal);
            foreach (var reference in GameplayReferenceScanner.ScanAssets(new[] { sequence }))
            {
                bool present = reference.Kind == GameplayReferenceKind.Tag
                    ? tags.Contains(reference.Value) : attributes.Contains(reference.Value);
                if (GameplayReferenceDiagnostics.IsValid(reference.Kind, reference.Value) && !present)
                    issues.Add(reference.AssetPath + "." + reference.PropertyPath + ": Unregistered " +
                        reference.Kind + " " + reference.Value);
            }
            return issues;
        }
    }
}
