using System;
using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

namespace FRFrame
{
    public enum SafeAreaAspectPreset
    {
        None,
        Custom,
        Aspect16x9,
        Aspect21x9,
        Aspect4x3,
        Screen
    }

    [Serializable]
    public sealed class UISafeAreaChangedEvent : UnityEvent<Rect>
    {
    }

    [RequireComponent(typeof(RectTransform))]
    public sealed class UISafeAreaAdapter : MonoBehaviour
    {
        [Header("安全区域适配方向")]
        public bool fitLeft = true;
        public bool fitRight = true;
        public bool fitTop = true;
        public bool fitBottom = true;

        [Header("应用设置")]
        public bool onlyApplyOnMobile;
        public bool applyOnEnable = true;
        public bool listenScreenChange = true;

        [Tooltip("安全区域之外额外添加的像素边距")]
        public RectOffset extraPadding = new RectOffset();

        [Header("宽高比设置")]
        [Tooltip("将当前 RectTransform 限制在安全区内的设计比例。None 仅应用安全区。")]
        public SafeAreaAspectPreset aspectPreset = SafeAreaAspectPreset.None;

        [Tooltip("宽高比，例如 16:9 填写 1.7777778")]
        [Min(0.0001f)]
        [FormerlySerializedAs("keepRatioValue")]
        public float customAspect = 16f / 9f;

        [Tooltip("固定比例模式下仍覆盖整个父 Canvas 的元素，例如背景图。")]
        [FormerlySerializedAs("equalRatioImgs")]
        public RectTransform[] fullScreenElements;

        [Header("事件")]
        public UISafeAreaChangedEvent onSafeAreaChanged =
            new UISafeAreaChangedEvent();

        private RectTransform _rectTransform;
        private Rect _lastSafeArea;
        private Rect _lastAvailableArea;
        private Vector2Int _lastScreenSize;
        private bool _hasApplied;
        private DrivenRectTransformTracker _drivenTracker;

        private void Awake()
        {
            CacheRectTransform();
        }

        private void OnEnable()
        {
            CacheRectTransform();

            if (applyOnEnable)
                Apply();
        }

        private void OnDisable()
        {
            _drivenTracker.Clear();
            _hasApplied = false;
            _lastAvailableArea = default;
        }

        private void Update()
        {
            if (!listenScreenChange)
                return;

            if (!ShouldApply())
                return;

            Rect safeArea = GetSafeArea();
            CacheRectTransform();
            RectTransform parent = _rectTransform != null
                ? _rectTransform.parent as RectTransform
                : null;
            Rect availableArea = GetAvailableLocalArea(parent);
            Vector2Int screenSize =
                new Vector2Int(Screen.width, Screen.height);

            if (!_hasApplied ||
                !Approximately(_lastSafeArea, safeArea) ||
                !Approximately(_lastAvailableArea, availableArea) ||
                _lastScreenSize != screenSize)
            {
                Apply();
            }
        }

        private void OnValidate()
        {
            if (Application.isPlaying && isActiveAndEnabled)
                Apply();
        }

        /// <summary>
        /// 手动应用安全区域和宽高比。
        /// </summary>
        public void Apply()
        {
            if (!ShouldApply())
                return;

            CacheRectTransform();

            if (_rectTransform == null)
                return;

            Rect safeArea = GetSafeArea();
            Rect paddedArea = ApplyExtraPadding(safeArea);

            RectTransform parent = _rectTransform.parent as RectTransform;
            Rect availableArea = GetAvailableLocalArea(parent);
            Rect paddedLocalArea = ScreenRectToLocalRect(parent, paddedArea);
            Rect targetArea = Intersect(paddedLocalArea, availableArea);
            if (targetArea.width <= 0f || targetArea.height <= 0f)
                targetArea = availableArea;

            float targetAspect = GetConfiguredAspect();
            if (targetAspect > 0f && UsesOwnAspectRatio())
                targetArea = FitInside(targetArea, targetAspect);

            // A fixed design ratio must remain intact; the ratio rectangle is
            // already computed inside the safe area. Fit flags remain the
            // legacy behavior for adapters that do not constrain their own
            // aspect ratio (for example blur panels).
            if (!UsesOwnAspectRatio())
                ApplyFitFlags(ref targetArea, availableArea);
            ApplyLocalArea(_rectTransform, targetArea);
            ApplyFullScreenElements(parent, availableArea);

            // 让当前 RectTransform 精确贴合锚点区域。
            _rectTransform.offsetMin = Vector2.zero;
            _rectTransform.offsetMax = Vector2.zero;

            bool changed =
                !_hasApplied ||
                !Approximately(_lastSafeArea, safeArea) ||
                _lastScreenSize.x != Screen.width ||
                _lastScreenSize.y != Screen.height;

            _lastSafeArea = safeArea;
            _lastAvailableArea = availableArea;
            _lastScreenSize =
                new Vector2Int(Screen.width, Screen.height);
            _hasApplied = true;

            if (changed)
                onSafeAreaChanged?.Invoke(safeArea);
        }

        private bool UsesOwnAspectRatio()
        {
            return aspectPreset != SafeAreaAspectPreset.None && GetConfiguredAspect() > 0f;
        }

        private float GetConfiguredAspect()
        {
            switch (aspectPreset)
            {
                case SafeAreaAspectPreset.Aspect16x9:
                    return 16f / 9f;
                case SafeAreaAspectPreset.Aspect21x9:
                    return 21f / 9f;
                case SafeAreaAspectPreset.Aspect4x3:
                    return 4f / 3f;
                case SafeAreaAspectPreset.Screen:
                    Rect screenArea = GetAvailableScreenArea();
                    return screenArea.height > 0f ? screenArea.width / screenArea.height : 0f;
                case SafeAreaAspectPreset.Custom:
                    return customAspect;
                default:
                    return 0f;
            }
        }

        private Rect GetAvailableScreenArea()
        {
            RectTransform parent = _rectTransform.parent as RectTransform;
            if (parent == null)
                return new Rect(0f, 0f, Mathf.Max(1f, Screen.width), Mathf.Max(1f, Screen.height));

            Canvas canvas = parent.GetComponentInParent<Canvas>();
            Camera camera = GetEventCamera(canvas);
            Vector3[] corners = new Vector3[4];
            parent.GetWorldCorners(corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static Rect GetAvailableLocalArea(RectTransform parent)
        {
            if (parent != null)
                return parent.rect;

            return new Rect(0f, 0f, Mathf.Max(1f, Screen.width), Mathf.Max(1f, Screen.height));
        }

        private static Rect ScreenRectToLocalRect(RectTransform target, Rect screenArea)
        {
            if (target == null)
                return screenArea;

            Canvas canvas = target.GetComponentInParent<Canvas>();
            Camera camera = GetEventCamera(canvas);
            Vector2 localMin;
            Vector2 localMax;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                target, new Vector2(screenArea.xMin, screenArea.yMin), camera, out localMin);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                target, new Vector2(screenArea.xMax, screenArea.yMax), camera, out localMax);
            return Rect.MinMaxRect(localMin.x, localMin.y, localMax.x, localMax.y);
        }

        private void ApplyLocalArea(RectTransform target, Rect localArea)
        {
            if (target == null)
                return;

            RectTransform parent = target.parent as RectTransform;
            if (parent == null)
                return;

            _drivenTracker.Add(this, target,
                DrivenTransformProperties.AnchorMin |
                DrivenTransformProperties.AnchorMax |
                DrivenTransformProperties.AnchoredPosition |
                DrivenTransformProperties.SizeDelta);

            Rect parentRect = parent.rect;
            if (parentRect.width <= 0f || parentRect.height <= 0f)
                return;

            target.anchorMin = new Vector2(
                NormalizeUnclamped(localArea.xMin, parentRect.xMin, parentRect.xMax),
                NormalizeUnclamped(localArea.yMin, parentRect.yMin, parentRect.yMax));
            target.anchorMax = new Vector2(
                NormalizeUnclamped(localArea.xMax, parentRect.xMin, parentRect.xMax),
                NormalizeUnclamped(localArea.yMax, parentRect.yMin, parentRect.yMax));
            target.offsetMin = Vector2.zero;
            target.offsetMax = Vector2.zero;
        }

        private void ApplyFullScreenElements(RectTransform areaSpace, Rect availableArea)
        {
            if (aspectPreset == SafeAreaAspectPreset.None ||
                fullScreenElements == null || fullScreenElements.Length == 0)
                return;

            foreach (RectTransform item in fullScreenElements)
            {
                if (item != null && item != _rectTransform)
                {
                    RectTransform itemParent = item.parent as RectTransform;
                    Rect itemParentArea = TransformRect(areaSpace, itemParent, availableArea);
                    ApplyLocalArea(item, itemParentArea);
                }
            }
        }

        private static Rect TransformRect(RectTransform source, RectTransform target, Rect area)
        {
            if (source == null || target == null || source == target)
                return area;

            Vector3 bottomLeft = target.InverseTransformPoint(
                source.TransformPoint(new Vector3(area.xMin, area.yMin)));
            Vector3 topRight = target.InverseTransformPoint(
                source.TransformPoint(new Vector3(area.xMax, area.yMax)));
            return Rect.MinMaxRect(bottomLeft.x, bottomLeft.y, topRight.x, topRight.y);
        }

        private static float NormalizeUnclamped(float value, float min, float max)
        {
            float range = max - min;
            return Mathf.Abs(range) > Mathf.Epsilon ? (value - min) / range : 0f;
        }

        private void ApplyFitFlags(ref Rect targetArea, Rect availableArea)
        {
            if (!fitLeft) targetArea.xMin = availableArea.xMin;
            if (!fitRight) targetArea.xMax = availableArea.xMax;
            if (!fitBottom) targetArea.yMin = availableArea.yMin;
            if (!fitTop) targetArea.yMax = availableArea.yMax;
        }

        private static Rect FitInside(Rect area, float aspect)
        {
            if (aspect <= 0f || area.width <= 0f || area.height <= 0f)
                return area;

            float currentAspect = area.width / area.height;
            if (currentAspect > aspect)
            {
                float width = area.height * aspect;
                return new Rect(area.center.x - width * 0.5f, area.yMin, width, area.height);
            }

            float height = area.width / aspect;
            return new Rect(area.xMin, area.center.y - height * 0.5f, area.width, height);
        }

        private static Rect Intersect(Rect first, Rect second)
        {
            float xMin = Mathf.Max(first.xMin, second.xMin);
            float yMin = Mathf.Max(first.yMin, second.yMin);
            float xMax = Mathf.Min(first.xMax, second.xMax);
            float yMax = Mathf.Min(first.yMax, second.yMax);
            return Rect.MinMaxRect(xMin, yMin, Mathf.Max(xMin, xMax), Mathf.Max(yMin, yMax));
        }

        private static Camera GetEventCamera(Canvas canvas)
        {
            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                return null;
            return canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
        }

        private void CacheRectTransform()
        {
            if (_rectTransform == null)
                _rectTransform = transform as RectTransform;
        }

        private Rect GetSafeArea()
        {
            return Screen.safeArea;
        }

        private bool ShouldApply()
        {
            if (!Application.isPlaying)
                return false;

            if (onlyApplyOnMobile &&
                !Application.isMobilePlatform)
            {
                return false;
            }

            return true;
        }

        private Rect ApplyExtraPadding(Rect safeArea)
        {
            if (extraPadding == null)
                return safeArea;

            Rect result = safeArea;

            result.xMin += extraPadding.left;
            result.xMax -= extraPadding.right;
            result.yMin += extraPadding.bottom;
            result.yMax -= extraPadding.top;

            if (result.width < 0f)
                result.width = 0f;

            if (result.height < 0f)
                result.height = 0f;

            return result;
        }

        private static bool Approximately(Rect a, Rect b)
        {
            return Mathf.Approximately(a.x, b.x) &&
                   Mathf.Approximately(a.y, b.y) &&
                   Mathf.Approximately(a.width, b.width) &&
                   Mathf.Approximately(a.height, b.height);
        }
    }
}
