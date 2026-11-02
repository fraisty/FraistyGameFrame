using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace FRFrame
{
    public sealed class UINavigator
    {
        private readonly UIManager _uiManager;
        private readonly List<UINavEntry> _stack = new List<UINavEntry>();

        public UINavigator(UIManager uiManager)
        {
            _uiManager = uiManager;
        }

        public int Count => _stack.Count;
        public IReadOnlyList<UINavEntry> Stack => _stack;

        public async UniTask<TForm> PushAsync<TForm>(object userData = null, CancellationToken cancellationToken = default) where TForm : UIForm
        {
            return await PushAsync<TForm>(userData, UINavClosePolicy.HidePrevious, cancellationToken);
        }

        public async UniTask<TForm> PushAsync<TForm, TParam>(TParam param, CancellationToken cancellationToken = default) where TForm : UIForm
        {
            return await PushAsync<TForm>(param, UINavClosePolicy.HidePrevious, cancellationToken);
        }

        public async UniTask<TForm> PushAsync<TForm>(object userData, UINavClosePolicy closePolicy, CancellationToken cancellationToken = default) where TForm : UIForm
        {
            UINavigationResult<TForm> result = await TryPushAsync<TForm>(userData, closePolicy, cancellationToken);
            return result.Form;
        }

        public async UniTask<UINavigationResult<TForm>> TryPushAsync<TForm>(object userData = null, UINavClosePolicy closePolicy = UINavClosePolicy.HidePrevious, CancellationToken cancellationToken = default) where TForm : UIForm
        {
            UINavEntry previous = _stack.Count > 0 ? _stack[_stack.Count - 1] : null;
            bool previousHidden = false;

            if (previous?.Form != null && closePolicy == UINavClosePolicy.HidePrevious)
            {
                previous.Form.Hide();
                previousHidden = true;
            }

            TForm form = await _uiManager.OpenAsync<TForm>(userData, cancellationToken);
            if (form == null)
            {
                if (previousHidden && previous?.Form != null)
                    previous.Form.Show(previous.UserData);

                return cancellationToken.IsCancellationRequested
                    ? UINavigationResult<TForm>.Failure(UINavigationFailureReason.Canceled, typeof(TForm).Name)
                    : UINavigationResult<TForm>.Failure(UINavigationFailureReason.OpenFailed, typeof(TForm).Name);
            }

            if (previous?.Form != null && closePolicy == UINavClosePolicy.ClosePrevious)
                previous.Form.Close();

            WarnIfNotNormalLayer(form);
            _stack.Add(new UINavEntry
            {
                FormId = form.FormId,
                Form = form,
                UserData = userData,
                Layer = form.Config != null ? form.Config.layer : UIGroupLayer.Normal,
                ClosePolicy = closePolicy
            });
            return UINavigationResult<TForm>.Success(form);
        }

        public async UniTask<TForm> ReplaceAsync<TForm>(object userData = null, CancellationToken cancellationToken = default) where TForm : UIForm
        {
            UINavigationResult<TForm> result = await TryReplaceAsync<TForm>(userData, cancellationToken);
            return result.Form;
        }

        public async UniTask<TForm> ReplaceAsync<TForm, TParam>(TParam param, CancellationToken cancellationToken = default) where TForm : UIForm
        {
            return await ReplaceAsync<TForm>(param, cancellationToken);
        }

        public async UniTask<UINavigationResult<TForm>> TryReplaceAsync<TForm>(object userData = null, CancellationToken cancellationToken = default) where TForm : UIForm
        {
            UINavEntry top = _stack.Count > 0 ? _stack[_stack.Count - 1] : null;
            UINavigationResult<TForm> result = await TryPushAsync<TForm>(userData, UINavClosePolicy.KeepPrevious, cancellationToken);
            if (!result.Succeeded)
                return result;

            if (top != null)
            {
                _stack.Remove(top);
                top.Form?.Close();
            }

            return result;
        }

        public bool Back(object userData = null)
        {
            if (_stack.Count <= 1)
                return false;

            UINavEntry top = _stack[_stack.Count - 1];
            _stack.RemoveAt(_stack.Count - 1);
            if (top.Form != null)
                top.Form.Close(userData);

            UINavEntry previous = _stack[_stack.Count - 1];
            if (previous.Form != null)
                previous.Form.Show(previous.UserData);

            return true;
        }

        public void BackToRoot(object userData = null)
        {
            while (_stack.Count > 1)
                Back(userData);
        }

        public void Clear(bool closeForms = false)
        {
            if (closeForms)
            {
                for (int i = _stack.Count - 1; i >= 0; i--)
                    _stack[i].Form?.Close();
            }

            _stack.Clear();
        }

        private void WarnIfNotNormalLayer(UIForm form)
        {
            if (form?.Config != null && form.Config.layer != UIGroupLayer.Normal)
                FRLog.Warning($"UINavigator warning, pushed form should use Normal layer. form : {form.FormId}, layer : {form.Config.layer}");
        }

        internal void Remove(UIForm form)
        {
            if (form == null)
                return;

            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                if (_stack[i].Form == form)
                    _stack.RemoveAt(i);
            }
        }
    }
}
