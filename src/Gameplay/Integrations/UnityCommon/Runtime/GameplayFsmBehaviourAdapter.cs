using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Standard;

namespace FRFrame.Gameplay.Unity
{
    /// <summary>Adapts the existing FRFrame Core/Fsm. Explicit aliases preserve host transition policy.
    /// Borrowed controllers are neither destroyed nor ticked unless the caller requests it.</summary>
    public sealed class GameplayFsmBehaviourAdapter : IGameplayBehaviourDriver
    {
        private readonly FsmController _controller;
        private readonly Dictionary<string,Type> _states;
        private readonly bool _advance, _ownsController;
        private bool _disposed;
        public string CurrentState => _controller.CurrentStateName;
        public GameplayFsmBehaviourAdapter(FsmController controller, IReadOnlyDictionary<string,Type> states,
            bool advanceWithWorld=false, bool ownsController=false)
        {
            _controller=controller??throw new ArgumentNullException(nameof(controller));
            if(controller.IsDisposed)throw new ObjectDisposedException(nameof(controller));
            if(states==null)throw new ArgumentNullException(nameof(states));
            _states=new Dictionary<string,Type>(StringComparer.Ordinal);
            foreach(var state in states)
            {
                if(string.IsNullOrWhiteSpace(state.Key)||state.Value==null||!controller.TryGetState(state.Value,out _))
                    throw new ArgumentException("Every state alias must reference a registered host state.",nameof(states));
                _states.Add(state.Key,state.Value);
            }
            _advance=advanceWithWorld;_ownsController=ownsController;
        }
        public bool TryChangeState(string state)
            => !_disposed && !_controller.IsDisposed && state!=null && _states.TryGetValue(state,out var type) && _controller.ChangeState(type);
        public void Advance(float amount)
        {
            if(!float.IsFinite(amount)||amount<0)throw new ArgumentOutOfRangeException(nameof(amount));
            if(!_disposed&&_advance)_controller.Update(amount,amount);
        }
        public void Dispose(){if(_disposed)return;_disposed=true;if(_ownsController)_controller.Dispose();}
    }
}
