using UnityEngine;

namespace FRFrame
{
    public enum UIRichTextLinkPointerEvent
    {
        Click,
        LongPress,
        Hover
    }

    public sealed class UIRichTextClickContext
    {
        public UIRichText Source { get; }
        public string LinkId { get; }
        public string Scheme { get; }
        public string Payload { get; }
        public string Text { get; }
        public int LinkIndex { get; }
        public Vector2 ScreenPosition { get; }
        public UIRichTextLinkPointerEvent PointerEvent { get; }

        public UIRichTextClickContext(UIRichText source, string linkId, string text, int linkIndex, Vector2 screenPosition, UIRichTextLinkPointerEvent pointerEvent)
        {
            Source = source;
            LinkId = linkId ?? string.Empty;
            Text = text ?? string.Empty;
            LinkIndex = linkIndex;
            ScreenPosition = screenPosition;
            PointerEvent = pointerEvent;

            int splitIndex = LinkId.IndexOf(':');
            if (splitIndex >= 0)
            {
                Scheme = LinkId.Substring(0, splitIndex);
                Payload = LinkId.Substring(splitIndex + 1);
            }
            else
            {
                Scheme = LinkId;
                Payload = string.Empty;
            }
        }
    }
}
