using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FRFrame.Live2D
{
    [RequireComponent(typeof(RawImage))]
    public class Live2DRawImageInputRuntime : MonoBehaviour,
        IPointerDownHandler,
        IPointerUpHandler,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerMoveHandler
    {
        [Header("References")]
        public Live2DMotionController controller;
        public Live2DInteractionProfile profile;
        public RawImage targetRawImage;

        [Header("Tap")]
        public float tapMaxDuration = 0.3f;
        public float tapMaxMovePixels = 10f;
        public bool acceptTapWhenOverlayPlaying = true;

        [Header("Drag")]
        public float dragStartThresholdPixels = 10f;
        public float dragRepeatInterval = 0.15f;
        public bool dragOnlyInsideRawImage = true;

        [Header("Long Press")]
        public bool enableLongPress = true;
        public float longPressDuration = 0.6f;

        [Header("Hover")]
        public bool enableHover = true;

        [Header("Swipe")]
        public bool enableSwipe = true;
        public float swipeMinDistance = 80f;

        [Header("Profile Transform")]
        public bool applyRawImageTransformFromProfileOnStart = true;
        public bool applyModelTransformFromProfileOnStart = true;

        [Header("Debug")]
        public bool debugLog;
        public bool debugDrawOverlay = true;

#if UNITY_EDITOR
        [Header("Editor Overlay")]
        public bool showEditorAreaOverlay = false;
        public bool enableEditorAreaEditing;
        public string editorRecordingLayoutId = "Default";
        public string editorRecordingAreaPrefix = "Area";
        public Live2DTouchShapeType editorRecordingShapeType = Live2DTouchShapeType.Rect;
#endif

        private PointerSession _pointer;
        private Live2DTouchArea _hoverArea;
#if UNITY_EDITOR
        private enum EditorEditHandle
        {
            None,
            Move,
            Left,
            Right,
            Top,
            Bottom,
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }

        private bool _editorRecordingDrag;
        private Vector2 _editorRecordingStartGui;
        private Vector2 _editorRecordingCurrentGui;
        private Live2DTouchArea _editorSelectedArea;
        private Rect _editorEditStartRect;
        private Vector2 _editorEditStartGui;
        private EditorEditHandle _editorEditHandle;
        private const float EditorHandleSize = 12f;
#endif
        private static Texture2D _whiteTex;

        private class PointerSession
        {
            public bool active;
            public int pointerId;
            public Vector2 downScreenPos;
            public Vector2 lastScreenPos;
            public float downTime;
            public bool dragStarted;
            public bool longPressTriggered;
            public float lastDragTriggerTime = -999f;
            public Live2DTouchArea lastDragArea;
        }

        public string CurrentBaseMotionId => controller != null ? controller.CurrentBaseClipId : string.Empty;
        public Live2DInteractionLayout CurrentLayout => profile != null ? profile.GetLayoutForBase(CurrentBaseMotionId) : null;
        public Live2DTouchArea HoverArea => _hoverArea;

        private void Reset()
        {
            targetRawImage = GetComponent<RawImage>();
            controller = FindFirstObjectByType<Live2DMotionController>();
        }

        private void Awake()
        {
            if (targetRawImage == null) targetRawImage = GetComponent<RawImage>();
        }

        private void Start()
        {
            RefreshCurrentModel();
        }

        private void Update()
        {
            if (_pointer != null && _pointer.active && enableLongPress && !_pointer.longPressTriggered && !_pointer.dragStarted)
            {
                float duration = Time.unscaledTime - _pointer.downTime;
                float move = Vector2.Distance(_pointer.downScreenPos, _pointer.lastScreenPos);
                if (duration >= longPressDuration && move <= tapMaxMovePixels)
                {
                    _pointer.longPressTriggered = true;
                    TryTrigger(_pointer.lastScreenPos, GetTargetEventCamera(), Live2DTriggerType.LongPress);
                }
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            UpdateHover(eventData.position, eventData.pressEventCamera, true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_hoverArea != null) TriggerArea(_hoverArea, Live2DTriggerType.HoverExit);
            _hoverArea = null;
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            UpdateHover(eventData.position, eventData.pressEventCamera, true);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _pointer = new PointerSession
            {
                active = true,
                pointerId = eventData.pointerId,
                downScreenPos = eventData.position,
                lastScreenPos = eventData.position,
                downTime = Time.unscaledTime
            };
            UpdateHover(eventData.position, eventData.pressEventCamera, false);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!IsActivePointer(eventData)) return;
            _pointer.lastScreenPos = eventData.position;
            if (Vector2.Distance(_pointer.downScreenPos, eventData.position) >= dragStartThresholdPixels)
            {
                _pointer.dragStarted = true;
                TryTrigger(eventData.position, eventData.pressEventCamera, Live2DTriggerType.DragStart);
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!IsActivePointer(eventData)) return;
            _pointer.lastScreenPos = eventData.position;
            UpdateHover(eventData.position, eventData.pressEventCamera, true);

            if (!_pointer.dragStarted && Vector2.Distance(_pointer.downScreenPos, eventData.position) >= dragStartThresholdPixels)
            {
                _pointer.dragStarted = true;
                TryTrigger(eventData.position, eventData.pressEventCamera, Live2DTriggerType.DragStart);
            }

            if (!_pointer.dragStarted) return;
            if (dragOnlyInsideRawImage && !IsInsideRawImage(eventData.position, eventData.pressEventCamera)) return;
            if (Time.unscaledTime - _pointer.lastDragTriggerTime < dragRepeatInterval) return;

            if (TryTrigger(eventData.position, eventData.pressEventCamera, Live2DTriggerType.DragRepeat))
                _pointer.lastDragTriggerTime = Time.unscaledTime;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!IsActivePointer(eventData)) return;
            TryTrigger(eventData.position, eventData.pressEventCamera, Live2DTriggerType.DragEnd);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!IsActivePointer(eventData)) return;
            _pointer.lastScreenPos = eventData.position;
            bool tap = ShouldTreatAsTap(eventData);
            if (tap) TryTrigger(eventData.position, eventData.pressEventCamera, Live2DTriggerType.Tap);
            if (enableSwipe && Vector2.Distance(_pointer.downScreenPos, eventData.position) >= swipeMinDistance)
                TryTrigger(eventData.position, eventData.pressEventCamera, Live2DTriggerType.Swipe);
            _pointer.active = false;
        }

        public void RefreshCurrentModel()
        {
            controller = FindFirstObjectByType<Live2DMotionController>();
            if (controller != null) profile = controller.interactionProfile;
            ApplyProfileTransforms();
        }

        public void RefreshCurrentModel(Live2DMotionController currentController)
        {
            controller = currentController;
            profile = controller != null ? controller.interactionProfile : null;
            ApplyProfileTransforms();
        }

        public bool TryScreenPointToNormalized(Vector2 screenPoint, Camera eventCamera, out Vector2 normalized)
        {
            normalized = default;
            if (targetRawImage == null) return false;
            RectTransform rt = targetRawImage.rectTransform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screenPoint, eventCamera, out var localPoint)) return false;
            Rect rect = rt.rect;
            if (!rect.Contains(localPoint)) return false;
            normalized = new Vector2(Mathf.InverseLerp(rect.xMin, rect.xMax, localPoint.x), Mathf.InverseLerp(rect.yMin, rect.yMax, localPoint.y));
            return true;
        }

        public bool IsInsideRawImage(Vector2 screenPoint, Camera eventCamera) => TryScreenPointToNormalized(screenPoint, eventCamera, out _);

        public Camera GetTargetEventCamera()
        {
            if (targetRawImage == null || targetRawImage.canvas == null) return null;
            return targetRawImage.canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : targetRawImage.canvas.worldCamera;
        }

        public Rect GetRawImageScreenRect()
        {
            if (targetRawImage == null) return default;
            var corners = new Vector3[4];
            targetRawImage.rectTransform.GetWorldCorners(corners);
            var cam = GetTargetEventCamera();
            Vector3 bl = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
            Vector3 tr = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
            float xMin = Mathf.Min(bl.x, tr.x);
            float yMin = Mathf.Min(bl.y, tr.y);
            float xMax = Mathf.Max(bl.x, tr.x);
            float yMax = Mathf.Max(bl.y, tr.y);
            return new Rect(xMin, Screen.height - yMax, xMax - xMin, yMax - yMin);
        }

        public Rect NormalizedToScreenGuiRect(Rect normalizedRect)
        {
            Rect imageRect = GetRawImageScreenRect();
            return new Rect(
                imageRect.x + normalizedRect.xMin * imageRect.width,
                imageRect.y + (1f - normalizedRect.yMax) * imageRect.height,
                normalizedRect.width * imageRect.width,
                normalizedRect.height * imageRect.height);
        }

        public Rect ScreenRectToNormalized(Rect screenRect, Camera eventCamera)
        {
            if (!TryScreenPointToNormalized(new Vector2(screenRect.xMin, screenRect.yMin), eventCamera, out var a)) return default;
            if (!TryScreenPointToNormalized(new Vector2(screenRect.xMax, screenRect.yMax), eventCamera, out var b)) return default;
            float xMin = Mathf.Clamp01(Mathf.Min(a.x, b.x));
            float xMax = Mathf.Clamp01(Mathf.Max(a.x, b.x));
            float yMin = Mathf.Clamp01(Mathf.Min(a.y, b.y));
            float yMax = Mathf.Clamp01(Mathf.Max(a.y, b.y));
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

#if UNITY_EDITOR
        public Live2DInteractionLayout GetOrCreateEditorRecordingLayout()
        {
            if (profile == null) return null;
            var layout = profile.GetLayout(editorRecordingLayoutId);
            if (layout != null) return layout;
            layout = new Live2DInteractionLayout { layoutId = editorRecordingLayoutId, displayName = editorRecordingLayoutId };
            profile.layouts.Add(layout);
            return layout;
        }

        public Live2DTouchArea CreateRecordedArea(Rect normalizedRect)
        {
            var layout = GetOrCreateEditorRecordingLayout();
            if (layout == null) return null;
            int index = layout.areas != null ? layout.areas.Count + 1 : 1;
            if (layout.areas == null) layout.areas = new List<Live2DTouchArea>();
            var area = new Live2DTouchArea
            {
                areaId = $"{editorRecordingAreaPrefix}_{index}",
                displayName = $"{editorRecordingAreaPrefix} {index}",
                shapeType = editorRecordingShapeType,
                normalizedRect = normalizedRect
            };
            if (area.shapeType == Live2DTouchShapeType.Polygon)
            {
                area.normalizedPoints.Add(new Vector2(normalizedRect.xMin, normalizedRect.yMin));
                area.normalizedPoints.Add(new Vector2(normalizedRect.xMax, normalizedRect.yMin));
                area.normalizedPoints.Add(new Vector2(normalizedRect.xMax, normalizedRect.yMax));
                area.normalizedPoints.Add(new Vector2(normalizedRect.xMin, normalizedRect.yMax));
            }
            layout.areas.Add(area);
            return area;
        }
#endif

        private void UpdateHover(Vector2 screenPoint, Camera eventCamera, bool triggerEvents)
        {
            if (!enableHover || !TryScreenPointToNormalized(screenPoint, eventCamera, out var normalized))
            {
                if (triggerEvents && _hoverArea != null) TriggerArea(_hoverArea, Live2DTriggerType.HoverExit);
                _hoverArea = null;
                return;
            }

            var newHover = profile != null ? profile.Hit(normalized, CurrentBaseMotionId) : null;
            if (newHover == _hoverArea) return;
            if (triggerEvents && _hoverArea != null) TriggerArea(_hoverArea, Live2DTriggerType.HoverExit);
            _hoverArea = newHover;
            if (triggerEvents && _hoverArea != null) TriggerArea(_hoverArea, Live2DTriggerType.HoverEnter);
        }

        private bool TryTrigger(Vector2 screenPoint, Camera eventCamera, Live2DTriggerType trigger)
        {
            if (!TryScreenPointToNormalized(screenPoint, eventCamera, out var normalized)) return false;
            var area = profile != null ? profile.Hit(normalized, CurrentBaseMotionId) : null;
            if (area == null) return false;
            if (_pointer != null && trigger == Live2DTriggerType.DragRepeat) _pointer.lastDragArea = area;
            return TriggerArea(area, trigger);
        }

        private bool TriggerArea(Live2DTouchArea area, Live2DTriggerType trigger)
        {
            if (area == null || controller == null || area.bindings == null) return false;
            if (!acceptTapWhenOverlayPlaying && trigger == Live2DTriggerType.Tap && controller.IsOverlayPlaying) return false;
            bool any = false;
            foreach (var binding in area.bindings)
            {
                if (binding == null || !binding.enabled || binding.trigger != trigger) continue;
                if (Time.unscaledTime < binding.cooldownUntil) continue;
                if (!EvaluateConditions(binding, area, trigger)) continue;
                foreach (var action in binding.actions) any |= controller.ExecuteAction(action, trigger.ToString());
                binding.cooldownUntil = Time.unscaledTime + Mathf.Max(0f, binding.cooldown);
            }
            if (debugLog && any) Debug.Log($"[Live2DInput] {trigger} area={area.displayName}", this);
            return any;
        }

        private bool EvaluateConditions(Live2DInteractionBinding binding, Live2DTouchArea area, Live2DTriggerType trigger)
        {
            if (binding.conditions == null || binding.conditions.Count == 0) return true;
            var context = new Live2DInteractionContext(controller, trigger, area != null ? area.areaId : string.Empty);
            foreach (var condition in binding.conditions)
            {
                if (condition != null && !condition.Evaluate(context)) return false;
            }
            return true;
        }

        private bool IsActivePointer(PointerEventData eventData) => _pointer != null && _pointer.active && _pointer.pointerId == eventData.pointerId;

        private bool ShouldTreatAsTap(PointerEventData eventData)
        {
            if (_pointer == null || !_pointer.active || _pointer.dragStarted || _pointer.longPressTriggered) return false;
            if (Time.unscaledTime - _pointer.downTime > tapMaxDuration) return false;
            return Vector2.Distance(_pointer.downScreenPos, eventData.position) <= tapMaxMovePixels;
        }

        private void ApplyProfileTransforms()
        {
            if (profile == null) return;
            if (applyRawImageTransformFromProfileOnStart && targetRawImage != null && profile.rawImageTransform.enabled)
            {
                var rt = targetRawImage.rectTransform;
                rt.anchoredPosition = profile.rawImageTransform.anchoredPosition;
                rt.localScale = profile.rawImageTransform.localScale;
            }

            if (applyModelTransformFromProfileOnStart && controller != null && controller.cubismModel != null && profile.modelTransform.enabled)
            {
                var t = controller.cubismModel.transform;
                t.localPosition = profile.modelTransform.localPosition;
                t.localEulerAngles = profile.modelTransform.localEulerAngles;
                t.localScale = profile.modelTransform.localScale;
            }
        }

        private void OnGUI()
        {
#if UNITY_EDITOR            
            if (showEditorAreaOverlay && profile != null)
            {
                EnsureWhiteTex();
                var layout = CurrentLayout;
                if (layout != null)
                {
                    foreach (var area in layout.areas)
                    {
                        if (area == null || !area.enabled) continue;
                        if (area.shapeType == Live2DTouchShapeType.Rect)
                        {
                            Rect r = NormalizedToScreenGuiRect(area.normalizedRect);
                            bool selected = area == _editorSelectedArea;
                            DrawRect(r, selected ? new Color(0f, 1f, 1f, 0.16f) : new Color(0f, 1f, 0f, 0.1f));
                            DrawBorder(r, selected ? Color.cyan : (area == _hoverArea ? Color.cyan : Color.green), selected || area == _hoverArea ? 3f : 2f);
                            GUI.Label(new Rect(r.x + 4, r.y + 2, r.width - 8, 20), $"{area.displayName} ({area.priority})");
                            if (enableEditorAreaEditing && selected) DrawEditorHandles(r);
                        }
                        else if (area.normalizedPoints != null && area.normalizedPoints.Count >= 3)
                        {
                            DrawPolygon(area, area == _hoverArea ? Color.cyan : Color.green);
                        }
                    }
                }
            }
            
            HandleEditorAreaRecordingGUI();
#endif
        }

#if UNITY_EDITOR
        private void HandleEditorAreaRecordingGUI()
        {
            if (!enableEditorAreaEditing || profile == null || targetRawImage == null) return;
            Event e = Event.current;
            Rect imageRect = GetRawImageScreenRect();
            if (e.type == EventType.MouseDown && e.button == 0 && imageRect.Contains(e.mousePosition))
            {
                _editorSelectedArea = HitEditorArea(e.mousePosition);
                if (_editorSelectedArea != null && _editorSelectedArea.shapeType == Live2DTouchShapeType.Rect)
                {
                    Rect areaRect = NormalizedToScreenGuiRect(_editorSelectedArea.normalizedRect);
                    _editorEditHandle = ResolveEditorHandle(areaRect, e.mousePosition);
                    _editorEditStartRect = _editorSelectedArea.normalizedRect;
                    _editorEditStartGui = e.mousePosition;
                    e.Use();
                    return;
                }

                _editorRecordingDrag = true;
                _editorRecordingStartGui = e.mousePosition;
                _editorRecordingCurrentGui = e.mousePosition;
                e.Use();
            }
            else if (_editorEditHandle != EditorEditHandle.None && e.type == EventType.MouseDrag)
            {
                ApplyEditorAreaEdit(e.mousePosition - _editorEditStartGui);
                UnityEditor.EditorUtility.SetDirty(profile);
                e.Use();
            }
            else if (_editorEditHandle != EditorEditHandle.None && e.type == EventType.MouseUp && e.button == 0)
            {
                ApplyEditorAreaEdit(e.mousePosition - _editorEditStartGui);
                _editorEditHandle = EditorEditHandle.None;
                UnityEditor.EditorUtility.SetDirty(profile);
                e.Use();
            }
            else if (_editorRecordingDrag && e.type == EventType.MouseDrag)
            {
                _editorRecordingCurrentGui = e.mousePosition;
                e.Use();
            }
            else if (_editorRecordingDrag && e.type == EventType.MouseUp && e.button == 0)
            {
                _editorRecordingCurrentGui = e.mousePosition;
                Rect guiRect = MakeGuiRect(_editorRecordingStartGui, _editorRecordingCurrentGui);
                Rect screenRect = new Rect(guiRect.x, Screen.height - guiRect.yMax, guiRect.width, guiRect.height);
                Rect normalized = ScreenRectToNormalized(screenRect, GetTargetEventCamera());
                if (normalized.width > 0.001f && normalized.height > 0.001f)
                {
                    _editorSelectedArea = CreateRecordedArea(normalized);
                    UnityEditor.EditorUtility.SetDirty(profile);
                }
                _editorRecordingDrag = false;
                e.Use();
            }

            if (_editorRecordingDrag)
            {
                Rect preview = MakeGuiRect(_editorRecordingStartGui, _editorRecordingCurrentGui);
                DrawRect(preview, new Color(1f, 1f, 0f, 0.15f));
                DrawBorder(preview, Color.yellow, 2f);
            }
        }

        private Live2DTouchArea HitEditorArea(Vector2 guiPoint)
        {
            var layout = CurrentLayout;
            if (layout == null || layout.areas == null) return null;
            Vector2 screenPoint = new Vector2(guiPoint.x, Screen.height - guiPoint.y);
            if (!TryScreenPointToNormalized(screenPoint, GetTargetEventCamera(), out var normalized)) return null;
            Live2DTouchArea best = null;
            int bestPriority = int.MinValue;
            foreach (var area in layout.areas)
            {
                if (area == null || !area.enabled || !area.Contains(normalized)) continue;
                if (area.priority >= bestPriority)
                {
                    best = area;
                    bestPriority = area.priority;
                }
            }
            return best;
        }

        private void ApplyEditorAreaEdit(Vector2 guiDelta)
        {
            if (_editorSelectedArea == null) return;
            Rect imageRect = GetRawImageScreenRect();
            float dx = guiDelta.x / Mathf.Max(1f, imageRect.width);
            float dy = -guiDelta.y / Mathf.Max(1f, imageRect.height);
            Rect r = _editorEditStartRect;

            switch (_editorEditHandle)
            {
                case EditorEditHandle.Move:
                    r.x += dx;
                    r.y += dy;
                    break;
                case EditorEditHandle.Left:
                    r.xMin += dx;
                    break;
                case EditorEditHandle.Right:
                    r.xMax += dx;
                    break;
                case EditorEditHandle.Top:
                    r.yMax += dy;
                    break;
                case EditorEditHandle.Bottom:
                    r.yMin += dy;
                    break;
                case EditorEditHandle.TopLeft:
                    r.xMin += dx;
                    r.yMax += dy;
                    break;
                case EditorEditHandle.TopRight:
                    r.xMax += dx;
                    r.yMax += dy;
                    break;
                case EditorEditHandle.BottomLeft:
                    r.xMin += dx;
                    r.yMin += dy;
                    break;
                case EditorEditHandle.BottomRight:
                    r.xMax += dx;
                    r.yMin += dy;
                    break;
            }

            _editorSelectedArea.normalizedRect = ClampEditorRect(r);
        }

        private static Rect ClampEditorRect(Rect r)
        {
            if (r.width < 0f)
            {
                r.x += r.width;
                r.width = -r.width;
            }
            if (r.height < 0f)
            {
                r.y += r.height;
                r.height = -r.height;
            }
            r.width = Mathf.Clamp(r.width, 0.01f, 1f);
            r.height = Mathf.Clamp(r.height, 0.01f, 1f);
            r.x = Mathf.Clamp(r.x, 0f, 1f - r.width);
            r.y = Mathf.Clamp(r.y, 0f, 1f - r.height);
            return r;
        }

        private static EditorEditHandle ResolveEditorHandle(Rect r, Vector2 p)
        {
            if (GetCornerTopLeft(r).Contains(p)) return EditorEditHandle.TopLeft;
            if (GetCornerTopRight(r).Contains(p)) return EditorEditHandle.TopRight;
            if (GetCornerBottomLeft(r).Contains(p)) return EditorEditHandle.BottomLeft;
            if (GetCornerBottomRight(r).Contains(p)) return EditorEditHandle.BottomRight;
            if (GetEdgeLeft(r).Contains(p)) return EditorEditHandle.Left;
            if (GetEdgeRight(r).Contains(p)) return EditorEditHandle.Right;
            if (GetEdgeTop(r).Contains(p)) return EditorEditHandle.Top;
            if (GetEdgeBottom(r).Contains(p)) return EditorEditHandle.Bottom;
            return r.Contains(p) ? EditorEditHandle.Move : EditorEditHandle.None;
        }

        private static void DrawEditorHandles(Rect r)
        {
            DrawHandle(GetCornerTopLeft(r));
            DrawHandle(GetCornerTopRight(r));
            DrawHandle(GetCornerBottomLeft(r));
            DrawHandle(GetCornerBottomRight(r));
            DrawHandle(GetEdgeLeft(r));
            DrawHandle(GetEdgeRight(r));
            DrawHandle(GetEdgeTop(r));
            DrawHandle(GetEdgeBottom(r));
        }

        private static void DrawHandle(Rect r)
        {
            DrawRect(r, Color.white);
            DrawBorder(r, Color.black, 1f);
        }

        private static Rect GetCornerTopLeft(Rect r) => new Rect(r.xMin - EditorHandleSize * 0.5f, r.yMin - EditorHandleSize * 0.5f, EditorHandleSize, EditorHandleSize);
        private static Rect GetCornerTopRight(Rect r) => new Rect(r.xMax - EditorHandleSize * 0.5f, r.yMin - EditorHandleSize * 0.5f, EditorHandleSize, EditorHandleSize);
        private static Rect GetCornerBottomLeft(Rect r) => new Rect(r.xMin - EditorHandleSize * 0.5f, r.yMax - EditorHandleSize * 0.5f, EditorHandleSize, EditorHandleSize);
        private static Rect GetCornerBottomRight(Rect r) => new Rect(r.xMax - EditorHandleSize * 0.5f, r.yMax - EditorHandleSize * 0.5f, EditorHandleSize, EditorHandleSize);
        private static Rect GetEdgeLeft(Rect r) => new Rect(r.xMin - EditorHandleSize * 0.5f, r.yMin + EditorHandleSize * 0.5f, EditorHandleSize, Mathf.Max(0f, r.height - EditorHandleSize));
        private static Rect GetEdgeRight(Rect r) => new Rect(r.xMax - EditorHandleSize * 0.5f, r.yMin + EditorHandleSize * 0.5f, EditorHandleSize, Mathf.Max(0f, r.height - EditorHandleSize));
        private static Rect GetEdgeTop(Rect r) => new Rect(r.xMin + EditorHandleSize * 0.5f, r.yMin - EditorHandleSize * 0.5f, Mathf.Max(0f, r.width - EditorHandleSize), EditorHandleSize);
        private static Rect GetEdgeBottom(Rect r) => new Rect(r.xMin + EditorHandleSize * 0.5f, r.yMax - EditorHandleSize * 0.5f, Mathf.Max(0f, r.width - EditorHandleSize), EditorHandleSize);

        private static Rect MakeGuiRect(Vector2 a, Vector2 b)
        {
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }
#endif

        private static void EnsureWhiteTex()
        {
            if (_whiteTex != null) return;
            _whiteTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _whiteTex.SetPixel(0, 0, Color.white);
            _whiteTex.Apply();
        }

        public static void DrawRect(Rect rect, Color color)
        {
            EnsureWhiteTex();
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _whiteTex);
            GUI.color = old;
        }

        public static void DrawBorder(Rect rect, Color color, float thickness)
        {
            DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        private void DrawPolygon(Live2DTouchArea area, Color color)
        {
            Rect imageRect = GetRawImageScreenRect();
            for (int i = 0; i < area.normalizedPoints.Count; i++)
            {
                Vector2 a = NormalizedPointToGui(area.normalizedPoints[i], imageRect);
                Vector2 b = NormalizedPointToGui(area.normalizedPoints[(i + 1) % area.normalizedPoints.Count], imageRect);
                DrawLine(a, b, color, 2f);
            }
        }

        private static Vector2 NormalizedPointToGui(Vector2 p, Rect imageRect)
        {
            return new Vector2(imageRect.x + p.x * imageRect.width, imageRect.y + (1f - p.y) * imageRect.height);
        }

        private static void DrawLine(Vector2 a, Vector2 b, Color color, float thickness)
        {
            EnsureWhiteTex();
            Matrix4x4 oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;
            float angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
            float length = Vector2.Distance(a, b);
            GUI.color = color;
            GUIUtility.RotateAroundPivot(angle, a);
            GUI.DrawTexture(new Rect(a.x, a.y - thickness * 0.5f, length, thickness), _whiteTex);
            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
        }
    }
}
