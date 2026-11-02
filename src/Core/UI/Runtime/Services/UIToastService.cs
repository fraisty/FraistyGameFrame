using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace FRFrame
{
    public sealed class UIToastService
    {
        private readonly UIManager _uiManager;
        private readonly UIBuiltinConfig _config;
        private readonly List<UIToastRequest> _queue = new List<UIToastRequest>();
        private bool _showing;
        private UIToastRequest _current;

        public UIToastService(UIManager uiManager, UIBuiltinConfig config)
        {
            _uiManager = uiManager;
            _config = config;
        }

        public void Show(string message, float duration = 2f)
        {
            Show(new UIToastRequest { Message = message, Duration = duration });
        }

        public void Show(UIToastRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Message))
                return;

            if (request.MergeSameMessage)
            {
                for (int i = 0; i < _queue.Count; i++)
                {
                    if (_queue[i] != null && _queue[i].Message == request.Message && _queue[i].Position == request.Position)
                    {
                        if (request.Priority >= _queue[i].Priority)
                            _queue[i] = request;
                        return;
                    }
                }
            }

            _queue.Add(request);
            _queue.Sort(CompareToast);
            if (!_showing)
                RunAsync().Forget();
        }

        public void Clear()
        {
            _queue.Clear();
        }

        public UIToastSnapshot CreateSnapshot()
        {
            return new UIToastSnapshot
            {
                QueueCount = _queue.Count,
                Showing = _showing,
                CurrentMessage = _current != null ? _current.Message : null,
                CurrentPosition = _current != null ? _current.Position : UIToastPosition.Bottom,
                CurrentPriority = _current != null ? _current.Priority : 0
            };
        }

        private async UniTaskVoid RunAsync()
        {
            _showing = true;
            while (_queue.Count > 0)
            {
                UIToastRequest request = _queue[0];
                _queue.RemoveAt(0);
                _current = request;
                UIToastForm form = await OpenToastFormAsync(request);
                if (form != null)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(Math.Max(0.1f, request.Duration)), cancellationToken: CancellationToken.None);
                    await form.CloseAsync();
                }
                _current = null;
            }
            _showing = false;
        }

        public void Update(float deltaTime, float unscaledDeltaTime) { }

        private async UniTask<UIToastForm> OpenToastFormAsync(UIToastRequest request)
        {
            string formId = string.IsNullOrEmpty(_config?.ToastFormId) ? nameof(UIToastForm) : _config.ToastFormId;
            UIForm form = await _uiManager.OpenAsync(formId, request);
            if (form is UIToastForm toastForm)
                return toastForm;

            if (_config == null || !_config.EnableFallbackView)
                return null;

            _uiManager.RegisterCodeFormConfig<UIToastForm>(UIGroupLayer.Top, false, UIFormCloseMode.Destroy, false, UICoverVisualMode.KeepVisible, UIAnimationPreset.Fade, UIAnimationPreset.Fade, true);
            form = await _uiManager.OpenAsync(nameof(UIToastForm), request);
            return form as UIToastForm;
        }

        private static int CompareToast(UIToastRequest x, UIToastRequest y)
        {
            int priority = y.Priority.CompareTo(x.Priority);
            return priority != 0 ? priority : x.Position.CompareTo(y.Position);
        }
    }
}
