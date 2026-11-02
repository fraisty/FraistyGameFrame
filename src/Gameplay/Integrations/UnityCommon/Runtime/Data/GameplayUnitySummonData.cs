using FRFrame.Gameplay.Authoring.Graph;
using System;
using System.Globalization;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Standard;
using UnityEngine;

namespace FRFrame.Gameplay.Unity
{
    /// <summary>Actual pooled prefab binding for a Standard summoned actor. The view lease is returned
    /// only after actor containers/presentation are disposed; a pooled view never retains old grants.</summary>
    [System.Serializable, UnityEngine.Scripting.Preserve]
    public sealed class GameplayUnitySummonData:GameplaySummonDefinitionData
    {
        public GameObject Prefab;
        public GameplayFloatingTextAsset FloatingText;
        protected override IGameplaySummonFactory CreateFactory(GameplayAuthoringBuildContext context)=>new Factory(Prefab,context.ResolveOptionalDefinition(FloatingText, this, nameof(FloatingText), () => FloatingText.Build(context)));
        protected override void ValidateContent(GameplayAuthoringValidationContext validation,string path)
        {base.ValidateContent(validation,path);if(Prefab==null)validation.Error(path+".Prefab","Summon prefab required.");if(FloatingText!=null)validation.Asset(FloatingText,path+".FloatingText");}
        private sealed class Factory:IGameplaySummonFactory
        {
            private readonly GameObject _prefab;private readonly GameplayFloatingTextDefinition _floating;
            internal Factory(GameObject prefab,GameplayFloatingTextDefinition floating){_prefab=prefab;_floating=floating;}
            public GameplaySummonBinding Create(GameplayStandardActor owner,GameplayContext context,GameplayPoint position)
            {
                if(!(owner.Presentation is GameplayUnityAnimationController presenter))throw new InvalidOperationException("Summon prefab needs the real caster presentation binding.");
                var pool=presenter.SpatialViewPool;var key=new ResourceKey("Gameplay.SummonPrefab",_prefab.GetInstanceID().ToString(CultureInfo.InvariantCulture));
                var view=pool.GetOrCreate(key,_prefab.name,_prefab);GameplayStandardActor actor=null;
                try
                {
                    view.transform.SetParent(null,false);view.transform.position=new Vector3(position.X,position.Y,position.Z);
                    foreach(var child in view.GetComponentsInChildren<Transform>(true))child.gameObject.layer=presenter.Subject.layer;
                    foreach(var body in view.GetComponentsInChildren<Collider2D>(true))body.enabled=owner.World.Spatial is GameplayPhysics2DBackend;
                    foreach(var body in view.GetComponentsInChildren<Collider>(true))body.enabled=owner.World.Spatial is GameplayPhysics3DBackend;
                    actor=owner.World.CreateActor(position,owner.Faction,owner.Layer,()=>{var p=view.transform.position;return new GameplayPoint(p.x,p.y,p.z);},p=>view.transform.position=new Vector3(p.X,p.Y,p.Z),
                        ()=>{var d=view.transform.right;return new FRFrame.Gameplay.Extensions.GameplayDirection(d.x,d.y,d.z);},
                        d=>view.transform.rotation=Quaternion.FromToRotation(Vector3.right,new Vector3(d.X,d.Y,d.Z)));
                    var binding=view.GetComponent<GameplayUnityActorBinding>();if(binding==null)binding=view.AddComponent<GameplayUnityActorBinding>();binding.BindExisting(actor);
                    actor.Presentation=new GameplayUnityAnimationController(actor,view){FloatingText=_floating,FloatingTextCamera=presenter.FloatingTextCamera};
                    return new GameplaySummonBinding(actor,new ViewLease(pool,key,_prefab.name,view,binding,actor));
                }
                catch{try{actor?.Dispose();var binding=view.GetComponent<GameplayUnityActorBinding>();if(actor!=null&&binding!=null&&ReferenceEquals(binding.Actor,actor))binding.UnbindExisting(actor);}finally{pool.Release(key,_prefab.name,view);}throw;}
            }
        }
        private sealed class ViewLease:IDisposable
        {
            private readonly GameObjectPoolModule _pool;private readonly ResourceKey _key;private readonly string _location;private GameObject _view;
            private readonly GameplayUnityActorBinding _binding;private readonly GameplayStandardActor _actor;
            internal ViewLease(GameObjectPoolModule pool,ResourceKey key,string location,GameObject view,GameplayUnityActorBinding binding,GameplayStandardActor actor){_pool=pool;_key=key;_location=location;_view=view;_binding=binding;_actor=actor;}
            public void Dispose(){if(_view==null)return;var view=_view;_view=null;try{_binding.UnbindExisting(_actor);}finally{_pool.Release(_key,_location,view);}}
        }
    
    }
}
