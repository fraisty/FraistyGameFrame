using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using FRFrame;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace FRFrame.Tests.UIExtensions
{
    public sealed class UIRuntimeExtensionsPlayModeTests
    {
        private readonly List<Object> _created = new List<Object>();

        [UnityTest]
        public IEnumerator ReopeningKeepsOneOpenSubscriptionAndRevokesTheOldClose() => VerifyFormOwnership().ToCoroutine();

        private async UniTask VerifyFormOwnership()
        {
            var manager = CreateFormManager();
            manager.RegisterCodeFormConfig<FormOperationTestProbe>();
            try
            {
                var options = UIOpenOptions.Default;
                options.PlayAnimation = false;
                options.UserData = (System.Action<UIForm>)manager.Hide;
                string id = nameof(FormOperationTestProbe);
                Assert.That(manager.ConfigRegistry.TryGet(id, out var config), Is.True);
                config.disableGameObjectWhenHidden = true;
                Assert.That(await manager.OpenAsync(id,options), Is.Null, "A synchronous Hide in OnOpen owns the result.");
                var form = manager.Get<FormOperationTestProbe>();
                Assert.That(form.State, Is.EqualTo(UIFormState.Hidden));
                options.UserData = null;
                Assert.That(await manager.OpenAsync(id,options), Is.SameAs(form));

                form.CloseGate = new UniTaskCompletionSource();
                var closing = manager.CloseAsync(form);
                Assert.That(form.State, Is.EqualTo(UIFormState.Closing));
                Assert.That(await manager.OpenAsync(id,options), Is.SameAs(form));
                form.CloseGate.TrySetResult();
                await closing;
                form.Signal.Invoke();
                Assert.That(form.Opened, Is.EqualTo(1));
                Assert.That(form.Shown, Is.EqualTo(1), "Revoking a visible close does not start a new visible lifetime.");
                Assert.That(form.Closed, Is.Zero);
                Assert.That(form.Received, Is.EqualTo(1));
                Assert.That(form.State, Is.EqualTo(UIFormState.Opened));

                form.Hidden = () => manager.Show(form);
                manager.Hide(form);
                form.Hidden = null;
                Assert.That(form.State, Is.EqualTo(UIFormState.Opened));
                Assert.That(form.gameObject.activeInHierarchy, Is.True, "OnHide's synchronous Show owns activation.");
                form.Signal.Invoke();
                Assert.That(form.Received, Is.EqualTo(2));

                form.CloseGate = null;
                await manager.CloseAsync(form);
                form.Signal.Invoke();
                Assert.That(form.Closed, Is.EqualTo(1));
                Assert.That(form.Received, Is.EqualTo(2));
                Assert.That(form.State, Is.EqualTo(UIFormState.Hidden));

                Assert.That(await manager.OpenAsync(id,options), Is.SameAs(form));
                Assert.That(form.Opened, Is.EqualTo(2));
                Assert.That(form.Shown, Is.EqualTo(2), "A completed Close requires a new OnOpen, not an extra OnShow.");
                form.Signal.Invoke();
                Assert.That(form.Received, Is.EqualTo(3));
                await manager.CloseAsync(form);
                form.Signal.Invoke();
                Assert.That(form.Closed, Is.EqualTo(2));
                Assert.That(form.Received, Is.EqualTo(3));
            }
            finally { manager.Destroy(); }
        }

        [UnityTest]
        public IEnumerator PreloadSharesPendingWarmupAndLosesOwnershipToExplicitFormOperations() => VerifyPreloadOwnership().ToCoroutine();

        private async UniTask VerifyPreloadOwnership()
        {
            var manager = CreateFormManager();
            var options = UIOpenOptions.Default;
            options.PlayAnimation = false;
            CancellationToken handlerToken = default;
            UniTaskCompletionSource gate = null;

            UniTask Warm(string id, CancellationToken token = default)
            {
                manager.RegisterConfig(new UIFormConfig
                {
                    formId = id, logicType = typeof(FormOperationTestProbe).FullName,
                    allowMultiInstance = false,
                    closeMode = UIFormCloseMode.Destroy, disableGameObjectWhenHidden = true
                });
                gate = new UniTaskCompletionSource();
                return manager.PreloadAsync(id, UIPreloadMode.InstanceWarmup, token);
            }

            FormOperationTestProbe GetProbe(string id) => (FormOperationTestProbe)manager.Get(id);

            FormOperationTestProbe.PreloadHandler = token =>
            {
                handlerToken = token;
                return gate.Task;
            };
            try
            {
                var warm = Warm("warm-shared");
                var same = manager.PreloadAsync("warm-shared", UIPreloadMode.InstanceWarmup);
                Assert.That(same.Status, Is.EqualTo(UniTaskStatus.Pending));
                Assert.That(manager.GetAll("warm-shared"), Has.Count.EqualTo(1));
                var preloaded = GetProbe("warm-shared");
                Assert.That(preloaded.State, Is.EqualTo(UIFormState.Initialized));
                gate.TrySetResult();
                await warm;
                await same;
                Assert.That(preloaded.State, Is.EqualTo(UIFormState.Preloaded));
                Assert.That(preloaded.gameObject.activeSelf, Is.False);
                Assert.That(manager.IsPreloaded("warm-shared"), Is.True);
                Assert.That(await manager.OpenAsync("warm-shared",options), Is.SameAs(preloaded));
                Assert.That(preloaded.Opened, Is.EqualTo(1));

                warm = Warm("warm-open");
                var opened = GetProbe("warm-open");
                Assert.That(await manager.OpenAsync("warm-open", options), Is.SameAs(opened));
                Assert.That(handlerToken.IsCancellationRequested, Is.True);
                await warm;
                gate.TrySetResult();
                Assert.That(opened.State, Is.EqualTo(UIFormState.Opened));
                Assert.That(opened.gameObject.activeSelf, Is.True);
                Assert.That(manager.IsPreloaded("warm-open"), Is.False);

                warm = Warm("warm-close");
                var closed = GetProbe("warm-close");
                await manager.CloseAsync(closed.SerialId);
                await warm;
                gate.TrySetResult();
                Assert.That(closed.State, Is.EqualTo(UIFormState.Disposed));
                Assert.That(closed.Closed, Is.Zero);
                Assert.That(closed.Disposed, Is.EqualTo(1));
                Assert.That(manager.Get("warm-close"), Is.Null);

                warm = Warm("warm-reenter");
                var reentered = GetProbe("warm-reenter");
                using (handlerToken.Register(() => manager.Show(reentered)))
                    await manager.CloseAsync(reentered);
                await warm;
                gate.TrySetResult();
                Assert.That(reentered.State, Is.EqualTo(UIFormState.Opened));
                Assert.That(reentered.CloseAnimations, Is.Zero);

                using var cancel = new CancellationTokenSource();
                warm = Warm("warm-cancel", cancel.Token);
                var canceled = GetProbe("warm-cancel");
                var waiter = manager.PreloadAsync("warm-cancel", UIPreloadMode.InstanceWarmup);
                cancel.Cancel();
                await warm;
                await waiter;
                gate.TrySetResult();
                Assert.That(canceled.State, Is.EqualTo(UIFormState.Disposed));
                Assert.That(canceled.Disposed, Is.EqualTo(1));
                Assert.That(manager.Get("warm-cancel"), Is.Null);
                Assert.That(manager.IsPreloaded("warm-cancel"), Is.False);

                warm = Warm("warm-destroy");
                var destroyed = GetProbe("warm-destroy");
                manager.Destroy();
                await warm;
                gate.TrySetResult();
                Assert.That(handlerToken.IsCancellationRequested, Is.True);
                Assert.That(destroyed.State, Is.EqualTo(UIFormState.Disposed));
                Assert.That(destroyed.Disposed, Is.EqualTo(1));
                Assert.That(manager.IsAnyLoading(), Is.False);
                Assert.That(manager.Get("warm-destroy"), Is.Null);
            }
            finally
            {
                FormOperationTestProbe.PreloadHandler = null;
                manager.Destroy();
                gate?.TrySetResult();
            }
        }

        private UIManager CreateFormManager()
        {
            var root = Track(new GameObject("Form operation test", typeof(RectTransform)));
            var manager = new UIManager();
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(UIManager).GetField("_configRegistry", fields).SetValue(manager, new UIConfigRegistry());
            typeof(UIManager).GetField("_maskService", fields).SetValue(manager, new UIMaskService());
            var groups = (Dictionary<UIGroupLayer, UIGroup>)typeof(UIManager).GetField("_groups", fields).GetValue(manager);
            groups.Add(UIGroupLayer.Normal, new UIGroup(UIGroupLayer.Normal, "Test", root.transform, 0));
            return manager;
        }

        [UnityTest]
        public IEnumerator WorldHealthBarRebindAndPoolReturnRevokePreviousLease()
        {
            var root = Track(new GameObject("World bars"));
            var camera = Track(new GameObject("World camera",typeof(Camera))).GetComponent<Camera>();
            camera.transform.position = new Vector3(0,0,-10);
            var bar = WorldHealthBar.CreateDefault(root.transform,0);
            var canvas = bar.GetComponent<Canvas>();
            var old = bar.Bind();
            old.Present(new WorldHealthSnapshot(25,100),Vector3.zero,camera,.02f);
            Assert.That(canvas.enabled,Is.True);
            var pool = new GameObjectPoolModule();
            var key = new ResourceKey("Tests","WorldBar");
            pool.SetRoot(root.transform);
            pool.Release(key,"WorldBar",bar.gameObject);
            Assert.That(pool.TryGet(key),Is.SameAs(bar.gameObject));
            var next = bar.Bind();
            next.Present(new WorldHealthSnapshot(80,100),Vector3.zero,camera,.02f);
            old.Dispose();
            old.Present(default,Vector3.zero,camera,.02f);
            Assert.That(canvas.enabled,Is.True);
            next.Present(new WorldHealthSnapshot(80,100),new Vector3(100,0,0),camera,.02f);
            Assert.That(canvas.enabled,Is.False);
            yield return null;
        }

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

    public sealed class FormOperationTestProbe : UIForm, IUIPreloadHandler
    {
        internal readonly UnityEvent Signal = new UnityEvent();
        internal static System.Func<CancellationToken, UniTask> PreloadHandler;
        internal System.Action Hidden;
        internal UniTaskCompletionSource CloseGate;
        internal int Opened, Shown, Closed, Received, CloseAnimations, Disposed;

        public UniTask OnPreloadAsync(CancellationToken cancellationToken) =>
            PreloadHandler?.Invoke(cancellationToken) ?? UniTask.CompletedTask;

        protected override void OnOpen(object userData)
        {
            Opened++;
            AddUIEvent(Signal, () => Received++, UIEventListenMode.WhileOpen);
            (userData as System.Action<UIForm>)?.Invoke(this);
        }

        protected override void OnShow(object userData) => Shown++;
        protected override void OnHide() => Hidden?.Invoke();
        protected override void OnClose(object userData) => Closed++;
        protected override void OnDispose() => Disposed++;
        protected override UniTask PlayCloseAnimationAsync(CancellationToken cancellationToken)
        {
            CloseAnimations++;
            return CloseGate?.Task ?? UniTask.CompletedTask;
        }
    }
}
