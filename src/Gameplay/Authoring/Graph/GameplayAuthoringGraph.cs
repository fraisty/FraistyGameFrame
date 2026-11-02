using System;
using System.Collections.Generic;
using UnityEngine;

namespace FRFrame.Gameplay.Authoring.Graph
{
    /// <summary>Marks persistent authoring identity that does not enter runtime behavior values.</summary>
    [AttributeUsage(AttributeTargets.Field)] public sealed class GameplayAuthoringIdentityAttribute:Attribute { }
    /// <summary>The sole persisted behavior source of a schema-2 SkillDocument. Layout never
    /// participates in compilation; all executable ordering is carried by connections/events.</summary>
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true,"FRFrame.Gameplay.Authoring.V2","FRFrame.Gameplay.Authoring","GameplayAuthoringGraph")]
    public sealed class GameplayAuthoringGraph
    {
        public const int CurrentVersion = 2;
        public int Version = CurrentVersion;
        public string Scope = "FRFrame";
        public string AbilityNodeId;
        public string PrimaryRuleNodeId;
        public List<string> AdditionalRuleNodeIds = new List<string>();
        public List<GameplayAuthoringNode> Nodes = new List<GameplayAuthoringNode>();
        public List<GameplayAuthoringConnection> Connections = new List<GameplayAuthoringConnection>();
        public List<GameplayAuthoringTimeline> Timelines = new List<GameplayAuthoringTimeline>();
        public List<GameplayLocalDefinition> LocalDefinitions = new List<GameplayLocalDefinition>();
        public List<GameplayAuthoringLegacyResource> LegacyResources = new List<GameplayAuthoringLegacyResource>();

        /// <summary>New entities receive an identity once. Reordering and moving never regenerate it.</summary>
        public static string NewId() => Guid.NewGuid().ToString("N");
    }

    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true,"FRFrame.Gameplay.Authoring.V2","FRFrame.Gameplay.Authoring","GameplayAuthoringNode")]
    public sealed class GameplayAuthoringNode
    {
        public string Id = GameplayAuthoringGraph.NewId();
        public string Title;
        [SerializeReference] public GameplayAuthoringNodePayload Payload;
        public Vector2 Position;
        public Vector2 Size;
        public bool Expanded = true;
        public float TimelineZoom = 1;
        public float TimelineStart;
        /// <summary>Explicit creation provenance for the "create owned callback" macro only.
        /// A lone incoming edge never establishes ownership. Reused receivers are retained.</summary>
        [HideInInspector] public string CallbackOwnerDefinitionId;
        // Migration provenance survives without retaining a mutable V1 authoring asset.
        public string SourceDefinitionId;
        public long SourceActionId = -1;
    }

    /// <summary>Port keys describe actual consumers, not visual coordinates. Order is local to
    /// one output array and remains authoritative after save/reorder/Undo.</summary>
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true,"FRFrame.Gameplay.Authoring.V2","FRFrame.Gameplay.Authoring","GameplayAuthoringConnection")]
    public sealed class GameplayAuthoringConnection
    {
        public string Id = GameplayAuthoringGraph.NewId();
        public string FromNodeId;
        public string OutputPortKey;
        public string ToNodeId;
        public string InputPortKey = "Execute";
        public int Order;
    }

    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true,"FRFrame.Gameplay.Authoring.V2","FRFrame.Gameplay.Authoring","GameplayAuthoringNodePayload")]
    public abstract class GameplayAuthoringNodePayload
    {
        public List<GameplayDefinitionBinding> Definitions = new List<GameplayDefinitionBinding>();
    }

    /// <summary>Existing value/action authoring is reused as an embedded parameter payload.
    /// In schema 2 all child action arrays and definition SO slots must be empty: explicit
    /// connections and Definitions bindings own those relationships.</summary>
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true,"FRFrame.Gameplay.Authoring.V2","FRFrame.Gameplay.Authoring","GameplayActionNodePayload")]
    public sealed class GameplayActionNodePayload : GameplayAuthoringNodePayload
    {
        [SerializeReference] public GameplaySkillActionAuthoring Action;
    }

    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true,"FRFrame.Gameplay.Authoring.V2","FRFrame.Gameplay.Authoring","GameplayRuleNodePayload")]
    public sealed class GameplayRuleNodePayload : GameplayAuthoringNodePayload
    {
        public bool Once;
        public bool PreserveTriggerContext;
        [SerializeReference] public GameplaySkillTriggerAuthoring Trigger;
        [SerializeReference] public GameplaySkillConditionAuthoring[] Conditions = Array.Empty<GameplaySkillConditionAuthoring>();
        [SerializeReference] public GameplaySkillTargetAuthoring Target;
    }

    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true,"FRFrame.Gameplay.Authoring.V2","FRFrame.Gameplay.Authoring","GameplayAbilityNodePayload")]
    public sealed class GameplayAbilityNodePayload : GameplayAuthoringNodePayload
    {
        [SerializeReference] public GameplayLocalDefinitionPayload Settings;
    }

    /// <summary>References one local definition OR one deliberately shared external definition.
    /// An empty optional binding is absent; local and external cannot both be set.</summary>
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true,"FRFrame.Gameplay.Authoring.V2","FRFrame.Gameplay.Authoring","GameplayDefinitionBinding")]
    public sealed class GameplayDefinitionBinding
    {
        public string PropertyPath;
        public string LocalId;
        public GameplayDefinitionAsset External;
    }

    /// <summary>A reusable value definition inside this document. Its stable identity is shared
    /// by all local consumers; it is never materialized as a temporary or persistent SO.</summary>
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true,"FRFrame.Gameplay.Authoring.V2","FRFrame.Gameplay.Authoring","GameplayLocalDefinition")]
    public sealed class GameplayLocalDefinition
    {
        public string Id = GameplayAuthoringGraph.NewId();
        public string Name;
        public string SourceDefinitionId;
        [SerializeReference] public GameplayLocalDefinitionPayload Payload;
        public List<GameplayDefinitionBinding> Definitions = new List<GameplayDefinitionBinding>();
    }

    /// <summary>Unity integrations supply concrete serializable value builders in their own
    /// assembly. The Authoring assembly has no reverse dependency on SDKs or game assemblies.</summary>
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true,"FRFrame.Gameplay.Authoring.V2","FRFrame.Gameplay.Authoring","GameplayLocalDefinitionPayload")]
    public abstract class GameplayLocalDefinitionPayload
    {
        public abstract Type DefinitionType { get; }
        public abstract object BuildDefinition(GameplayAuthoringBuildContext context);
        public virtual string CapabilityNote => null;
        public void Validate(GameplayAuthoringValidationContext validation, string path) => ValidateContent(validation, path);
        protected abstract void ValidateContent(GameplayAuthoringValidationContext validation, string path);
        protected static T Instantiate<T>(T source) where T : UnityEngine.Object => UnityEngine.Object.Instantiate(source);
    }

    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true,"FRFrame.Gameplay.Authoring.V2","FRFrame.Gameplay.Authoring","GameplayAuthoringTimeline")]
    public sealed class GameplayAuthoringTimeline
    {
        public string Id = GameplayAuthoringGraph.NewId();
        public string Name;
        public string SourceDefinitionId;
        public float DurationSeconds = 1;
        public int FramesPerSecond = 30;
        public List<GameplayScheduledEntry> Events = new List<GameplayScheduledEntry>();
    }

    /// <summary>A timed invocation, independent from another event at the same second.
    /// TimeSeconds is the rule authority; frames are a display/snap policy only.</summary>
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true,"FRFrame.Gameplay.Authoring.V2","FRFrame.Gameplay.Authoring","GameplayScheduledEntry")]
    public sealed class GameplayScheduledEntry
    {
        public string Id = GameplayAuthoringGraph.NewId();
        public float TimeSeconds;
        public int Order;
        public string EntryNodeId;
        [SerializeReference] public GameplaySkillConditionAuthoring[] Conditions = Array.Empty<GameplaySkillConditionAuthoring>();
        [SerializeReference] public GameplaySkillTargetAuthoring Target;
    }

    /// <summary>Preserved migration material, explicitly excluded from the executable graph.</summary>
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true,"FRFrame.Gameplay.Authoring.V2","FRFrame.Gameplay.Authoring","GameplayAuthoringLegacyResource")]
    public sealed class GameplayAuthoringLegacyResource
    {
        public UnityEngine.Object Resource;
        public string Reason;
    }
}
