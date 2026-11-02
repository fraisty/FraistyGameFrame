using System;
using Cysharp.Threading.Tasks;

namespace FRFrame
{
    public sealed class UIOpenHandle<TForm> where TForm : UIForm
    {
        private readonly Action _cancelAction;

        public UIOpenHandle(UniTask<TForm> task, Action cancelAction)
        {
            Task = task;
            _cancelAction = cancelAction;
        }

        public UniTask<TForm> Task { get; }
        public bool IsDone => Task.Status != UniTaskStatus.Pending;
        public bool IsCanceled { get; private set; }

        public void Cancel()
        {
            if (IsCanceled)
                return;

            IsCanceled = true;
            _cancelAction?.Invoke();
        }
    }
}
