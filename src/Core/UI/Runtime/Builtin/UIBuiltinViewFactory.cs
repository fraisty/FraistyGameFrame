using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FRFrame
{
    internal static class UIBuiltinViewFactory
    {
        /// <summary>创建全屏拉伸 RectTransform，用于纯代码窗口根节点。</summary>
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
            RectTransform rect = Stretch(go);
            go.GetComponent<Image>().color = color;
            return rect;
        }

        public static TextMeshProUGUI CreateText(string name, Transform parent, string text, float size, TextAlignmentOptions alignment, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
            UIBuiltinFontProvider.Apply(label);
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
            go.GetComponent<Image>().color = new Color(0.20f, 0.35f, 0.60f, 1f);
            TextMeshProUGUI label = CreateText("Label", go.transform, text, 24f, TextAlignmentOptions.Center, Color.white);
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
}
