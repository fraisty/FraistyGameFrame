using FRFrame;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    public sealed partial class RoleItem : UIItem<ShopItemData>
    {
        private Text _nameText;
        private Text _descText;
        private Text _priceText;
        private Text _stateText;
        private Button _buyButton;
        private int _purchaseCount;

        protected override void OnInit()
        {
            RectTransform root = ExampleUIFactory.Stretch(gameObject);

            _nameText = ExampleUIFactory.CreateText("Name", root, string.Empty, 18, TextAnchor.MiddleLeft, Color.white);
            ExampleUIFactory.SetRect(_nameText.rectTransform, new Vector2(0f, 1f), new Vector2(0.58f, 1f), new Vector2(18f, -34f), new Vector2(0f, -6f));

            _descText = ExampleUIFactory.CreateText("Desc", root, string.Empty, 14, TextAnchor.MiddleLeft, new Color(0.75f, 0.78f, 0.85f));
            ExampleUIFactory.SetRect(_descText.rectTransform, new Vector2(0f, 0f), new Vector2(0.66f, 1f), new Vector2(18f, 10f), new Vector2(0f, -38f));

            _priceText = ExampleUIFactory.CreateText("Price", root, string.Empty, 17, TextAnchor.MiddleRight, new Color(1f, 0.86f, 0.3f));
            ExampleUIFactory.SetRect(_priceText.rectTransform, new Vector2(0.58f, 0.35f), new Vector2(0.78f, 1f), new Vector2(0f, 0f), new Vector2(-10f, 0f));

            _stateText = ExampleUIFactory.CreateText("State", root, "未购买", 13, TextAnchor.MiddleRight, new Color(0.62f, 0.78f, 1f));
            ExampleUIFactory.SetRect(_stateText.rectTransform, new Vector2(0.58f, 0f), new Vector2(0.78f, 0.45f), new Vector2(0f, 4f), new Vector2(-10f, 0f));

            _buyButton = ExampleUIFactory.CreateButton("BuyButton", root, "购买");
            ExampleUIFactory.SetRect(_buyButton.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-120f, -22f), new Vector2(-18f, 22f));
            AddButtonClick(_buyButton, OnClickBuy, UIEventListenMode.UntilDispose);
            AddPointerEnter(gameObject, (go, eventData) => _descText.color = Color.white, UIEventListenMode.UntilDispose);
            AddPointerExit(gameObject, (go, eventData) => _descText.color = new Color(0.75f, 0.78f, 0.85f), UIEventListenMode.UntilDispose);

            UILongPressTrigger longPress = _buyButton.gameObject.AddComponent<UILongPressTrigger>();
            longPress.longPressThreshold = 0.6f;
            longPress.repeatLongPress = true;
            longPress.repeatInterval = 0.35f;
            longPress.onLongPressed.AddListener(() => FRFM.UI.Toast.Show(TypedData != null ? $"长按购买 {TypedData.Name}" : "长按购买商品"));
            longPress.onLongPressRepeated.AddListener(() => FRFM.UI.Toast.Show(TypedData != null ? $"连续购买 {TypedData.Name}" : "连续购买商品"));
        }

        protected override void OnSetData(int index, ShopItemData data)
        {
            if (data == null)
                return;

            _purchaseCount = 0;
            _nameText.text = $"{index + 1}. {data.Name}";
            _descText.text = data.Desc;
            _priceText.text = data.SoldOut ? "已售罄" : $"{data.Price}G";
            _stateText.text = data.SoldOut ? "不可购买" : "未购买";
            _stateText.color = data.SoldOut ? new Color(0.9f, 0.45f, 0.45f) : new Color(0.62f, 0.78f, 1f);
            _buyButton.interactable = !data.SoldOut;
            Text buttonText = _buyButton.GetComponentInChildren<Text>(true);
            if (buttonText != null)
                buttonText.text = data.SoldOut ? "售罄" : "购买";
            SetSelected(false);
        }

        protected override void OnSelectedChanged(bool selected)
        {
            Image image = GetComponent<Image>();
            if (image != null)
                image.color = selected ? new Color(0.22f, 0.34f, 0.52f, 1f) : new Color(0.14f, 0.17f, 0.25f, 1f);
        }

        private void OnClickBuy()
        {
            _purchaseCount++;
            SetSelected(true);
            _stateText.text = $"已购买 x{_purchaseCount}";
            _stateText.color = new Color(0.55f, 1f, 0.65f);
            _buyButton.GetComponentInChildren<Text>(true).text = "再买";
            FRFM.UI.Toast.Show(TypedData != null ? $"购买 {TypedData.Name}" : "购买商品");
        }
    }
}
