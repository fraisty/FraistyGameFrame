using System;
using FRFrame.Gameplay.Authoring.Graph;
using FRFrame.Gameplay.Standard;
using UnityEngine;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>The embedded Schema 2 hit window fields. All callback ports lower to the ordinary Action runner.</summary>
    [Serializable, Preserve]
    public sealed class GameplayHitWindowData : GameplayLocalDefinitionPayload
    {
        public override Type DefinitionType => typeof(GameplayHitWindowDefinition);
        public override object BuildDefinition(GameplayAuthoringBuildContext context) => Build(context);

        public GameplaySearchShape Shape = GameplaySearchShape.Sector;
        public GameplaySpatialPlane Plane;
        public GameplayHitAnchorKind AnchorKind;
        public string AnchorPath;
        public string AnchorBone;
        public Vector3 Offset;
        public float DirectionAngle;
        public float Radius = 2;
        public float Angle = 120;
        public float Length = 2;
        public float Width = 1;
        public float Height = 1;
        public float Duration = .08f;
        public bool FollowAnchor = true;
        public GameplayHitSweepPolicy SweepPolicy = GameplayHitSweepPolicy.Swept;
        public int SamplingBudget = 64;
        public float MaxSweepDistance = 12;
        public float MaxSweepAngle = 135;
        public GameplayFactionFilter Faction = GameplayFactionFilter.Enemies;
        public GameplayTagQueryAuthoring Tags = new GameplayTagQueryAuthoring();
        public int LayerMask = -1;
        public GameplayTargetOrder Order;
        public int MaxDistinctTargets = 1;
        public int MaxHitsPerTarget = 1;
        public float RehitInterval;
        public bool OwnedByAbility = true;
        public GameplayTagReference StopTag;
        [SerializeReference] public GameplaySkillActionAuthoring[] OnHit = Array.Empty<GameplaySkillActionAuthoring>();
        [SerializeReference] public GameplaySkillActionAuthoring[] OnWindowFinished = Array.Empty<GameplaySkillActionAuthoring>();
        [SerializeReference] public GameplaySkillActionAuthoring[] OnCancelled = Array.Empty<GameplaySkillActionAuthoring>();

        public GameplayHitWindowDefinition Build(GameplayAuthoringBuildContext context) => context.Build(this,
            () => CreateDefinition(GameplaySpatialAuthoringUtility.Build(OnHit, context),
                GameplaySpatialAuthoringUtility.Build(OnWindowFinished, context), GameplaySpatialAuthoringUtility.Build(OnCancelled, context)));

        /// <summary>Reads the same canonical geometry for editor previews without compiling unrelated callbacks.</summary>
        public GameplayHitWindowDefinition PreviewDefinition() => CreateDefinition(null, null, null);

        private GameplayHitWindowDefinition CreateDefinition(IGameplaySkillAction[] hit, IGameplaySkillAction[] end, IGameplaySkillAction[] cancel) =>
            new GameplayHitWindowDefinition(Shape, Plane, new GameplayHitAnchor(AnchorKind, AnchorPath, AnchorBone),
                Duration, Radius, Angle, Length, Width, Height, new GameplayPoint(Offset.x, Offset.y, Offset.z), DirectionAngle,
                FollowAnchor, Faction, Tags?.Build(), LayerMask, Order, MaxDistinctTargets, MaxHitsPerTarget, RehitInterval,
                OwnedByAbility, string.IsNullOrEmpty(StopTag.Name) ? default : StopTag.Build(), SweepPolicy, SamplingBudget,
                MaxSweepDistance, MaxSweepAngle, hit, end, cancel);

        protected override void ValidateContent(GameplayAuthoringValidationContext v, string path)
        {
            try { PreviewDefinition(); }
            catch (ArgumentException error) { v.Error(path, error.Message); }
            Tags?.Validate(v, path + ".Tags");
            if(!string.IsNullOrEmpty(StopTag.Name)) v.Tag(StopTag, path + ".StopTag");
            GameplaySpatialAuthoringUtility.Validate(OnHit, v, path + ".OnHit");
            GameplaySpatialAuthoringUtility.Validate(OnWindowFinished, v, path + ".OnWindowFinished");
            GameplaySpatialAuthoringUtility.Validate(OnCancelled, v, path + ".OnCancelled");
        }
    }

    /// <summary>Typed resource slot for HitWindow. The workbench creates embedded values by default.</summary>
    public sealed class GameplayHitWindowDefinitionAsset : GameplayDefinitionAsset
    {
        [SerializeField] private GameplayHitWindowData values = new GameplayHitWindowData();
        /// <summary>Space required by this shared definition when choosing a workbench host.</summary>
        public GameplaySpatialPlane Plane => values.Plane;
        public GameplayHitWindowDefinition Build(GameplayAuthoringBuildContext context) => values.Build(context);
        public override GameplayLocalDefinitionPayload CaptureGraphValues() => (GameplayHitWindowData)GameplayGraphFields.Clone(values);
        public override object BuildSharedDefinition(GameplayAuthoringBuildContext context) => Build(context);
        protected override void ValidateContent(GameplayAuthoringValidationContext v, string path) => values.Validate(v, path);
    }

    [Serializable, Preserve]
    public sealed class GameplayOpenHitWindowActionAuthoring : GameplaySkillActionAuthoring
    {
        public GameplayHitWindowDefinitionAsset Window;
        public override void Validate(GameplayAuthoringValidationContext v, string path) => v.Asset(Window, path + ".Window");
        public override void ValidateReferences(GameplayAuthoringValidationContext v, string path) => Validate(v, path);
        public override IGameplaySkillAction Build(GameplayAuthoringBuildContext context) =>
            new GameplayOpenHitWindowAction(context.ResolveDefinition(Window, this, nameof(Window), () => Window.Build(context)));
    }
}

