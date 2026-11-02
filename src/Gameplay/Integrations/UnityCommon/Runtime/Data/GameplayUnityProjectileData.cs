using FRFrame.Gameplay.Authoring.Graph;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Standard;
using UnityEngine;

namespace FRFrame.Gameplay.Unity
{
    [System.Serializable, UnityEngine.Scripting.Preserve]
    public sealed class GameplayUnityProjectileData:GameplayProjectileDefinitionData
    {
        public GameObject Prefab;
        protected override IGameplaySpatialViewFactory CreateView(GameplayAuthoringBuildContext context)=>Prefab==null?null:new GameplayUnitySpatialViewFactory(Prefab,Plane);
        protected override void ValidateContent(GameplayAuthoringValidationContext v,string p)
        {base.ValidateContent(v,p);if(Prefab==null)v.Error(p+".Prefab","Scene projectile requires an actual prefab; use Logical Projectile for no view.");}
    
    }
}
