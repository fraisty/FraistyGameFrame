using FRFrame;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    public sealed class FontRefreshLabWidget : RuntimeLabWidget
    {
        private UIFontTextureRefresh _refresh;
        private RectTransform _uguiRoot;
        private Text _status;
        private Font _fontA,
            _fontB;
        private int _batch;

        protected override void OnInit()
        {
            Root = ExampleUIFactory.Stretch(gameObject);
            RectTransform bar = Toolbar(Root, 68);
            Button(
                bar,
                "Request 字体 A",
                () =>
                {
                    _refresh.RequestRefresh(_fontA);
                    Status("已排队 A，LateUpdate 合并处理");
                }
            );
            Button(
                bar,
                "同帧 A+B",
                () =>
                {
                    _refresh.RequestRefresh(_fontA);
                    _refresh.RequestRefresh(_fontB);
                    Status("同帧排队两个有效 Font 引用");
                }
            );
            Button(
                bar,
                "RefreshAll",
                () =>
                {
                    _refresh.RefreshAll();
                    Status("同步 RefreshAll");
                }
            );
            Button(bar, "创建 Text + Rebuild", CreateBatch);
            _status = Label(bar, "", 11, TextAnchor.MiddleCenter);
            _uguiRoot = ExampleUIFactory.CreatePanel(
                "UGUIRoot",
                Root,
                new Color(.08f, .10f, .16f, 1)
            );
            ExampleUIFactory.SetRect(
                _uguiRoot,
                new Vector2(0, .08f),
                new Vector2(.63f, 1),
                new Vector2(12, 10),
                new Vector2(-6, -86)
            );
            Text active = Label(
                _uguiRoot,
                "UGUI Text 活跃组：动态字体图集刷新目标",
                17,
                TextAnchor.UpperLeft
            );
            ExampleUIFactory.SetRect(
                active.rectTransform,
                Vector2.zero,
                Vector2.one,
                new Vector2(12, 10),
                new Vector2(-12, -10)
            );
            Text hidden = Label(_uguiRoot, "UGUI Text 隐藏组（includeInactive=true）", 15);
            hidden.gameObject.SetActive(false);
            RectTransform tmpRoot = ExampleUIFactory.CreatePanel(
                "TMPControl",
                Root,
                new Color(.13f, .08f, .14f, 1)
            );
            ExampleUIFactory.SetRect(
                tmpRoot,
                new Vector2(.64f, .08f),
                new Vector2(1, 1),
                new Vector2(6, 10),
                new Vector2(-12, -86)
            );
            TMP_Text tmp = ExampleUIFactory.CreateTmpText(
                "TMP",
                tmpRoot,
                "TMP 对照组\nTMP_Text 使用 TMP FontAsset，明确不属于 UIFontTextureRefresh 扫描/刷新范围。",
                16,
                TextAlignmentOptions.Center,
                Color.white
            );
            ExampleUIFactory.Stretch(tmp.gameObject);
            _fontA = ExampleUIFactory.DefaultFont;
            _fontB = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_fontB == null)
                _fontB = _fontA;
            _refresh = gameObject.AddComponent<UIFontTextureRefresh>();
            _refresh.targetRoot = _uguiRoot;
            _refresh.includeInactive = true;
            _refresh.RebuildTargets();
            Status("A/B 均为非空内置字体引用（可指向同一 Font 实例）");
        }

        private void LateUpdate()
        {
            if (_refresh != null)
                Status(null);
        }

        private void CreateBatch()
        {
            for (int i = 0; i < 6; i++)
            {
                Text text = Label(_uguiRoot, $"动态 Text #{++_batch}", 12);
                RectTransform r = text.rectTransform;
                r.anchorMin = r.anchorMax = new Vector2(.05f, .1f);
                r.sizeDelta = new Vector2(160, 18);
                r.anchoredPosition = new Vector2((_batch % 3) * 170, (_batch / 3) * 20);
                text.font = i % 2 == 0 ? _fontA : _fontB;
            }
            _refresh.RebuildTargets();
            Status("创建 6 个 UGUI Text 后 RebuildTargets");
        }

        private void Status(string note)
        {
            if (_status == null || _refresh == null)
                return;
            _status.text =
                $"PendingFontCount={_refresh.PendingFontCount} Scanned={_refresh.LastScannedCount}\nRefreshed={_refresh.LastRefreshedCount} · TMP 不属于组件";
            if (!string.IsNullOrEmpty(note))
                Emit(note);
        }
    }
}
