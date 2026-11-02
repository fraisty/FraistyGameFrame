using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace FRFrame
{
    public abstract class AsyncFsmState : FsmState
    {
        private CancellationTokenSource _cancellationTokenSource;

        protected CancellationToken CancellationToken => _cancellationTokenSource?.Token ?? CancellationToken.None;

        public override void OnEnter()
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
            RunEnterAsync(_cancellationTokenSource.Token).Forget();
        }

        public override void OnExit()
        {
            if (_cancellationTokenSource == null)
                return;

            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
        }

        protected virtual UniTask OnEnterAsync(CancellationToken cancellationToken)
        {
            return UniTask.CompletedTask;
        }

        private async UniTaskVoid RunEnterAsync(CancellationToken cancellationToken)
        {
            try
            {
                await OnEnterAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                FRLog.Exception("FSM", e, $"Async state failed. State:{GetType().FullName}");
                if (fsmController is FsmTaskController taskController)
                    taskController.Fail(e.Message, e);
            }
        }
    }
}
