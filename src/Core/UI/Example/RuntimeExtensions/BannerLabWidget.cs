using System.Collections.Generic;
using FRFrame;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    public sealed class BannerLabWidget : RuntimeLabWidget
    {
        private UIInfiniteCarousel _carousel;
        private GameObject _template;
        private Text _status;
        private readonly List<BannerData> _all = new List<BannerData>();
        private bool _paused;
        private float _interval = 2f;

        protected override void OnInit()
        {
            Root = ExampleUIFactory.Stretch(gameObject);
            RectTransform bar = Toolbar(Root, 68);
            Button(bar, "Previous", () => _carousel.Previous());
            Button(bar, "Next", () => _carousel.Next());
            Button(bar, "MoveTo 3", () => _carousel.MoveToIndex(2));
            Button(bar, "暂停/继续", TogglePause);
            Button(bar, "间隔 +/-", ChangeInterval);
            Button(bar, "切换 1/2/5 项", ChangeCount);
            _status = Label(bar, "", 12, TextAnchor.MiddleCenter);
            RectTransform viewport = ExampleUIFactory.CreatePanel(
                "BannerViewport",
                Root,
                new Color(.03f, .04f, .07f, 1)
            );
            ExampleUIFactory.SetRect(
                viewport,
                Vector2.zero,
                Vector2.one,
                new Vector2(90, 45),
                new Vector2(-90, -100)
            );
            viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform content = ExampleUIFactory.CreatePanel("Content", viewport, Color.clear);
            ExampleUIFactory.Stretch(content.gameObject);
            _template = Template("BannerCardPrefab", new Color(.18f, .28f, .48f));
            Text templateText = ExampleUIFactory.CreateText(
                "Label",
                _template.transform,
                "",
                20,
                TextAnchor.MiddleCenter,
                Color.white
            );
            ExampleUIFactory.Stretch(templateText.gameObject);
            for (int i = 0; i < 5; i++)
                _all.Add(new BannerData { Title = "限时活动 " + (i + 1), Reward = (i + 1) * 500 });
            _carousel = viewport.gameObject.AddComponent<UIInfiniteCarousel>();
            _carousel.Configure(viewport, content, _template, 5);
            AddUIEvent(
                _carousel.onIndexChanged,
                i =>
                {
                    Status();
                    Emit("Banner CurrentIndex=" + i);
                }
            );
            AddUIEvent(
                _carousel.onSettled,
                i =>
                {
                    Status();
                    Emit("Banner settled=" + i);
                }
            );
            AddUIEvent(
                _carousel.onItemClicked,
                c =>
                {
                    if (c.IsCenter)
                    {
                        BannerData d = (BannerData)c.Data;
                        Emit($"点击中心活动：{d.Title}，奖励 {d.Reward}");
                    }
                    else
                        Emit("旁卡点击：可拖动或 Previous/Next 切到中心");
                }
            );
            SetCount(5);
            _carousel.SetAutoPlay(true, _interval);
            Status();
        }

        protected override void OnShow(object userData)
        {
            if (_paused)
                _carousel.PauseAutoPlay();
            else
                _carousel.ResumeAutoPlay();
        }

        protected override void OnHide()
        {
            _carousel?.PauseAutoPlay();
        }

        protected override void OnDispose()
        {
            if (_template != null)
                Object.Destroy(_template);
        }

        private void Bind(UIInfiniteCarouselItemContext c)
        {
            BannerData d = (BannerData)c.Data;
            Text t = c.Slot.GetComponentInChildren<Text>();
            t.text =
                $"{d.Title}\n累计登录奖励 {d.Reward}\n{(c.IsCenter ? "点击查看详情" : "旁卡")}";
            Image image = c.Slot.GetComponent<Image>();
            image.color = c.IsCenter ? new Color(.20f, .55f, .90f) : new Color(.12f, .18f, .28f);
            c.RectTransform.localScale = c.IsCenter ? Vector3.one : Vector3.one * .82f;
        }

        private void TogglePause()
        {
            _paused = !_paused;
            if (_paused)
                _carousel.PauseAutoPlay();
            else
                _carousel.ResumeAutoPlay();
            Emit("自动轮播暂停=" + _paused);
            Status();
        }

        private void ChangeInterval()
        {
            _interval = _interval >= 3f ? 1f : _interval + 1f;
            _carousel.SetAutoPlay(true, _interval);
            if (_paused)
                _carousel.PauseAutoPlay();
            Emit("轮播间隔=" + _interval + "s");
            Status();
        }

        private void ChangeCount()
        {
            int next =
                _carousel.Count == 5 ? 1
                : _carousel.Count == 1 ? 2
                : 5;
            SetCount(next);
            Emit("动态 Banner 数量=" + next);
        }

        private void SetCount(int count)
        {
            _carousel.SetData(_all.GetRange(0, count), Bind);
            Status();
        }

        private void Status()
        {
            if (_status != null && _carousel != null)
                _status.text =
                    $"count={_carousel.Count} CurrentIndex={_carousel.CurrentIndex}\nIsSettling={_carousel.IsSettling} interval={_interval:F0}s paused={_paused}";
        }
    }

    public sealed class BannerData
    {
        public string Title;
        public int Reward;
    }
}
