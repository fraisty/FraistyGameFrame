using System;
using FRFrame.Gameplay.Standard;

namespace FRFrame.Gameplay.Authoring
{
    internal static class GameplaySpatialAuthoringUtility
    {
        internal static IGameplaySkillAction[] Build(GameplaySkillActionAuthoring[] actions,GameplayAuthoringBuildContext context)=>Array.ConvertAll(actions,a=>context.BuildAction(a));
        internal static void Validate(GameplaySkillActionAuthoring[] actions,GameplayAuthoringValidationContext validation,string path)
        {
            if(actions==null){validation.Error(path,"Callback list is null.");return;}
            for(int i=0;i<actions.Length;i++)if(actions[i]==null)validation.Error(path+"["+i+"]","Action is null.");else actions[i].Validate(validation,path+"["+i+"]");
        }
        internal static void Value(GameplayFloatValueAuthoring value,GameplayAuthoringValidationContext validation,string path)
        {if(value==null)validation.Error(path,"Value is required.");else value.Validate(validation,path);}
    }
}
