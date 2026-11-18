using System;
using System.Collections.Generic;

namespace FRFrame.Gameplay.Authoring.Graph
{
    /// <summary>A Player-visible scope requirement. A shared asset may declare its own scope.</summary>
    public interface IGameplayScopedAuthoring
    {
        string RequiredScope { get; }
    }

    /// <summary>May disable a real action-array output or the existing Then output.</summary>
    public interface IGameplayGraphPortConstraint
    {
        string UnavailableOutput(string port);
    }

    /// <summary>Declares input requirements and the actual context delivered by each output.</summary>
    public interface IGameplayGraphContextContract
    {
        GameplayGraphContextPolicy.Requirement RequiredInputs { get; }
        GameplayGraphContextPolicy.Inputs OutputInputs(string port, GameplayGraphContextPolicy.Inputs input);
    }

    /// <summary>Validates one occurrence using the actual ordered actions. Placement is independent
    /// of the caller's lifetime guard, so raw assets and graph consumers have the same contract.</summary>
    public interface IGameplayActionPlacementConstraint
    {
        string PlacementReason(IReadOnlyList<GameplaySkillActionAuthoring> actions, int index);
    }

    public static class GameplayActionArrayPolicy
    {
        public static string ScopeReason(object authoring, string scope)
        {
            if (!(authoring is IGameplayScopedAuthoring value)) return null;
            try
            {
                string required = value.RequiredScope;
                return !string.IsNullOrEmpty(required) && !string.Equals(required, scope, StringComparison.Ordinal)
                    ? $"此配置需要 {required} Scope。" : null;
            }
            catch (Exception error) { return $"{authoring.GetType().Name} Scope 合同失败 · {error.Message}"; }
        }

        public static string PlacementReason(IReadOnlyList<GameplaySkillActionAuthoring> actions)
        {
            for (int i = 0; i < actions.Count; i++)
                if (actions[i] is IGameplayActionPlacementConstraint constraint)
                {
                    try
                    {
                        string reason = constraint.PlacementReason(actions, i);
                        if (reason != null) return $"动作 {i + 1} ({actions[i].GetType().Name}) · {reason}";
                    }
                    catch (Exception error) { return $"动作 {i + 1} 的顺序合同失败 · {error.Message}"; }
                }
            return null;
        }

        /// <summary>Shared raw array validation used by Player authoring and editor placement checks.</summary>
        internal static void ValidateArray(GameplaySkillActionAuthoring[] actions, string scope,
            GameplayAuthoringValidationContext validation, string path)
        {
            if (actions == null) return;
            string placement = PlacementReason(actions);
            if (placement != null) validation.Error(path, placement);
            for (int i = 0; i < actions.Length; i++)
            {
                var action = actions[i];
                string source = path + "[" + i + "]";
                string reason = ScopeReason(action, scope);
                if (reason != null) validation.Error(source, reason);
                ValidateCallbacks(action, scope, validation, source);
            }
        }

        internal static void ValidateCallbacks(object payload, string scope, GameplayAuthoringValidationContext validation, string path)
        {
            foreach (var slot in GameplayGraphFields.ActionArrays(payload))
            {
                var actions = (GameplaySkillActionAuthoring[])slot.Value;
                string location = path + "." + slot.Path;
                if (actions?.Length > 0 && slot.Owner is IGameplayGraphPortConstraint)
                {
                    string reason = GameplayGraphPortPolicy.Unavailable(slot.Owner, slot.Field.Name);
                    if (reason != null) validation.Error(location, reason);
                }
                ValidateArray(actions, scope, validation, location);
            }
        }
    }
}
