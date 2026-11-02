using FRFrame.Gameplay.Authoring.Graph;
using System;
using System.Collections.Generic;
using FRFrame.Gameplay;
using UnityEngine;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>Authored child callback list; the game owns callback routing and the child's disposable lifetime.</summary>
    [System.Serializable, UnityEngine.Scripting.Preserve]
    public sealed class GameplayContinuationDefinitionData : GameplayLocalDefinitionPayload
    {
        public override System.Type DefinitionType => typeof(GameplayContinuationDefinition);
        public override object BuildDefinition(GameplayAuthoringBuildContext context) => Build(context);

        [SerializeReference] public GameplaySkillActionAuthoring[] Actions = Array.Empty<GameplaySkillActionAuthoring>();
        protected override void ValidateContent(GameplayAuthoringValidationContext v, string p)
        {
            if (Actions == null) { v.Error(p+".Actions","Missing action list."); return; }
            for (int i=0;i<Actions.Length;i++)
            {
                string path=p+".Actions["+i+"]";
                if (Actions[i]==null) v.Error(path,"Null action.");
                else { Actions[i].Validate(v,path); Actions[i].ValidateReferences(v,path); }
            }
        }
        public GameplayContinuationDefinition Build(GameplayAuthoringBuildContext context)
        {
            if(context==null) throw new ArgumentNullException(nameof(context));
            return context.Build(this,()=> { var actions=new List<IGameplaySkillAction>(); foreach(var action in Actions) actions.Add(context.BuildAction(action)); return new GameplayContinuationDefinition(actions); });
        }
    
    }
}
