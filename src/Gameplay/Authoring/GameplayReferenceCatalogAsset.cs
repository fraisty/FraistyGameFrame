using System;
using System.Collections.Generic;
using FRFrame.Gameplay;
using UnityEngine;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>Project authoring registry of canonical strings; Runtime identity remains GameplayTag/GameplayAttributeId.</summary>
    [CreateAssetMenu(menuName = "FRFrame Gameplay/Reference Catalog")]
    public sealed class GameplayReferenceCatalogAsset : ScriptableObject
    {
        public string[] Tags = Array.Empty<string>();
        public string[] Attributes = Array.Empty<string>();

        public IReadOnlyList<GameplayAuthoringValidationIssue> Validate()
        {
            var issues = new List<GameplayAuthoringValidationIssue>();
            ValidateEntries(Tags, "Tags", value => new GameplayTag(value), issues);
            ValidateEntries(Attributes, "Attributes", value => new GameplayAttributeId(value), issues);
            return issues;
        }

        private static void ValidateEntries<T>(string[] values, string path, Func<string, T> construct,
            List<GameplayAuthoringValidationIssue> issues)
        {
            if (values == null)
            {
                issues.Add(new GameplayAuthoringValidationIssue(GameplayAuthoringIssueSeverity.Error, path, "List is null."));
                return;
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < values.Length; i++)
            {
                string entryPath = path + "[" + i + "]";
                try { construct(values[i]); }
                catch (ArgumentException ex)
                {
                    issues.Add(new GameplayAuthoringValidationIssue(GameplayAuthoringIssueSeverity.Error, entryPath, ex.Message));
                    continue;
                }
                if (!seen.Add(values[i]))
                    issues.Add(new GameplayAuthoringValidationIssue(GameplayAuthoringIssueSeverity.Error, entryPath,
                        "Duplicate canonical identity: " + values[i]));
            }
        }
    }
}
