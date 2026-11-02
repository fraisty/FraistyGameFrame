using System;
using System.Collections.Generic;
using FRFrame;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    public sealed class GuildChatLabWidget : RuntimeLabWidget
    {
        private readonly ChatSource _source = new ChatSource();
        private readonly List<GameObject> _templates = new List<GameObject>();
        private UIDynamicVirtualList _list;
        private Text _stats;
        private long _serial = 100;
        private bool _follow = true;
        private int _created,
            _visible,
            _recycled;

        protected override void OnInit()
        {
            Root = ExampleUIFactory.Stretch(gameObject);
            RectTransform bar = Toolbar(Root, 68);
            Button(bar, "追加", () => Append(1));
            Button(bar, "批量追加", () => Append(6));
            Button(bar, "头插历史", Prepend);
            Button(bar, "展开可见", ExpandVisible);
            Button(bar, "回到底部", () => _list.ScrollToBottom());
            Button(
                bar,
                "自动跟随",
                () =>
                {
                    _follow = !_follow;
                    Emit("自动跟随=" + _follow);
                }
            );
            _stats = Label(bar, "", 11, TextAnchor.MiddleCenter);
            ScrollRect scroll = Scroll(
                Root,
                out RectTransform viewport,
                out RectTransform content,
                true
            );
            ExampleUIFactory.SetRect(
                scroll.GetComponent<RectTransform>(),
                Vector2.zero,
                Vector2.one,
                new Vector2(10, 10),
                new Vector2(-10, -84)
            );
            GameObject self = Template("SelfMessagePrefab", new Color(.12f, .30f, .42f));
            GameObject other = Template("OtherMessagePrefab", new Color(.18f, .20f, .28f));
            GameObject system = Template("SystemMessagePrefab", new Color(.32f, .24f, .10f));
            _templates.AddRange(new[] { self, other, system });
            _list = scroll.gameObject.AddComponent<UIDynamicVirtualList>();
            _list.Configure(scroll, viewport, content, self, 5, 5, 5, 2, 30);
            _list.Initialize(OwnerForm, typeof(SelfChatItem));
            _list.ConfigureTemplates(
                new[]
                {
                    new UIVirtualListTemplate("self", self, typeof(SelfChatItem)),
                    new UIVirtualListTemplate("other", other, typeof(OtherChatItem)),
                    new UIVirtualListTemplate("system", system, typeof(SystemChatItem)),
                }
            );
            _list.AnchorMode = UIVirtualListAnchorMode.PreserveFirstVisibleKey;
            _list.SizeAnchorMode = UIDynamicSizeAnchorMode.PreserveFirstVisibleKey;
            _list.SetSizeProvider((i, d) => ((ChatData)d).Height);
            AddUIEvent(
                _list.onItemCreated,
                _ =>
                {
                    _created++;
                    Stats();
                }
            );
            AddUIEvent(
                _list.onItemVisible,
                (_, __) =>
                {
                    _visible++;
                    Stats();
                }
            );
            AddUIEvent(
                _list.onItemRecycled,
                (_, __) =>
                {
                    _recycled++;
                    Stats();
                }
            );
            AddUIEvent(_list.onVisibleRangeChanged, (_, __) => Stats());
            for (int i = 0; i < 45; i++)
                _source.Items.Add(
                    Make(
                        i % 9 == 0 ? "system"
                            : i % 2 == 0 ? "self"
                            : "other",
                        "消息 " + (i + 1) + "：公会远征准备中"
                    )
                );
            _list.SetDataSource(_source);
            _list.ScrollToBottom(false);
            Stats();
        }

        protected override void OnDispose()
        {
            foreach (GameObject go in _templates)
                if (go != null)
                    UnityEngine.Object.Destroy(go);
        }

        protected override void OnShow(object userData)
        {
            Canvas.ForceUpdateCanvases();
            _list?.ForceRefresh();
        }

        private ChatData Make(string type, string text) =>
            new ChatData
            {
                Key = ++_serial,
                Type = type,
                Text = text,
                Height = type == "system" ? 42 : 58,
            };

        private void Append(int count)
        {
            bool wasEnd = _list.IsAtEnd();
            int start = _source.Count;
            for (int i = 0; i < count; i++)
                _source.Items.Add(
                    Make(
                        i % 5 == 4 ? "system"
                            : i % 2 == 0 ? "self"
                            : "other",
                        "新消息 #" + _serial
                    )
                );
            _list.AnchorMode = UIVirtualListAnchorMode.FollowEndIfAtEnd;
            _list.InsertRange(start, count);
            _list.AnchorMode = UIVirtualListAnchorMode.PreserveFirstVisibleKey;
            if (_follow && wasEnd)
                _list.ScrollToBottom();
            Emit($"增量追加 {count} 条；追加前 IsAtEnd={wasEnd}，follow={_follow}");
            Stats();
        }

        private void Prepend()
        {
            var history = new List<ChatData>();
            for (int i = 0; i < 8; i++)
                history.Add(Make(i == 0 ? "system" : "other", "历史消息 -" + (8 - i)));
            _source.Items.InsertRange(0, history);
            _list.AnchorMode = UIVirtualListAnchorMode.PreserveFirstVisibleKey;
            _list.InsertRange(0, history.Count);
            Emit("头插 8 条历史，PreserveFirstVisibleKey 保持阅读锚点");
            Stats();
        }

        private void ExpandVisible()
        {
            _list.GetVisibleRange(out int start, out int end);
            if (start < 0)
                return;
            _list.BeginSizeUpdate();
            for (int i = start; i <= end; i++)
            {
                ChatData d = _source.Items[i];
                d.Expanded = !d.Expanded;
                d.Height = d.Expanded ? 104 : (d.Type == "system" ? 42 : 58);
                _list.RefreshItem(i);
                _list.NotifyItemSizeChanged(i, d.Height);
            }
            _list.EndSizeUpdate();
            Emit($"批量尺寸 API：展开可见 [{start},{end}]，锚点模式={_list.SizeAnchorMode}");
            Stats();
        }

        private void Stats()
        {
            if (_stats == null || _list == null)
                return;
            long? key = _list.GetFirstVisibleKey();
            _stats.text =
                $"created={_created} visible={_visible} recycled={_recycled}\nfirstKey={key?.ToString() ?? "-"} IsAtEnd={_list.IsAtEnd()} anchor={_list.AnchorMode}/{_list.SizeAnchorMode}";
        }
    }

    public sealed class ChatData
    {
        public long Key;
        public string Type;
        public string Text;
        public float Height;
        public bool Expanded;
    }

    public sealed class ChatSource : IUIVirtualListDataSource
    {
        public readonly List<ChatData> Items = new List<ChatData>();
        public int Count => Items.Count;

        public long GetKey(int i) => Items[i].Key;

        public object GetData(int i) => Items[i];

        public string GetTemplateId(int i) => Items[i].Type;
    }

    public abstract class ChatLabItem : UIItem<ChatData>
    {
        private Text _text;
        private int _visible,
            _recycled;
        protected abstract string Side { get; }

        protected override void OnInit()
        {
            _text = ExampleUIFactory.CreateText(
                "Message",
                transform,
                "",
                13,
                TextAnchor.MiddleLeft,
                Color.white
            );
            ExampleUIFactory.SetRect(
                _text.rectTransform,
                Vector2.zero,
                Vector2.one,
                new Vector2(12, 4),
                new Vector2(-12, -4)
            );
        }

        protected override void OnSetData(int i, ChatData d)
        {
            Draw(d);
        }

        protected override void OnItemVisible(int i, ChatData d)
        {
            _visible++;
            Draw(d);
        }

        protected override void OnItemRecycled(int i, ChatData d)
        {
            _recycled++;
        }

        private void Draw(ChatData d)
        {
            if (d != null && _text != null)
                _text.text =
                    $"[{Side}] Key={d.Key} · {d.Text}{(d.Expanded ? "\n已展开：战术坐标与成员状态详情" : "")}\n复用 visible={_visible} recycled={_recycled}";
        }
    }

    public sealed class SelfChatItem : ChatLabItem
    {
        protected override string Side => "self";
    }

    public sealed class OtherChatItem : ChatLabItem
    {
        protected override string Side => "other";
    }

    public sealed class SystemChatItem : ChatLabItem
    {
        protected override string Side => "system";
    }
}
