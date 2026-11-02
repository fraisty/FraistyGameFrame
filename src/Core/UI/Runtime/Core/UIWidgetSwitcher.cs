using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace FRFrame
{
    public sealed class UIWidgetSwitcher
    {
        private readonly UIForm _ownerForm;
        private readonly UIWidget _ownerWidget;
        private readonly Transform _parent;
        private readonly Dictionary<string, UIWidget> _widgets = new Dictionary<string, UIWidget>();

        public UIWidgetSwitcher(UIForm ownerForm, Transform parent, UIWidget ownerWidget = null)
        {
            _ownerForm = ownerForm;
            _ownerWidget = ownerWidget;
            _parent = parent;
        }

        public UIWidget Current { get; private set; }

        public async UniTask<TWidget> ShowAsync<TWidget>(string key, string assetPath, object userData = null, CancellationToken cancellationToken = default) where TWidget : UIWidget
        {
            if (string.IsNullOrEmpty(key))
                key = typeof(TWidget).Name;

            if (_widgets.TryGetValue(key, out UIWidget existing))
            {
                Show(key, userData);
                return existing as TWidget;
            }

            if (_ownerForm == null || string.IsNullOrEmpty(assetPath) || cancellationToken.IsCancellationRequested)
                return null;

            GameObject instance = await FRFM.Resource.InstantiateAsync(assetPath, _parent);
            if (instance == null)
                return null;

            if (cancellationToken.IsCancellationRequested)
            {
                FRFM.Resource.Recycle(instance);
                return null;
            }

            TWidget widget = instance.GetComponent<TWidget>();
            if (widget == null)
                widget = instance.AddComponent<TWidget>();

            int layer = _ownerWidget != null ? _ownerWidget.gameObject.layer : _ownerForm.gameObject.layer;
            instance.SetLayerRecursively(layer);
            widget.InternalInitialize(_ownerForm, _ownerWidget);
            widget.InternalOpen(userData);
            instance.SetLayerRecursively(layer);
            _ownerForm.RegisterWidget(widget);
            _widgets[key] = widget;

            if (Current != null && Current != widget)
                Current.InternalHide();

            Current = widget;
            return widget;
        }

        public bool Show(string key, object userData = null)
        {
            if (string.IsNullOrEmpty(key) || !_widgets.TryGetValue(key, out UIWidget widget))
                return false;

            if (Current != null && Current != widget)
                Current.InternalHide();

            Current = widget;
            widget.InternalShow(userData);
            return true;
        }

        public void HideCurrent()
        {
            Current?.InternalHide();
            Current = null;
        }

        public void Release(string key)
        {
            if (string.IsNullOrEmpty(key) || !_widgets.TryGetValue(key, out UIWidget widget))
                return;

            if (Current == widget)
                Current = null;

            _widgets.Remove(key);
            widget.InternalDispose();
            if (widget.gameObject != null)
                widget.ReleaseWidgetGameObject(widget.gameObject);
        }

        public void ReleaseAll()
        {
            foreach (var pair in _widgets)
            {
                UIWidget widget = pair.Value;
                if (widget == null)
                    continue;

                widget.InternalDispose();
                if (widget.gameObject != null)
                    widget.ReleaseWidgetGameObject(widget.gameObject);
            }

            _widgets.Clear();
            Current = null;
        }
    }
}
