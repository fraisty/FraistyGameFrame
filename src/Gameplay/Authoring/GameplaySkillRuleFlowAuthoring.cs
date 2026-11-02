using System;
using System.Collections.Generic;
using FRFrame.Gameplay;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>Nested action tree, directly serialized without graph IDs/edges. Cycles are rejected before Build.</summary>
    [Serializable, Preserve]
    public sealed class GameplaySkillBranchActionAuthoring : GameplaySkillActionAuthoring
    {
        [UnityEngine.SerializeReference] public GameplaySkillConditionAuthoring Condition;
        [UnityEngine.SerializeReference] public GameplaySkillActionAuthoring[] TrueActions = Array.Empty<GameplaySkillActionAuthoring>();
        [UnityEngine.SerializeReference] public GameplaySkillActionAuthoring[] FalseActions = Array.Empty<GameplaySkillActionAuthoring>();
        public override void Validate(GameplayAuthoringValidationContext validation, string path) => ValidateTree(validation, path, new HashSet<GameplaySkillBranchActionAuthoring>());
        // Validate owns both nested content and asset references so callers do not visit each branch twice.
        private void ValidateTree(GameplayAuthoringValidationContext v, string p, HashSet<GameplaySkillBranchActionAuthoring> active)
        {
            if (!active.Add(this)) { v.Error(p, "Circular branch action tree."); return; }
            try
            {
                if (Condition == null) v.Error(p + ".Condition", "Missing branch condition."); else Condition.Validate(v, p + ".Condition");
                ValidateActions(TrueActions, v, p + ".TrueActions", active); ValidateActions(FalseActions, v, p + ".FalseActions", active);
            }
            finally { active.Remove(this); }
        }
        private static void ValidateActions(GameplaySkillActionAuthoring[] actions, GameplayAuthoringValidationContext v, string p, HashSet<GameplaySkillBranchActionAuthoring> active)
        {
            if (actions == null) { v.Error(p, "Missing action list."); return; }
            for (int i=0; i<actions.Length; i++)
            {
                string path = p + "[" + i + "]";
                if (actions[i] == null) v.Error(path, "Null action.");
                else if (actions[i] is GameplaySkillBranchActionAuthoring branch) branch.ValidateTree(v, path, active);
                else { actions[i].Validate(v, path); actions[i].ValidateReferences(v, path); }
            }
        }
        public override IGameplaySkillAction Build(GameplayAuthoringBuildContext context) => BuildTree(context, new HashSet<GameplaySkillBranchActionAuthoring>());
        private IGameplaySkillAction BuildTree(GameplayAuthoringBuildContext context, HashSet<GameplaySkillBranchActionAuthoring> active)
        {
            if (!active.Add(this)) throw new InvalidOperationException("Circular branch action tree.");
            try { return new GameplaySkillBranchAction(Condition.Build(), BuildActions(TrueActions, context, active), BuildActions(FalseActions, context, active)); }
            finally { active.Remove(this); }
        }
        private static IEnumerable<IGameplaySkillAction> BuildActions(GameplaySkillActionAuthoring[] actions, GameplayAuthoringBuildContext context, HashSet<GameplaySkillBranchActionAuthoring> active)
        {
            var built = new List<IGameplaySkillAction>();
            foreach (var action in actions) built.Add(context.BuildAction(action,()=>action is GameplaySkillBranchActionAuthoring branch ? branch.BuildTree(context, active) : action.Build(context)));
            return built;
        }
    }
    /// <summary>Finite context scalar comparison, used by authored Branch conditions.</summary>
    /// <summary>Compares a typed scalar from the current branch context; missing keys are false.</summary>
    [Serializable, Preserve]
    public sealed class GameplaySkillContextValueConditionAuthoring : GameplaySkillConditionAuthoring
    {
        public GameplayTagReference Key;
        public GameplaySkillContextComparison Comparison;
        public float Constant;
        public override void Validate(GameplayAuthoringValidationContext v, string p) { v.Tag(Key,p+".Key"); v.Enum(Comparison,p+".Comparison"); v.Finite(Constant,p+".Constant"); }
        public override IGameplaySkillCondition Build() => new GameplaySkillContextValueCondition(Key.Build(), Comparison, Constant);
    }
    /// <summary>Writes a constant to the derived context; no mutable value is stored in the asset at runtime.</summary>
    /// <summary>Produces an immutable context snapshot with one authored finite scalar added/replaced.</summary>
    [Serializable, Preserve]
    public sealed class GameplaySkillSetContextValueActionAuthoring : GameplaySkillActionAuthoring
    {
        public GameplayTagReference Key;
        public float Value;
        public override void Validate(GameplayAuthoringValidationContext v, string p) { v.Tag(Key,p+".Key"); v.Finite(Value,p+".Value"); }
        public override IGameplaySkillAction Build(GameplayAuthoringBuildContext context) => new GameplaySkillSetContextValueAction(Key.Build(),Value);
    }
}
