using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FRFrame
{
    public abstract class UIWidget : MonoBehaviour
    {
        public UIForm OwnerForm { get; private set; }
        public UIWidget ParentWidget { get; private set; }
        public UIForm RootForm { get; private set; }
        public UIWidgetState State { get; private set; }
        public bool IsInitialized { get; private set; }
        public bool IsVisible { get; private set; }

        protected UIBindData _bindData;
        protected RectTransform _rectTransform;

        private readonly List<UIWidget> _children = new List<UIWidget>();
        private readonly UIEventScopeSet _eventScopes = new UIEventScopeSet();

        internal void InternalInitialize(UIForm ownerForm, UIWidget parentWidget = null)
        {
            OwnerForm = ownerForm;
            ParentWidget = parentWidget;
            RootForm = ownerForm != null ? ownerForm : parentWidget?.RootForm;
            _rectTransform = transform as RectTransform;
            _bindData = GetComponent<UIBindData>();

            if (!IsInitialized)
            {
                BindInit();
                BindEvent();
                OnInit();
                IsInitialized = true;
                State = UIWidgetState.Initialized;
            }
        }

        internal void InternalOpen(object userData = null)
        {
            gameObject.SetActive(true);
            IsVisible = true;
            State = UIWidgetState.Opened;
            OnOpen(userData);
        }

        internal void InternalShow(object userData = null)
        {
            gameObject.SetActive(true);
            IsVisible = true;
            State = UIWidgetState.Opened;
            OnShow(userData);
        }

        internal void InternalHide()
        {
            _eventScopes.ClearVisible();
            for (int i = 0; i < _children.Count; i++)
            {
                if (_children[i] != null && _children[i].IsVisible)
                    _children[i].InternalHide();
            }

            gameObject.SetActive(false);
            IsVisible = false;
            State = UIWidgetState.Hidden;
            OnHide();
        }

        internal void InternalRefresh(object userData = null)
        {
            OnRefresh(userData);
        }

        internal void InternalDispose()
        {
            if (State == UIWidgetState.Disposed)
                return;

            ReleaseAllChildWidgets();
            _eventScopes.Dispose();
            UnbindEvent();
            OnDispose();
            State = UIWidgetState.Disposed;
        }

        public void Refresh(object userData = null)
        {
            InternalRefresh(userData);
        }

        /// <summary>Opens a world-space or standalone widget through the same initialization and
        /// visibility lifecycle as form-owned widgets. The caller owns placement/pooling; this does
        /// not register a UIForm, create a timer or replace UIManager for ordinary application UI.</summary>
        public void OpenStandalone(object userData = null)
        {
            if (OwnerForm != null || ParentWidget != null)
                throw new InvalidOperationException("A form-owned widget cannot be opened as standalone.");
            if (State == UIWidgetState.Disposed)
                throw new ObjectDisposedException(GetType().Name);
            InternalInitialize(null);
            InternalOpen(userData);
        }

        public void SetVisible(bool visible)
        {
            if (visible)
                InternalShow();
            else
                InternalHide();
        }

        protected T CreateChildWidget<T>(GameObject root, object userData = null) where T : UIWidget
        {
            if (root == null)
                return null;

            T widget = root.GetComponent<T>();
            if (widget == null)
                widget = root.AddComponent<T>();

            root.SetLayerRecursively(gameObject.layer);
            widget.InternalInitialize(OwnerForm, this);
            widget.InternalOpen(userData);
            root.SetLayerRecursively(gameObject.layer);
            RegisterChildWidget(widget);
            OwnerForm?.RegisterWidget(widget);
            return widget;
        }

        protected async UniTask<T> LoadChildWidgetAsync<T>(string assetPath, Transform parent, object userData = null, CancellationToken cancellationToken = default) where T : UIWidget
        {
            if (string.IsNullOrEmpty(assetPath) || cancellationToken.IsCancellationRequested)
                return null;

            GameObject instance = await FRFM.Resource.InstantiateAsync(assetPath, parent);
            if (instance == null)
                return null;

            if (cancellationToken.IsCancellationRequested)
            {
                FRFM.Resource.Recycle(instance);
                return null;
            }

            return CreateChildWidget<T>(instance, userData);
        }

        protected UIWidgetSwitcher CreateWidgetSwitcher(Transform parent)
        {
            return new UIWidgetSwitcher(OwnerForm, parent, this);
        }

        private void RegisterChildWidget(UIWidget widget)
        {
            if (widget != null && !_children.Contains(widget))
                _children.Add(widget);
        }

        private void ReleaseAllChildWidgets()
        {
            for (int i = _children.Count - 1; i >= 0; i--)
            {
                UIWidget child = _children[i];
                if (child == null)
                    continue;

                child.InternalDispose();
                ReleaseWidgetGameObject(child.gameObject);
            }

            _children.Clear();
        }

        internal void ReleaseWidgetGameObject(GameObject widgetGameObject)
        {
            if (widgetGameObject == null)
                return;

            if (widgetGameObject.GetComponent<AssetInstanceTracker>() != null)
                FRFM.Resource.Recycle(widgetGameObject);
            else
                Destroy(widgetGameObject);
        }

        protected IDisposable AddButtonClick(Button button, UnityAction action, UIEventListenMode mode = UIEventListenMode.UntilDispose)
        {
            return AddEventSubscription(UIEventUtility.AddButtonClick(button, action), mode);
        }

        protected IDisposable AddToggleChanged(Toggle toggle, UnityAction<bool> action, UIEventListenMode mode = UIEventListenMode.UntilDispose)
        {
            return AddEventSubscription(UIEventUtility.AddToggleChanged(toggle, action), mode);
        }

        protected IDisposable AddSliderChanged(Slider slider, UnityAction<float> action, UIEventListenMode mode = UIEventListenMode.UntilDispose)
        {
            return AddEventSubscription(UIEventUtility.AddSliderChanged(slider, action), mode);
        }

        protected IDisposable AddInputChanged(InputField input, UnityAction<string> action, UIEventListenMode mode = UIEventListenMode.UntilDispose)
        {
            return AddEventSubscription(UIEventUtility.AddInputChanged(input, action), mode);
        }

        protected IDisposable AddInputChanged(TMP_InputField input, UnityAction<string> action, UIEventListenMode mode = UIEventListenMode.UntilDispose)
        {
            return AddEventSubscription(UIEventUtility.AddInputChanged(input, action), mode);
        }

        protected IDisposable AddUIEvent(UnityEvent unityEvent, UnityAction action, UIEventListenMode mode = UIEventListenMode.UntilDispose)
        {
            return AddEventSubscription(UIEventUtility.AddUIEvent(unityEvent, action), mode);
        }

        protected IDisposable AddUIEvent<T>(UnityEvent<T> unityEvent, UnityAction<T> action, UIEventListenMode mode = UIEventListenMode.UntilDispose)
        {
            return AddEventSubscription(UIEventUtility.AddUIEvent(unityEvent, action), mode);
        }

        protected IDisposable AddPointerClick(GameObject target, Action<GameObject, PointerEventData> action, UIEventListenMode mode = UIEventListenMode.UntilDispose)
        {
            return AddEventSubscription(UIPointerEventListener.Get(target)?.AddClick(action), mode);
        }

        protected IDisposable AddPointerDown(GameObject target, Action<GameObject, PointerEventData> action, UIEventListenMode mode = UIEventListenMode.UntilDispose)
        {
            return AddEventSubscription(UIPointerEventListener.Get(target)?.AddDown(action), mode);
        }

        protected IDisposable AddPointerUp(GameObject target, Action<GameObject, PointerEventData> action, UIEventListenMode mode = UIEventListenMode.UntilDispose)
        {
            return AddEventSubscription(UIPointerEventListener.Get(target)?.AddUp(action), mode);
        }

        protected IDisposable AddPointerBeginDrag(GameObject target, Action<GameObject, PointerEventData> action, UIEventListenMode mode = UIEventListenMode.UntilDispose)
        {
            return AddEventSubscription(UIPointerEventListener.Get(target)?.AddBeginDrag(action), mode);
        }

        protected IDisposable AddPointerDrag(GameObject target, Action<GameObject, PointerEventData> action, UIEventListenMode mode = UIEventListenMode.UntilDispose)
        {
            return AddEventSubscription(UIPointerEventListener.Get(target)?.AddDrag(action), mode);
        }

        protected IDisposable AddPointerEndDrag(GameObject target, Action<GameObject, PointerEventData> action, UIEventListenMode mode = UIEventListenMode.UntilDispose)
        {
            return AddEventSubscription(UIPointerEventListener.Get(target)?.AddEndDrag(action), mode);
        }

        protected IDisposable AddPointerDrop(GameObject target, Action<GameObject, PointerEventData> action, UIEventListenMode mode = UIEventListenMode.UntilDispose)
        {
            return AddEventSubscription(UIPointerEventListener.Get(target)?.AddDrop(action), mode);
        }

        protected IDisposable AddPointerScroll(GameObject target, Action<GameObject, PointerEventData> action, UIEventListenMode mode = UIEventListenMode.UntilDispose)
        {
            return AddEventSubscription(UIPointerEventListener.Get(target)?.AddScroll(action), mode);
        }

        protected IDisposable AddPointerEnter(GameObject target, Action<GameObject, PointerEventData> action, UIEventListenMode mode = UIEventListenMode.UntilDispose)
        {
            return AddEventSubscription(UIPointerEventListener.Get(target)?.AddEnter(action), mode);
        }

        protected IDisposable AddPointerExit(GameObject target, Action<GameObject, PointerEventData> action, UIEventListenMode mode = UIEventListenMode.UntilDispose)
        {
            return AddEventSubscription(UIPointerEventListener.Get(target)?.AddExit(action), mode);
        }

        protected IDisposable AddPointerSelect(GameObject target, Action<GameObject, BaseEventData> action, UIEventListenMode mode = UIEventListenMode.UntilDispose)
        {
            return AddEventSubscription(UIPointerEventListener.Get(target)?.AddSelect(action), mode);
        }

        protected IDisposable AddPointerDeselect(GameObject target, Action<GameObject, BaseEventData> action, UIEventListenMode mode = UIEventListenMode.UntilDispose)
        {
            return AddEventSubscription(UIPointerEventListener.Get(target)?.AddDeselect(action), mode);
        }

        protected IDisposable AddPointerMove(GameObject target, Action<GameObject, AxisEventData> action, UIEventListenMode mode = UIEventListenMode.UntilDispose)
        {
            return AddEventSubscription(UIPointerEventListener.Get(target)?.AddMove(action), mode);
        }

        protected IDisposable Listen<TEvent>(Action<TEvent> handler, UIEventListenMode mode = UIEventListenMode.UntilDispose, int priority = 0)
        {
            return FRFM.Event == null ? null : AddEventSubscription(FRFM.Event.Subscribe(handler, this, priority), mode);
        }

        protected IDisposable ListenOnce<TEvent>(Action<TEvent> handler, UIEventListenMode mode = UIEventListenMode.UntilDispose, int priority = 0)
        {
            return FRFM.Event == null ? null : AddEventSubscription(FRFM.Event.SubscribeOnce(handler, this, priority), mode);
        }

        protected void Publish<TEvent>(TEvent evt)
        {
            FRFM.Event?.Publish(evt);
        }

        protected void Post<TEvent>(TEvent evt)
        {
            FRFM.Event?.Post(evt);
        }

        protected IDisposable AddEventSubscription(IDisposable subscription, UIEventListenMode mode)
        {
            return _eventScopes.Add(subscription, mode);
        }

        protected virtual void BindInit() { }
        protected virtual void BindEvent() { }
        protected virtual void UnbindEvent() { }
        protected virtual void OnInit() { }
        protected virtual void OnOpen(object userData) { }
        protected virtual void OnShow(object userData) { }
        protected virtual void OnHide() { }
        protected virtual void OnRefresh(object userData) { }
        protected virtual void OnDispose() { }

        protected virtual void OnDestroy()
        {
            InternalDispose();
        }
    }
}
