using System;
using System.Collections.Generic;
using System.Globalization;
using FRFrame.Gameplay.Standard;
using TMPro;
using UnityEngine;

namespace FRFrame.Gameplay.Unity
{
    /// <summary>Presentation policy for committed combat outcomes on this actor.</summary>
    public enum GameplayCombatTextMode
    {
        [InspectorName("自动 · 技能显式优先")] AutomaticWithOverrides,
        [InspectorName("仅技能显式")] ExplicitOnly,
        [InspectorName("关闭反馈")] Disabled
    }

    public sealed partial class GameplayUnityAnimationController
    {
        private readonly List<FloatingLease> _floating=new List<FloatingLease>();
        private readonly List<(GameplayCombatOutcome Outcome, bool Shown)> _pendingCombatText =
            new List<(GameplayCombatOutcome, bool)>();
        /// <summary>Automatic feedback fills unclaimed outcomes at the next presentation step.</summary>
        public GameplayCombatTextMode CombatTextMode { get; set; }
        /// <summary>Fallback style for automatic feedback when the selector has no style.</summary>
        public GameplayFloatingTextDefinition FloatingText {get;set;}
        /// <summary>Optional game-side resource selection over a committed outcome, never a rule or clock.</summary>
        public Func<GameplayCombatOutcome,GameplayFloatingTextDefinition> FloatingTextSelector {get;set;}
        /// <summary>Optional host font for text the authored prefab font cannot render.</summary>
        public TMP_FontAsset FloatingTextFallbackFont {get;set;}
        public Camera FloatingTextCamera {get;set;}
        public int ActiveFloatingTextCount=>_floating.Count;
        private void OnFloatingCombat(GameplayCombatOutcome outcome)
        {
            _pendingCombatText.Add((outcome, false));
        }

        // Only a current, still pending result may be claimed. Independent commits stay in
        // the queue even when an authored action claims the last result on this actor.
        internal bool TryShowAuthoredCombatText(GameplayFloatingTextDefinition definition, GameplayContext context)
        {
            if (!Actor.LastCombatOutcome.HasValue) return false;
            var outcome = Actor.LastCombatOutcome.Value;
            if (!ReferenceEquals(outcome.ExecutionContext, context) ||
                outcome.Context.EffectiveSourceId != context.EffectiveSourceId ||
                outcome.Context.OriginSourceId != context.OriginSourceId ||
                outcome.Context.ActivationSourceId != context.ActivationSourceId) return false;

            for (int i = _pendingCombatText.Count - 1; i >= 0; i--)
            {
                var pending = _pendingCombatText[i];
                if (!ReferenceEquals(pending.Outcome.Context, outcome.Context) || !pending.Outcome.Equals(outcome)) continue;
                if (pending.Shown || CombatTextMode == GameplayCombatTextMode.Disabled) return true;
                if (!ShowFloatingText(definition, outcome)) return false;
                _pendingCombatText[i] = (outcome, true);
                return true;
            }
            return false;
        }

        private void FlushCombatText()
        {
            if (CombatTextMode == GameplayCombatTextMode.AutomaticWithOverrides)
                foreach (var pending in _pendingCombatText)
                {
                    if (pending.Shown) continue;
                    try
                    {
                        var definition = FloatingTextSelector?.Invoke(pending.Outcome) ?? FloatingText;
                        if (definition != null) ShowFloatingText(definition, pending.Outcome);
                    }
                    catch (Exception ex) { Debug.LogException(ex, Subject); }
                }
            _pendingCombatText.Clear();
        }
        /// <summary>Actor-owned short feedback over an already committed outcome. It can outlive
        /// the instigator cast; target disposal and presentation disposal always release it.</summary>
        public bool ShowFloatingText(GameplayFloatingTextDefinition definition,GameplayCombatOutcome outcome)
        {
            if(definition==null)throw new ArgumentNullException(nameof(definition));
            if(outcome.Context==null)throw new ArgumentException("Committed feedback context required.",nameof(outcome));
            if(_disposed||Subject==null||Actor.IsDisposed||outcome.Context.TargetData.PrimaryTargetId!=Actor.Id)return false;
            if(definition.SuppressEmptyHealing&&outcome.Kind==GameplayCombatOutcomeKind.Heal&&outcome.Applied==0)return false;
            var content=definition.Format(outcome);
            TMP_FontAsset fontOverride=null;
            if(FloatingTextFallbackFont!=null&&!string.IsNullOrEmpty(content.Text))
            {
                var font=definition.Prefab.GetComponentInChildren<TMP_Text>(true).font;
                foreach(char character in content.Text)
                    if(font==null||!font.HasCharacter(character,true))
                    {
                        fontOverride=FloatingTextFallbackFont;
                        break;
                    }
            }
            var pool=SpatialViewPool;
            string viewId=definition.Prefab.GetInstanceID().ToString(CultureInfo.InvariantCulture);
            if(fontOverride!=null)viewId+="/"+fontOverride.GetInstanceID().ToString(CultureInfo.InvariantCulture);
            var key=new ResourceKey("Gameplay.FloatingText",viewId);
            var view=pool.GetOrCreate(key,definition.Prefab.name,definition.Prefab);
            try
            {
                foreach(var child in view.GetComponentsInChildren<Transform>(true))child.gameObject.layer=Subject.layer;
                var anchor=string.IsNullOrEmpty(definition.Binding)?Subject.transform:Subject.transform.Find(definition.Binding)??Subject.transform;
                view.transform.SetParent(definition.Follow&&!definition.Point.HasValue?anchor:null,false);view.transform.position=(definition.Point??anchor.position)+definition.Offset+Vector3.up*(_floating.Count*.5f);
                var widget=view.GetComponent<GameplayFloatingTextWidget>();
                widget.FontOverride=fontOverride;
                widget.OpenStandalone(content);
                var camera=FloatingTextCamera!=null?FloatingTextCamera:Camera.main;
                if(camera!=null){view.transform.rotation=camera.transform.rotation;var canvas=view.GetComponent<Canvas>();if(canvas!=null)canvas.worldCamera=camera;}
                _floating.Add(new FloatingLease(this,pool,key,view,widget,definition));return true;
            }
            catch{pool.Release(key,definition.Prefab.name,view);throw;}
        }
        private sealed class FloatingLease:IDisposable
        {
            private readonly GameplayUnityAnimationController _owner;private readonly GameObjectPoolModule _pool;private readonly ResourceKey _key;
            private readonly GameObject _view;private readonly GameplayFloatingTextWidget _widget;private readonly GameplayFloatingTextDefinition _definition;
            private readonly Vector3 _start;private double _age;private bool _disposed;
            private Vector3 _velocity,_travel;
            internal FloatingLease(GameplayUnityAnimationController owner,GameObjectPoolModule pool,ResourceKey key,GameObject view,GameplayFloatingTextWidget widget,GameplayFloatingTextDefinition definition)
            {_owner=owner;_pool=pool;_key=key;_view=view;_widget=widget;_definition=definition;_start=definition.Follow&&!definition.Point.HasValue?view.transform.localPosition:view.transform.position;
                var spread=definition.VelocitySpread;_velocity=Vector3.up*definition.RiseSpeed+new Vector3(UnityEngine.Random.Range(-spread.x,spread.x),UnityEngine.Random.Range(-spread.y,spread.y),UnityEngine.Random.Range(-spread.z,spread.z));}
            internal void Advance(float amount)
            {
                if(_disposed)return;if(_view==null||_owner.Actor.IsDisposed){Dispose();return;}
                double next=Math.Min(_definition.Duration,_age+amount);float step=(float)(next-_age);_age=next;
                _velocity+=_definition.Acceleration*step;_travel+=_velocity*step;var position=_start+_travel;
                if(_definition.Follow&&!_definition.Point.HasValue)_view.transform.localPosition=position;else _view.transform.position=position;
                _widget.SetOpacity(Mathf.Clamp01((1-(float)(_age/_definition.Duration))/(1-_definition.FadeStartRatio)));
                if(_age>=_definition.Duration)Dispose();
            }
            public void Dispose()
            {if(_disposed)return;_disposed=true;_owner._floating.Remove(this);if(_view!=null)_pool.Release(_key,_definition.Prefab.name,_view);}
        }
    }
}
