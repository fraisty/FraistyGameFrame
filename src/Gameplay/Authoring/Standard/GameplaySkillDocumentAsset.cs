using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Standard;
using UnityEngine;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>Player-safe skill identity and installation entry. Schema 2 owns one embedded
    /// behavior graph; immutable runtime definitions are compiled values, never editable mirrors.</summary>
    public sealed class GameplaySkillDocumentAsset : GameplayDefinitionAsset
    {
        [Min(1)] public int SkillId = 2010;
        public string DisplayName = "新技能";
        public Sprite Icon;
        [SerializeField] private int authoringSchemaVersion = 1;
        [SerializeField] private Graph.GameplayAuthoringGraph authoringGraph;
        public int AuthoringSchemaVersion => authoringSchemaVersion;
        public Graph.GameplayAuthoringGraph AuthoringGraph => authoringGraph;
        /// <summary>Commits a prevalidated V2 source for migration/new-document transactions.
        /// The caller owns Undo. Old execution roots are cleared so there is one authority.</summary>
        public void SetAuthoringGraph(Graph.GameplayAuthoringGraph graph)
        {
            if (graph == null || graph.Version != Graph.GameplayAuthoringGraph.CurrentVersion)
                throw new ArgumentException("A supported authoring graph is required.", nameof(graph));
            authoringGraph = graph; authoringSchemaVersion = Graph.GameplayAuthoringGraph.CurrentVersion;
            Ability = null; Rules = null; AdditionalRules = Array.Empty<GameplaySkillDefinitionAsset>();
        }
        /// <summary>Effective authoring metadata used by scene equipment validation. V2 never
        /// falls back to an obsolete Ability SO when its graph is incomplete.</summary>
        public int EffectiveMaxLevel
        {
            get
            {
                if (authoringSchemaVersion != 2) return 0;
                var node = authoringGraph?.Nodes.Find(n => n.Id == authoringGraph.AbilityNodeId);
                var ability = node?.Payload as Graph.GameplayAbilityNodePayload;
                if (ability?.Settings is GameplayAbilityDefinitionData settings) return settings.MaxLevel;
                return (ability?.Definitions.Find(b => b.PropertyPath == "Settings")?.External as GameplayAbilityDefinitionAsset)?.MaxLevel ?? 0;
            }
        }
        /// <summary>Display-only cost source for the existing skill-slot snapshot. Payment is
        /// still performed exclusively by the installed immutable Ability definition.</summary>
        public GameplayAbilityCostAuthoring EffectiveCost
        {
            get
            {
                if (authoringSchemaVersion != 2) return null;
                var node = authoringGraph?.Nodes.Find(n => n.Id == authoringGraph.AbilityNodeId);
                var ability = node?.Payload as Graph.GameplayAbilityNodePayload;
                if (ability?.Settings is GameplayAbilityDefinitionData settings) return settings.Cost;
                return (ability?.Definitions.Find(b => b.PropertyPath == "Settings")?.External as GameplayAbilityDefinitionAsset)?.Cost;
            }
        }
        [SerializeField,HideInInspector] private GameplayAbilityDefinitionAsset Ability;
        [SerializeField,HideInInspector] private GameplaySkillDefinitionAsset Rules;
        [SerializeField,HideInInspector] private GameplaySkillDefinitionAsset[] AdditionalRules = Array.Empty<GameplaySkillDefinitionAsset>();
        public GameplayStandardActivationMode Mode;
        public bool RunRulesOnPassiveActivation;
        public GameplayStandardEventSubject Subject = GameplayStandardEventSubject.OwnerTarget;
        public GameplayTagReference CastEvent;
        public GameplayScalarInputAuthoring[] Defaults = Array.Empty<GameplayScalarInputAuthoring>();
        [SerializeReference] public GameplaySkillLearningRequirementAuthoring[] LearningRequirements=Array.Empty<GameplaySkillLearningRequirementAuthoring>();
        public override object BuildSharedDefinition(GameplayAuthoringBuildContext context) => Build(context);
        protected override void ValidateContent(GameplayAuthoringValidationContext v, string p)
        {
            if (SkillId <= 0) v.Error(p + ".SkillId", "SkillId must be positive.");
            if(LearningRequirements==null)v.Error(p+".LearningRequirements","Missing learning requirements.");
            else for(int i=0;i<LearningRequirements.Length;i++){
                if(LearningRequirements[i]==null)v.Error(p+".LearningRequirements["+i+"]","Missing learning requirement.");
                else LearningRequirements[i].Validate(v,p+".LearningRequirements["+i+"]");
            }
            if (authoringSchemaVersion == 2) Graph.GameplayGraphCompiler.ValidateStructure(authoringGraph, v, p + ".AuthoringGraph");
            else v.Error(p,"Unsupported authoring schema. Only the embedded behavior graph can build.");
            v.Enum(Mode, p + ".Mode"); v.Enum(Subject, p + ".Subject");
            if(RunRulesOnPassiveActivation&&Mode!=GameplayStandardActivationMode.Passive)v.Error(p,"仅被动技能可配置启动规则。");
            if (Mode == GameplayStandardActivationMode.Active||RunRulesOnPassiveActivation)
            {
                v.Tag(CastEvent, p + ".CastEvent");
            }
            if (Defaults == null) v.Error(p + ".Defaults", "Scalar defaults are null.");
            else
            {
                var keys = new HashSet<string>();
                for (int i = 0; i < Defaults.Length; i++)
                {
                    if (Defaults[i] == null) { v.Error(p + $".Defaults[{i}]", "Missing scalar input."); continue; }
                    v.Tag(Defaults[i].Key, p + $".Defaults[{i}].Key"); v.Finite(Defaults[i].Value, p + $".Defaults[{i}].Value");
                    if (!keys.Add(Defaults[i].Key.Name)) v.Error(p + $".Defaults[{i}]", "Duplicate default key.");
                }
            }
        }
        public GameplaySkillDocumentDefinition Build(GameplayAuthoringBuildContext context)
        {
            if(authoringSchemaVersion==2)return Graph.GameplayGraphCompiler.Build(this,authoringGraph,context);
            throw new InvalidOperationException("Unsupported skill authoring schema. Only Schema 2 behavior graphs are executable.");
        }
        /// <summary>Explicit local library binding used by non-cast Lab controls. The stable ID
        /// selects the same embedded values that the Workbench edits.</summary>
        public T BuildLocalResource<T>(string localId,GameplayAuthoringBuildContext context) where T:class
        {
            if(authoringSchemaVersion!=2)throw new InvalidOperationException("Local resources require a Schema 2 behavior graph.");
            return Graph.GameplayGraphCompiler.BuildLocalResource<T>(this,localId,context);
        }

    }
    [Serializable]
    public sealed class GameplayScalarInputAuthoring { public GameplayTagReference Key; public float Value; }
}
