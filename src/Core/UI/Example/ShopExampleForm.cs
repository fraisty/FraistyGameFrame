using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using FRFrame;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    public enum ShopTab
    {
        Item,
        Weapon,
        Fashion
    }

    public sealed class ShopExampleFormOpenParam
    {
        public string PlayerName;
        public int Gold;
        public int Diamond;
        public ShopTab InitialTab = ShopTab.Item;
    }

    public sealed class ShopItemData
    {
        public string Name;
        public string Desc;
        public int Price;
        public bool SoldOut;
    }

    public sealed class ShopTabParam
    {
        public ShopTab Tab;
        public IReadOnlyList<ShopItemData> Items;
    }

    public sealed partial class ShopExampleForm : UIForm, IUIOpenParamReceiver<ShopExampleFormOpenParam>, IUIPreloadHandler
    {
        private RectTransform _tabRoot;
        private RectTransform _listRoot;
        private Text _titleText;
        private UIRichText _richText;
        private Text _richTextStatus;
        private bool _alternateRichTextStyle;
        private readonly Dictionary<ShopTab, ShopPageWidget> _pages = new Dictionary<ShopTab, ShopPageWidget>();
        private CurrencyWidget _currencyWidget;
        private ShopPageWidget _currentPage;
        private RuntimeExtensionsExampleWidget _extensionsWidget;
        private bool _showingExtensions;
        private ShopExampleFormOpenParam _param;

        public void SetOpenParam(ShopExampleFormOpenParam param)
        {
            _param = param;
        }

        public async UniTask OnPreloadAsync(CancellationToken cancellationToken)
        {
            await UniTask.DelayFrame(1, cancellationToken: cancellationToken).SuppressCancellationThrow();
        }

        protected override void OnInit(object userData)
        {
            BuildPureCodeView();
            Transform currencyRoot = _tabRoot.Find("CurrencyRoot");
            _currencyWidget = CreateWidget<CurrencyWidget>(currencyRoot != null ? currencyRoot.gameObject : _tabRoot.gameObject, BuildCurrencyData());
        }

        protected override void OnOpen(object userData)
        {
            ShopExampleFormOpenParam openParam = _param ?? userData as ShopExampleFormOpenParam ?? new ShopExampleFormOpenParam { PlayerName = "Guest" };
            _param = openParam;
            _titleText.text = $"商店示例 - {openParam.PlayerName}";
            _currencyWidget.Refresh(BuildCurrencyData());
            _richText.SetText(BuildRichTextExample(openParam));
            if (_showingExtensions)
                ShowExtensionsExample();
            else
                SwitchTab(openParam.InitialTab);
        }

        protected override void OnShow(object userData)
        {
            _currencyWidget?.Refresh(BuildCurrencyData());
        }

        private void BuildPureCodeView()
        {
            ExampleUIFactory.Stretch(gameObject);
            RectTransform root = ExampleUIFactory.CreatePanel("ShopRoot", transform, new Color(0.05f, 0.07f, 0.10f, 0.96f));
            UISafeAreaAdapter safeAreaAdapter = root.gameObject.AddComponent<UISafeAreaAdapter>();
            safeAreaAdapter.extraPadding = new RectOffset(8, 8, 8, 8);

            _titleText = ExampleUIFactory.CreateText("Title", root, "商店示例", 28, TextAnchor.MiddleLeft, Color.white);
            ExampleUIFactory.SetRect(_titleText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -52f), new Vector2(-180f, -14f));

            Text descText = ExampleUIFactory.CreateText("Desc", root, "纯代码 UIForm + UIWidget + UIItem + RichText 综合示例", 16, TextAnchor.MiddleLeft, new Color(0.68f, 0.76f, 0.90f));
            ExampleUIFactory.SetRect(descText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -84f), new Vector2(-180f, -54f));

            Button closeButton = ExampleUIFactory.CreateButton("CloseButton", root, "关闭");
            ExampleUIFactory.SetRect(closeButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-150f, -58f), new Vector2(-24f, -18f));
            AddButtonClick(closeButton, () => Close(), UIEventListenMode.UntilDispose);

            _tabRoot = ExampleUIFactory.CreatePanel("TabRoot", root, new Color(0.10f, 0.12f, 0.18f, 1f));
            ExampleUIFactory.SetRect(_tabRoot, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(24f, 144f), new Vector2(224f, -104f));

            Text tabTitle = ExampleUIFactory.CreateText("TabTitle", _tabRoot, "分类切换", 18, TextAnchor.MiddleLeft, Color.white);
            ExampleUIFactory.SetRect(tabTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -36f), new Vector2(-16f, -8f));

            RectTransform currencyRoot = ExampleUIFactory.CreatePanel("CurrencyRoot", _tabRoot, new Color(0.12f, 0.15f, 0.22f, 1f));
            ExampleUIFactory.SetRect(currencyRoot, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -116f), new Vector2(-16f, -48f));

            _listRoot = ExampleUIFactory.CreatePanel("ListRoot", root, new Color(0.09f, 0.10f, 0.14f, 1f));
            ExampleUIFactory.SetRect(_listRoot, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(248f, 144f), new Vector2(-24f, -104f));

            RectTransform richPanel = ExampleUIFactory.CreatePanel("RichTextPanel", root, new Color(0.08f, 0.10f, 0.16f, 1f));
            ExampleUIFactory.SetRect(richPanel, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(24f, 24f), new Vector2(-24f, 124f));

            Text richTitle = ExampleUIFactory.CreateText("RichTitle", richPanel, "RichText 同名嵌套 / gradient 包 link / 配置缓存", 15, TextAnchor.MiddleLeft, new Color(0.85f, 0.90f, 1f));
            ExampleUIFactory.SetRect(richTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -26f), new Vector2(-220f, -4f));

            Button richStyleButton = ExampleUIFactory.CreateButton("RichStyleButton", richPanel, "切换链接样式");
            ExampleUIFactory.SetRect(richStyleButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-204f, -30f), new Vector2(-88f, -4f));
            AddButtonClick(richStyleButton, ToggleRichTextStyle, UIEventListenMode.UntilDispose);
            _richTextStatus = ExampleUIFactory.CreateText("RichStatus", richPanel, "warning=0 · cache=0", 11, TextAnchor.MiddleRight, new Color(0.62f, 0.78f, 1f));
            ExampleUIFactory.SetRect(_richTextStatus.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-84f, -28f), new Vector2(-8f, -4f));

            TMP_Text richLabel = ExampleUIFactory.CreateTmpText("RichText", richPanel, string.Empty, 15, TextAlignmentOptions.TopLeft, Color.white);
            richLabel.textWrappingMode = TextWrappingModes.Normal;
            richLabel.overflowMode = TextOverflowModes.Overflow;
            richLabel.margin = new Vector4(0f, 0f, 0f, 0f);
            ExampleUIFactory.SetRect(richLabel.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(16f, 10f), new Vector2(-16f, -34f));
            _richText = richLabel.gameObject.AddComponent<UIRichText>();
            _richText.targetText = richLabel;
            _richText.config.defaultLinkColor = new Color(0.45f, 0.75f, 1f, 1f);
            _richText.config.gradientAliases.Add(new UIRichTextGradientAlias { key = "quality5", fallbackColor = "#FFB84D" });
            _richText.RegisterLinkHandler("player", context => FRFM.UI.Toast.Show($"点击玩家：{context.Text}"));
            _richText.RegisterLinkHandler("item", context => FRFM.UI.Toast.Show($"点击道具：{context.Text}"));
            _richText.RegisterLinkHandler("shop", context => FRFM.UI.Toast.Show($"打开商店入口：{context.Payload}"));
            _richText.onLinkLongPressed.AddListener(context => FRFM.UI.Toast.Show($"长按链接：{context.Text}"));
            _richText.onParseCompleted.AddListener(_ => UpdateRichTextStatus());

            CreateTabButton("ItemTab", "道具", ShopTab.Item, -132f);
            CreateTabButton("WeaponTab", "武器", ShopTab.Weapon, -188f);
            CreateTabButton("FashionTab", "时装", ShopTab.Fashion, -244f);

            Button extensionsButton = ExampleUIFactory.CreateButton("ExtensionsTab", _tabRoot, "扩展示例");
            ExampleUIFactory.SetRect(extensionsButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -344f), new Vector2(-16f, -300f));
            AddButtonClick(extensionsButton, ShowExtensionsExample, UIEventListenMode.UntilDispose);
        }

        private void CreateTabButton(string name, string text, ShopTab tab, float top)
        {
            Button button = ExampleUIFactory.CreateButton(name, _tabRoot, text);
            ExampleUIFactory.SetRect(button.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, top - 44f), new Vector2(-16f, top));
            AddButtonClick(button, () => SwitchTab(tab), UIEventListenMode.UntilDispose);
        }

        private void SwitchTab(ShopTab tab)
        {
            _showingExtensions = false;
            if (_extensionsWidget != null)
                HideWidget(_extensionsWidget);
            if (_currentPage != null)
                HideWidget(_currentPage);

            if (!_pages.TryGetValue(tab, out ShopPageWidget page) || page == null)
            {
                GameObject pageGo = new GameObject(BuildTabRootName(tab), typeof(RectTransform), typeof(Image));
                pageGo.transform.SetParent(_listRoot, false);
                page = CreateWidget<ShopPageWidget>(pageGo, BuildTabParam(tab));
                _pages[tab] = page;
            }
            else
            {
                ShowWidget(page, BuildTabParam(tab));
            }

            _currentPage = page;
            _currentPage.Refresh(BuildTabParam(tab));
        }

        private void ShowExtensionsExample()
        {
            if (_currentPage != null)
                HideWidget(_currentPage);

            if (_extensionsWidget == null)
            {
                GameObject widgetGo = new GameObject("RuntimeExtensionsExample", typeof(RectTransform), typeof(Image));
                widgetGo.transform.SetParent(_listRoot, false);
                _extensionsWidget = CreateWidget<RuntimeExtensionsExampleWidget>(widgetGo);
            }
            else
            {
                ShowWidget(_extensionsWidget);
            }

            _showingExtensions = true;
        }

        private ShopTabParam BuildTabParam(ShopTab tab)
        {
            return new ShopTabParam
            {
                Tab = tab,
                Items = BuildItems(tab)
            };
        }

        private string BuildTabRootName(ShopTab tab)
        {
            return $"{tab}Page";
        }

        private CurrencyWidget.CurrencyData BuildCurrencyData()
        {
            return new CurrencyWidget.CurrencyData
            {
                Gold = _param != null ? _param.Gold : 0,
                Diamond = _param != null ? _param.Diamond : 0
            };
        }

        private void ToggleRichTextStyle()
        {
            _alternateRichTextStyle = !_alternateRichTextStyle;
            _richText.config.defaultLinkColor = _alternateRichTextStyle ? new Color(1f, 0.58f, 0.30f, 1f) : new Color(0.45f, 0.75f, 1f, 1f);
            _richText.config.underlineLinks = !_alternateRichTextStyle;
            _richText.Refresh();
            UpdateRichTextStatus();
        }

        private void UpdateRichTextStatus()
        {
            if (_richTextStatus == null || _richText == null)
                return;
            int warningCount = _richText.LastParseResult != null ? _richText.LastParseResult.Warnings.Count : 0;
            _richTextStatus.text = $"warning={warningCount} · cache={_richText.CacheCount}";
        }

        private string BuildRichTextExample(ShopExampleFormOpenParam openParam)
        {
            string playerName = openParam != null ? openParam.PlayerName : "Guest";
            int gold = openParam != null ? openParam.Gold : 0;
            return $"[player:10001]{playerName} 与 [player:10002]同名[player:10003]嵌套玩家[/player][/player][/player] 获得金币 x{gold}\n" +
                   "[gradient:quality5][item:2001]gradient 包裹可点击霜火长弓[/item][/gradient] · [shop:daily]商店入口[/shop] · 未知标签会转义：<script>";
        }

        private IReadOnlyList<ShopItemData> BuildItems(ShopTab tab)
        {
            switch (tab)
            {
                case ShopTab.Weapon:
                    return new[]
                    {
                        new ShopItemData { Name = "训练木剑", Desc = "适合新手的基础武器", Price = 300 },
                        new ShopItemData { Name = "霜火长弓", Desc = "带有减速效果", Price = 1800 },
                        new ShopItemData { Name = "星陨法杖", Desc = "提升技能伤害", Price = 2600, SoldOut = true }
                    };
                case ShopTab.Fashion:
                    return new[]
                    {
                        new ShopItemData { Name = "学院制服", Desc = "常驻时装", Price = 900 },
                        new ShopItemData { Name = "夏日泳装", Desc = "限定外观", Price = 2200 },
                        new ShopItemData { Name = "执事礼服", Desc = "提升魅力值", Price = 1600 }
                    };
                default:
                    return new[]
                    {
                        new ShopItemData { Name = "小型体力药", Desc = "恢复 30 点体力", Price = 120 },
                        new ShopItemData { Name = "扫荡券", Desc = "快速完成已通关副本", Price = 80 },
                        new ShopItemData { Name = "强化石", Desc = "装备强化材料", Price = 240 }
                    };
            }
        }
    }
}
