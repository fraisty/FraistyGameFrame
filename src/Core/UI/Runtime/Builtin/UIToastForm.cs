using TMPro;
using UnityEngine;

namespace FRFrame
{
    /// <summary>
    /// 内置Toast窗口。
    /// 对应配置 formId = UIToastForm
    /// </summary>
    public partial class UIToastForm : UIForm
    {
        protected override void OnInit(object userData)
        {
            if (_txtMessage != null)
                return;

            RectTransform root = UIBuiltinViewFactory.Stretch(gameObject);
            RectTransform panel = UIBuiltinViewFactory.CreatePanel("Panel", root, new Color(0f, 0f, 0f, 0.78f));
            UIBuiltinViewFactory.SetRect(panel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-360f, 72f), new Vector2(360f, 144f));

            _txtMessage = UIBuiltinViewFactory.CreateText("Message", panel, string.Empty, 24f, TMPro.TextAlignmentOptions.Center, Color.white);
            UIBuiltinViewFactory.SetRect(_txtMessage.rectTransform, Vector2.zero, Vector2.one, new Vector2(24f, 8f), new Vector2(-24f, -8f));
        }

        protected override void OnOpen(object userData)
        {
            if (userData is UIToastRequest req)
            {
                if (_txtMessage != null)
                    _txtMessage.text = req.Message;
                ApplyPosition(req.Position);
            }
        }

        private void ApplyPosition(UIToastPosition position)
        {
            RectTransform rect = transform as RectTransform;
            if (rect == null)
                return;

            switch (position)
            {
                case UIToastPosition.Top:
                    rect.anchorMin = new Vector2(0.5f, 1f);
                    rect.anchorMax = new Vector2(0.5f, 1f);
                    rect.pivot = new Vector2(0.5f, 1f);
                    rect.anchoredPosition = new Vector2(0f, -120f);
                    break;
                case UIToastPosition.Center:
                    rect.anchorMin = new Vector2(0.5f, 0.5f);
                    rect.anchorMax = new Vector2(0.5f, 0.5f);
                    rect.pivot = new Vector2(0.5f, 0.5f);
                    rect.anchoredPosition = Vector2.zero;
                    break;
                default:
                    rect.anchorMin = new Vector2(0.5f, 0f);
                    rect.anchorMax = new Vector2(0.5f, 0f);
                    rect.pivot = new Vector2(0.5f, 0f);
                    rect.anchoredPosition = new Vector2(0f, 120f);
                    break;
            }
        }
    }
}
