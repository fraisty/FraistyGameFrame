using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Debugging;
using FRFrame.Gameplay.Standard;
using UnityEngine;

namespace FRFrame.Gameplay.Unity
{
    public enum GameplayUnitySpatialBackend { Logical, Physics2D, Physics3D }
    /// <summary>Default Unity world driver; advances existing Core containers once per caller step.
    /// Turn hosts may disable AutomaticAdvance and call Advance explicitly. Backend queries never tick rules.</summary>
    [DefaultExecutionOrder(1000)]
    public sealed class GameplayUnityWorldHost : MonoBehaviour, IGameplayRuntimeDebugSource
    {
        public GameplayUnitySpatialBackend SpatialBackend;
        public bool AutomaticAdvance = true;
        public bool CollectSkillTrace;
        public GameplayStandardWorld World { get; private set; }
        private long _tick;
        public string DisplayName => "FRFrame Standard / " + name;
        private void Awake()
        {
            World = new GameplayStandardWorld();
            World.Spatial = SpatialBackend == GameplayUnitySpatialBackend.Physics2D ? (IGameplaySpatialQueryBackend)new GameplayPhysics2DBackend(World) :
                SpatialBackend == GameplayUnitySpatialBackend.Physics3D ? new GameplayPhysics3DBackend(World) : World.Spatial;
        }
        private void Update() { if (AutomaticAdvance) { Physics.SyncTransforms(); Physics2D.SyncTransforms(); Advance(Time.deltaTime); } }
        public void Advance(float amount) { if(World!=null){foreach(var actor in World.Actors)actor.SkillTrace.Enabled=CollectSkillTrace;World.Advance(amount);} _tick++; }
        private void OnDestroy() { World?.Dispose(); World = null; }
        public GameplayRuntimeDebugSnapshot CaptureSnapshot()
        {
            return new GameplayRuntimeDebugSnapshot(_tick, !AutomaticAdvance, GameplayStandardDebugSnapshot.Units(World), World?.Events.GetHistorySnapshot() ?? Array.Empty<GameplayEventHistoryEntry>());
        }
    }
}
