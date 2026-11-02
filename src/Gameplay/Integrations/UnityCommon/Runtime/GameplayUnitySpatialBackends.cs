using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Standard;
using UnityEngine;

namespace FRFrame.Gameplay.Unity
{
    /// <summary>Real Physics2D broadphase followed by the exact same centre geometry/filter used by
    /// the logical backend. Collider ownership resolution is injected or uses the official actor binding.
    /// Duplicate colliders never duplicate targets; there is no independent rule Update.</summary>
    public sealed class GameplayPhysics2DBackend : IGameplaySpatialQueryBackend
    {
        private readonly GameplayStandardWorld _world;
        private readonly Func<Collider2D, IGameplaySpatialActor> _resolve;
        private readonly bool _useColliderGeometry;
        /// <param name="useColliderGeometry">Opt in when a presentation's actor anchor is at its feet:
        /// circles and swept capsules use the collider surface; directional searches use its bounds centre.
        /// Identity, faction, tags and targetability still come from the registered actor.</param>
        public GameplayPhysics2DBackend(GameplayStandardWorld world, Func<Collider2D, IGameplaySpatialActor> resolve = null, bool useColliderGeometry = false)
        { _world = world ?? throw new ArgumentNullException(nameof(world)); _resolve = resolve ?? (c => c.GetComponentInParent<GameplayUnityActorBinding>()?.Actor); _useColliderGeometry=useColliderGeometry; }
        public GameplayTargetData Query(GameplaySpatialSearchRequest request)
        {
            if (request.Geometry.Plane != GameplaySpatialPlane.XY) throw new InvalidOperationException("Physics2D backend supports XY queries; select logical/Physics3D for XZ or 3D.");
            if (request.Geometry.Shape == GameplaySearchShape.Single) return request.Select(_world.Actors);
            Physics2D.SyncTransforms();
            var g = request.Geometry;
            var candidates = new List<IGameplaySpatialActor>();
            foreach (var collider in Physics2D.OverlapCircleAll(new Vector2(g.Origin.X, g.Origin.Y), g.BoundsRadius,
                request.LayerMask, g.Origin.Z - g.Height / 2, g.Origin.Z + g.Height / 2))
            {
                var actor = _resolve(collider);
                if (actor != null && _world.TryGetActor(actor.Id, out var registered) && ReferenceEquals(actor, registered))
                    candidates.Add(_useColliderGeometry ? new ColliderCandidate(actor,ContactPoint(collider,g)) : actor);
            }
            return request.Select(candidates);
        }
        private static Vector3 ContactPoint(Collider2D collider,GameplaySearchGeometry geometry)
        {
            var centre=collider.bounds.center;
            if(geometry.Shape!=GameplaySearchShape.Circle&&geometry.Shape!=GameplaySearchShape.Capsule)return centre;
            var origin=new Vector2(geometry.Origin.X,geometry.Origin.Y);
            var axisPoint=origin;
            if(geometry.Shape==GameplaySearchShape.Capsule){
                var direction=new Vector2(geometry.Direction.X,geometry.Direction.Y).normalized;
                axisPoint+=direction*Mathf.Clamp(Vector2.Dot((Vector2)centre-origin,direction),0,geometry.Length);
            }
            var contact=collider.ClosestPoint(axisPoint);
            return new Vector3(contact.x,contact.y,centre.z);
        }
        private sealed class ColliderCandidate : IGameplaySpatialActor
        {
            private readonly IGameplaySpatialActor _actor;
            public GameplayPoint Position {get;}
            public GameplaySourceId Id=>_actor.Id;
            public GameplayTagContainer Tags=>_actor.Tags;
            public int Faction=>_actor.Faction;
            public int Layer=>_actor.Layer;
            public bool IsTargetable=>_actor.IsTargetable;
            public ColliderCandidate(IGameplaySpatialActor actor,Vector3 centre)
            {_actor=actor;Position=new GameplayPoint(centre.x,centre.y,centre.z);}
        }
    }
    /// <summary>Real Physics3D sphere broadphase with shared exact sphere/cone/box/capsule/grid filtering.</summary>
    public sealed class GameplayPhysics3DBackend : IGameplaySpatialQueryBackend
    {
        private readonly GameplayStandardWorld _world;
        private readonly Func<Collider, IGameplaySpatialActor> _resolve;
        public GameplayPhysics3DBackend(GameplayStandardWorld world, Func<Collider, IGameplaySpatialActor> resolve = null)
        { _world = world ?? throw new ArgumentNullException(nameof(world)); _resolve = resolve ?? (c => c.GetComponentInParent<GameplayUnityActorBinding>()?.Actor); }
        public GameplayTargetData Query(GameplaySpatialSearchRequest request)
        {
            if (request.Geometry.Shape == GameplaySearchShape.Single) return request.Select(_world.Actors);
            Physics.SyncTransforms();
            var g = request.Geometry; var candidates = new List<IGameplaySpatialActor>();
            foreach (var collider in Physics.OverlapSphere(new Vector3(g.Origin.X, g.Origin.Y, g.Origin.Z), g.BoundsRadius,
                request.LayerMask, QueryTriggerInteraction.Collide))
            {
                var actor = _resolve(collider);
                if (actor != null && _world.TryGetActor(actor.Id, out var registered) && ReferenceEquals(actor, registered)) candidates.Add(actor);
            }
            return request.Select(candidates);
        }
    }
}
