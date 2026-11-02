using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace FRFrame
{
    public sealed class UIMaskService
    {
        private readonly Dictionary<UIGroupLayer, MaskView> _masks = new Dictionary<UIGroupLayer, MaskView>();
        private UIManager _uiManager;

        public void Initialize(UIManager uiManager, IReadOnlyDictionary<UIGroupLayer, UIGroup> groups)
        {
            _uiManager = uiManager;
            for (int layer = (int)UIGroupLayer.Background; layer <= (int)UIGroupLayer.System; layer++)
            {
                UIGroupLayer groupLayer = (UIGroupLayer)layer;
                Transform parent = groups != null && groups.TryGetValue(groupLayer, out UIGroup group) ? group.Root : null;
                if (parent == null)
                    continue;

                GameObject go = CreateStretchNode($"{groupLayer}Mask", parent, typeof(Canvas), typeof(GraphicRaycaster), typeof(Image), typeof(Button));

                Canvas canvas = go.GetComponent<Canvas>();
                canvas.overrideSorting = true;

                Image image = go.GetComponent<Image>();
                image.color = Color.clear;
                image.raycastTarget = true;

                GameObject backdropGo = CreateStretchNode("Backdrop", go.transform, typeof(CanvasRenderer), typeof(RawImage));
                RawImage backdrop = backdropGo.GetComponent<RawImage>();
                backdrop.raycastTarget = false;
                backdrop.color = Color.white;

                GameObject tintGo = CreateStretchNode("Tint", go.transform, typeof(CanvasRenderer), typeof(RawImage));
                RawImage tint = tintGo.GetComponent<RawImage>();
                tint.texture = Texture2D.whiteTexture;
                tint.raycastTarget = false;

                Button button = go.GetComponent<Button>();
                MaskView view = new MaskView(go, canvas, image, backdrop, tint, button);
                _masks[groupLayer] = view;
                go.SetActive(false);
            }
        }

        public UniTask PrepareBlurAsync(UIForm target)
        {
            if (target?.Group == null || !target.UseMask || target.MaskMode != UIMaskMode.Blur)
                return UniTask.CompletedTask;

            return _masks.TryGetValue(target.Group.Layer, out MaskView view)
                ? view.PrepareBlurAsync(target)
                : UniTask.CompletedTask;
        }

        public void Refresh(IReadOnlyDictionary<UIGroupLayer, UIGroup> groups)
        {
            foreach (var pair in _masks)
            {
                if (!groups.TryGetValue(pair.Key, out UIGroup group))
                {
                    pair.Value.Hide();
                    continue;
                }

                UIForm target = group.GetTopMaskForm();
                if (target == null)
                {
                    pair.Value.Hide();
                    continue;
                }

                pair.Value.Show(target, _uiManager);
            }
        }

        public void HideAll()
        {
            foreach (var pair in _masks)
                pair.Value.Hide();
        }

        public void Dispose()
        {
            foreach (var pair in _masks)
                pair.Value.Dispose();
            _masks.Clear();
        }

        public List<UIMaskSnapshot> CreateSnapshots()
        {
            List<UIMaskSnapshot> snapshots = new List<UIMaskSnapshot>();
            foreach (var pair in _masks)
                snapshots.Add(pair.Value.CreateSnapshot(pair.Key));
            return snapshots;
        }

        private static GameObject CreateStretchNode(string name, Transform parent, params System.Type[] components)
        {
            GameObject go = new GameObject(name, components);
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            RectTransform rect = go.transform as RectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return go;
        }

        private sealed class MaskView
        {
            private const int BlurDownsample = 2;
            private const int BlurIterations = 2;
            private static readonly int ParameterId = Shader.PropertyToID("_Parameter");
            private static readonly int DirectionId = Shader.PropertyToID("_Direction");
            private static Shader _blurShader;
            private static bool _blurShaderLoaded;

            private readonly GameObject _go;
            private readonly Canvas _canvas;
            private readonly Image _image;
            private readonly RawImage _backdrop;
            private readonly RawImage _tint;
            private readonly Button _button;
            private UIForm _target;
            private UIManager _uiManager;
            private Material _blurMaterial;
            private RenderTexture _blurTexture;

            public MaskView(GameObject go, Canvas canvas, Image image, RawImage backdrop, RawImage tint, Button button)
            {
                _go = go;
                _canvas = canvas;
                _image = image;
                _backdrop = backdrop;
                _tint = tint;
                _button = button;
                _button.onClick.AddListener(OnClick);
            }

            public async UniTask PrepareBlurAsync(UIForm target)
            {
                _target = target;
                // The capture must exclude this mask. Show() can be reached later in
                // the same open flow, so keep the mask hidden until the frame-end grab.
                _go.SetActive(false);
                await UniTask.WaitForEndOfFrame();
                CaptureBlur();
            }

            public void Show(UIForm target, UIManager uiManager)
            {
                _target = target;
                _uiManager = uiManager;

                _canvas.sortingOrder = Mathf.Max(0, target.SortingOrder - 1);
                _tint.color = GetColor(target);
                _go.SetActive(true);

                bool useBlur = target.MaskMode == UIMaskMode.Blur;
                if (!useBlur)
                {
                    _backdrop.gameObject.SetActive(false);
                    ReleaseBlurTexture();
                    return;
                }

                // PrepareBlurAsync performs the frame-end capture before this object is
                // shown. Avoid a synchronous fallback here: it would capture the mask.
                _backdrop.gameObject.SetActive(_blurTexture != null);
            }

            public void Hide()
            {
                _target = null;
                _go.SetActive(false);
                ReleaseBlurTexture();
            }

            public void Dispose()
            {
                ReleaseBlurTexture();
                if (_blurMaterial != null)
                {
                    Object.Destroy(_blurMaterial);
                    _blurMaterial = null;
                }
            }

            public UIMaskSnapshot CreateSnapshot(UIGroupLayer layer)
            {
                return new UIMaskSnapshot
                {
                    Layer = layer,
                    Active = _go != null && _go.activeSelf,
                    SortingOrder = _canvas != null ? _canvas.sortingOrder : 0,
                    TargetFormId = _target != null ? _target.FormId : null,
                    TargetSerialId = _target != null ? _target.SerialId : 0,
                    MaskMode = _target != null ? _target.MaskMode : UIMaskMode.None,
                    Color = _tint != null ? _tint.color : Color.clear,
                    CloseOnMaskClick = _target != null && _target.CloseOnMaskClick
                };
            }

            private void CaptureBlur()
            {
                ReleaseBlurTexture();
                Shader shader = GetBlurShader();
                if (shader == null)
                    return;

                if (_blurMaterial == null)
                    _blurMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };

                int width = Mathf.Max(1, Screen.width / BlurDownsample);
                int height = Mathf.Max(1, Screen.height / BlurDownsample);
                Texture2D screenshot = null;
                RenderTexture ping = null;
                RenderTexture pong = null;
                RenderTexture result = null;
                bool previousSrgbWrite = GL.sRGBWrite;
                try
                {
                    screenshot = ScreenCapture.CaptureScreenshotAsTexture();
                    if (screenshot == null)
                        return;

                    ping = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
                    pong = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
                    screenshot.wrapMode = TextureWrapMode.Clamp;
                    screenshot.filterMode = FilterMode.Bilinear;
                    ping.wrapMode = TextureWrapMode.Clamp;
                    ping.filterMode = FilterMode.Bilinear;
                    pong.wrapMode = TextureWrapMode.Clamp;
                    pong.filterMode = FilterMode.Bilinear;
                    GL.sRGBWrite = false;
                    _blurMaterial.SetVector(ParameterId, Vector4.one);
                    _blurMaterial.SetVector(DirectionId, Vector2.zero);
                    Graphics.Blit(screenshot, ping, _blurMaterial, 0);
                    for (int i = 0; i < BlurIterations; i++)
                    {
                        _blurMaterial.SetVector(ParameterId, new Vector4(1f + i, 0f, 0f, 0f));
                        _blurMaterial.SetVector(DirectionId, Vector2.right);
                        Graphics.Blit(ping, pong, _blurMaterial);
                        _blurMaterial.SetVector(DirectionId, Vector2.up);
                        Graphics.Blit(pong, ping, _blurMaterial);
                    }

                    result = new RenderTexture(width, height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.sRGB)
                    {
                        name = "UI Backdrop Blur",
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp
                    };
                    if (!result.Create())
                        return;

                    // The source already contains display-encoded screenshot values.
                    // Store those values unchanged in the sRGB-tagged UI texture.
                    GL.sRGBWrite = false;
                    Graphics.Blit(ping, result);
                    _blurTexture = result;
                    result = null;
                    _backdrop.texture = _blurTexture;
                }
                finally
                {
                    GL.sRGBWrite = previousSrgbWrite;
                    if (screenshot != null)
                        Object.Destroy(screenshot);
                    if (ping != null)
                        RenderTexture.ReleaseTemporary(ping);
                    if (pong != null)
                        RenderTexture.ReleaseTemporary(pong);
                    if (result != null)
                    {
                        result.Release();
                        Object.Destroy(result);
                    }
                }
            }

            private static Shader GetBlurShader()
            {
                if (_blurShaderLoaded)
                    return _blurShader;

                _blurShaderLoaded = true;
                _blurShader = Resources.Load<Shader>("UIBackdropBlur");
                if (_blurShader == null)
                    FRLog.Warning("UI blur mask shader was not found.");
                return _blurShader;
            }

            private void ReleaseBlurTexture()
            {
                _backdrop.texture = null;
                if (_blurTexture == null)
                    return;

                _blurTexture.Release();
                Object.Destroy(_blurTexture);
                _blurTexture = null;
            }

            private void OnClick()
            {
                if (_target == null || _target.Config == null)
                    return;

                if (_target.CloseOnMaskClick)
                    _uiManager?.Close(_target);
            }

            private static Color GetColor(UIForm form)
            {
                if (form == null)
                    return Color.clear;

                switch (form.MaskMode)
                {
                    case UIMaskMode.Dim:
                        return new Color(0f, 0f, 0f, 0.6f);
                    case UIMaskMode.CustomColor:
                        return form.MaskColor;
                    case UIMaskMode.Blur:
                        Color blurColor = form.MaskColor;
                        if (QualitySettings.activeColorSpace == ColorSpace.Linear && blurColor.a > 0f)
                            blurColor.a = 1f - Mathf.Pow(1f - blurColor.a, 2.6f);
                        return blurColor;
                    default:
                        return Color.clear;
                }
            }
        }
    }
}
