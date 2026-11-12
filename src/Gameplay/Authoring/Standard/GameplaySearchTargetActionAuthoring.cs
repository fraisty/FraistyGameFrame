using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Extensions;
using FRFrame.Gameplay.Standard;
using UnityEngine;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>Standard search with actual logical/Physics consumers. All dimensions use the common
    /// value AST; authoring callback lists build in the same identity-preserving session.</summary>
    [Serializable, Preserve]
    public sealed class GameplaySearchTargetActionAuthoring : GameplaySkillActionAuthoring
    {
        public GameplaySearchShape Shape = GameplaySearchShape.Sector;
        public GameplaySpatialPlane Plane = GameplaySpatialPlane.XY;
        public GameplaySearchOrigin Origin;
        [Tooltip("所选原点的世界坐标偏移；用于脚底Actor与技能查询中心不同的真实绑定。")]
        public Vector3 OriginOffset;
        public GameplaySearchDirection Direction;
        [Tooltip("绝对角度或朝向偏移，单位度；与实际查询/范围预览共用表达式。")]
        [SerializeReference] public GameplayFloatValueAuthoring DirectionAngle = new GameplayConstantFloatValueAuthoring { Value = 0 };
        [Tooltip("线/盒/射线/胶囊长度使用原点到所选目标/地点的距离；不会读取 Length。")]
        public bool LengthFromTarget;
        [SerializeReference] public GameplayFloatValueAuthoring Radius = new GameplayConstantFloatValueAuthoring { Value = 6 };
        [SerializeReference] public GameplayFloatValueAuthoring Angle = new GameplayConstantFloatValueAuthoring { Value = 120 };
        [SerializeReference] public GameplayFloatValueAuthoring Length = new GameplayConstantFloatValueAuthoring { Value = 6 };
        [SerializeReference] public GameplayFloatValueAuthoring Width = new GameplayConstantFloatValueAuthoring { Value = 2 };
        [SerializeReference] public GameplayFloatValueAuthoring Height = new GameplayConstantFloatValueAuthoring { Value = 1 };
        public GameplayFactionFilter Faction = GameplayFactionFilter.Enemies;
        public GameplayTagQueryAuthoring Tags = new GameplayTagQueryAuthoring();
        public int LayerMask = -1;
        [Min(1)] public int MaxTargets = 32;
        public GameplayTargetOrder Order = GameplayTargetOrder.Nearest;
        public bool IncludeSelf;
        [Tooltip("跳过输入的主要目标，例如弹体命中后为子弹重选目标。")]
        public bool ExcludePrimaryTarget;
        [Tooltip("每次选最近的未访问目标，再从命中地点重选。目标上限是总次数。")]
        public bool RepeatFromResult;
        [Range(0,1), Tooltip("下一跳的倍率乘此值。逐目标下游读取 Context.Data.Standard.Search.Multiplier。")]
        public float RepeatMultiplier = 1;
        /// <summary>Seconds between repeated queries. Zero executes every hop in the same rule step.</summary>
        [Tooltip("0 在同一规则帧完成；正值仅用于圆形逐次重选，首跳立即查询，之后按世界时钟重选。最终结果由搜索完成输出。")]
        public float HopIntervalSeconds;
        [Tooltip("世界坐标单元中心；按 CellSize 对所在格进行匹配，不依赖 Physics。")]
        public Vector3[] Cells = Array.Empty<Vector3>();
        public float CellSize = 1;
        [SerializeReference] public GameplaySkillActionAuthoring[] OnBegin = Array.Empty<GameplaySkillActionAuthoring>();
        [SerializeReference] public GameplaySkillActionAuthoring[] EachTarget = Array.Empty<GameplaySkillActionAuthoring>();
        [SerializeReference] public GameplaySkillActionAuthoring[] Completed = Array.Empty<GameplaySkillActionAuthoring>();
        [SerializeReference] public GameplaySkillActionAuthoring[] NoTargets = Array.Empty<GameplaySkillActionAuthoring>();
        public override void Validate(GameplayAuthoringValidationContext v, string p)
        {
            v.Enum(Shape, p + ".Shape"); v.Enum(Plane, p + ".Plane"); v.Enum(Origin, p + ".Origin");
            if (RepeatFromResult && (Shape != GameplaySearchShape.Circle || !(RepeatMultiplier >= 0 && RepeatMultiplier <= 1)))
                v.Error(p + ".RepeatFromResult", "逐次重选需要圆形查询与 0–1 的倍率。");
            if (!float.IsFinite(HopIntervalSeconds) || HopIntervalSeconds < 0 ||
                (HopIntervalSeconds > 0 && (!RepeatFromResult || Shape != GameplaySearchShape.Circle)))
                v.Error(p + ".HopIntervalSeconds", "逐跳间隔必须是非负有限秒数；正值需要圆形查询和逐次重选。");
            if(!float.IsFinite(OriginOffset.x)||!float.IsFinite(OriginOffset.y)||!float.IsFinite(OriginOffset.z))v.Error(p+".OriginOffset","原点偏移必须有限。");
            if (Directional) { v.Enum(Direction,p+".Direction"); DirectionAngle?.Validate(v,p+".DirectionAngle"); }
            if (UsesLength && LengthFromTarget && Direction != GameplaySearchDirection.ToPrimaryTarget && Direction != GameplaySearchDirection.ToContextPoint)
                v.Error(p+".LengthFromTarget","两点长度要求线/盒/射线/胶囊，方向选择目标或地点。");
            v.Enum(Faction, p + ".Faction"); v.Enum(Order, p + ".Order");
            if (MaxTargets < 1) v.Error(p + ".MaxTargets", "MaxTargets must be positive.");
            if (Shape == GameplaySearchShape.GridCells && (!float.IsFinite(CellSize) || CellSize <= 0)) v.Error(p + ".CellSize", "CellSize must be finite and positive.");
            if (UsesRadius) Value(v, p + ".Radius", Radius);
            if (Shape == GameplaySearchShape.Sector) { Value(v, p + ".Angle", Angle);
                if (Angle is GameplayConstantFloatValueAuthoring constant && constant.Value > 360) v.Error(p + ".Angle", "Angle must be at most 360 degrees."); }
            if (UsesLength && !LengthFromTarget) Value(v, p + ".Length", Length);
            if (UsesWidth) Value(v, p + ".Width", Width);
            Value(v, p + ".Height", Height);
            Tags?.Validate(v, p + ".Tags");
            if (Shape == GameplaySearchShape.GridCells) {
                if (Cells == null || Cells.Length == 0) v.Error(p + ".Cells", "Configure grid cells.");
                else for (int i = 0; i < Cells.Length; i++) { v.Finite(Cells[i].x, p + ".Cells.X"); v.Finite(Cells[i].y, p + ".Cells.Y"); v.Finite(Cells[i].z, p + ".Cells.Z"); }
            }
            Actions(v, p + ".OnBegin", OnBegin); Actions(v, p + ".EachTarget", EachTarget); Actions(v, p + ".Completed", Completed); Actions(v, p + ".NoTargets", NoTargets);
        }
        private static void Value(GameplayAuthoringValidationContext v, string p, GameplayFloatValueAuthoring value)
        {
            if (value == null) { v.Error(p, "Missing dimension."); return; } value.Validate(v, p);
            if (value is GameplayConstantFloatValueAuthoring constant && constant.Value <= 0) v.Error(p, "Dimension must be positive.");
        }
        private static void Actions(GameplayAuthoringValidationContext v, string p, GameplaySkillActionAuthoring[] actions)
        {
            if (actions == null) { v.Error(p, "Callback list is null."); return; }
            for (int i = 0; i < actions.Length; i++) if (actions[i] == null) v.Error(p + $"[{i}]", "Missing action."); else actions[i].Validate(v, p + $"[{i}]");
        }
        public override void ValidateReferences(GameplayAuthoringValidationContext v, string p)
        {
            foreach (var list in new[] { OnBegin, EachTarget, Completed, NoTargets })
                if (list != null) for (int i = 0; i < list.Length; i++) list[i]?.ValidateReferences(v, p + $".Callbacks[{i}]");
        }
        public override IGameplaySkillAction Build(GameplayAuthoringBuildContext context)
        {
            var cells = new List<GameplayPoint>(); if (Shape == GameplaySearchShape.GridCells && Cells != null) foreach (var cell in Cells) cells.Add(new GameplayPoint(cell.x, cell.y, cell.z));
            return new GameplaySearchTargetAction(Shape, Plane, Origin, Dimension(Radius,UsesRadius), Dimension(Angle,Shape == GameplaySearchShape.Sector), Dimension(Length,UsesLength && !LengthFromTarget), Dimension(Width,UsesWidth), Height.Build(),
                Faction, Tags?.Build(), LayerMask, MaxTargets, Order, IncludeSelf,
                Build(EachTarget, context), Build(Completed, context), Build(NoTargets, context), cells, Shape == GameplaySearchShape.GridCells ? CellSize : 1,
                Directional ? Direction : GameplaySearchDirection.OwnerFacing, Directional ? DirectionAngle?.Build() : null,
                UsesLength && LengthFromTarget,new GameplayPoint(OriginOffset.x,OriginOffset.y,OriginOffset.z),
                ExcludePrimaryTarget, RepeatFromResult, RepeatMultiplier, Build(OnBegin, context), HopIntervalSeconds);
        }
        // Inactive dimensions remain serialized drafts. Never build/evaluate their AST until a shape consumes them.
        private bool UsesRadius => Shape == GameplaySearchShape.Circle || Shape == GameplaySearchShape.Sector || Shape == GameplaySearchShape.Capsule;
        private bool UsesLength => Shape == GameplaySearchShape.Box || Shape == GameplaySearchShape.Line || Shape == GameplaySearchShape.Ray || Shape == GameplaySearchShape.Capsule;
        private bool UsesWidth => Shape == GameplaySearchShape.Box || Shape == GameplaySearchShape.Line || Shape == GameplaySearchShape.Ray;
        internal bool Directional => Shape == GameplaySearchShape.Sector || UsesLength;
        private static IGameplayFloatValue Dimension(GameplayFloatValueAuthoring value,bool used) => used ? value.Build() : new GameplayConstantFloatValue(1);
        private static IEnumerable<IGameplaySkillAction> Build(GameplaySkillActionAuthoring[] nodes, GameplayAuthoringBuildContext context)
        { foreach (var node in nodes) yield return context.BuildAction(node); }
    }
    [Serializable, Preserve]
    public sealed class GameplayApplyToTargetsActionAuthoring : GameplaySkillActionAuthoring
    {
        public GameplayEffectDefinitionAsset Effect;
        public bool OwnedByAbility;
        public override bool SupportsEffectLifecycle => !OwnedByAbility;
        public override void Validate(GameplayAuthoringValidationContext v, string p)
        {
            v.Asset(Effect, p + ".Effect");
            if (OwnedByAbility && Effect != null && Effect.Stacking != null) v.Error(p + ".OwnedByAbility", "Ability-owned target effects must be unstacked; shared Area buffs use contribution leases.");
        }
        public override void ValidateReferences(GameplayAuthoringValidationContext v, string p) => v.Asset(Effect, p + ".Effect");
        public override IGameplaySkillAction Build(GameplayAuthoringBuildContext context) => new GameplayApplyToTargetsAction(context.ResolveDefinition(Effect, this, nameof(Effect), () => Effect.Build(context)), OwnedByAbility);
    }
}
