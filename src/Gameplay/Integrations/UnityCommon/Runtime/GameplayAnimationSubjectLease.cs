using System;
using System.Collections.Generic;
using UnityEngine;

namespace FRFrame.Gameplay.Unity
{
    /// <summary>Shares component-wide manual sampling flags across independent native layers/tracks.
    /// The first writer captures baseline state; only the last writer restores it. Used by Animator
    /// and Spine players, including direct Editor preview players.</summary>
    public sealed class GameplayAnimationSubjectLease : IDisposable
    {
        private sealed class State { internal int Count; internal Action Restore; }
        private static readonly Dictionary<Component,State> States=new Dictionary<Component,State>();
        private readonly Component _target;private bool _disposed;
        private GameplayAnimationSubjectLease(Component target)=>_target=target;
        public static GameplayAnimationSubjectLease Acquire(Component target,Action enter,Action restore)
        {
            if(!States.TryGetValue(target,out var state)){
                enter();state=new State{Restore=restore};States.Add(target,state);
            }
            state.Count++;return new GameplayAnimationSubjectLease(target);
        }
        public void Dispose()
        {
            if(_disposed)return;_disposed=true;
            if(!States.TryGetValue(_target,out var state)||--state.Count>0)return;
            States.Remove(_target);if(_target!=null)state.Restore();
        }
    }
}
