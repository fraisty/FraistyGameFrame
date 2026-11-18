using System;
using UnityEngine;
using UnityEngine.UI;

namespace FRFrame
{
    /// <summary>One presentation sample. Hidden and unbound objects can use the default value.</summary>
    public readonly struct WorldHealthSnapshot
    {
        public readonly float Current, Maximum, Shield;
        public readonly bool Visible;

        public WorldHealthSnapshot(float current, float maximum, float shield = 0, bool visible = true)
        {
            Current = current; Maximum = maximum; Shield = shield; Visible = visible;
        }
    }

    /// <summary>Camera-facing world-space UI with no dependency on a game or health authority.
    /// Keep the view under a neutral presentation root and pass the actor as a separate anchor.</summary>
    [RequireComponent(typeof(Canvas),typeof(RectTransform))]
    public sealed class WorldHealthBar : MonoBehaviour, IPoolRecycleHandler
    {
        [SerializeField] private Image background, health, shield;
        [SerializeField] private Text numbers;
        [SerializeField] private Vector2 size = new Vector2(100,8);
        [SerializeField, Min(.001f)] private float worldScale = .01f;
        [SerializeField] private Vector3 worldOffset;
        [SerializeField] private bool hideAtFullHealth;
        [SerializeField, Min(0)] private float response = 16;
        [SerializeField, Min(0)] private float followResponse;

        private Canvas _canvas;
        private float _healthFill, _shieldFill;
        private bool _hasSample;
        private Vector3 _shownPoint;
        private int _generation;
        private int _lastHp, _lastMax;

        /// <summary>Bind this view or a pooled prefab instance. Rebinding immediately invalidates the old handle.</summary>
        public Lease Bind(Transform anchor = null)
        {
            ResetBinding();
            return new Lease(this,_generation,anchor);
        }

        public sealed class Lease : IDisposable
        {
            private readonly WorldHealthBar _view;
            private readonly int _generation;
            private readonly Transform _anchor;

            internal Lease(WorldHealthBar view,int generation,Transform anchor)
            { _view=view; _generation=generation; _anchor=anchor; }

            /// <summary>Read the anchor position and render the supplied authoritative values.</summary>
            public void Present(WorldHealthSnapshot snapshot, Camera camera, float viewDelta)
            {
                if (_anchor && _anchor.gameObject.activeInHierarchy)
                    Present(snapshot,_anchor.position,camera,viewDelta);
                else
                    Present(default,Vector3.zero,camera,viewDelta);
            }

            /// <summary>Use an explicit world point, for example for a snapshot-driven stage avatar.</summary>
            public void Present(WorldHealthSnapshot snapshot, Vector3 point, Camera camera, float viewDelta)
            {
                if (_view && _view._generation == _generation && _view.isActiveAndEnabled)
                    _view.Render(snapshot,point,camera,viewDelta);
            }

            public void Dispose()
            {
                if (_view && _view._generation == _generation) _view.ResetBinding();
            }
        }

        private void Awake()
        {
            _canvas = GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.enabled = false;
        }

        private void OnDisable() => ResetBinding();
        public void OnRecycleToPool() => ResetBinding();

        private void ResetBinding()
        {
            _generation++;
            _hasSample = false;
            _healthFill = _shieldFill = 0;
            _lastHp = _lastMax = -1;
            if (_canvas) { _canvas.enabled = false; _canvas.worldCamera = null; }
            if (numbers) numbers.text = "";
        }

        private void Render(WorldHealthSnapshot snapshot,Vector3 point,Camera camera,float viewDelta)
        {
            point += worldOffset;
            bool show = snapshot.Visible && snapshot.Maximum > 0 && snapshot.Current > 0 &&
                float.IsFinite(snapshot.Current) && float.IsFinite(snapshot.Maximum) && float.IsFinite(snapshot.Shield) &&
                (!hideAtFullHealth || snapshot.Current < snapshot.Maximum || snapshot.Shield > 0) &&
                camera && camera.isActiveAndEnabled;
            if (show)
            {
                Vector3 viewport = camera.WorldToViewportPoint(point);
                show = viewport.z >= camera.nearClipPlane && viewport.z <= camera.farClipPlane &&
                    viewport.x >= 0 && viewport.x <= 1 && viewport.y >= 0 && viewport.y <= 1;
            }
            _canvas.enabled = show;
            if (!show) { _hasSample = false; return; }
            if (_canvas.worldCamera != camera) _canvas.worldCamera = camera;
            float follow = !_hasSample || followResponse == 0 ? 1 : 1 - Mathf.Exp(-followResponse * viewDelta);
            _shownPoint = Vector3.Lerp(_shownPoint,point,follow);
            transform.SetPositionAndRotation(_shownPoint,camera.transform.rotation);
            transform.localScale = Vector3.one * worldScale;
            float ease = !_hasSample || response == 0 ? 1 : 1 - Mathf.Exp(-response * viewDelta);
            _hasSample = true;
            _healthFill = Mathf.Lerp(_healthFill,Mathf.Clamp01(snapshot.Current / snapshot.Maximum),ease);
            _shieldFill = Mathf.Lerp(_shieldFill,Mathf.Clamp01(snapshot.Shield / snapshot.Maximum),ease);
            SetBand(background,size.x,size.y,0);
            SetBand(health,size.x * _healthFill,size.y,0);
            SetBand(shield,size.x * _shieldFill,size.y * .45f,size.y * .72f);
            if (numbers)
            {
                int hp = Mathf.CeilToInt(snapshot.Current), max = Mathf.CeilToInt(snapshot.Maximum);
                if (hp != _lastHp || max != _lastMax)
                { _lastHp=hp; _lastMax=max; numbers.text=$"{hp}/{max}"; }
            }
        }

        private void SetBand(Image band,float width,float height,float y)
        {
            if (!band) return;
            band.enabled = width > .001f;
            band.rectTransform.anchoredPosition = new Vector2(-size.x / 2,y);
            band.rectTransform.sizeDelta = new Vector2(width,height);
        }

        /// <summary>Small default view used by hosts without a configured prefab. The scene owns its lifetime.</summary>
        public static WorldHealthBar CreateDefault(Transform viewRoot,int layer,float width = 1)
        {
            var root = new GameObject("World Health Bar",typeof(RectTransform),typeof(Canvas),typeof(WorldHealthBar));
            root.layer = layer;
            root.transform.SetParent(viewRoot,false);
            var bar = root.GetComponent<WorldHealthBar>();
            bar.worldScale = width / bar.size.x;
            bar.background = AddBand(root.transform,layer,"Background",new Color(.04f,.07f,.11f,.9f));
            bar.health = AddBand(root.transform,layer,"Health",new Color(.23f,.85f,.60f));
            bar.shield = AddBand(root.transform,layer,"Shield",new Color(.28f,.72f,1));
            return bar;
        }

        private static Image AddBand(Transform parent,int layer,string name,Color color)
        {
            var child = new GameObject(name,typeof(RectTransform),typeof(Image));
            child.layer = layer;
            child.transform.SetParent(parent,false);
            var image = child.GetComponent<Image>();
            image.raycastTarget = false;
            image.color = color;
            image.rectTransform.pivot = new Vector2(0,.5f);
            return image;
        }
    }
}
