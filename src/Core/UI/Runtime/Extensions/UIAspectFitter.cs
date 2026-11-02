using UnityEngine;
using UnityEngine.UI;

namespace FRFrame
{
    public enum UIAspectFitMode
    {
        Contain = 0,
        Cover = 1,
        FitWidth = 2,
        FitHeight = 3,
        Fill = 4
    }

    public enum UIAspectAlignment
    {
        TopLeft = 0,
        TopCenter = 1,
        TopRight = 2,
        MiddleLeft = 3,
        MiddleCenter = 4,
        MiddleRight = 5,
        BottomLeft = 6,
        BottomCenter = 7,
        BottomRight = 8
    }

    public enum UIAspectFitStrategy
    {
        ResizeTarget = 0,
        ScaleHierarchy = 1
    }

    [ExecuteAlways]
    public sealed class UIAspectFitter : MonoBehaviour
    {
        [SerializeField] private RectTransform _container;
        [SerializeField] private RectTransform _target;
        [SerializeField] private UIAspectFitMode _fitMode = UIAspectFitMode.Contain;
        [SerializeField] private UIAspectFitStrategy _strategy = UIAspectFitStrategy.ScaleHierarchy;
        [SerializeField] private UIAspectAlignment _alignment = UIAspectAlignment.MiddleCenter;
        [SerializeField] private Vector2 _referenceSize;
        [SerializeField] private bool _useGraphicSource = true;
        [SerializeField] private bool _useTargetSizeAsReference = true;
        [SerializeField] private bool _snapToAlignment = true;
        [SerializeField, HideInInspector] private Vector2 _initialTargetSize;
        [SerializeField, HideInInspector] private Vector3 _initialTargetScale;

        private RectTransform _rectTransform;
        private Vector2 _lastContainerSize;

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

        private void Update()
        {
            CacheComponents();
            Vector2 size = _container != null ? _container.rect.size : Vector2.zero;
            if (_lastContainerSize != size)
                Refresh();
        }

        private void OnRectTransformDimensionsChange()
        {
            Refresh();
        }

        private void OnValidate()
        {
            CacheComponents();
            Refresh();
        }

        public void SetReferenceSize(Vector2 size)
        {
            _referenceSize = size;
            Refresh();
        }

        public void SetAlignment(UIAspectAlignment alignment)
        {
            _alignment = alignment;
            Refresh();
        }

        public void Refresh()
        {
            CacheComponents();
            if (_container == null || _target == null)
                return;

            _lastContainerSize = _container.rect.size;
            Vector2 reference = GetReferenceSize();
            if (_lastContainerSize.x <= 0f || _lastContainerSize.y <= 0f || reference.x <= 0f || reference.y <= 0f)
                return;

            if (_strategy == UIAspectFitStrategy.ScaleHierarchy)
                ScaleHierarchy(reference);
            else
                ResizeTarget(reference);
        }

        private void ScaleHierarchy(Vector2 reference)
        {
            float widthScale = _lastContainerSize.x / reference.x;
            float heightScale = _lastContainerSize.y / reference.y;
            float scale;

            switch (_fitMode)
            {
                case UIAspectFitMode.Cover:
                    scale = Mathf.Max(widthScale, heightScale);
                    break;
                case UIAspectFitMode.FitWidth:
                    scale = widthScale;
                    break;
                case UIAspectFitMode.FitHeight:
                    scale = heightScale;
                    break;
                case UIAspectFitMode.Fill:
                    _target.localScale = new Vector3(
                        _initialTargetScale.x * widthScale,
                        _initialTargetScale.y * heightScale,
                        _initialTargetScale.z);
                    ApplyAlignment();
                    return;
                default:
                    scale = Mathf.Min(widthScale, heightScale);
                    break;
            }

            _target.localScale = new Vector3(
                _initialTargetScale.x * scale,
                _initialTargetScale.y * scale,
                _initialTargetScale.z);
            ApplyAlignment();
        }

        private void ResizeTarget(Vector2 reference)
        {
            if (_fitMode == UIAspectFitMode.Fill)
            {
                _target.anchorMin = Vector2.zero;
                _target.anchorMax = Vector2.one;
                _target.offsetMin = Vector2.zero;
                _target.offsetMax = Vector2.zero;
                return;
            }

            float containerAspect = _lastContainerSize.x / _lastContainerSize.y;
            float referenceAspect = reference.x / reference.y;
            Vector2 size = _lastContainerSize;

            switch (_fitMode)
            {
                case UIAspectFitMode.Cover:
                    if (referenceAspect > containerAspect)
                        size.x = _lastContainerSize.y * referenceAspect;
                    else
                        size.y = _lastContainerSize.x / referenceAspect;
                    break;
                case UIAspectFitMode.FitWidth:
                    size.y = _lastContainerSize.x / referenceAspect;
                    break;
                case UIAspectFitMode.FitHeight:
                    size.x = _lastContainerSize.y * referenceAspect;
                    break;
                default:
                    if (referenceAspect > containerAspect)
                        size.y = _lastContainerSize.x / referenceAspect;
                    else
                        size.x = _lastContainerSize.y * referenceAspect;
                    break;
            }

            ApplyAlignment();
            _target.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size.x);
            _target.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size.y);
        }

        private void ApplyAlignment()
        {
            if (!_snapToAlignment)
                return;

            Vector2 alignment = GetAlignment();
            _target.anchorMin = alignment;
            _target.anchorMax = alignment;
            _target.pivot = alignment;
            _target.anchoredPosition = Vector2.zero;
        }

        private Vector2 GetAlignment()
        {
            switch (_alignment)
            {
                case UIAspectAlignment.TopLeft:
                    return new Vector2(0f, 1f);
                case UIAspectAlignment.TopCenter:
                    return new Vector2(0.5f, 1f);
                case UIAspectAlignment.TopRight:
                    return Vector2.one;
                case UIAspectAlignment.MiddleLeft:
                    return new Vector2(0f, 0.5f);
                case UIAspectAlignment.MiddleRight:
                    return new Vector2(1f, 0.5f);
                case UIAspectAlignment.BottomLeft:
                    return Vector2.zero;
                case UIAspectAlignment.BottomCenter:
                    return new Vector2(0.5f, 0f);
                case UIAspectAlignment.BottomRight:
                    return new Vector2(1f, 0f);
                default:
                    return new Vector2(0.5f, 0.5f);
            }
        }

        private Vector2 GetReferenceSize()
        {
            if (_useGraphicSource && _target != null)
            {
                Image image = _target.GetComponent<Image>();
                if (image != null && image.sprite != null)
                    return image.sprite.rect.size;

                RawImage rawImage = _target.GetComponent<RawImage>();
                if (rawImage != null && rawImage.texture != null)
                    return new Vector2(rawImage.texture.width, rawImage.texture.height);
            }

            if (_referenceSize.x > 0f && _referenceSize.y > 0f)
                return _referenceSize;

            return _useTargetSizeAsReference ? _initialTargetSize : Vector2.zero;
        }

        private void CacheComponents()
        {
            if (_rectTransform == null)
                _rectTransform = transform as RectTransform;
            if (_target == null)
                _target = _rectTransform;
            if (_container == null && _target != null)
                _container = _target.parent as RectTransform;
            if (_target != null && (_initialTargetSize.x <= 0f || _initialTargetSize.y <= 0f))
            {
                _initialTargetSize = _target.rect.size;
                _initialTargetScale = _target.localScale;
            }
        }
    }
}
