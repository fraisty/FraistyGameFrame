using System;
using System.Collections.Generic;
using UnityEngine;

namespace FRFrame
{
    public abstract partial class FsmController : IDisposable
    {
        private readonly Dictionary<Type, FsmState> _states = new Dictionary<Type, FsmState>(8);
        private readonly List<string> _stateNames = new List<string>(8);
        private readonly HashSet<FsmTransitionKey> _transitionRules = new HashSet<FsmTransitionKey>();
        private readonly List<FsmStateChangeRecord> _stateHistory = new List<FsmStateChangeRecord>(32);
        private readonly List<string> _blackboardKeysCache = new List<string>(8);

        private float _runTime;

        public string Name => GetType().Name;
        public string CurrentStateName => CurState?.GetType().Name ?? string.Empty;
        public FsmState CurState { get; private set; }
        public bool IsDisposed { get; private set; }
        public bool IsPaused { get; private set; }
        public object Owner { get; internal set; }
        public float RunTime => _runTime;
        public int StateCount => _states.Count;
        public IReadOnlyList<string> StateNames => _stateNames;
        public IReadOnlyList<FsmStateChangeRecord> StateHistory => _stateHistory;
        public bool EnableLog { get; internal set; }
        public FsmTransitionPolicy TransitionPolicy { get; set; } = FsmTransitionPolicy.Free;
        public int MaxStateHistoryCount { get; set; } = 32;

        public event Action<FsmController> OnInitialized;
        public event Action<FsmController, FsmState, FsmState> OnStateChanged;
        public event Action<FsmController> OnDisposed;

        public void Initialize()
        {
            OnInitialize();
            OnInitialized?.Invoke(this);
            LogInfo($"[FSM] Initialize => {Name}");
        }

        protected virtual void OnInitialize()
        {
        }

        public virtual void Update(float elapseSeconds, float realElapseSeconds)
        {
            if (IsDisposed) return;

            _runTime += elapseSeconds;
            if (IsPaused) return;

            CurState?.OnUpdate(elapseSeconds, realElapseSeconds);
        }

        public void Pause()
        {
            if (IsDisposed || IsPaused)
                return;

            IsPaused = true;
            CurState?.OnPause();
            LogInfo($"[FSM] Pause => {Name}");
        }

        public void Resume()
        {
            if (IsDisposed || !IsPaused)
                return;

            IsPaused = false;
            CurState?.OnResume();
            LogInfo($"[FSM] Resume => {Name}");
        }

        protected T AddState<T>() where T : FsmState, new()
        {
            var type = typeof(T);
            if (_states.TryGetValue(type, out var existed))
                return (T)existed;

            var state = new T();
            state.OnInit(this);
            _states.Add(type, state);
            _stateNames.Add(type.Name);
            LogInfo($"[FSM] {Name} AddState => {type.Name}");
            return state;
        }

        protected FsmState AddState(FsmState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            var type = state.GetType();
            if (_states.TryGetValue(type, out var existed))
                return existed;

            state.OnInit(this);
            _states.Add(type, state);
            _stateNames.Add(type.Name);
            LogInfo($"[FSM] {Name} AddState => {type.Name}");
            return state;
        }

        public T GetState<T>() where T : FsmState
        {
            _states.TryGetValue(typeof(T), out var state);
            return state as T;
        }

        public bool TryGetState(Type stateType, out FsmState state)
        {
            if (stateType == null)
            {
                state = null;
                return false;
            }

            return _states.TryGetValue(stateType, out state);
        }

        public bool TryGetState(string stateName, out FsmState state)
        {
            state = null;
            if (string.IsNullOrEmpty(stateName))
                return false;

            FsmState simpleMatch = null;
            foreach (var pair in _states)
            {
                Type type = pair.Key;
                if (type.FullName == stateName || pair.Value.StateName == stateName)
                {
                    state = pair.Value;
                    return true;
                }

                if (type.Name == stateName)
                {
                    if (simpleMatch != null)
                    {
                        LogError($"[FSM] {Name} TryGetState Error => State name conflict: {stateName}");
                        state = null;
                        return false;
                    }

                    simpleMatch = pair.Value;
                }
            }

            state = simpleMatch;
            return state != null;
        }

        public bool HasState<T>() where T : FsmState
        {
            return _states.ContainsKey(typeof(T));
        }

        public virtual bool ChangeState<T>() where T : FsmState
        {
            return ChangeState(typeof(T));
        }

        public virtual bool ChangeState(Type stateType)
        {
            if (!TryGetState(stateType, out FsmState next))
            {
                var msg = $"状态未注册: {stateType?.Name ?? "null"}";
                LogError($"[FSM] {Name} ChangeState Error => {msg}");
                throw new Exception(msg);
            }

            return ChangeStateInternal(next);
        }

        public virtual bool ChangeState(string stateName)
        {
            if (!TryGetState(stateName, out FsmState next))
            {
                LogError($"[FSM] {Name} ChangeState Error => 状态未注册: {stateName}");
                return false;
            }

            return ChangeStateInternal(next);
        }

        protected void AddTransition<TFrom, TTo>() where TFrom : FsmState where TTo : FsmState
        {
            AddTransition(typeof(TFrom), typeof(TTo));
        }

        protected void AddTransition(Type from, Type to)
        {
            if (from == null || to == null)
                throw new ArgumentNullException(from == null ? nameof(from) : nameof(to));

            _transitionRules.Add(new FsmTransitionKey(from, to));
        }

        protected virtual bool CanChangeState(Type from, Type to)
        {
            if (_transitionRules.Count == 0 || from == null)
                return true;

            return _transitionRules.Contains(new FsmTransitionKey(from, to));
        }

        public void ClearStateHistory()
        {
            _stateHistory.Clear();
        }

        public virtual FsmDebugInfo GetDebugInfo()
        {
            GetBlackboardKeys(_blackboardKeysCache);
            return new FsmDebugInfo
            {
                Name = Name,
                FsmName = Name,
                TypeName = GetType().FullName,
                OwnerName = Owner?.ToString() ?? string.Empty,
                OwnerTypeName = Owner?.GetType().FullName ?? string.Empty,
                CurrentStateName = CurrentStateName,
                CurrentStateTypeName = CurState?.GetType().FullName ?? string.Empty,
                IsDisposed = IsDisposed,
                IsPaused = IsPaused,
                RunTime = RunTime,
                StateCount = StateCount,
                StateNames = new List<string>(_stateNames),
                BlackboardKeys = new List<string>(_blackboardKeysCache),
                StateHistory = new List<FsmStateChangeRecord>(_stateHistory),
                IsTask = this is FsmTaskController,
                IsTaskFsm = this is FsmTaskController,
                TaskState = this is FsmTaskController taskFsm ? taskFsm.TaskState : TaskState.None
            };
        }

        protected void LogDebug(string message)
        {
            if (EnableLog) FRLog.DebugT("FSM", message);
        }

        protected void LogInfo(string message)
        {
            if (EnableLog) FRLog.InfoT("FSM", message);
        }

        protected void LogWarning(string message)
        {
            if (EnableLog) FRLog.WarningT("FSM", message);
        }

        protected void LogError(string message)
        {
            if (EnableLog) FRLog.ErrorT("FSM", message);
        }

        protected void Log(string message)
        {
            LogInfo(message);
        }

        public virtual void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;

            LogInfo($"[FSM] Dispose => {Name}");
            CurState?.OnExit();
            CurState = null;
            _states.Clear();
            _transitionRules.Clear();
            _stateHistory.Clear();
            _blackboard.Clear();
            _stateNames.Clear();
            Owner = null;
            OnDisposed?.Invoke(this);
            OnInitialized = null;
            OnStateChanged = null;
            OnDisposed = null;
        }

        private bool ChangeStateInternal(FsmState next)
        {
            if (ReferenceEquals(CurState, next))
                return false;

            Type fromType = CurState?.GetType();
            Type toType = next.GetType();
            bool canChange = CanChangeState(fromType, toType);
            if (!canChange)
            {
                string message = $"[FSM] {Name} Invalid transition => {fromType?.Name ?? "None"} -> {toType.Name}";
                if (TransitionPolicy == FsmTransitionPolicy.Strict)
                {
                    LogError(message);
                    return false;
                }

                if (TransitionPolicy == FsmTransitionPolicy.WarningOnly)
                    LogWarning(message);
            }

            var prev = CurState;
            prev?.OnExit();
            CurState = next;
            CurState.OnEnter();
            RecordStateChange(prev, CurState);
            LogInfo($"[FSM] {Name} ChangeState => {prev?.GetType().Name ?? "None"} -> {CurState.GetType().Name}");
            OnStateChanged?.Invoke(this, prev, CurState);
            return true;
        }

        private void RecordStateChange(FsmState from, FsmState to)
        {
            if (MaxStateHistoryCount <= 0)
                return;

            _stateHistory.Add(new FsmStateChangeRecord
            {
                FromState = from?.GetType().Name ?? string.Empty,
                ToState = to?.GetType().Name ?? string.Empty,
                Time = Time.realtimeSinceStartup,
                Frame = Time.frameCount
            });

            while (_stateHistory.Count > MaxStateHistoryCount)
                _stateHistory.RemoveAt(0);
        }

        private readonly struct FsmTransitionKey : IEquatable<FsmTransitionKey>
        {
            private readonly Type _from;
            private readonly Type _to;

            public FsmTransitionKey(Type from, Type to)
            {
                _from = from;
                _to = to;
            }

            public bool Equals(FsmTransitionKey other)
            {
                return _from == other._from && _to == other._to;
            }

            public override bool Equals(object obj)
            {
                return obj is FsmTransitionKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((_from != null ? _from.GetHashCode() : 0) * 397) ^ (_to != null ? _to.GetHashCode() : 0);
                }
            }
        }
    }
}
