using System.Collections.Generic;
using FRFrame;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    public sealed class RuntimeExtensionsExampleWidget : UIWidget
    {
        private readonly List<UIWidget> _scenes = new List<UIWidget>();
        private readonly List<string> _logEntries = new List<string>(12);
        private readonly string[] _names =
        {
            "战术地图",
            "背包整理",
            "公会聊天",
            "活动 Banner",
            "任务分页",
            "字体诊断",
            "常用交互",
        };
        private RectTransform _workspace;
        private Text _eventLog;
        private int _current = -1;

        protected override void OnInit()
        {
            RectTransform root = ExampleUIFactory.Stretch(gameObject);
            (GetComponent<Image>() ?? gameObject.AddComponent<Image>()).color = new Color(
                0.055f,
                0.065f,
                0.095f,
                1f
            );
            Text title = ExampleUIFactory.CreateText(
                "Title",
                root,
                "UI Runtime 业务实验室",
                20,
                TextAnchor.MiddleLeft,
                Color.white
            );
            ExampleUIFactory.SetRect(
                title.rectTransform,
                new Vector2(0f, 1f),
                Vector2.one,
                new Vector2(14f, -31f),
                new Vector2(-14f, -4f)
            );

            RectTransform tabs = ExampleUIFactory.CreatePanel(
                "SceneTabs",
                root,
                new Color(0.08f, 0.10f, 0.15f, 1f)
            );
            ExampleUIFactory.SetRect(
                tabs,
                new Vector2(0f, 1f),
                Vector2.one,
                new Vector2(10f, -70f),
                new Vector2(-10f, -34f)
            );
            HorizontalLayoutGroup layout = tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(4, 4, 3, 3);
            layout.spacing = 4f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            for (int i = 0; i < _names.Length; i++)
            {
                int index = i;
                Button button = ExampleUIFactory.CreateButton("Tab" + i, tabs, _names[i]);
                button.GetComponentInChildren<Text>().fontSize = 13;
                AddButtonClick(button, () => SwitchScene(index), UIEventListenMode.UntilDispose);
            }

            _workspace = ExampleUIFactory.CreatePanel(
                "Workspace",
                root,
                new Color(0.075f, 0.085f, 0.12f, 1f)
            );
            ExampleUIFactory.SetRect(
                _workspace,
                Vector2.zero,
                Vector2.one,
                new Vector2(10f, 74f),
                new Vector2(-10f, -74f)
            );
            RectTransform logPanel = ExampleUIFactory.CreatePanel(
                "EventLog",
                root,
                new Color(0.035f, 0.045f, 0.07f, 1f)
            );
            ExampleUIFactory.SetRect(
                logPanel,
                Vector2.zero,
                new Vector2(1f, 0f),
                new Vector2(10f, 8f),
                new Vector2(-10f, 68f)
            );
            _eventLog = ExampleUIFactory.CreateText(
                "Text",
                logPanel,
                "事件日志：选择场景开始业务验收",
                12,
                TextAnchor.UpperLeft,
                new Color(0.65f, 0.82f, 1f)
            );
            ExampleUIFactory.SetRect(
                _eventLog.rectTransform,
                Vector2.zero,
                Vector2.one,
                new Vector2(8f, 4f),
                new Vector2(-8f, -4f)
            );

            CreateScene<TacticalMapLabWidget>("TacticalMap");
            CreateScene<InventoryLabWidget>("Inventory");
            CreateScene<GuildChatLabWidget>("GuildChat");
            CreateScene<BannerLabWidget>("Banner");
            CreateScene<SnapPageLabWidget>("SnapPage");
            CreateScene<FontRefreshLabWidget>("FontRefresh");
            CreateScene<CommonInteractionLabWidget>("CommonInteraction");
            SwitchScene(0);
        }

        protected override void OnShow(object userData)
        {
            if (_current >= 0 && _current < _scenes.Count)
                _scenes[_current].SetVisible(true);
        }

        protected override void OnHide()
        {
            if (_current >= 0 && _current < _scenes.Count && _scenes[_current].IsVisible)
                _scenes[_current].SetVisible(false);
        }

        private void CreateScene<T>(string name)
            where T : UIWidget
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_workspace, false);
            ExampleUIFactory.Stretch(go);
            go.GetComponent<Image>().color = Color.clear;
            T scene = CreateChildWidget<T>(go, (System.Action<string>)Log);
            scene.SetVisible(false);
            _scenes.Add(scene);
        }

        private void SwitchScene(int index)
        {
            if (index < 0 || index >= _scenes.Count)
                return;

            if (index == _current)
            {
                if (!_scenes[index].IsVisible)
                    _scenes[index].SetVisible(true);
                return;
            }

            if (_current >= 0 && _scenes[_current].IsVisible)
                _scenes[_current].SetVisible(false);

            _current = index;
            _scenes[index].SetVisible(true);
            Log("切换场景：" + _names[index]);
        }

        private void Log(string message)
        {
            if (_eventLog == null)
                return;

            _logEntries.Insert(0, $"[{Time.unscaledTime:F1}] {message}");
            if (_logEntries.Count > 12)
                _logEntries.RemoveRange(12, _logEntries.Count - 12);
            _eventLog.text = string.Join("\n", _logEntries);
        }
    }
}
