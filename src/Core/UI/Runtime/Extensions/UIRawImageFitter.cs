using UnityEngine;
using UnityEngine.UI;

namespace FRFrame
{
    public enum UIRawImageFitMode
    {
        /// <summary>
        /// 保持图片比例，完整显示图片，容器可能留白。
        /// </summary>
        Contain = 0,

        /// <summary>
        /// 保持图片比例，完全覆盖容器，图片可能被裁剪。
        /// </summary>
        Cover = 1,

        /// <summary>
        /// 强制填满容器，可能拉伸变形。
        /// </summary>
        Fill = 2,

        /// <summary>
        /// 使用纹理的原始尺寸。
        /// </summary>
        NativeSize = 3
    }

    [ExecuteAlways]
    [RequireComponent(typeof(RawImage))]
    public sealed class UIRawImageFitter : MonoBehaviour
    {
        [Header("Fit Settings")]
        [SerializeField]
        private UIRawImageFitMode _fitMode = UIRawImageFitMode.Cover;

        [Tooltip("Cover 模式下通过修改 uvRect 裁剪图片，而不是放大 RawImage。")]
        [SerializeField]
        private bool _useUvRect = true;

        [Header("Container")]
        [Tooltip("自动使用当前对象所在的根 Canvas 作为适配区域。启用后会忽略 Fit Container。")]
        [SerializeField]
        private bool _useRootCanvas = true;

        [Tooltip("需要适配的目标区域。Use Root Canvas 启用时会忽略此字段。")]
        [SerializeField]
        private RectTransform _fitContainer;

        [Header("Transform Options")]
        [Tooltip("自动将 RawImage 的中心对齐到目标容器中心。")]
        [SerializeField]
        private bool _alignToContainer = true;

        [Tooltip("刷新时将 RawImage 的 Local Scale 重置为 1。推荐启用。")]
        [SerializeField]
        private bool _resetLocalScale = true;

        [Header("Debug")]
        [Tooltip("强制每帧刷新。一般不需要启用，排查层级或布局问题时可以开启。")]
        [SerializeField]
        private bool _refreshEveryFrame = false;

        private RawImage _rawImage;
        private RectTransform _rectTransform;

        private readonly Vector3[] _containerWorldCorners = new Vector3[4];

        // 上一次状态，用于运行时检测变化。
        private Texture _lastTexture;
        private Vector2 _lastTextureSize;
        private Vector2 _lastContainerSize;
        private Vector3 _lastContainerWorldCenter;

        private UIRawImageFitMode _lastFitMode;
        private bool _lastUseUvRect;
        private bool _lastUseRootCanvas;
        private bool _lastAlignToContainer;
        private bool _lastResetLocalScale;

        private RectTransform _lastConfiguredContainer;
        private RectTransform _lastResolvedContainer;
        private Transform _lastTargetParent;

        private bool _refreshing;

        public UIRawImageFitMode FitMode
        {
            get => _fitMode;
            set
            {
                if (_fitMode == value)
                    return;

                _fitMode = value;
                Refresh();
            }
        }

        public bool UseUvRect
        {
            get => _useUvRect;
            set
            {
                if (_useUvRect == value)
                    return;

                _useUvRect = value;
                Refresh();
            }
        }

        public bool UseRootCanvas
        {
            get => _useRootCanvas;
            set
            {
                if (_useRootCanvas == value)
                    return;

                _useRootCanvas = value;
                Refresh();
            }
        }

        public RectTransform FitContainer
        {
            get => _fitContainer;
            set
            {
                if (_fitContainer == value)
                    return;

                _fitContainer = value;
                Refresh();
            }
        }

        public bool AlignToContainer
        {
            get => _alignToContainer;
            set
            {
                if (_alignToContainer == value)
                    return;

                _alignToContainer = value;
                Refresh();
            }
        }

        public bool ResetLocalScale
        {
            get => _resetLocalScale;
            set
            {
                if (_resetLocalScale == value)
                    return;

                _resetLocalScale = value;
                Refresh();
            }
        }

        public RawImage RawImage
        {
            get
            {
                CacheComponents();
                return _rawImage;
            }
        }

        private void Reset()
        {
            CacheComponents();

            _fitMode = UIRawImageFitMode.Cover;
            _useUvRect = true;
            _useRootCanvas = true;
            _alignToContainer = true;
            _resetLocalScale = true;

            Refresh();
        }

        private void Awake()
        {
            CacheComponents();
            Refresh();
        }

        private void OnEnable()
        {
            CacheComponents();
            Refresh();
        }

        private void Start()
        {
            Refresh();
        }

        private void Update()
        {
            CacheComponents();

            if (_rawImage == null || _rectTransform == null)
                return;

            if (_refreshEveryFrame || HasStateChanged())
                Refresh();
        }

        private void OnValidate()
        {
            CacheComponents();

            // Inspector 中修改 FitMode、UseUvRect 等字段时立即刷新。
            if (isActiveAndEnabled)
                Refresh();
        }

        private void OnTransformParentChanged()
        {
            Refresh();
        }

        private void OnRectTransformDimensionsChange()
        {
            if (!_refreshing)
                Refresh();
        }

        /// <summary>
        /// 设置 RawImage 的纹理并刷新。
        /// </summary>
        public void SetTexture(Texture texture, bool refresh = true)
        {
            CacheComponents();

            if (_rawImage == null)
                return;

            _rawImage.texture = texture;

            if (refresh)
                Refresh();
        }

        /// <summary>
        /// 强制重新计算 RawImage 的位置、尺寸和 UV。
        /// </summary>
        public void Refresh()
        {
            if (_refreshing)
                return;

            CacheComponents();

            if (_rawImage == null || _rectTransform == null)
                return;

            _refreshing = true;

            try
            {
                if (_resetLocalScale)
                    _rectTransform.localScale = Vector3.one;

                Texture texture = _rawImage.texture;
                RectTransform container = ResolveFitContainer();

                if (!TryGetContainerMetrics(
                        container,
                        out Vector2 containerSize,
                        out Vector3 containerWorldCenter))
                {
                    SaveCurrentState(
                        texture,
                        container,
                        Vector2.zero,
                        Vector3.zero);

                    return;
                }

                SaveCurrentState(
                    texture,
                    container,
                    containerSize,
                    containerWorldCenter);

                if (texture == null)
                {
                    ResetUvRect();

                    if (_alignToContainer)
                        AlignToWorldCenter(containerWorldCenter);

                    return;
                }

                float textureWidth = texture.width;
                float textureHeight = texture.height;

                if (textureWidth <= 0f || textureHeight <= 0f)
                    return;

                if (containerSize.x <= 0f || containerSize.y <= 0f)
                    return;

                Vector2 textureSize = new Vector2(
                    textureWidth,
                    textureHeight);

                switch (_fitMode)
                {
                    case UIRawImageFitMode.NativeSize:
                    {
                        ResetUvRect();
                        SetSizeAndAlign(
                            textureSize,
                            containerWorldCenter);

                        break;
                    }

                    case UIRawImageFitMode.Fill:
                    {
                        ResetUvRect();
                        SetSizeAndAlign(
                            containerSize,
                            containerWorldCenter);

                        break;
                    }

                    case UIRawImageFitMode.Contain:
                    {
                        ResetUvRect();

                        float scale = Mathf.Min(
                            containerSize.x / textureSize.x,
                            containerSize.y / textureSize.y);

                        Vector2 finalSize = textureSize * scale;

                        SetSizeAndAlign(
                            finalSize,
                            containerWorldCenter);

                        break;
                    }

                    case UIRawImageFitMode.Cover:
                    {
                        ApplyCover(
                            textureSize,
                            containerSize,
                            containerWorldCenter);

                        break;
                    }
                }
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void ApplyCover(
            Vector2 textureSize,
            Vector2 containerSize,
            Vector3 containerWorldCenter)
        {
            if (_useUvRect)
            {
                // RawImage 本身与容器大小一致，
                // 通过 UV 裁掉纹理多余部分。
                SetSizeAndAlign(
                    containerSize,
                    containerWorldCenter);

                float containerAspect =
                    containerSize.x / containerSize.y;

                float textureAspect =
                    textureSize.x / textureSize.y;

                if (textureAspect > containerAspect)
                {
                    // 图片比容器更宽，裁剪图片左右两侧。
                    float uvWidth =
                        containerAspect / textureAspect;

                    float uvX =
                        (1f - uvWidth) * 0.5f;

                    _rawImage.uvRect = new Rect(
                        uvX,
                        0f,
                        uvWidth,
                        1f);
                }
                else
                {
                    // 图片比容器更高，裁剪图片上下两侧。
                    float uvHeight =
                        textureAspect / containerAspect;

                    float uvY =
                        (1f - uvHeight) * 0.5f;

                    _rawImage.uvRect = new Rect(
                        0f,
                        uvY,
                        1f,
                        uvHeight);
                }
            }
            else
            {
                // 不裁剪 UV，直接将 RawImage 等比例放大，
                // 直到完全覆盖容器。
                ResetUvRect();

                float scale = Mathf.Max(
                    containerSize.x / textureSize.x,
                    containerSize.y / textureSize.y);

                Vector2 finalSize = textureSize * scale;

                SetSizeAndAlign(
                    finalSize,
                    containerWorldCenter);
            }
        }

        /// <summary>
        /// 获取实际使用的目标容器。
        /// </summary>
        private RectTransform ResolveFitContainer()
        {
            if (_useRootCanvas)
            {
                Canvas canvas = GetComponentInParent<Canvas>();

                if (canvas != null)
                {
                    Canvas rootCanvas = canvas.rootCanvas;

                    if (rootCanvas != null)
                        return rootCanvas.transform as RectTransform;

                    return canvas.transform as RectTransform;
                }
            }

            if (_fitContainer != null)
                return _fitContainer;

            if (_rectTransform != null)
            {
                RectTransform parent =
                    _rectTransform.parent as RectTransform;

                if (parent != null)
                    return parent;
            }

            return _rectTransform;
        }

        /// <summary>
        /// 将目标容器的世界空间范围转换到 RawImage 父节点的局部空间。
        ///
        /// 这样即使 FitContainer 和 RawImage 不在同一个父节点下，
        /// 中间存在缩放或多级 RectTransform，也能得到正确尺寸。
        /// </summary>
        private bool TryGetContainerMetrics(
            RectTransform container,
            out Vector2 sizeInTargetParent,
            out Vector3 worldCenter)
        {
            sizeInTargetParent = Vector2.zero;
            worldCenter = Vector3.zero;

            if (container == null || _rectTransform == null)
                return false;

            RectTransform targetParent =
                _rectTransform.parent as RectTransform;

            worldCenter = container.TransformPoint(
                container.rect.center);

            // 正常 UI 层级下，RawImage 应该有 RectTransform 父节点。
            if (targetParent == null)
            {
                sizeInTargetParent = container.rect.size;
                return sizeInTargetParent.x > 0f &&
                       sizeInTargetParent.y > 0f;
            }

            container.GetWorldCorners(_containerWorldCorners);

            Vector3 min = new Vector3(
                float.PositiveInfinity,
                float.PositiveInfinity,
                0f);

            Vector3 max = new Vector3(
                float.NegativeInfinity,
                float.NegativeInfinity,
                0f);

            for (int i = 0; i < _containerWorldCorners.Length; i++)
            {
                Vector3 localPoint =
                    targetParent.InverseTransformPoint(
                        _containerWorldCorners[i]);

                min.x = Mathf.Min(min.x, localPoint.x);
                min.y = Mathf.Min(min.y, localPoint.y);

                max.x = Mathf.Max(max.x, localPoint.x);
                max.y = Mathf.Max(max.y, localPoint.y);
            }

            sizeInTargetParent = new Vector2(
                max.x - min.x,
                max.y - min.y);

            return sizeInTargetParent.x > 0f &&
                   sizeInTargetParent.y > 0f;
        }

        private void SetSizeAndAlign(
            Vector2 size,
            Vector3 containerWorldCenter)
        {
            SetSize(size);

            if (_alignToContainer)
                AlignToWorldCenter(containerWorldCenter);
        }

        private void SetSize(Vector2 size)
        {
            if (_rectTransform == null)
                return;

            _rectTransform.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal,
                size.x);

            _rectTransform.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                size.y);
        }

        /// <summary>
        /// 对齐的是 RawImage 的几何中心，而不仅仅是 Pivot。
        /// 因此即使 Pivot 不是 0.5，也可以正确居中。
        /// </summary>
        private void AlignToWorldCenter(Vector3 targetWorldCenter)
        {
            if (_rectTransform == null)
                return;

            Vector3 currentWorldCenter =
                _rectTransform.TransformPoint(
                    _rectTransform.rect.center);

            Vector3 offset =
                targetWorldCenter - currentWorldCenter;

            _rectTransform.position += offset;
        }

        private void ResetUvRect()
        {
            if (_rawImage != null)
                _rawImage.uvRect = new Rect(0f, 0f, 1f, 1f);
        }

        private bool HasStateChanged()
        {
            if (_rawImage == null || _rectTransform == null)
                return true;

            Texture texture = _rawImage.texture;

            Vector2 textureSize = texture != null
                ? new Vector2(texture.width, texture.height)
                : Vector2.zero;

            RectTransform resolvedContainer =
                ResolveFitContainer();

            TryGetContainerMetrics(
                resolvedContainer,
                out Vector2 containerSize,
                out Vector3 containerWorldCenter);

            if (_lastTexture != texture)
                return true;

            // RenderTexture 可能还是同一个对象，但内部尺寸发生了变化。
            if (!Approximately(_lastTextureSize, textureSize))
                return true;

            if (!Approximately(_lastContainerSize, containerSize))
                return true;

            if (!Approximately(
                    _lastContainerWorldCenter,
                    containerWorldCenter))
            {
                return true;
            }

            if (_lastFitMode != _fitMode)
                return true;

            if (_lastUseUvRect != _useUvRect)
                return true;

            if (_lastUseRootCanvas != _useRootCanvas)
                return true;

            if (_lastAlignToContainer != _alignToContainer)
                return true;

            if (_lastResetLocalScale != _resetLocalScale)
                return true;

            if (_lastConfiguredContainer != _fitContainer)
                return true;

            if (_lastResolvedContainer != resolvedContainer)
                return true;

            if (_lastTargetParent != _rectTransform.parent)
                return true;

            return false;
        }

        private void SaveCurrentState(
            Texture texture,
            RectTransform resolvedContainer,
            Vector2 containerSize,
            Vector3 containerWorldCenter)
        {
            _lastTexture = texture;

            _lastTextureSize = texture != null
                ? new Vector2(texture.width, texture.height)
                : Vector2.zero;

            _lastContainerSize = containerSize;
            _lastContainerWorldCenter = containerWorldCenter;

            _lastFitMode = _fitMode;
            _lastUseUvRect = _useUvRect;
            _lastUseRootCanvas = _useRootCanvas;
            _lastAlignToContainer = _alignToContainer;
            _lastResetLocalScale = _resetLocalScale;

            _lastConfiguredContainer = _fitContainer;
            _lastResolvedContainer = resolvedContainer;
            _lastTargetParent = _rectTransform.parent;
        }

        private void CacheComponents()
        {
            if (_rawImage == null)
                _rawImage = GetComponent<RawImage>();

            if (_rectTransform == null)
                _rectTransform = transform as RectTransform;
        }

        private static bool Approximately(Vector2 a, Vector2 b)
        {
            return Mathf.Approximately(a.x, b.x) &&
                   Mathf.Approximately(a.y, b.y);
        }

        private static bool Approximately(Vector3 a, Vector3 b)
        {
            const float tolerance = 0.001f;

            return (a - b).sqrMagnitude <=
                   tolerance * tolerance;
        }
    }
}