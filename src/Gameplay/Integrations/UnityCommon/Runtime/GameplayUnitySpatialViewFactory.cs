using System;
using System.Globalization;
using FRFrame.Gameplay.Extensions;
using FRFrame.Gameplay.Standard;
using UnityEngine;

namespace FRFrame.Gameplay.Unity
{
    /// <summary>Actual scene projection for neutral spatial rules. Shares the actor's existing
    /// FRFrame pool; never ticks a motion, collision, lifetime or gameplay container.</summary>
    internal sealed class GameplayUnitySpatialViewFactory:IGameplaySpatialViewFactory
    {
        private readonly GameObject _prefab;
        private readonly GameplaySpatialPlane _plane;
        internal GameplayUnitySpatialViewFactory(GameObject prefab,GameplaySpatialPlane plane){_prefab=prefab;_plane=plane;}
        public IGameplaySpatialView Create(GameplayStandardActor actor,GameplayContext context)
        {
            if(!(actor.Presentation is GameplayUnityAnimationController presentation))throw new InvalidOperationException("Unity spatial view requires the actual actor presentation binding.");
            var pool=presentation.SpatialViewPool;var key=new ResourceKey("Gameplay.DirectPrefab",_prefab.GetInstanceID().ToString(CultureInfo.InvariantCulture));
            var view=pool.GetOrCreate(key,_prefab.name,_prefab);
            view.transform.SetParent(null,false);
            foreach(var child in view.GetComponentsInChildren<Transform>())child.gameObject.layer=presentation.Subject.layer;
            foreach(var particles in view.GetComponentsInChildren<ParticleSystem>()){var main=particles.main;main.stopAction=ParticleSystemStopAction.None;particles.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);particles.Play(false);}
            return new View(view,pool,key,_prefab.name,_plane);
        }
        private sealed class View:IGameplaySpatialView
        {
            private GameObject _view;
            private readonly GameObjectPoolModule _pool;
            private readonly ResourceKey _key;
            private readonly string _location;
            private readonly GameplaySpatialPlane _plane;
            internal View(GameObject view,GameObjectPoolModule pool,ResourceKey key,string location,GameplaySpatialPlane plane){_view=view;_pool=pool;_key=key;_location=location;_plane=plane;}
            public void Sync(GameplayPoint point,GameplayDirection direction)
            {
                if(_view==null)return;_view.transform.position=new Vector3(point.X,point.Y,point.Z);
                _view.transform.rotation=_plane==GameplaySpatialPlane.XY?Quaternion.Euler(0,0,Mathf.Atan2(direction.Y,direction.X)*Mathf.Rad2Deg):
                    Quaternion.LookRotation(new Vector3(direction.X,direction.Y,direction.Z),Mathf.Abs(direction.Y)>.99f?Vector3.forward:Vector3.up);
            }
            public void Dispose()
            {
                if(_view==null)return;
                foreach(var particles in _view.GetComponentsInChildren<ParticleSystem>())particles.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                _pool.Release(_key,_location,_view);_view=null;
            }
        }
    }
}
