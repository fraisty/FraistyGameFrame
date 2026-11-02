using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace FRFrame
{
    public class FsmTaskController : FsmController
    {
        private CancellationToken _token;
        private Type _completeStateType;
        private UniTaskCompletionSource<FsmTaskController> _tcs;
        private UniTaskCompletionSource<FsmTaskResult> _resultTcs;

        public TaskState TaskState { get; private set; } = TaskState.None;
        public bool IsDone => TaskState == TaskState.Completed || TaskState == TaskState.Failed || TaskState == TaskState.Canceled;
        public bool IsCancel => TaskState == TaskState.Canceled || (_token.CanBeCanceled && _token.IsCancellationRequested);
        public string CompleteStateName => _completeStateType?.Name ?? string.Empty;
        public string Error { get; private set; }
        public Exception Exception { get; private set; }
        public string CancelReason { get; private set; }
        public bool ValidateCompleteStateOnBegin { get; set; } = true;

        public void BeginTask(Type completeStateType, CancellationToken token = default)
        {
            if (completeStateType != null && !typeof(FsmState).IsAssignableFrom(completeStateType))
            {
                Fail($"Complete state type is not FsmState: {completeStateType.FullName}");
                return;
            }

            TaskState = TaskState.Running;
            _completeStateType = completeStateType;
            _token = token;
            _tcs = new UniTaskCompletionSource<FsmTaskController>();
            _resultTcs = new UniTaskCompletionSource<FsmTaskResult>();
            Error = null;
            Exception = null;
            CancelReason = null;

            if (ValidateCompleteStateOnBegin && _completeStateType != null && !TryGetState(_completeStateType, out _))
            {
                Fail($"Complete state not registered: {_completeStateType.FullName}");
                return;
            }

            if (_completeStateType != null)
                LogInfo($"[FSM-TASK] {Name} BeginTask => CompleteState: {_completeStateType.Name}");
        }

        public void BeginTask<TCompleteState>(CancellationToken token = default) where TCompleteState : FsmState
        {
            BeginTask(typeof(TCompleteState), token);
        }

        public UniTask<FsmTaskController> WaitAsync()
        {
            if (IsDone)
                return UniTask.FromResult(this);

            _tcs ??= new UniTaskCompletionSource<FsmTaskController>();
            return _tcs.Task;
        }

        public UniTask<FsmTaskResult> WaitResultAsync()
        {
            if (IsDone)
                return UniTask.FromResult(CreateResult());

            _resultTcs ??= new UniTaskCompletionSource<FsmTaskResult>();
            return _resultTcs.Task;
        }

        public override bool ChangeState(Type stateType)
        {
            bool changed = base.ChangeState(stateType);
            if (changed && _completeStateType != null && stateType == _completeStateType)
                Complete();
            return changed;
        }

        public override bool ChangeState(string stateName)
        {
            if (!TryGetState(stateName, out FsmState state))
                return base.ChangeState(stateName);

            bool changed = base.ChangeState(state.GetType());
            if (changed && _completeStateType != null && state.GetType() == _completeStateType)
                Complete();
            return changed;
        }

        public void Complete()
        {
            if (IsDone)
                return;

            if (_token.CanBeCanceled && _token.IsCancellationRequested)
            {
                Cancel("CancellationToken canceled");
                return;
            }

            TaskState = TaskState.Completed;
            LogInfo($"[FSM-TASK] {Name} Complete => Success");
            CompleteSources();
        }

        public void Cancel()
        {
            Cancel(null);
        }

        public void Cancel(string reason = null)
        {
            if (IsDone)
                return;

            TaskState = TaskState.Canceled;
            CancelReason = reason;
            LogWarning($"[FSM-TASK] {Name} Cancel => {reason ?? string.Empty}");
            CompleteSources();
        }

        public void Fail()
        {
            Fail(null, null);
        }

        public void Fail(string error = null, Exception exception = null)
        {
            if (IsDone)
                return;

            TaskState = TaskState.Failed;
            Error = error;
            Exception = exception;
            if (exception != null)
                LogError($"[FSM-TASK] {Name} Fail => {error} {exception.Message}");
            else
                LogError($"[FSM-TASK] {Name} Fail => {error ?? string.Empty}");
            CompleteSources();
        }

        public void ResetTaskState()
        {
            TaskState = TaskState.None;
            _completeStateType = null;
            _token = default;
            _tcs = null;
            _resultTcs = null;
            Error = null;
            Exception = null;
            CancelReason = null;
            LogInfo($"[FSM-TASK] {Name} ResetTaskState");
        }

        public override FsmDebugInfo GetDebugInfo()
        {
            var info = base.GetDebugInfo();
            info.TaskState = TaskState;
            return info;
        }

        public override void Dispose()
        {
            if (!IsDone && TaskState == TaskState.Running)
                Cancel("FSM disposed");

            _tcs = null;
            _resultTcs = null;
            TaskState = TaskState.None;
            _completeStateType = null;
            _token = default;
            base.Dispose();
        }

        private void CompleteSources()
        {
            _tcs?.TrySetResult(this);
            _resultTcs?.TrySetResult(CreateResult());
        }

        private FsmTaskResult CreateResult()
        {
            return new FsmTaskResult
            {
                State = TaskState,
                CompleteStateName = CompleteStateName,
                Error = Error,
                Exception = Exception,
                CancelReason = CancelReason,
                Controller = this
            };
        }
    }
}
