using System;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Standard;
using UnityEngine;

namespace FRFrame.Gameplay.Unity
{
    /// <summary>Official scene binding to the neutral actor composition. Profile/skill assets are built
    /// once; mutable state stays in Actor. Supplied collider centres are the canonical query anchor.</summary>
    [DefaultExecutionOrder(-900)]
    public sealed class GameplayUnityActorBinding : MonoBehaviour
    {
        public GameplayUnityWorldHost Host;
        public GameplayActorProfileAsset Profile;
        public GameplayFloatingTextAsset FloatingText;
        public GameplaySkillDocumentAsset[] Skills = Array.Empty<GameplaySkillDocumentAsset>();
        public int Faction;
        public Vector3 Facing = Vector3.right;
        public Collider2D Body2D;
        public Collider Body3D;
        [Tooltip("命中窗口读取已指定的 SphereCollider 或 CircleCollider2D。未启用时沿用逻辑 Hurtbox。")]
        public bool UseBodyAsHurtbox;
        public GameplayStandardActor Actor { get; private set; }
        private Func<GameplayHurtbox> _hitBody;
        private void Start()
        {
            if (Host == null || Host.World == null || Profile == null) throw new InvalidOperationException("Actor requires its world and formal profile.");
            var build = new GameplayAuthoringBuildContext();
            Actor = Host.World.CreateActor(ReadPosition(), Faction, gameObject.layer, ReadPosition, WritePosition, ReadFacing, WriteFacing);
            try
            {
                BindHitBody(Actor);
                Profile.Build(build).Initialize(Actor);
                Actor.Presentation = new GameplayUnityAnimationController(Actor,gameObject){FloatingText=FloatingText==null?null:FloatingText.Build(build)};
                Actor.Facing = new Extensions.GameplayDirection(Facing.x, Facing.y, Facing.z);
                foreach (var skill in Skills) { if (skill == null) throw new InvalidOperationException("Skill document binding is missing."); Actor.InstallDocument(skill.Build(build)); }
            }
            catch { Actor.Dispose(); Actor = null; throw; }
        }
        private GameplayPoint ReadPosition()
        {
            Vector3 position = Body3D != null ? Body3D.bounds.center : Body2D != null ? Body2D.bounds.center : transform.position;
            return new GameplayPoint(position.x, position.y, position.z);
        }
        private void Update()
        {
            if (Actor == null) return;
            Actor.Layer = gameObject.layer;
            Actor.Facing = new Extensions.GameplayDirection(Facing.x, Facing.y, Facing.z);
        }
        private void WritePosition(GameplayPoint point)
        {
            var anchor = ReadPosition();
            transform.position += new Vector3(point.X-anchor.X,point.Y-anchor.Y,point.Z-anchor.Z);
        }
        private Extensions.GameplayDirection ReadFacing()=>new Extensions.GameplayDirection(Facing.x,Facing.y,Facing.z);
        private void WriteFacing(Extensions.GameplayDirection direction)
        {
            Facing=new Vector3(direction.X,direction.Y,direction.Z);
            if(Body3D!=null)transform.rotation=Quaternion.LookRotation(Facing,Mathf.Abs(Vector3.Dot(Facing,Vector3.up))>.99f?Vector3.forward:Vector3.up);
            else transform.rotation=Quaternion.Euler(0,0,Mathf.Atan2(Facing.y,Facing.x)*Mathf.Rad2Deg);
        }
        /// <summary>Attaches a game's already registered actor without creating any containers/clock.</summary>
        public void BindExisting(GameplayStandardActor actor)
        {
            if (Actor != null) throw new InvalidOperationException("Actor already bound.");
            if (actor == null) throw new ArgumentNullException(nameof(actor));
            BindHitBody(actor);
            Actor = actor;
            enabled = false;
        }
        /// <summary>Detaches only this borrowed binding before its pooled view is reused. Actor
        /// disposal remains the owner's responsibility; form/scene bindings cannot be stolen.</summary>
        public void UnbindExisting(GameplayStandardActor actor)
        {if(enabled||!ReferenceEquals(Actor,actor))throw new InvalidOperationException("Existing actor binding mismatch.");UnbindHitBody();Actor=null;}

        private void BindHitBody(GameplayStandardActor actor)
        {
            if (!UseBodyAsHurtbox) return;
            if (Body3D is SphereCollider sphere)
            {
                _hitBody = () =>
                {
                    var shape = sphere.transform;
                    var scale = shape.lossyScale;
                    float radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                    return HitBody(actor, shape.TransformPoint(sphere.center), radius);
                };
            }
            else if (Body2D is CircleCollider2D circle)
            {
                _hitBody = () =>
                {
                    var shape = circle.transform;
                    var scale = shape.lossyScale;
                    float radius = circle.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
                    return HitBody(actor, shape.TransformPoint(circle.offset), radius);
                };
            }
            else throw new InvalidOperationException("Hit body requires an explicitly assigned SphereCollider or CircleCollider2D.");
            actor.HurtboxSource = _hitBody;
        }

        private static GameplayHurtbox HitBody(GameplayStandardActor actor, Vector3 center, float radius)
        {
            var root = actor.Position;
            return new GameplayHurtbox(radius, new GameplayPoint(center.x-root.X, center.y-root.Y, center.z-root.Z));
        }

        private void UnbindHitBody()
        {
            if (Actor != null && _hitBody != null && ReferenceEquals(Actor.HurtboxSource, _hitBody))
                Actor.HurtboxSource = null;
            _hitBody = null;
        }

        private void OnDestroy() { UnbindHitBody(); if (enabled) Actor?.Dispose(); Actor = null; }
    }
}
