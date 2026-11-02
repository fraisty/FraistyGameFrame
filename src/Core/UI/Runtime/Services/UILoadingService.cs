using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace FRFrame
{
    public sealed class UILoadingService
    {
        private readonly UIManager _uiManager;
        private readonly UIBuiltinConfig _config;
        private int _refCount;
        private UILoadingForm _form;
        private UILoadingRequest _current;

        public UILoadingService(UIManager uiManager, UIBuiltinConfig config)
        {
            _uiManager = uiManager;
            _config = config;
        }

        public async UniTask ShowAsync(string message = "Loading...", CancellationToken cancellationToken = default)
        {
            await ShowAsync(new UILoadingRequest { Message = message }, cancellationToken);
        }

        public async UniTask ShowAsync(UILoadingRequest request, CancellationToken cancellationToken = default)
        {
            _refCount++;
            request ??= new UILoadingRequest { Message = "Loading..." };
            _current = request;
            CancellationTokenSource timeoutSource = request.Timeout > 0f ? new CancellationTokenSource() : null;
            if (timeoutSource != null)
            {
                timeoutSource.CancelAfterSlim(TimeSpan.FromSeconds(request.Timeout));
                timeoutSource.Token.Register(ForceHide);
            }

            if (_form == null)
            {
                _form = await OpenLoadingFormAsync(request, cancellationToken);
            }
            else
            {
                _form.SetMessage(request.Message);
                _form.SetProgress(request.Progress, request.ShowProgress);
                _form.Show();
            }
        }

        public void SetMessage(string message)
        {
            _form?.SetMessage(message);
        }

        public void SetProgress(float progress)
        {
            _form?.SetProgress(progress, true);
        }

        public void Hide()
        {
            _refCount--;
            if (_refCount <= 0)
                ForceHide();
        }

        public void ForceHide()
        {
            _refCount = 0;
            _current = null;
            if (_form != null)
            {
                _form.Close();
                _form = null;
            }
        }

        public IDisposable ShowScope(string message = "Loading...")
        {
            ShowAsync(message).Forget();
            return new LoadingScope(this);
        }

        public void Update(float deltaTime, float unscaledDeltaTime) { }

        public UILoadingSnapshot CreateSnapshot()
        {
            return new UILoadingSnapshot
            {
                RefCount = _refCount,
                Visible = _form != null && _form.State == UIFormState.Opened,
                Message = _current != null ? _current.Message : null,
                Progress = _current != null ? _current.Progress : 0f,
                ShowProgress = _current != null && _current.ShowProgress
            };
        }

        private async UniTask<UILoadingForm> OpenLoadingFormAsync(UILoadingRequest request, CancellationToken cancellationToken)
        {
            string formId = string.IsNullOrEmpty(_config?.LoadingFormId) ? nameof(UILoadingForm) : _config.LoadingFormId;
            UIOpenOptions options = UIOpenOptions.FromUserData(request, cancellationToken);
            UIForm form = await _uiManager.OpenAsync(formId, options);
            if (form is UILoadingForm loadingForm)
                return loadingForm;

            if (_config == null || !_config.EnableFallbackView)
                return null;

            _uiManager.RegisterCodeFormConfig<UILoadingForm>(UIGroupLayer.System, false, UIFormCloseMode.Cache, true, UICoverVisualMode.KeepVisible, UIAnimationPreset.Fade, UIAnimationPreset.Fade, true);
            form = await _uiManager.OpenAsync(nameof(UILoadingForm), options);
            return form as UILoadingForm;
        }

        private sealed class LoadingScope : IDisposable
        {
            private readonly UILoadingService _service;
            private bool _disposed;

            public LoadingScope(UILoadingService service)
            {
                _service = service;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                _service.Hide();
            }
        }
    }
}
