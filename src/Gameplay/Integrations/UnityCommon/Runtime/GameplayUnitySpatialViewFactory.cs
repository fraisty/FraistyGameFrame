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
        private readonly bool _fitCircle;
        private readonly float _fadeSeconds;
        internal GameplayUnitySpatialViewFactory(GameObject prefab,GameplaySpatialPlane plane,bool fitCircle=false,float fadeSeconds=0)
        {_prefab=prefab;_plane=plane;_fitCircle=fitCircle;_fadeSeconds=fadeSeconds;}
        public IGameplaySpatialView Create(GameplayStandardActor actor,GameplayContext context)
        {
            if(!(actor.Presentation is GameplayUnityAnimationController presentation))throw new InvalidOperationException("Unity spatial view requires the actual actor presentation binding.");
            var pool=presentation.SpatialViewPool;var key=new ResourceKey("Gameplay.DirectPrefab",_prefab.GetInstanceID().ToString(CultureInfo.InvariantCulture));
            var view=pool.GetOrCreate(key,_prefab.name,_prefab);
            view.transform.SetParent(null,false);
            view.transform.localScale=_prefab.transform.localScale;
            int layer=presentation.ViewRenderLayer??presentation.Subject.layer;
            foreach(var child in view.GetComponentsInChildren<Transform>(true))child.gameObject.layer=layer;
            foreach(var particles in view.GetComponentsInChildren<ParticleSystem>()){var main=particles.main;main.stopAction=ParticleSystemStopAction.None;particles.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);particles.Play(false);}
            return new View(view,pool,key,_prefab.name,_plane,_fitCircle,_fadeSeconds);
        }
        private sealed class View:IGameplaySpatialView,IGameplayAreaGeometryView
        {
            private GameObject _view;
            private readonly GameObjectPoolModule _pool;
            private readonly ResourceKey _key;
            private readonly string _location;
            private readonly GameplaySpatialPlane _plane;
            private readonly bool _fitCircle;
            private readonly float _fadeSeconds;
            private readonly TrailRenderer[] _trails;
            private readonly (SpriteRenderer Sprite, Color Color)[] _fadeSprites;
            private bool _positioned;
            internal View(GameObject view,GameObjectPoolModule pool,ResourceKey key,string location,GameplaySpatialPlane plane,bool fitCircle,float fadeSeconds)
            {
                _view=view;_pool=pool;_key=key;_location=location;_plane=plane;_fitCircle=fitCircle;_fadeSeconds=fadeSeconds;
                _trails=view.GetComponentsInChildren<TrailRenderer>(true);
                if (fadeSeconds>0)
                    _fadeSprites=Array.ConvertAll(view.GetComponentsInChildren<SpriteRenderer>(true),sprite=>(sprite,sprite.color));
            }
            public void SetGeometry(GameplaySearchGeometry geometry)
            {
                if (_fitCircle) _view.transform.localScale *= 2 * geometry.Radius;
            }
            public void SyncLifetime(float age,float duration)
            {
                if (_fadeSprites==null) return;
                float alpha=Mathf.Clamp01(Mathf.Min(age,duration-age)/_fadeSeconds);
                foreach(var item in _fadeSprites){var color=item.Color;color.a*=alpha;item.Sprite.color=color;}
            }
            public void Sync(GameplayPoint point,GameplayDirection direction)
            {
                if(_view==null)return;_view.transform.position=new Vector3(point.X,point.Y,point.Z);
                _view.transform.rotation=_plane==GameplaySpatialPlane.XY?Quaternion.Euler(0,0,Mathf.Atan2(direction.Y,direction.X)*Mathf.Rad2Deg):
                    Quaternion.LookRotation(new Vector3(direction.X,direction.Y,direction.Z),Mathf.Abs(direction.Y)>.99f?Vector3.forward:Vector3.up);
                if(!_positioned){foreach(var trail in _trails)trail.Clear();_positioned=true;}
            }
            public void Dispose()
            {
                if(_view==null)return;
                foreach(var trail in _trails)trail.Clear();
                if(_fadeSprites!=null)foreach(var item in _fadeSprites)item.Sprite.color=item.Color;
                foreach(var particles in _view.GetComponentsInChildren<ParticleSystem>())particles.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                _pool.Release(_key,_location,_view);_view=null;
            }
        }
    }
}
