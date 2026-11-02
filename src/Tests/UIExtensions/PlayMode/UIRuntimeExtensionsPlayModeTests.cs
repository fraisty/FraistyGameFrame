using System.Collections;
using System.Collections.Generic;
using FRFrame;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace FRFrame.Tests.UIExtensions
{
    public sealed class UIRuntimeExtensionsPlayModeTests
    {
        private readonly List<Object> _created = new List<Object>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                    Object.Destroy(_created[i]);
            }
            _created.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator LongPress_MovePastToleranceCancelsWithoutLateLongPress_AndDisableClearsState()
        {
            EventSystem eventSystem = Track(new GameObject("EventSystem", typeof(EventSystem))).GetComponent<EventSystem>();
            var trigger = Track(new GameObject("LongPress", typeof(UILongPressTrigger))).GetComponent<UILongPressTrigger>();
            trigger.longPressThreshold = 0.04f;
            trigger.moveTolerance = 5f;
            trigger.useUnscaledTime = true;
            int longPresses = 0;
            int cancellations = 0;
            trigger.onLongPressed.AddListener(() => longPresses++);
            trigger.onCanceled.AddListener(() => cancellations++);

            trigger.OnPointerDown(Pointer(eventSystem, Vector2.zero));
            trigger.OnDrag(Pointer(eventSystem, new Vector2(6f, 0f)));
            yield return new WaitForSecondsRealtime(0.08f);
            Assert.That(cancellations, Is.EqualTo(1));
            Assert.That(longPresses, Is.Zero);
            Assert.That(trigger.IsPressed, Is.False);

            trigger.OnPointerDown(Pointer(eventSystem, Vector2.zero));
            trigger.enabled = false;
            Assert.That(trigger.IsPressed, Is.False);
            Assert.That(trigger.IsLongPressed, Is.False);
            Assert.That(trigger.Progress, Is.Zero);
            yield return new WaitForSecondsRealtime(0.08f);
            Assert.That(longPresses, Is.Zero);
        }

        [UnityTest]
        public IEnumerator SnapScroll_ChangedStartsImmediately_AndOnlyFinalAnimationSettles()
        {
            UISnapScrollPage snap = CreateSnapScroll(0.3f);
            var events = new List<string>();
            snap.onPageChanged.AddListener(page => events.Add("changed:" + page));
            snap.onPageSettled.AddListener(page => events.Add("settled:" + page));

            snap.MoveToPage(1, true);
            Assert.That(events, Is.EqualTo(new[] { "changed:1" }));
            yield return new WaitForSecondsRealtime(0.02f);
            snap.MoveToPage(2, true);
            Assert.That(events, Is.EqualTo(new[] { "changed:1", "changed:2" }));
            float timeout = Time.realtimeSinceStartup + 1f;
            while (snap.IsSettling && Time.realtimeSinceStartup < timeout)
                yield return null;

            Assert.That(snap.IsSettling, Is.False, "The final page animation did not complete before the timeout.");
            Assert.That(events, Is.EqualTo(new[] { "changed:1", "changed:2", "settled:2" }));
        }

        [UnityTest]
        public IEnumerator SnapScroll_DisableCancelsLateSettledEvent()
        {
            UISnapScrollPage snap = CreateSnapScroll(0.04f);
            int settled = 0;
            snap.onPageSettled.AddListener(_ => settled++);

            snap.MoveToPage(1, true);
            snap.enabled = false;
            yield return new WaitForSecondsRealtime(0.08f);

            Assert.That(settled, Is.Zero);
            Assert.That(snap.IsSettling, Is.False);
        }

        [UnityTest]
        public IEnumerator VirtualList_ClearRecyclesOldDataBeforeReset_AndReportsEmptyRange()
        {
            GameObject root = Track(new GameObject("List", typeof(RectTransform), typeof(ScrollRect), typeof(UIVirtualList)));
            root.SetActive(false);
            var rootRect = (RectTransform)root.transform;
            rootRect.sizeDelta = new Vector2(100f, 100f);
            GameObject contentObject = Track(new GameObject("Content", typeof(RectTransform)));
            contentObject.transform.SetParent(root.transform, false);
            var content = (RectTransform)contentObject.transform;
            content.sizeDelta = new Vector2(100f, 100f);
            var scrollRect = root.GetComponent<ScrollRect>();
            scrollRect.viewport = rootRect;
            scrollRect.content = content;
            GameObject prefab = Track(new GameObject("ItemPrefab", typeof(RectTransform), typeof(RecyclingProbeItem)));
            prefab.SetActive(false);

            var list = root.GetComponent<UIVirtualList>();
            list.Configure(scrollRect, rootRect, content, prefab, UIVirtualListLayoutMode.Vertical,
                new Vector2(100f, 30f), Vector2.zero, default, 1, 0, 8);
            list.Initialize(null, typeof(RecyclingProbeItem));
            UIItem visibleItem = null;
            Vector2Int lastRange = new Vector2Int(int.MinValue, int.MinValue);
            list.onItemVisible.AddListener((_, item) => visibleItem = item);
            list.onVisibleRangeChanged.AddListener((start, end) => lastRange = new Vector2Int(start, end));
            root.SetActive(true);
            object oldData = new object();
            list.SetDataList(new[] { oldData });
            yield return null;

            Assert.That(visibleItem, Is.Not.Null);
            visibleItem.SetSelected(true);
            var probe = (RecyclingProbeItem)visibleItem;
            list.Clear();

            Assert.That(probe.RecycledIndex, Is.Zero);
            Assert.That(probe.RecycledData, Is.SameAs(oldData));
            Assert.That(probe.IndexSeenInsideRecycle, Is.Zero);
            Assert.That(probe.DataSeenInsideRecycle, Is.SameAs(oldData));
            Assert.That(visibleItem.Index, Is.EqualTo(-1));
            Assert.That(visibleItem.Data, Is.Null);
            Assert.That(visibleItem.IsSelected, Is.False);
            Assert.That(lastRange, Is.EqualTo(new Vector2Int(-1, -1)));
            list.GetVisibleRange(out int start, out int end);
            Assert.That(new Vector2Int(start, end), Is.EqualTo(new Vector2Int(-1, -1)));
        }

        [UnityTest]
        public IEnumerator ScrollText_DisableStopsPlaybackWithoutLateRecovery()
        {
            GameObject viewportObject = Track(new GameObject("Viewport", typeof(RectTransform)));
            viewportObject.SetActive(false);
            var viewport = (RectTransform)viewportObject.transform;
            viewport.sizeDelta = new Vector2(50f, 20f);
            GameObject textObject = Track(new GameObject("Text", typeof(RectTransform)));
            textObject.transform.SetParent(viewport, false);
            var textRoot = (RectTransform)textObject.transform;
            textRoot.sizeDelta = new Vector2(200f, 20f);
            var scrollText = viewportObject.AddComponent<UIScrollText>();
            scrollText.viewport = viewport;
            scrollText.textRoot = textRoot;
            scrollText.playOnEnable = false;
            scrollText.startDelay = 0.04f;
            viewportObject.SetActive(true);
            scrollText.Refresh();
            scrollText.Play();
            Assert.That(scrollText.IsOverflow, Is.True);
            Assert.That(scrollText.IsPlaying, Is.True);

            scrollText.enabled = false;
            Assert.That(scrollText.IsPlaying, Is.False);
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.That(scrollText.IsPlaying, Is.False);
        }

        private UISnapScrollPage CreateSnapScroll(float duration)
        {
            GameObject root = Track(new GameObject("Snap", typeof(RectTransform), typeof(ScrollRect), typeof(UISnapScrollPage)));
            root.SetActive(false);
            var viewport = (RectTransform)root.transform;
            viewport.sizeDelta = new Vector2(100f, 100f);
            GameObject contentObject = Track(new GameObject("Content", typeof(RectTransform)));
            contentObject.transform.SetParent(root.transform, false);
            var content = (RectTransform)contentObject.transform;
            content.anchorMin = new Vector2(0f, 0.5f);
            content.anchorMax = new Vector2(0f, 0.5f);
            content.pivot = new Vector2(0f, 0.5f);
            content.sizeDelta = new Vector2(300f, 100f);
            for (int i = 0; i < 3; i++)
            {
                GameObject page = Track(new GameObject("Page" + i, typeof(RectTransform)));
                page.transform.SetParent(content, false);
                var pageRect = (RectTransform)page.transform;
                pageRect.anchorMin = new Vector2(0f, 0.5f);
                pageRect.anchorMax = new Vector2(0f, 0.5f);
                pageRect.pivot = new Vector2(0f, 0.5f);
                pageRect.sizeDelta = new Vector2(100f, 100f);
                pageRect.anchoredPosition = new Vector2(i * 100f, 0f);
            }
            var scrollRect = root.GetComponent<ScrollRect>();
            scrollRect.viewport = viewport;
            scrollRect.content = content;
            var snap = root.GetComponent<UISnapScrollPage>();
            snap.scrollRect = scrollRect;
            snap.content = content;
            snap.snapDuration = duration;
            root.SetActive(true);
            snap.RefreshPages();
            return snap;
        }

        private static PointerEventData Pointer(EventSystem eventSystem, Vector2 position)
        {
            return new PointerEventData(eventSystem) { position = position };
        }

        private T Track<T>(T value) where T : Object
        {
            _created.Add(value);
            return value;
        }
    }

    public sealed class RecyclingProbeItem : UIItem
    {
        public int RecycledIndex { get; private set; } = -1;
        public object RecycledData { get; private set; }
        public int IndexSeenInsideRecycle { get; private set; } = -1;
        public object DataSeenInsideRecycle { get; private set; }

        protected override void OnItemRecycled(int index, object data)
        {
            RecycledIndex = index;
            RecycledData = data;
            IndexSeenInsideRecycle = Index;
            DataSeenInsideRecycle = Data;
        }
    }
}
