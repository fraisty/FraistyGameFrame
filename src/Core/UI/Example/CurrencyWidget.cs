using System.Collections.Generic;
using FRFrame;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    public sealed partial class CurrencyWidget : UIWidget
    {
        public sealed class CurrencyData
        {
            public int Gold;
            public int Diamond;
        }

        private Text _goldText;
        private Text _diamondText;

        protected override void OnInit()
        {
            RectTransform root = ExampleUIFactory.Stretch(gameObject);
            Image image = gameObject.GetComponent<Image>();
            if (image == null)
                image = gameObject.AddComponent<Image>();
            image.color = new Color(0.12f, 0.15f, 0.22f, 1f);

            _goldText = ExampleUIFactory.CreateText("GoldText", root, "Gold: 0", 16, TextAnchor.MiddleLeft, new Color(1f, 0.86f, 0.3f));
            ExampleUIFactory.SetRect(_goldText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -34f), new Vector2(-12f, -6f));

            _diamondText = ExampleUIFactory.CreateText("DiamondText", root, "Diamond: 0", 16, TextAnchor.MiddleLeft, new Color(0.55f, 0.9f, 1f));
            ExampleUIFactory.SetRect(_diamondText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -66f), new Vector2(-12f, -38f));
        }

        protected override void OnRefresh(object userData)
        {
            if (userData is CurrencyData data)
            {
                _goldText.text = $"Gold: {data.Gold}";
                _diamondText.text = $"Diamond: {data.Diamond}";
            }
        }
    }

    public sealed class ShopPageWidget : UIWidget
    {
        private readonly List<RoleItem> _items = new List<RoleItem>();
        private RectTransform _listRoot;
        private Text _titleText;

        protected override void OnInit()
        {
            RectTransform root = ExampleUIFactory.Stretch(gameObject);
            Image image = gameObject.GetComponent<Image>();
            if (image == null)
                image = gameObject.AddComponent<Image>();
            image.color = new Color(0.08f, 0.09f, 0.13f, 1f);

            _titleText = ExampleUIFactory.CreateText("Title", root, "非虚拟列表 · UIWidget + UIItem 示例", 22, TextAnchor.MiddleLeft, Color.white);
            ExampleUIFactory.SetRect(_titleText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -42f), new Vector2(-20f, -8f));

            Text hintText = ExampleUIFactory.CreateText("Hint", root, "这里手动创建 3 个 UIItem 卡片，用于展示子控件生命周期，不是 UIVirtualList", 14, TextAnchor.MiddleLeft, new Color(0.64f, 0.70f, 0.82f));
            ExampleUIFactory.SetRect(hintText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -68f), new Vector2(-20f, -44f));

            _listRoot = ExampleUIFactory.CreatePanel("List", root, new Color(0f, 0f, 0f, 0f));
            ExampleUIFactory.SetRect(_listRoot, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(20f, 20f), new Vector2(-20f, -82f));
        }

        protected override void OnOpen(object userData)
        {
            Refresh(userData);
        }

        protected override void OnShow(object userData)
        {
            Refresh(userData);
        }

        protected override void OnRefresh(object userData)
        {
            if (!(userData is ShopTabParam param))
                return;

            _titleText.text = $"{GetTabName(param.Tab)}列表 · 非虚拟 UIItem 卡片";
            EnsureItemCount(param.Items != null ? param.Items.Count : 0);

            for (int i = 0; i < _items.Count; i++)
            {
                bool active = param.Items != null && i < param.Items.Count;
                _items[i].gameObject.SetActive(active);
                if (active)
                    _items[i].SetItemData(i, param.Items[i]);
            }
        }

        private void EnsureItemCount(int count)
        {
            while (_items.Count < count)
            {
                GameObject itemGo = new GameObject($"Item_{_items.Count}", typeof(RectTransform), typeof(Image));
                itemGo.transform.SetParent(_listRoot, false);
                RectTransform rect = itemGo.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(0f, 84f);
                rect.anchoredPosition = new Vector2(0f, -_items.Count * 96f);
                Image itemImage = itemGo.GetComponent<Image>();
                itemImage.color = new Color(0.14f, 0.17f, 0.25f, 1f);
                Outline outline = itemGo.AddComponent<Outline>();
                outline.effectColor = new Color(0.36f, 0.48f, 0.70f, 0.85f);
                outline.effectDistance = new Vector2(1f, -1f);

                RoleItem item = CreateChildWidget<RoleItem>(itemGo);
                _items.Add(item);
            }
        }

        private string GetTabName(ShopTab tab)
        {
            switch (tab)
            {
                case ShopTab.Weapon:
                    return "武器";
                case ShopTab.Fashion:
                    return "时装";
                default:
                    return "道具";
            }
        }
    }
}
