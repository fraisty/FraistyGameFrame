using UnityEngine;
using UnityEngine.UI;

namespace FRFrame
{
    /// <summary>
    /// 内置Loading窗口。
    /// </summary>
    public partial class UILoadingForm : UIForm
    {
        protected override void OnInit(object userData)
        {
            if (_txtMessage != null)
                return;

            RectTransform root = UIBuiltinViewFactory.Stretch(gameObject);
            Image bg = gameObject.GetComponent<Image>();
            if (bg == null)
                bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.55f);
            bg.raycastTarget = true;

            RectTransform panel = UIBuiltinViewFactory.CreatePanel("Panel", root, new Color(0.08f, 0.09f, 0.12f, 0.96f));
            UIBuiltinViewFactory.SetRect(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-260f, -86f), new Vector2(260f, 86f));

            _txtMessage = UIBuiltinViewFactory.CreateText("Message", panel, "Loading...", 26f, TMPro.TextAlignmentOptions.Center, Color.white);
            UIBuiltinViewFactory.SetRect(_txtMessage.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 1f), new Vector2(24f, -18f), new Vector2(-24f, -10f));

            GameObject sliderGo = new GameObject("Progress", typeof(RectTransform), typeof(Slider));
            sliderGo.transform.SetParent(panel, false);
            UIBuiltinViewFactory.SetRect(sliderGo.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(42f, 24f), new Vector2(-42f, 48f));
            Slider slider = sliderGo.GetComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            sliderGo.SetActive(false);
        }

        protected override void OnOpen(object userData)
        {
            if (userData is UILoadingRequest req)
            {
                SetMessage(req.Message);
                SetProgress(req.Progress, req.ShowProgress);
            }
        }

        public void SetMessage(string message)
        {
            if (_txtMessage != null)
                _txtMessage.text = string.IsNullOrEmpty(message) ? "Loading..." : message;
        }

        public void SetProgress(float progress, bool showProgress)
        {
            Slider slider = GetComponentInChildren<Slider>(true);
            if (slider == null)
                return;

            slider.gameObject.SetActive(showProgress);
            slider.value = Mathf.Clamp01(progress);
        }
    }
}
