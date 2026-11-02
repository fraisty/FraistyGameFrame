using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FRFrame
{
    [Serializable]
    public sealed class UIScrollbarValueEvent : UnityEvent<float>
    {
    }

    public sealed class UIScrollbarDragEvents : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler, IPointerUpHandler
    {
        public Scrollbar target;
        public UIScrollbarValueEvent onPointerDown = new UIScrollbarValueEvent();
        public UIScrollbarValueEvent onPointerUp = new UIScrollbarValueEvent();
        public UIScrollbarValueEvent onBeginDrag = new UIScrollbarValueEvent();
        public UIScrollbarValueEvent onDragging = new UIScrollbarValueEvent();
        public UIScrollbarValueEvent onEndDrag = new UIScrollbarValueEvent();

        public Scrollbar Target
        {
            get
            {
                if (target == null)
                    target = GetComponent<Scrollbar>();
                return target;
            }
        }

        public float Value => Target != null ? Target.value : 0f;

        public void OnPointerDown(PointerEventData eventData) => onPointerDown.Invoke(Value);
        public void OnPointerUp(PointerEventData eventData) => onPointerUp.Invoke(Value);
        public void OnBeginDrag(PointerEventData eventData) => onBeginDrag.Invoke(Value);
        public void OnDrag(PointerEventData eventData) => onDragging.Invoke(Value);
        public void OnEndDrag(PointerEventData eventData) => onEndDrag.Invoke(Value);
    }
}
