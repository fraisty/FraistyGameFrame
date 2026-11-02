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
    [DisallowMultipleComponent]
    public abstract class UIForm : MonoBehaviour
    {
        public int SerialId { get; internal set; }
        public string FormId { get; internal set; }
        public UIFormConfig Config { get; internal set; }
        public UIGroup Group { get; internal set; }
        public UIFormState State { get; internal set; }

        public bool IsInitialized { get; private set; }
        public bool IsPaused { get; private set; }
        public bool IsCovered { get; private set; }
        public bool IsVisible { get; private set; }
        public bool IsClosing => State == UIFormState.Closing;
        public int SortingOrder => _canvas != null ? _canvas.sortingOrder : 0;
        public bool UseMask => _useMaskOverride ?? (Config != null && Config.useMask);
        public bool CloseOnMaskClick => _closeOnMaskClickOverride ?? (Config != null && Config.closeOnMaskClick);
        public UIMaskMode MaskMode => _maskModeOverride ?? (Config != null ? Config.maskMode : UIMaskMode.None);
        public Color MaskColor => _maskColorOverride ?? (Config != null ? Config.maskColor : Color.clear);

        protected Canvas _canvas;
        protected GraphicRaycaster _raycaster;
        protected CanvasGroup _canvasGroup;
        protected RectTransform _rectTransform;
        protected UIBindData _bindData;

        private readonly List<UIWidget> _widgets = new List<UIWidget>();
        private readonly UIEventScopeSet _eventScopes = new UIEventScopeSet();
        private bool? _useMaskOverride;
        private bool? _closeOnMaskClickOverride;
        private UIMaskMode? _maskModeOverride;
        private Color? _maskColorOverride;

        internal void InternalSetLoading()
        {
            State = UIFormState.Loading;
        }

        internal void ApplyOpenOptions(UIOpenOptions options)
        {
            _useMaskOverride = options.UseMaskOverride;
            _closeOnMaskClickOverride = options.CloseOnMaskClickOverride;
            _maskModeOverride = options.MaskModeOverride;
            _maskColorOverride = options.MaskColorOverride;
        }

        internal void InternalInitialize(UIFormOpenContext context)
        {
            FormId = context.FormId;
            Config = context.Config;

            _rectTransform = transform as RectTransform;
            _canvas = gameObject.GetComponent<Canvas>();
            if (_canvas == null)
                _canvas = gameObject.AddComponent<Canvas>();

            _canvas.overrideSorting = true;

            _raycaster = gameObject.GetComponent<GraphicRaycaster>();
            if (_raycaster == null)
                _raycaster = gameObject.AddComponent<GraphicRaycaster>();

            _canvasGroup = gameObject.GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();

            _bindData = gameObject.GetComponent<UIBindData>();

            if (!IsInitialized)
            {
                BindInit();
                BindEvent();
                OnInit(context.UserData);
                IsInitialized = true;
                State = UIFormState.Initialized;
            }
        }

        internal void InternalOpen(object userData)
        {
            gameObject.SetActive(true);
            SetVisible(true);
            IsPaused = false;
            IsCovered = false;
            State = UIFormState.Opened;
            OnOpen(userData);
        }

        internal void InternalClose(object userData)
        {
            _eventScopes.ClearOpen();
            OnClose(userData);
            State = UIFormState.Closed;
        }

        internal void InternalBeginClose()
        {
            State = UIFormState.Closing;
        }

        internal void InternalPreload()
        {
            gameObject.SetActive(false);
            SetVisible(false);
            State = UIFormState.Preloaded;
        }

        internal void InternalPause()
        {
            if (IsPaused) return;
            IsPaused = true;
            _eventScopes.ClearVisible();
            OnPause();
        }

        internal void InternalResume()
        {
            if (!IsPaused) return;
            IsPaused = false;
            OnResume();
        }

        internal void InternalCover()
        {
            if (IsCovered) return;
            IsCovered = true;
            _eventScopes.ClearVisible();

            switch (Config.coverVisualMode)
            {
                case UICoverVisualMode.KeepVisible:
                    _canvasGroup.interactable = false;
                    _canvasGroup.blocksRaycasts = false;
                    break;
                case UICoverVisualMode.HideByCanvasGroup:
                    _canvasGroup.alpha = 0f;
                    _canvasGroup.interactable = false;
                    _canvasGroup.blocksRaycasts = false;
                    break;
                case UICoverVisualMode.SetInactive:
                    gameObject.SetActive(false);
                    break;
            }

            OnCover();
        }

        internal void InternalReveal()
        {
            if (!IsCovered) return;
            IsCovered = false;

            switch (Config.coverVisualMode)
            {
                case UICoverVisualMode.KeepVisible:
                    _canvasGroup.interactable = true;
                    _canvasGroup.blocksRaycasts = true;
                    break;
                case UICoverVisualMode.HideByCanvasGroup:
                    _canvasGroup.alpha = 1f;
                    _canvasGroup.interactable = true;
                    _canvasGroup.blocksRaycasts = true;
                    break;
                case UICoverVisualMode.SetInactive:
                    gameObject.SetActive(true);
                    break;
            }

            OnReveal();
        }

        internal void InternalSetDepth(int sortingOrder)
        {
            if (_canvas != null)
                _canvas.sortingOrder = sortingOrder;

            OnDepthChanged(sortingOrder);
        }

        internal void InternalHide()
        {
            _eventScopes.ClearVisible();
            for (int i = 0; i < _widgets.Count; i++)
            {
                if (_widgets[i] != null && _widgets[i].IsVisible)
                    _widgets[i].InternalHide();
            }

            SetVisible(false);
            State = UIFormState.Hidden;
            OnHide();
            if (Config != null && Config.disableGameObjectWhenHidden)
                gameObject.SetActive(false);
        }

        internal void InternalShow(object userData)
        {
            gameObject.SetActive(true);
            SetVisible(true);
            State = UIFormState.Opened;
            OnShow(userData);
        }

        internal void InternalDispose()
        {
            if (State == UIFormState.Disposed)
                return;

            ReleaseAllWidgets();
            _eventScopes.Dispose();
            _useMaskOverride = null;
            _closeOnMaskClickOverride = null;
            _maskModeOverride = null;
            _maskColorOverride = null;
            UnbindEvent();
            OnDispose();
            State = UIFormState.Disposed;
        }

        internal void InternalUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (State != UIFormState.Opened || IsPaused)
                return;

            OnUpdate(deltaTime, unscaledDeltaTime);
        }

        internal UniTask InternalPlayOpenAnimationAsync(CancellationToken cancellationToken)
        {
            return PlayOpenAnimationAsync(cancellationToken);
        }

        internal UniTask InternalPlayCloseAnimationAsync(CancellationToken cancellationToken)
        {
            return PlayCloseAnimationAsync(cancellationToken);
        }

        public void Close(object userData = null)
        {
            FRFM.UI?.Close(this, userData);
        }

        public UniTask CloseAsync(object userData = null, CancellationToken cancellationToken = default)
        {
            return FRFM.UI == null ? UniTask.CompletedTask : FRFM.UI.CloseAsync(this, userData, cancellationToken);
        }

        public void Hide()
        {
            FRFM.UI?.Hide(this);
        }

        public void Show(object userData = null)
        {
            FRFM.UI?.Show(this, userData);
        }

        protected T CreateWidget<T>(GameObject root, object userData = null) where T : UIWidget
        {
            if (root == null)
            {
                FRLog.Error($"UIForm.CreateWidget failed, root is null. widget type : {typeof(T).Name}");
                return null;
            }

            T widget = root.GetComponent<T>();
            if (widget == null)
                widget = root.AddComponent<T>();

            root.SetLayerRecursively(gameObject.layer);
            widget.InternalInitialize(this);
            widget.InternalOpen(userData);
            root.SetLayerRecursively(gameObject.layer);
            RegisterWidget(widget);
            return widget;
        }

        protected async UniTask<T> LoadWidgetAsync<T>(ResourceKey key, Transform parent, object userData = null, CancellationToken cancellationToken = default) where T : UIWidget
        {
            if (string.IsNullOrEmpty(key.Location) || cancellationToken.IsCancellationRequested)
                return null;

            GameObject instance = await FRFM.Resource.InstantiateAsync(key, parent);
            if (instance == null)
                return null;

            if (cancellationToken.IsCancellationRequested)
            {
                FRFM.Resource.Recycle(instance);
                return null;
            }

            return CreateWidget<T>(instance, userData);
        }

        protected UIWidgetSwitcher CreateWidgetSwitcher(Transform parent)
        {
            return new UIWidgetSwitcher(this, parent);
        }

        protected void ShowWidget(UIWidget widget, object userData = null)
        {
            widget?.InternalShow(userData);
        }

        protected void HideWidget(UIWidget widget)
        {
            widget?.InternalHide();
        }

        protected void ReleaseWidget(UIWidget widget)
        {
            if (widget == null)
                return;

            _widgets.Remove(widget);
            widget.InternalDispose();
            ReleaseWidgetGameObject(widget.gameObject);
        }

        protected void ReleaseAllWidgets()
        {
            for (int i = _widgets.Count - 1; i >= 0; i--)
            {
                UIWidget widget = _widgets[i];
                if (widget == null)
                    continue;

                widget.InternalDispose();
                ReleaseWidgetGameObject(widget.gameObject);
            }

            _widgets.Clear();
        }

        internal void RegisterWidget(UIWidget widget)
        {
            if (widget != null && !_widgets.Contains(widget))
                _widgets.Add(widget);
        }

        private void ReleaseWidgetGameObject(GameObject widgetGameObject)
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

        protected void SetVisible(bool visible)
        {
            IsVisible = visible;
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = visible ? 1f : 0f;
                _canvasGroup.interactable = visible;
                _canvasGroup.blocksRaycasts = visible;
            }
        }

        protected virtual UniTask PlayOpenAnimationAsync(CancellationToken cancellationToken = default)
        {
            return UIDefaultAnimationPlayer.PlayOpenAsync(this, Config, cancellationToken);
        }

        protected virtual UniTask PlayCloseAnimationAsync(CancellationToken cancellationToken = default)
        {
            return UIDefaultAnimationPlayer.PlayCloseAsync(this, Config, cancellationToken);
        }

        protected virtual void BindInit() { }
        protected virtual void BindEvent() { }
        protected virtual void UnbindEvent() { }
        // Return true when a local page/widget consumes Back before the form closes.
        internal bool InternalTryHandleBack() => OnBackRequested();
        protected virtual bool OnBackRequested() => false;
        protected virtual void OnInit(object userData) { }
        protected virtual void OnOpen(object userData) { }
        protected virtual void OnClose(object userData) { }
        protected virtual void OnPause() { }
        protected virtual void OnResume() { }
        protected virtual void OnCover() { }
        protected virtual void OnReveal() { }
        protected virtual void OnHide() { }
        protected virtual void OnShow(object userData) { }
        protected virtual void OnDepthChanged(int sortingOrder) { }
        protected virtual void OnUpdate(float deltaTime, float unscaledDeltaTime) { }
        protected virtual void OnDispose() { }
    }
}
