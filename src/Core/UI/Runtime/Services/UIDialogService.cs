using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace FRFrame
{
    public sealed class UIDialogService
    {
        private readonly UIManager _uiManager;
        private readonly UIBuiltinConfig _config;
        private readonly Queue<DialogQueueItem> _queue = new Queue<DialogQueueItem>();
        private bool _queueRunning;
        private UIDialogParams _current;

        public UIDialogService(UIManager uiManager, UIBuiltinConfig config)
        {
            _uiManager = uiManager;
            _config = config;
        }

        public async UniTask<UIDialogResult> ShowAsync(UIDialogParams param, CancellationToken cancellationToken = default)
        {
            param ??= new UIDialogParams();
            _current = param;
            param.ResultSource = new UniTaskCompletionSource<UIDialogResult>();
            UIDialogForm form = await OpenDialogFormAsync(param, cancellationToken);
            if (form == null)
                return UIDialogResult.None;

            using (CancellationTokenSource timeoutSource = param.Timeout > 0f ? new CancellationTokenSource() : null)
            using (cancellationToken.Register(() => param.ResultSource.TrySetResult(UIDialogResult.None)))
            {
                if (timeoutSource != null)
                {
                    timeoutSource.CancelAfterSlim(System.TimeSpan.FromSeconds(param.Timeout));
                    timeoutSource.Token.Register(() => param.ResultSource.TrySetResult(UIDialogResult.Timeout));
                }

                UIDialogResult result = await param.ResultSource.Task;
                if (form != null && form.State == UIFormState.Opened)
                    form.Close();
                if (_current == param)
                    _current = null;
                return result;
            }
        }

        public UniTask<UIDialogResult> EnqueueAsync(UIDialogParams param, CancellationToken cancellationToken = default)
        {
            UniTaskCompletionSource<UIDialogResult> source = new UniTaskCompletionSource<UIDialogResult>();
            _queue.Enqueue(new DialogQueueItem { Param = param, CancellationToken = cancellationToken, Source = source });
            if (!_queueRunning)
                RunQueueAsync().Forget();
            return source.Task;
        }

        public void ClearQueue()
        {
            while (_queue.Count > 0)
                _queue.Dequeue().Source.TrySetResult(UIDialogResult.None);
        }

        public UIDialogSnapshot CreateSnapshot()
        {
            return new UIDialogSnapshot
            {
                QueueCount = _queue.Count,
                QueueRunning = _queueRunning,
                CurrentTitle = _current != null ? _current.Title : null,
                CurrentContent = _current != null ? _current.DisplayContent : null
            };
        }

        private async UniTask<UIDialogForm> OpenDialogFormAsync(UIDialogParams param, CancellationToken cancellationToken)
        {
            string formId = string.IsNullOrEmpty(_config?.DialogFormId) ? nameof(UIDialogForm) : _config.DialogFormId;
            UIOpenOptions options = UIOpenOptions.FromUserData(param, cancellationToken);
            options.CloseOnMaskClickOverride = param.CloseOnMaskClick;
            UIForm form = await _uiManager.OpenAsync(formId, options);
            if (form is UIDialogForm dialogForm)
                return dialogForm;

            if (_config == null || !_config.EnableFallbackView)
                return null;

            _uiManager.RegisterCodeFormConfig<UIDialogForm>(UIGroupLayer.Popup, true, UIFormCloseMode.Destroy, true, UICoverVisualMode.KeepVisible, UIAnimationPreset.FadeScale, UIAnimationPreset.Fade, true);
            form = await _uiManager.OpenAsync(nameof(UIDialogForm), options);
            return form as UIDialogForm;
        }

        private async UniTaskVoid RunQueueAsync()
        {
            _queueRunning = true;
            while (_queue.Count > 0)
            {
                DialogQueueItem item = _queue.Dequeue();
                if (item.CancellationToken.IsCancellationRequested)
                {
                    item.Source.TrySetResult(UIDialogResult.None);
                    continue;
                }

                UIDialogResult result = await ShowAsync(item.Param, item.CancellationToken);
                item.Source.TrySetResult(result);
            }
            _queueRunning = false;
        }

        private struct DialogQueueItem
        {
            public UIDialogParams Param;
            public CancellationToken CancellationToken;
            public UniTaskCompletionSource<UIDialogResult> Source;
        }
    }
}
