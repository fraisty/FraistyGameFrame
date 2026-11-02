using FRFrame;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    internal static class ExampleUIFactory
    {
        private static TMP_FontAsset _tmpFont;

        public static Font DefaultFont => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        public static TMP_FontAsset DefaultTmpFont => _tmpFont != null ? _tmpFont : _tmpFont = UIBuiltinFontProvider.Font;

        public static RectTransform Stretch(GameObject go)
        {
            RectTransform rect = go.GetComponent<RectTransform>();
            if (rect == null)
                rect = go.AddComponent<RectTransform>();

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            return rect;
        }

        public static RectTransform CreatePanel(string name, Transform parent, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Stretch(go);
            go.GetComponent<Image>().color = color;
            return go.GetComponent<RectTransform>();
        }

        public static Text CreateText(string name, Transform parent, string text, int size, TextAnchor anchor, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Text label = go.GetComponent<Text>();
            label.font = DefaultFont;
            label.text = text;
            label.fontSize = size;
            label.alignment = anchor;
            label.color = color;
            label.raycastTarget = false;
            return label;
        }

        public static TMP_Text CreateTmpText(string name, Transform parent, string text, int size, TextAlignmentOptions alignment, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            TMP_Text label = go.GetComponent<TMP_Text>();
            if (DefaultTmpFont != null)
                label.font = DefaultTmpFont;
            label.text = text;
            label.fontSize = size;
            label.alignment = alignment;
            label.color = color;
            label.raycastTarget = false;
            return label;
        }

        public static Button CreateButton(string name, Transform parent, string text)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.18f, 0.28f, 0.45f, 0.95f);

            Text label = CreateText("Label", go.transform, text, 18, TextAnchor.MiddleCenter, Color.white);
            Stretch(label.gameObject);
            return go.GetComponent<Button>();
        }

        public static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }
    }

    public static class ExampleUIBootstrap
    {
        public static async void OpenShopExample()
        {
            ShopExampleFormOpenParam param = new ShopExampleFormOpenParam
            {
                PlayerName = "Fraisty",
                Gold = 12800,
                Diamond = 320,
                InitialTab = ShopTab.Item
            };

            await FRFrame.FRFM.UI.OpenCodeAsync<ShopExampleForm, ShopExampleFormOpenParam>(param);
        }

        public static async void OpenShopExampleWithOptions()
        {
            UIFormOpenContextHint();
            UIOpenOptions options = UIOpenOptions.Default;
            options.Timeout = 8f;
            options.BringToFront = true;
            options.PlayAnimation = true;
            await FRFrame.FRFM.UI.OpenCodeAsync<ShopExampleForm, ShopExampleFormOpenParam>(new ShopExampleFormOpenParam { PlayerName = "Guest" }, options);
        }

        public static void OpenCancelableShopExample()
        {
            FRFrame.FRFM.UI.RegisterCodeFormConfig<ShopExampleForm>();
            UIOpenHandle<ShopExampleForm> handle = FRFrame.FRFM.UI.OpenWithHandle<ShopExampleForm, ShopExampleFormOpenParam>(new ShopExampleFormOpenParam { PlayerName = "Cancelable" });
            handle.Cancel();
        }

        public static async void PushShopByNavigationExample()
        {
            FRFrame.FRFM.UI.RegisterCodeFormConfig<ShopExampleForm>();
            await FRFrame.FRFM.UI.Navigation.PushAsync<ShopExampleForm, ShopExampleFormOpenParam>(new ShopExampleFormOpenParam { PlayerName = "Navigator" });
        }

        public static async void PreloadShopExample()
        {
            FRFrame.FRFM.UI.RegisterCodeFormConfig<ShopExampleForm>();
            await FRFrame.FRFM.UI.PreloadAsync<ShopExampleForm>(UIPreloadMode.AssetAndInstance);
        }

        private static void UIFormOpenContextHint()
        {
        }
    }
}
