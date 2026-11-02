using System;
using FRFrame;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Game.UI
{
    public abstract class RuntimeLabWidget : UIWidget
    {
        protected Action<string> Log;
        protected RectTransform Root;
        protected readonly Color PanelColor = new Color(0.10f, 0.12f, 0.18f, 1f);

        protected override void OnOpen(object userData)
        {
            Log = userData as Action<string>;
        }

        protected void Emit(string text)
        {
            Log?.Invoke(text);
        }

        protected void AddUIEvent<T0, T1>(UnityEvent<T0, T1> unityEvent, UnityAction<T0, T1> action)
        {
            if (unityEvent == null || action == null)
                return;

            unityEvent.AddListener(action);
            AddEventSubscription(
                new UnityEventSubscription(() => unityEvent.RemoveListener(action)),
                UIEventListenMode.UntilDispose
            );
        }

        protected Text Label(
            Transform parent,
            string text,
            int size = 13,
            TextAnchor anchor = TextAnchor.MiddleLeft
        )
        {
            return ExampleUIFactory.CreateText("Label", parent, text, size, anchor, Color.white);
        }

        protected Button Button(Transform parent, string text, Action action)
        {
            Button b = ExampleUIFactory.CreateButton(text.Replace(" ", ""), parent, text);
            b.GetComponentInChildren<Text>().fontSize = 13;
            AddButtonClick(b, () => action(), UIEventListenMode.UntilDispose);
            return b;
        }

        protected RectTransform Toolbar(Transform parent, float height = 34f)
        {
            RectTransform bar = ExampleUIFactory.CreatePanel(
                "Toolbar",
                parent,
                new Color(0.07f, 0.09f, 0.14f, 1f)
            );
            ExampleUIFactory.SetRect(
                bar,
                new Vector2(0f, 1f),
                Vector2.one,
                new Vector2(8f, -height - 8f),
                new Vector2(-8f, -8f)
            );
            HorizontalLayoutGroup layout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(4, 4, 3, 3);
            layout.spacing = 4f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            return bar;
        }

        protected ScrollRect Scroll(
            Transform parent,
            out RectTransform viewport,
            out RectTransform content,
            bool vertical = true
        )
        {
            GameObject go = new GameObject(
                "Scroll",
                typeof(RectTransform),
                typeof(Image),
                typeof(ScrollRect)
            );
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.035f, 0.045f, 0.07f, 1f);
            viewport = ExampleUIFactory.CreatePanel("Viewport", go.transform, Color.clear);
            ExampleUIFactory.Stretch(viewport.gameObject);
            viewport.gameObject.AddComponent<RectMask2D>();
            content = ExampleUIFactory.CreatePanel("Content", viewport, Color.clear);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0f, 1f);
            content.offsetMin = content.offsetMax = Vector2.zero;
            ScrollRect scroll = go.GetComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.vertical = vertical;
            scroll.horizontal = !vertical;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            return scroll;
        }

        protected GameObject Template(string name, Color color)
        {
            GameObject go = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image)
            );
            go.GetComponent<Image>().color = color;
            go.SetActive(false);
            return go;
        }
    }
}
