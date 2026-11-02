using System.Collections.Generic;
using FRFrame;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UI
{
    public sealed class TacticalMapLabWidget : RuntimeLabWidget
    {
        private UIMeshImage _mesh;
        private Text _status;
        private Texture2D _texture;
        private int _shape;
        private readonly string[] _shapes = { "圆形", "扇形", "环形", "矩形" };

        protected override void OnInit()
        {
            Root = ExampleUIFactory.Stretch(gameObject);
            RectTransform bar = Toolbar(Root);
            Button(bar, "命中 Rect/Triangles", ToggleRaycast);
            Button(bar, "适配 Stretch/Fit/Fill", CycleFit);
            Button(bar, "几何 圆/扇/环/矩形", CycleShape);
            _status = Label(bar, "等待点击地图", 12, TextAnchor.MiddleCenter);

            RectTransform map = ExampleUIFactory.CreatePanel(
                "HarborMap",
                Root,
                new Color(0.025f, 0.06f, 0.10f, 1f)
            );
            ExampleUIFactory.SetRect(
                map,
                Vector2.zero,
                Vector2.one,
                new Vector2(30f, 24f),
                new Vector2(-30f, -52f)
            );
            _mesh = new GameObject(
                "SkillRange",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(UIMeshImage)
            ).GetComponent<UIMeshImage>();
            _mesh.transform.SetParent(map, false);
            ExampleUIFactory.SetRect(
                _mesh.rectTransform,
                new Vector2(.15f, .1f),
                new Vector2(.85f, .9f),
                Vector2.zero,
                Vector2.zero
            );
            _mesh.color = new Color(1f, 1f, 1f, .86f);
            _mesh.RaycastMode = UIMeshRaycastMode.Triangles;
            _mesh.FitMode = UIMeshFitMode.Stretch;
            _texture = GradientTexture();
            _mesh.SetTexture(_texture);
            SetShape(0);
            AddPointerClick(_mesh.gameObject, OnMapClick, UIEventListenMode.UntilDispose);
            Text hint = Label(
                map,
                "运行时渐变技能区：透明外部不应被 Triangles 命中",
                13,
                TextAnchor.LowerCenter
            );
            ExampleUIFactory.SetRect(
                hint.rectTransform,
                Vector2.zero,
                Vector2.one,
                new Vector2(4f, 4f),
                new Vector2(-4f, -4f)
            );
        }

        protected override void OnDispose()
        {
            if (_texture != null)
                Object.Destroy(_texture);
        }

        private void ToggleRaycast()
        {
            _mesh.RaycastMode =
                _mesh.RaycastMode == UIMeshRaycastMode.Rect
                    ? UIMeshRaycastMode.Triangles
                    : UIMeshRaycastMode.Rect;
            Emit("地图命中模式=" + _mesh.RaycastMode);
        }

        private void CycleFit()
        {
            _mesh.FitMode =
                _mesh.FitMode == UIMeshFitMode.Stretch ? UIMeshFitMode.Fit
                : _mesh.FitMode == UIMeshFitMode.Fit ? UIMeshFitMode.Fill
                : UIMeshFitMode.Stretch;
            Emit("技能区适配=" + _mesh.FitMode);
        }

        private void CycleShape()
        {
            SetShape((_shape + 1) % _shapes.Length);
            Emit("技能几何=" + _shapes[_shape]);
        }

        private void OnMapClick(GameObject go, PointerEventData evt)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _mesh.rectTransform,
                evt.position,
                evt.pressEventCamera,
                out Vector2 local
            );
            bool hit = _mesh.Raycast(evt.position, evt.pressEventCamera);
            _status.text = $"local=({local.x:F1},{local.y:F1}) hit={hit}";
            Emit($"地图点击 local=({local.x:F1},{local.y:F1})，{_mesh.RaycastMode} hit={hit}");
        }

        private void SetShape(int shape)
        {
            _shape = shape;
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            if (shape == 3)
            {
                v.AddRange(
                    new[]
                    {
                        new Vector3(-1, -.55f),
                        new Vector3(-1, .55f),
                        new Vector3(1, .55f),
                        new Vector3(1, -.55f),
                    }
                );
                uv.AddRange(new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right });
                t.AddRange(new[] { 0, 1, 2, 2, 3, 0 });
            }
            else
            {
                int segments = 32;
                float start = shape == 1 ? -55f : 0f;
                float span = shape == 1 ? 110f : 360f;
                float inner = shape == 2 ? .52f : 0f;
                if (inner <= 0f)
                {
                    v.Add(Vector3.zero);
                    uv.Add(Vector2.one * .5f);
                    for (int i = 0; i <= segments; i++)
                    {
                        float a = (start + span * i / segments) * Mathf.Deg2Rad;
                        Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        v.Add(p);
                        uv.Add(p * .5f + Vector2.one * .5f);
                        if (i > 0)
                            t.AddRange(new[] { 0, i, i + 1 });
                    }
                }
                else
                {
                    for (int i = 0; i <= segments; i++)
                    {
                        float a = span * i / segments * Mathf.Deg2Rad;
                        Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        v.Add(p);
                        v.Add(p * inner);
                        uv.Add(p * .5f + Vector2.one * .5f);
                        uv.Add(p * inner * .5f + Vector2.one * .5f);
                        if (i > 0)
                        {
                            int n = i * 2;
                            t.AddRange(new[] { n - 2, n, n - 1, n, n + 1, n - 1 });
                        }
                    }
                }
            }
            _mesh.SetGeometry(v, uv, t);
        }

        private static Texture2D GradientTexture()
        {
            Texture2D tex = new Texture2D(96, 64, TextureFormat.RGBA32, false);
            tex.name = "RuntimeSkillGradient";
            for (int y = 0; y < tex.height; y++)
            for (int x = 0; x < tex.width; x++)
                tex.SetPixel(
                    x,
                    y,
                    Color.Lerp(
                        new Color(.1f, .9f, 1f, .75f),
                        new Color(1f, .2f, .12f, .9f),
                        x / (float)(tex.width - 1)
                    )
                );
            tex.Apply();
            return tex;
        }
    }
}
