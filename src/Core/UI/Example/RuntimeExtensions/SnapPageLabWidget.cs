using System.Collections;
using FRFrame;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    public sealed class SnapPageLabWidget : RuntimeLabWidget
    {
        private UISnapScrollPage _snap;
        private RectTransform _content;
        private Text _status;
        private Coroutine _reflowCoroutine;
        private int _serial = 5;

        protected override void OnInit()
        {
            Root = ExampleUIFactory.Stretch(gameObject);
            RectTransform bar = Toolbar(Root, 68f);
            Button(bar, "Previous", () => _snap.Previous());
            Button(bar, "Next", () => _snap.Next());
            Button(
                bar,
                "快速 Next x3",
                () =>
                {
                    _snap.Next();
                    _snap.Next();
                    _snap.Next();
                }
            );
            Button(bar, "Start/Center/End", Alignment);
            Button(bar, "Clamp/Wrap", IndexMode);
            Button(bar, "+ 页面", AddPage);
            Button(bar, "- 页面", RemovePage);
            Button(bar, "改变页宽", ChangeWidth);
            _status = Label(bar, "", 11, TextAnchor.MiddleCenter);

            ScrollRect scroll = Scroll(Root, out _, out _content, false);
            ExampleUIFactory.SetRect(
                scroll.GetComponent<RectTransform>(),
                Vector2.zero,
                Vector2.one,
                new Vector2(30f, 35f),
                new Vector2(-30f, -95f)
            );
            scroll.horizontal = true;
            scroll.vertical = false;

            _content.anchorMin = new Vector2(0f, 0.5f);
            _content.anchorMax = new Vector2(0f, 0.5f);
            _content.pivot = new Vector2(0f, 0.5f);
            _content.sizeDelta = new Vector2(0f, 190f);
            HorizontalLayoutGroup layout =
                _content.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 14f;
            layout.padding = new RectOffset(12, 12, 8, 8);
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            _content.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter
                .FitMode
                .PreferredSize;

            for (int i = 0; i < 5; i++)
                AddPage(130f + i * 45f);

            _snap = scroll.gameObject.AddComponent<UISnapScrollPage>();
            _snap.scrollRect = scroll;
            _snap.content = _content;
            _snap.alignment = UIPageAlignment.Center;
            _snap.indexMode = UIPageIndexMode.Clamp;
            _snap.enableCenterScale = true;
            AddUIEvent(
                _snap.onPageChanged,
                i =>
                {
                    Status();
                    Emit("SelectedPage=" + i);
                }
            );
            AddUIEvent(
                _snap.onPageSettled,
                i =>
                {
                    Status();
                    Emit("SettledPage=" + i);
                }
            );
            Reflow();
        }

        protected override void OnShow(object userData)
        {
            ScheduleReflow();
        }

        protected override void OnHide()
        {
            CancelReflow();
        }

        private void AddPage(float width)
        {
            int id = ++_serial;
            RectTransform card = ExampleUIFactory.CreatePanel(
                "QuestPage" + id,
                _content,
                new Color(0.12f + id % 3 * 0.06f, 0.24f, 0.38f, 1f)
            );
            LayoutElement element = card.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.minWidth = width;
            Text text = Label(
                card,
                $"任务章节 {id}\n实际宽度 {width:F0}\n由 HorizontalLayoutGroup 定位",
                15,
                TextAnchor.MiddleCenter
            );
            ExampleUIFactory.Stretch(text.gameObject);
        }

        private void Alignment()
        {
            _snap.alignment =
                _snap.alignment == UIPageAlignment.Start ? UIPageAlignment.Center
                : _snap.alignment == UIPageAlignment.Center ? UIPageAlignment.End
                : UIPageAlignment.Start;
            Reflow();
            Emit("页面对齐=" + _snap.alignment);
        }

        private void IndexMode()
        {
            _snap.indexMode =
                _snap.indexMode == UIPageIndexMode.Clamp
                    ? UIPageIndexMode.Wrap
                    : UIPageIndexMode.Clamp;
            Emit("页索引模式=" + _snap.indexMode);
            Status();
        }

        private void AddPage()
        {
            AddPage(115f + _content.childCount * 25f);
            ScheduleReflow();
            Emit("增加页面");
        }

        private void RemovePage()
        {
            if (_content.childCount <= 2)
            {
                Emit("至少保留两个页面");
                return;
            }

            Object.Destroy(_content.GetChild(_content.childCount - 1).gameObject);
            ScheduleReflow();
            Emit("删除末尾页面");
        }

        private void ChangeWidth()
        {
            if (_content.childCount == 0)
                return;

            int index = Mathf.Clamp(_snap.SelectedPage, 0, _content.childCount - 1);
            LayoutElement element = _content.GetChild(index).GetComponent<LayoutElement>();
            element.preferredWidth =
                element.preferredWidth >= 260f ? 120f : element.preferredWidth + 55f;
            element.minWidth = element.preferredWidth;
            Reflow();
            Emit($"页面 {index} 宽度改为 {element.preferredWidth} 并 RefreshPages");
        }

        private void ScheduleReflow()
        {
            CancelReflow();
            _reflowCoroutine = StartCoroutine(ReflowNextFrame());
        }

        private void CancelReflow()
        {
            if (_reflowCoroutine == null)
                return;

            StopCoroutine(_reflowCoroutine);
            _reflowCoroutine = null;
        }

        private IEnumerator ReflowNextFrame()
        {
            yield return null;
            _reflowCoroutine = null;
            Reflow();
        }

        private void Reflow()
        {
            _snap?.RefreshPages();
            Status();
        }

        private void Status()
        {
            if (_status == null || _snap == null)
                return;

            _status.text =
                $"pages={_snap.PageCount} Selected={_snap.SelectedPage} Settled={_snap.SettledPage}\nIsSettling={_snap.IsSettling} align={_snap.alignment} mode={_snap.indexMode}";
        }
    }
}
