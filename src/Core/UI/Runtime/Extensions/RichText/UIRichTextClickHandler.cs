using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FRFrame
{
    /// <summary>TMP 链接命中处理器，负责把点击和长按转发给 UIRichText。</summary>
    public sealed class UIRichTextClickHandler : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IPointerMoveHandler
    {
        public UIRichText owner;
        public TMP_Text targetText;

        private bool _pressed;
        private bool _suppressClick;
        private int _pressedLinkIndex = -1;
        private float _pressStartTime;
        private Vector2 _pressPosition;

        private void Update()
        {
            if (!_pressed || _suppressClick || owner == null || !owner.EnableLongPress)
                return;

            if (Time.unscaledTime - _pressStartTime < owner.LongPressThreshold)
                return;

            _suppressClick = true;
            Dispatch(_pressedLinkIndex, _pressPosition, UIRichTextLinkPointerEvent.LongPress);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _pressed = true;
            _suppressClick = false;
            _pressStartTime = Time.unscaledTime;
            _pressPosition = eventData.position;
            _pressedLinkIndex = FindLinkIndex(eventData.position);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _pressed = false;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_suppressClick || owner == null || !owner.EnableClick)
                return;

            Dispatch(FindLinkIndex(eventData.position), eventData.position, UIRichTextLinkPointerEvent.Click);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _pressed = false;
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            if (owner == null || !owner.EnableHover)
                return;

            Dispatch(FindLinkIndex(eventData.position), eventData.position, UIRichTextLinkPointerEvent.Hover);
        }

        private int FindLinkIndex(Vector2 screenPosition)
        {
            if (targetText == null)
                return -1;

            return TMP_TextUtilities.FindIntersectingLink(targetText, screenPosition, UIRichTextUtility.ResolveEventCamera(targetText));
        }

        private void Dispatch(int linkIndex, Vector2 screenPosition, UIRichTextLinkPointerEvent pointerEvent)
        {
            if (owner == null || targetText == null || linkIndex < 0 || linkIndex >= targetText.textInfo.linkCount)
                return;

            TMP_LinkInfo linkInfo = targetText.textInfo.linkInfo[linkIndex];
            owner.DispatchLinkEvent(new UIRichTextClickContext(owner, linkInfo.GetLinkID(), linkInfo.GetLinkText(), linkIndex, screenPosition, pointerEvent));
        }
    }
}
