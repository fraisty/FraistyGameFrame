using System;
using System.Collections.Generic;
using FRFrame;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    public sealed class InventoryLabWidget : RuntimeLabWidget
    {
        private readonly InventorySource _source = new InventorySource();
        private readonly List<GameObject> _templates = new List<GameObject>();
        private UIVirtualList _list;
        private ScrollRect _scroll;
        private RectTransform _viewport;
        private RectTransform _content;
        private GameObject _materialTemplate;
        private Text _stats;
        private long _selected;
        private int _created,
            _visible,
            _recycled;
        private long _serial = 5000;

        protected override void OnInit()
        {
            Root = ExampleUIFactory.Stretch(gameObject);
            RectTransform bar = Toolbar(Root, 68f);
            Button(bar, "随机获得", AddLoot);
            Button(bar, "删除选中", DeleteSelected);
            Button(bar, "品质排序", () => Sort(true));
            Button(bar, "数量排序", () => Sort(false));
            Button(bar, "移到最前", MoveFirst);
            Button(bar, "过滤低品质", Filter);
            Button(bar, "ScrollToKey", ScrollSelected);
            _stats = Label(bar, "", 11, TextAnchor.MiddleCenter);
            _scroll = Scroll(
                Root,
                out _viewport,
                out _content,
                true
            );
            ExampleUIFactory.SetRect(
                _scroll.GetComponent<RectTransform>(),
                Vector2.zero,
                Vector2.one,
                new Vector2(10, 10),
                new Vector2(-10, -84)
            );
            _materialTemplate = Template("MaterialPrefab", new Color(.12f, .30f, .35f));
            GameObject equipment = Template("EquipmentPrefab", new Color(.28f, .17f, .38f));
            _templates.AddRange(new[] { _materialTemplate, equipment });
            _content.anchorMax = new Vector2(0f, 1f);
            Canvas.ForceUpdateCanvases();
            _list = _scroll.gameObject.AddComponent<UIVirtualList>();
            ConfigureGrid();
            _list.Initialize(OwnerForm, typeof(InventoryMaterialItem));
            _list.ConfigureTemplates(
                new[]
                {
                    new UIVirtualListTemplate(
                        "material",
                        _materialTemplate,
                        typeof(InventoryMaterialItem),
                        20
                    ),
                    new UIVirtualListTemplate(
                        "equipment",
                        equipment,
                        typeof(InventoryEquipmentItem),
                        20
                    ),
                    new UIVirtualListTemplate(
                        "header",
                        _materialTemplate,
                        typeof(InventoryHeaderItem),
                        4
                    ),
                }
            );
            _list.AnchorMode = UIVirtualListAnchorMode.PreserveFirstVisibleKey;
            AddUIEvent(
                _list.onItemCreated,
                _ =>
                {
                    _created++;
                    UpdateStats();
                }
            );
            AddUIEvent(
                _list.onItemVisible,
                (_, item) =>
                {
                    _visible++;
                    BindSelect(item);
                    UpdateStats();
                }
            );
            AddUIEvent(
                _list.onItemRecycled,
                (_, __) =>
                {
                    _recycled++;
                    UpdateStats();
                }
            );
            _source.Reset();
            _list.SetDataSource(_source);
            UpdateStats();
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
            ConfigureGrid();
            _list?.ForceRefresh();
        }

        private void ConfigureGrid()
        {
            if (_list == null || _scroll == null || _viewport == null || _content == null || _materialTemplate == null)
                return;

            const int columns = 5;
            const float horizontalSpacing = 8f;
            const float horizontalPadding = 10f;
            float availableWidth = Mathf.Max(1f, _viewport.rect.width - horizontalPadding - horizontalSpacing * (columns - 1));
            float itemWidth = availableWidth / columns;
            _list.Configure(
                _scroll,
                _viewport,
                _content,
                _materialTemplate,
                UIVirtualListLayoutMode.GridVertical,
                new Vector2(itemWidth, 78f),
                new Vector2(horizontalSpacing, 8f),
                new UIVirtualListPadding
                {
                    top = 5,
                    bottom = 5,
                    left = 5,
                    right = 5,
                },
                columns,
                2,
                30
            );
        }

        private void BindSelect(UIItem item)
        {
            InventoryLabItem baseItem = item as InventoryLabItem;
            if (baseItem != null)
                baseItem.Select = key =>
                {
                    _selected = key;
                    _list.ForceRefresh();
                    Emit("选中背包 key=" + key);
                    UpdateStats();
                };
            item.SetSelected(item.Key == _selected);
        }

        private void AddLoot()
        {
            int index = Mathf.Min(1, _source.Count);
            var data = new InventoryData
            {
                Key = ++_serial,
                Name = "战利品" + _serial,
                Count = UnityEngine.Random.Range(1, 30),
                Quality = UnityEngine.Random.Range(1, 6),
                Template = UnityEngine.Random.value > .45f ? "equipment" : "material",
            };
            _source.Items.Insert(index, data);
            _list.InsertRange(index, 1);
            _selected = data.Key;
            Emit("增量 InsertRange 获得 " + data.Name);
            UpdateStats();
        }

        private void DeleteSelected()
        {
            int i = _source.Items.FindIndex(x => x.Key == _selected);
            if (i < 0)
                return;
            _source.Items.RemoveAt(i);
            _list.RemoveRange(i, 1);
            Emit("增量 RemoveRange 删除 key=" + _selected);
            _selected = 0;
            UpdateStats();
        }

        private void Sort(bool quality)
        {
            _source.Items.Sort(
                (a, b) =>
                    a.Template == "header" ? -1
                    : b.Template == "header" ? 1
                    : quality ? b.Quality.CompareTo(a.Quality)
                    : b.Count.CompareTo(a.Count)
            );
            _list.ReplaceRange(0, _source.Count);
            Emit("ReplaceRange：按" + (quality ? "品质" : "数量") + "排序，并保持首可见 key");
        }

        private void MoveFirst()
        {
            int i = _source.Items.FindIndex(x => x.Key == _selected);
            if (i <= 1)
                return;
            InventoryData d = _source.Items[i];
            _source.Items.RemoveAt(i);
            _source.Items.Insert(1, d);
            _list.Move(i, 1);
            Emit("Move：选中项移到最前");
        }

        private void Filter()
        {
            bool filtered = _source.Items.Exists(x => x.Quality < 3);
            if (filtered)
                _source.Items.RemoveAll(x => x.Template != "header" && x.Quality < 3);
            else
                _source.Reset();
            _list.ReplaceRange(0, _source.Count);
            Emit(filtered ? "过滤品质<3" : "恢复全部背包数据");
            UpdateStats();
        }

        private void ScrollSelected()
        {
            bool ok = _list.ScrollToKey(_selected, UIVirtualListAlignment.Center);
            Emit("ScrollToKey(" + _selected + ")=" + ok);
        }

        private void UpdateStats()
        {
            if (_stats != null)
                _stats.text =
                    $"data={_source.Count} created={_created} visible={_visible} recycled={_recycled}\nselected key={_selected} · PreserveFirstVisibleKey";
        }
    }

    public sealed class InventoryData
    {
        public long Key;
        public string Name;
        public int Count;
        public int Quality;
        public string Template;
    }

    public sealed class InventorySource : IUIVirtualListDataSource
    {
        public readonly List<InventoryData> Items = new List<InventoryData>();
        public int Count => Items.Count;

        public long GetKey(int i) => Items[i].Key;

        public object GetData(int i) => Items[i];

        public string GetTemplateId(int i) => Items[i].Template;

        public void Reset()
        {
            Items.Clear();
            Items.Add(
                new InventoryData
                {
                    Key = 100,
                    Name = "材料与装备",
                    Template = "header",
                }
            );
            for (int i = 0; i < 80; i++)
                Items.Add(
                    new InventoryData
                    {
                        Key = 1000 + i,
                        Name = (i % 4 == 0 ? "装备" : "素材") + " #" + (i + 1),
                        Count = i % 17 + 1,
                        Quality = i % 5 + 1,
                        Template = i % 4 == 0 ? "equipment" : "material",
                    }
                );
        }
    }

    public abstract class InventoryLabItem : UIItem<InventoryData>
    {
        protected Text Text;
        protected int Visible,
            Recycled;
        public Action<long> Select;
        protected abstract string Kind { get; }

        protected override void OnInit()
        {
            Text = ExampleUIFactory.CreateText(
                "Data",
                transform,
                "",
                13,
                TextAnchor.MiddleLeft,
                Color.white
            );
            ExampleUIFactory.SetRect(
                Text.rectTransform,
                Vector2.zero,
                Vector2.one,
                new Vector2(12, 4),
                new Vector2(-8, -4)
            );
            AddPointerClick(
                gameObject,
                (g, e) => Select?.Invoke(Key),
                UIEventListenMode.UntilDispose
            );
        }

        protected override void OnSetData(int i, InventoryData d)
        {
            Draw(d);
        }

        protected override void OnItemVisible(int i, InventoryData d)
        {
            Visible++;
            Draw(d);
        }

        protected override void OnItemRecycled(int i, InventoryData d)
        {
            Recycled++;
        }

        protected override void OnSelectedChanged(bool s)
        {
            transform.localScale = s ? Vector3.one * 1.03f : Vector3.one;
        }

        private void Draw(InventoryData d)
        {
            if (d != null && Text != null)
                Text.text =
                    $"[{Kind}] Key={d.Key} · Q{d.Quality} · {d.Name} x{d.Count}\n复用 visible={Visible} recycled={Recycled}";
        }
    }

    public sealed class InventoryMaterialItem : InventoryLabItem
    {
        protected override string Kind => "material";
    }

    public sealed class InventoryEquipmentItem : InventoryLabItem
    {
        protected override string Kind => "equipment";
    }

    public sealed class InventoryHeaderItem : InventoryLabItem
    {
        protected override string Kind => "header";
    }
}
