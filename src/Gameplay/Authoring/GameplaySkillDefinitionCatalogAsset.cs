using System;
using System.Collections.Generic;
using FRFrame.Gameplay;
using UnityEngine;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Authoring
{
    [CreateAssetMenu(menuName = "FRFrame Gameplay/Skill Catalog")]
    public sealed class GameplaySkillDefinitionCatalogAsset : ScriptableObject
    {
        public GameplaySkillDefinitionAsset[] Skills = Array.Empty<GameplaySkillDefinitionAsset>();
        public IReadOnlyDictionary<string, GameplaySkillDefinition> Build(GameplayAuthoringBuildContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (Skills == null) throw new InvalidOperationException($"{name}: Skill list is null.");
            var result = new Dictionary<string, GameplaySkillDefinition>(StringComparer.Ordinal);
            for (int i = 0; i < Skills.Length; i++)
            {
                var skill = Skills[i];
                if (skill == null) throw new InvalidOperationException($"{name}.Skills[{i}]: Missing skill asset.");
                var definition = skill.Build(context);
                if (result.ContainsKey(skill.DefinitionId)) throw new InvalidOperationException($"{name}.Skills[{i}]: Duplicate DefinitionId '{skill.DefinitionId}'.");
                result.Add(skill.DefinitionId, definition);
            }
            return result;
        }
    }
}
