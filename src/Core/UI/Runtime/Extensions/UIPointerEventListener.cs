using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FRFrame
{
    public sealed class UIPointerEventListener : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler, IScrollHandler, ISelectHandler, IDeselectHandler, IMoveHandler
    {
        private sealed class DisposableAction : IDisposable
        {
            private readonly Action _disposeAction;
            private bool _disposed;

            public DisposableAction(Action disposeAction)
            {
                _disposeAction = disposeAction;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                _disposeAction?.Invoke();
            }
        }

        private event Action<GameObject, PointerEventData> PointerClick;
        private event Action<GameObject, PointerEventData> PointerDown;
        private event Action<GameObject, PointerEventData> PointerUp;
        private event Action<GameObject, PointerEventData> PointerEnter;
        private event Action<GameObject, PointerEventData> PointerExit;
        private event Action<GameObject, PointerEventData> BeginDrag;
        private event Action<GameObject, PointerEventData> Drag;
        private event Action<GameObject, PointerEventData> EndDrag;
        private event Action<GameObject, PointerEventData> Drop;
        private event Action<GameObject, PointerEventData> Scroll;
        private event Action<GameObject, BaseEventData> Select;
        private event Action<GameObject, BaseEventData> Deselect;
        private event Action<GameObject, AxisEventData> Move;

        public static UIPointerEventListener Get(GameObject go)
        {
            if (go == null)
                return null;

            UIPointerEventListener listener = go.GetComponent<UIPointerEventListener>();
            if (listener == null)
                listener = go.AddComponent<UIPointerEventListener>();
            return listener;
        }

        public IDisposable AddClick(Action<GameObject, PointerEventData> callback)
        {
            if (callback == null) return null;
            PointerClick += callback;
            return new DisposableAction(() => PointerClick -= callback);
        }

        public IDisposable AddDown(Action<GameObject, PointerEventData> callback)
        {
            if (callback == null) return null;
            PointerDown += callback;
            return new DisposableAction(() => PointerDown -= callback);
        }

        public IDisposable AddUp(Action<GameObject, PointerEventData> callback)
        {
            if (callback == null) return null;
            PointerUp += callback;
            return new DisposableAction(() => PointerUp -= callback);
        }

        public IDisposable AddEnter(Action<GameObject, PointerEventData> callback)
        {
            if (callback == null) return null;
            PointerEnter += callback;
            return new DisposableAction(() => PointerEnter -= callback);
        }

        public IDisposable AddExit(Action<GameObject, PointerEventData> callback)
        {
            if (callback == null) return null;
            PointerExit += callback;
            return new DisposableAction(() => PointerExit -= callback);
        }

        public IDisposable AddBeginDrag(Action<GameObject, PointerEventData> callback)
        {
            if (callback == null) return null;
            BeginDrag += callback;
            return new DisposableAction(() => BeginDrag -= callback);
        }

        public IDisposable AddDrag(Action<GameObject, PointerEventData> callback)
        {
            if (callback == null) return null;
            Drag += callback;
            return new DisposableAction(() => Drag -= callback);
        }

        public IDisposable AddEndDrag(Action<GameObject, PointerEventData> callback)
        {
            if (callback == null) return null;
            EndDrag += callback;
            return new DisposableAction(() => EndDrag -= callback);
        }

        public IDisposable AddDrop(Action<GameObject, PointerEventData> callback)
        {
            if (callback == null) return null;
            Drop += callback;
            return new DisposableAction(() => Drop -= callback);
        }

        public IDisposable AddScroll(Action<GameObject, PointerEventData> callback)
        {
            if (callback == null) return null;
            Scroll += callback;
            return new DisposableAction(() => Scroll -= callback);
        }

        public IDisposable AddSelect(Action<GameObject, BaseEventData> callback)
        {
            if (callback == null) return null;
            Select += callback;
            return new DisposableAction(() => Select -= callback);
        }

        public IDisposable AddDeselect(Action<GameObject, BaseEventData> callback)
        {
            if (callback == null) return null;
            Deselect += callback;
            return new DisposableAction(() => Deselect -= callback);
        }

        public IDisposable AddMove(Action<GameObject, AxisEventData> callback)
        {
            if (callback == null) return null;
            Move += callback;
            return new DisposableAction(() => Move -= callback);
        }

        public void OnPointerClick(PointerEventData eventData) => PointerClick?.Invoke(gameObject, eventData);
        public void OnPointerDown(PointerEventData eventData) => PointerDown?.Invoke(gameObject, eventData);
        public void OnPointerUp(PointerEventData eventData) => PointerUp?.Invoke(gameObject, eventData);
        public void OnPointerEnter(PointerEventData eventData) => PointerEnter?.Invoke(gameObject, eventData);
        public void OnPointerExit(PointerEventData eventData) => PointerExit?.Invoke(gameObject, eventData);

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (HasDragHandlers)
                BeginDrag?.Invoke(gameObject, eventData);
            else
                ForwardDrag(eventData, ExecuteEvents.beginDragHandler);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (HasDragHandlers)
                Drag?.Invoke(gameObject, eventData);
            else
                ForwardDrag(eventData, ExecuteEvents.dragHandler);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (HasDragHandlers)
                EndDrag?.Invoke(gameObject, eventData);
            else
                ForwardDrag(eventData, ExecuteEvents.endDragHandler);
        }

        public void OnDrop(PointerEventData eventData) => Drop?.Invoke(gameObject, eventData);
        public void OnScroll(PointerEventData eventData) => Scroll?.Invoke(gameObject, eventData);
        public void OnSelect(BaseEventData eventData) => Select?.Invoke(gameObject, eventData);
        public void OnDeselect(BaseEventData eventData) => Deselect?.Invoke(gameObject, eventData);
        public void OnMove(AxisEventData eventData) => Move?.Invoke(gameObject, eventData);

        private bool HasDragHandlers => BeginDrag != null || Drag != null || EndDrag != null;

        private void ForwardDrag<THandler>(PointerEventData eventData, ExecuteEvents.EventFunction<THandler> callback)
            where THandler : IEventSystemHandler
        {
            Transform parent = transform.parent;
            while (parent != null)
            {
                if (ExecuteEvents.CanHandleEvent<THandler>(parent.gameObject))
                {
                    ExecuteEvents.Execute(parent.gameObject, eventData, callback);
                    return;
                }
                parent = parent.parent;
            }
        }

        private void OnDestroy()
        {
            PointerClick = null;
            PointerDown = null;
            PointerUp = null;
            PointerEnter = null;
            PointerExit = null;
            BeginDrag = null;
            Drag = null;
            EndDrag = null;
            Drop = null;
            Scroll = null;
            Select = null;
            Deselect = null;
            Move = null;
        }
    }
}
