using FRFrame;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    public sealed class CommonInteractionLabWidget : RuntimeLabWidget
    {
        private Texture2D _texture;
        private UIScrollText _marquee;

        protected override void OnInit()
        {
            Root = ExampleUIFactory.Stretch(gameObject);
            RectTransform left = ExampleUIFactory.CreatePanel("Interactions", Root, PanelColor);
            ExampleUIFactory.SetRect(
                left,
                Vector2.zero,
                new Vector2(.5f, 1),
                new Vector2(10, 10),
                new Vector2(-5, -10)
            );
            Text title = Label(left, "长按购买 / Toggle 动态订阅", 15);
            ExampleUIFactory.SetRect(
                title.rectTransform,
                new Vector2(0, 1),
                Vector2.one,
                new Vector2(12, -30),
                new Vector2(-12, -5)
            );
            ToggleGroup group = left.gameObject.AddComponent<ToggleGroup>();
            UIToggleGroupBinder binder = left.gameObject.AddComponent<UIToggleGroupBinder>();
            binder.toggleGroup = group;
            binder.autoCollectChildren = true;
            RectTransform toggles = ExampleUIFactory.CreatePanel("Toggles", left, Color.clear);
            ExampleUIFactory.SetRect(
                toggles,
                new Vector2(0, 1),
                Vector2.one,
                new Vector2(12, -78),
                new Vector2(-12, -36)
            );
            HorizontalLayoutGroup h = toggles.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 5;
            h.childForceExpandWidth = true;
            for (int i = 0; i < 3; i++)
            {
                GameObject go = new GameObject(
                    "Toggle" + i,
                    typeof(RectTransform),
                    typeof(Image),
                    typeof(Toggle),
                    typeof(UIToggleBinder)
                );
                go.transform.SetParent(toggles, false);
                go.GetComponent<Image>().color = new Color(.18f, .3f, .5f);
                Toggle t = go.GetComponent<Toggle>();
                t.group = group;
                t.targetGraphic = go.GetComponent<Image>();
                go.GetComponent<UIToggleBinder>().index = i;
                Text l = Label(go.transform, "配装 " + (i + 1), 13, TextAnchor.MiddleCenter);
                ExampleUIFactory.Stretch(l.gameObject);
            }
            binder.RefreshToggles();
            AddUIEvent(binder.onSelectedIndexChanged, i => Emit("Toggle 页签 index=" + i));
            Button buy = Button(left, "按住确认购买", () => { });
            ExampleUIFactory.SetRect(
                buy.GetComponent<RectTransform>(),
                new Vector2(.08f, .52f),
                new Vector2(.92f, .66f),
                Vector2.zero,
                Vector2.zero
            );
            Text progress = Label(left, "progress 0% · 移出/拖动取消", 13, TextAnchor.MiddleCenter);
            ExampleUIFactory.SetRect(
                progress.rectTransform,
                new Vector2(0, .43f),
                new Vector2(1, .51f),
                Vector2.zero,
                Vector2.zero
            );
            UILongPressTrigger hold = buy.gameObject.AddComponent<UILongPressTrigger>();
            hold.longPressThreshold = .9f;
            AddUIEvent(
                hold.onProgressChanged,
                v => progress.text = $"progress {v:P0} · 移出/拖动取消"
            );
            AddUIEvent(hold.onLongPressed, () => Emit("长按购买确认"));
            AddUIEvent(hold.onCanceled, () => Emit("长按取消"));
            AddUIEvent(hold.onClicked, () => Emit("普通点击，未过阈值"));

            RectTransform right = ExampleUIFactory.CreatePanel("Visual", Root, PanelColor);
            ExampleUIFactory.SetRect(
                right,
                new Vector2(.5f, 0),
                Vector2.one,
                new Vector2(5, 10),
                new Vector2(-10, -10)
            );
            RectTransform marqueeView = ExampleUIFactory.CreatePanel(
                "Marquee",
                right,
                new Color(.03f, .04f, .07f, 1)
            );
            ExampleUIFactory.SetRect(
                marqueeView,
                new Vector2(.04f, .78f),
                new Vector2(.96f, .92f),
                Vector2.zero,
                Vector2.zero
            );
            marqueeView.gameObject.AddComponent<RectMask2D>();
            Text marqueeText = Label(marqueeView, "", 14);
            marqueeText.horizontalOverflow = HorizontalWrapMode.Overflow;
            RectTransform mr = marqueeText.rectTransform;
            mr.anchorMin = new Vector2(0, 0);
            mr.anchorMax = new Vector2(0, 1);
            mr.pivot = new Vector2(0, .5f);
            mr.sizeDelta = new Vector2(1000, 0);
            _marquee = marqueeView.gameObject.AddComponent<UIScrollText>();
            _marquee.viewport = marqueeView;
            _marquee.textRoot = mr;
            _marquee.uguiText = marqueeText;
            _marquee.SetText("公会公告：今晚 20:00 港口争夺战，请完成配装并准时集结。");
            Button marqueeButton = Button(
                right,
                "切换/停止跑马灯",
                () =>
                {
                    if (_marquee.IsPlaying)
                        _marquee.Stop();
                    else
                    {
                        _marquee.SetText("限时公告：补给商店已刷新，连续 SetText 可安全重入。");
                        _marquee.Play();
                    }
                    Emit("跑马灯 playing=" + _marquee.IsPlaying);
                }
            );
            ExampleUIFactory.SetRect(
                marqueeButton.GetComponent<RectTransform>(),
                new Vector2(.2f, .68f),
                new Vector2(.8f, .76f),
                Vector2.zero,
                Vector2.zero
            );
            RectTransform rawBox = ExampleUIFactory.CreatePanel(
                "RawBox",
                right,
                new Color(.03f, .04f, .07f, 1)
            );
            ExampleUIFactory.SetRect(
                rawBox,
                new Vector2(.04f, .30f),
                new Vector2(.46f, .64f),
                Vector2.zero,
                Vector2.zero
            );
            rawBox.gameObject.AddComponent<RectMask2D>();
            GameObject rawGo = new GameObject("Raw", typeof(RectTransform), typeof(RawImage));
            rawGo.transform.SetParent(rawBox, false);
            ExampleUIFactory.Stretch(rawGo);
            UIRawImageFitter fitter = rawGo.AddComponent<UIRawImageFitter>();
            fitter.FitContainer = rawBox;
            _texture = Gradient();
            fitter.SetTexture(_texture);
            AddPointerClick(
                rawGo,
                (g, e) =>
                {
                    fitter.FitMode =
                        fitter.FitMode == UIRawImageFitMode.Contain ? UIRawImageFitMode.Cover
                        : fitter.FitMode == UIRawImageFitMode.Cover ? UIRawImageFitMode.Fill
                        : UIRawImageFitMode.Contain;
                    Emit("RawImage FitMode=" + fitter.FitMode);
                },
                UIEventListenMode.UntilDispose
            );
            RectTransform zoomView = ExampleUIFactory.CreatePanel(
                "PinchZoom",
                right,
                new Color(.03f, .04f, .07f, 1)
            );
            ExampleUIFactory.SetRect(
                zoomView,
                new Vector2(.52f, .30f),
                new Vector2(.96f, .64f),
                Vector2.zero,
                Vector2.zero
            );
            zoomView.gameObject.AddComponent<RectMask2D>();
            RectTransform target = ExampleUIFactory.CreatePanel(
                "Map",
                zoomView,
                new Color(.18f, .55f, .85f)
            );
            target.anchorMin = target.anchorMax = target.pivot = new Vector2(.5f, .5f);
            target.sizeDelta = new Vector2(150, 100);
            target.anchoredPosition = Vector2.zero;
            Text z = Label(target, "双指缩放地图", 13, TextAnchor.MiddleCenter);
            ExampleUIFactory.Stretch(z.gameObject);
            UIPinchZoom pinch = zoomView.gameObject.AddComponent<UIPinchZoom>();
            pinch.viewport = zoomView;
            pinch.target = target;
            AddUIEvent(pinch.onZoomChanged, s => Emit("PinchZoom scale=" + s.ToString("F2")));
            Button reset = Button(right, "Reset Zoom", () => pinch.ResetZoom());
            ExampleUIFactory.SetRect(
                reset.GetComponent<RectTransform>(),
                new Vector2(.55f, .16f),
                new Vector2(.93f, .25f),
                Vector2.zero,
                Vector2.zero
            );
        }

        protected override void OnShow(object userData)
        {
            _marquee?.Play();
        }

        protected override void OnHide()
        {
            _marquee?.Stop();
        }

        protected override void OnDispose()
        {
            if (_texture != null)
                Object.Destroy(_texture);
        }

        private static Texture2D Gradient()
        {
            Texture2D t = new Texture2D(96, 48);
            for (int y = 0; y < t.height; y++)
            for (int x = 0; x < t.width; x++)
                t.SetPixel(
                    x,
                    y,
                    Color.Lerp(new Color(.1f, .55f, 1), new Color(1, .45f, .1f), x / 95f)
                );
            t.Apply();
            return t;
        }
    }
}
