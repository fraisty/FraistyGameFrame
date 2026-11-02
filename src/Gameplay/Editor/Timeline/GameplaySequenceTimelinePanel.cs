using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Gameplay.Editor.Timeline
{
    /// <summary>Shared Sequence asset panel. Standalone Timeline and Skill Workbench use this same editor surface.</summary>
    public sealed class GameplaySequenceTimelinePanel
    {
        private GameplaySequenceDefinitionAsset _sequence;
        private readonly Action _repaint;
        private Vector2 _inspectorScroll;
        private int _selected = -1, _dragging = -1, _dragControl;
        private GameplaySequenceTimelineModel.MarkerDrag _drag;
        private float _playhead, _zoom = 1f, _horizontalSeconds, _presentationDuration, _dragGrabOffsetPixels;
        private bool _fitToWindow = true;
        private bool _snap = true, _frames;
        private bool _showMarker = true, _showEvents, _showValidation;
        private string[] _issues = Array.Empty<string>();
        private const float LabelWidth = GameplayEditorStyles.TrackHeaderWidth;
        private const float TrackHeight = GameplayEditorStyles.TrackHeight;
        private string _scope="FRFrame";
        private long _selectedReference=-1;
        private float _selectedTime;
        public int FramesPerSecond { get; private set; }=60;
        public event Action<int> MarkerSelected;
        public event Action<int> AddRequested;
        public void ConfigureScope(string scope)
        {
            _scope=scope??"FRFrame";FramesPerSecond=_scope=="XH"?30:Mathf.Clamp(EditorPrefs.GetInt("FRFrame.Timeline.FPS."+_scope,60),1,240);_frames=true;
        }
        public GameplaySequenceDefinitionAsset CurrentSequence => _sequence;
        public int SelectedMarker => _selected;
        public float Playhead => _playhead;
        internal bool IsDragging=>_drag!=null;
        /// <summary>Editor display range may extend past rule Sequence duration to show a presentation animation.</summary>
        public float DisplayDuration => Mathf.Max(_sequence == null ? 0f : _sequence.Duration, _presentationDuration);
        public event Action<float> PlayheadChanged;

        public GameplaySequenceTimelinePanel(Action repaint) { _repaint = repaint ?? (() => { }); }
        public void Select(GameplaySequenceDefinitionAsset sequence)
        {
            if (_sequence == sequence) return;
            CancelDrag();
            _sequence = sequence;
            _selected = -1;_selectedReference=-1;
            _playhead = _horizontalSeconds = 0f;
            _presentationDuration = 0f;
            _fitToWindow = true;
            Validate();
            PlayheadChanged?.Invoke(_playhead);
            _repaint();
        }
        public void OnUndo() { CancelDrag();Validate();if(_sequence!=null&&_selected>=0){float time=_sequence.Markers[_selected].Time;if(!Mathf.Approximately(time,_selectedTime))SetPlayhead(time);_selectedTime=time;}_repaint(); }
        /// <summary>Workbench navigation selects the canonical marker inspector without moving its time or advancing gameplay.</summary>
        public void SelectMarker(int index)
        {
            if (_sequence == null || index < 0 || index >= _sequence.Markers.Length) return;
            CancelDrag();
            _selected = index;
            _selectedTime=_sequence.Markers[index].Time;
            using(var so=new SerializedObject(_sequence)) _selectedReference=so.FindProperty("Markers").GetArrayElementAtIndex(index).managedReferenceId;
            _showMarker = true;
            _repaint();
        }
        public void CancelDrag()
        {
            _drag?.Cancel();
            _drag = null;
            _dragging = -1;
            if (_dragControl != 0 && GUIUtility.hotControl == _dragControl) GUIUtility.hotControl = 0;
            _dragControl = 0;
            _dragGrabOffsetPixels = 0f;
            Validate();
            _repaint();
        }
        public void Validate()
        {
            _issues = GameplaySequenceTimelineValidation.Check(_sequence).ToArray();
            if(_sequence!=null&&_selectedReference>=0) {
                using(var so=new SerializedObject(_sequence)) {
                    var array=so.FindProperty("Markers");_selected=-1;
                    for(int i=0;i<array.arraySize;i++) if(array.GetArrayElementAtIndex(i).managedReferenceId==_selectedReference){_selected=i;break;}
                }
            }
            if(_sequence==null || _selected>=_sequence.Markers.Length) _selected=-1;
        }
        private float PresentedTime(int index)=>_drag!=null&&_drag.CurrentIndex==index?_drag.PreviewTime:_sequence.Markers[index].Time;

        public void SetPlayhead(float seconds)
        {
            _playhead = Mathf.Clamp(seconds, 0f, DisplayDuration);
            PlayheadChanged?.Invoke(_playhead);
            _repaint();
        }
        public void SetPresentationDuration(float seconds)
        {
            _presentationDuration = Mathf.Max(0f, seconds);
            if (_playhead > DisplayDuration) SetPlayhead(DisplayDuration);
        }
        public void DrawTimeline(float viewportWidth, float presentationDuration = 0f,bool compact=false)
        {
            SetPresentationDuration(presentationDuration);
            GUILayout.BeginVertical(GUILayout.Width(viewportWidth), GUILayout.ExpandHeight(!compact));
            if (_sequence == null)
            { GUILayout.FlexibleSpace(); GUILayout.Label("从左侧选择 Sequence Asset", EditorStyles.centeredGreyMiniLabel); GUILayout.FlexibleSpace(); GUILayout.EndVertical(); return; }
            if (_fitToWindow)
                _zoom = Mathf.Clamp((Mathf.Max(500f, viewportWidth) - LabelWidth - 45f) /
                    (Mathf.Max(.1f, DisplayDuration) * 220f), .25f, 8f);
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (viewportWidth >= 760f)
            { GUILayout.Label(_sequence.name, EditorStyles.boldLabel, GUILayout.MaxWidth(210f)); GUILayout.Space(8f); }
            _frames = GUILayout.Toggle(_frames, _frames ? "帧" : "秒", EditorStyles.toolbarButton, GUILayout.Width(40));
            _snap = GUILayout.Toggle(_snap, "吸附 1/"+FramesPerSecond+"秒", EditorStyles.toolbarButton, GUILayout.Width(95));
            GUILayout.Label(new GUIContent(FramesPerSecond+"f = 1.000s（创作刻度）","只改变编辑器显示 / 吸附，Runtime 按秒；不是 Spine 或 Unity 渲染帧率。"),EditorStyles.miniLabel);
            int fps;using(new EditorGUI.DisabledScope(_scope=="XH"))fps=EditorGUILayout.IntField(FramesPerSecond,GUILayout.Width(35));
            if(fps!=FramesPerSecond){FramesPerSecond=Mathf.Clamp(fps,1,240);EditorPrefs.SetInt("FRFrame.Timeline.FPS."+_scope,FramesPerSecond);}
            GUILayout.Label("FPS",GUILayout.Width(25));
            GUILayout.Label("缩放", GUILayout.Width(30));
            float zoom = GUILayout.HorizontalSlider(_zoom, .25f, 8f, GUILayout.Width(100));
            if (!Mathf.Approximately(zoom, _zoom)) { _zoom = zoom; _fitToWindow = false; }
            GUILayout.Label(_zoom.ToString("0.0") + "×", GUILayout.Width(35));
            if (GUILayout.Button("适配", EditorStyles.toolbarButton, GUILayout.Width(40)))
            { _fitToWindow = true; _horizontalSeconds = 0f; }
            if(!compact)GUILayout.FlexibleSpace();
            GUILayout.Label(viewportWidth >= 760f
                ? $"预览 {_playhead:0.000}s  ·  序列 {_sequence.Duration:0.###}s  ·  显示 {DisplayDuration:0.###}s"
                : $"{_playhead:0.000}s / {DisplayDuration:0.###}s", EditorStyles.miniLabel);
            GUILayout.EndHorizontal();

            float pixelsPerSecond = 220f * _zoom;
            float visibleWidth = Mathf.Max(200f, viewportWidth - LabelWidth - 24f);
            float maxScroll = Mathf.Max(0f, DisplayDuration - visibleWidth / pixelsPerSecond);
            _horizontalSeconds = GUILayout.HorizontalSlider(_horizontalSeconds, 0f, maxScroll);
            Rect area = GUILayoutUtility.GetRect(100f,
                GameplayEditorStyles.RulerHeight + TrackHeight * GameplaySequenceTimelineModel.TrackCount + 2f, GUILayout.ExpandWidth(true));
            DrawCanvas(area, pixelsPerSecond);
            if(!compact)GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
        }

        private void AddMarker(int track)
        {
            if (_sequence == null || _playhead > _sequence.Duration) return;
            if(AddRequested!=null){AddRequested.Invoke(track);return;}
            GameplaySequenceMarkerAuthoring marker = track == 0
                ? (GameplaySequenceMarkerAuthoring)new GameplaySequenceApplyEffectMarkerAuthoring { Time = _playhead }
                : track == 1 ? (GameplaySequenceMarkerAuthoring)new GameplaySequenceCueMarkerAuthoring { Time = _playhead }
                : new GameplaySequenceSkillStepMarkerAuthoring { Time = _playhead, Target = new GameplaySkillSelfTargetAuthoring() };
            SelectMarker(GameplaySequenceTimelineModel.Add(_sequence, marker));MarkerSelected?.Invoke(_selected);
            Validate();
            _repaint();
        }

        private void DrawCanvas(Rect area, float scale)
        {
            int canvasControl = GUIUtility.GetControlID(FocusType.Passive, area);
            EditorGUI.DrawRect(area, GameplayEditorStyles.Surface);
            Rect ruler = new Rect(area.x + LabelWidth, area.y, area.width - LabelWidth,
                GameplayEditorStyles.RulerHeight);
            float contentX = ruler.x - _horizontalSeconds * scale;
            Rect content = new Rect(ruler.x, area.y + GameplayEditorStyles.RulerHeight,
                ruler.width, TrackHeight * GameplaySequenceTimelineModel.TrackCount);
            for (int track = 0; track < GameplaySequenceTimelineModel.TrackCount; track++)
            {
                float rowY = content.y + track * TrackHeight;
                EditorGUI.DrawRect(new Rect(area.x, rowY, area.width, TrackHeight),
                    track == 0 ? GameplayEditorStyles.TrackA : GameplayEditorStyles.TrackB);
                EditorGUI.DrawRect(new Rect(area.x, rowY, LabelWidth, TrackHeight), GameplayEditorStyles.Panel);
                GUI.Label(new Rect(area.x + 8f, rowY + 4f, LabelWidth - 35f, 20f),
                    track == 0 ? "效果 Effect" : track == 1 ? "表现 Cue" : "规则 SkillStep", EditorStyles.miniBoldLabel);
                using (new EditorGUI.DisabledScope(_playhead > _sequence.Duration))
                    if (GUI.Button(new Rect(area.x + LabelWidth - 27f, rowY + 6f, 21f, 20f),
                        new GUIContent("+", track == 0 ? "在播放头添加效果" : track == 1 ? "在播放头添加表现" : "在播放头添加规则步骤"),
                        EditorStyles.miniButton)) AddMarker(track);
                EditorGUI.DrawRect(new Rect(area.x, rowY + TrackHeight - 1f, area.width, 1f),
                    GameplayEditorStyles.Grid);
            }
            EditorGUI.DrawRect(new Rect(ruler.x - 1f, area.y, 1f, area.height), GameplayEditorStyles.Grid);
            float endX = contentX + _sequence.Duration * scale;
            if (DisplayDuration > _sequence.Duration && endX < area.xMax)
            {
                float tailX = Mathf.Max(ruler.x, endX);
                EditorGUI.DrawRect(new Rect(tailX, content.y, area.xMax - tailX, content.height),
                    GameplayEditorStyles.Tail);
                if (area.xMax - tailX > 110f)
                    GUI.Label(new Rect(tailX + 8f, content.y + 4f, 110f, 18f), "表现动画预览", EditorStyles.miniLabel);
            }
            float step = scale >= 440f ? .1f : scale >= 180f ? .25f : .5f;
            int first = Mathf.Max(0, Mathf.CeilToInt(_horizontalSeconds / step));
            for (int n = first; n * step <= DisplayDuration + .0001f; n++)
            {
                float time = n * step, x = contentX + time * scale;
                if (x > area.xMax) break;
                if (x < ruler.x) continue;
                bool major = n % (step == .1f ? 5 : 2) == 0;
                EditorGUI.DrawRect(new Rect(x, area.y + (major ? 12f : 22f), 1f,
                    area.height - (major ? 12f : 22f)), GameplayEditorStyles.Grid * (major ? 1.2f : .75f));
                if (major) GUI.Label(new Rect(x + 3f, area.y, 72f, 17f), _frames ?
                    Mathf.RoundToInt(time * FramesPerSecond).ToString() + "f" : time.ToString("0.##") + "s", EditorStyles.miniLabel);
            }
            if (endX >= ruler.x && endX <= area.xMax)
            {
                EditorGUI.DrawRect(new Rect(endX, area.y + 1f, 2f, area.height - 1f), GameplayEditorStyles.RuleEnd);
                GUI.Label(new Rect(endX + 4f, area.y + 15f, 95f, 17f), "序列结束", EditorStyles.miniLabel);
            }
            List<GameplaySequenceMarkerLayout.Entry> markers = GameplaySequenceMarkerLayout.Build(
                _sequence.Markers, area, contentX, scale, PresentedTime);
            foreach (var entry in markers)
            {
                Rect box = entry.Rect;
                if (box.xMax < content.x || box.x > content.xMax) continue;
                Color accent = entry.Track == 0 ? GameplayEditorStyles.Effect :
                    entry.Track == 1 ? GameplayEditorStyles.Cue : GameplayEditorStyles.ActionNode;
                Color fill = Color.Lerp(GameplayEditorStyles.Panel, accent,
                    entry.Index == _selected ? .62f : PresentedTime(entry.Index) <= _playhead ? .48f : .25f);
                GUI.BeginGroup(content);
                Rect local = new Rect(box.x - content.x, box.y - content.y, box.width, box.height);
                EditorGUI.DrawRect(local, fill);
                if (entry.Index == _selected)
                {
                    EditorGUI.DrawRect(new Rect(local.x, local.y, local.width, 2f), GameplayEditorStyles.Accent);
                    EditorGUI.DrawRect(new Rect(local.x, local.yMax - 2f, local.width, 2f), GameplayEditorStyles.Accent);
                }
                GUI.Label(new Rect(local.x + 6f, local.y, local.width - 10f, local.height),
                    (_frames?Mathf.RoundToInt(PresentedTime(entry.Index)*FramesPerSecond)+"f":PresentedTime(entry.Index).ToString("0.###")+"s")+" · "+MarkerName(_sequence.Markers[entry.Index]), GameplayEditorStyles.MarkerText);
                GUI.EndGroup();
            }
            float playX = contentX + _playhead * scale;
            if (playX >= ruler.x && playX <= area.xMax)
            {
                EditorGUI.DrawRect(new Rect(playX, area.y + 7f, 2f, area.height - 7f), GameplayEditorStyles.Playhead);
                GUI.Label(new Rect(playX - 5f, area.y - 4f, 18f, 18f), "▼", EditorStyles.miniBoldLabel);
            }
            HandleCanvasEvents(area, ruler, content, contentX, scale, markers, canvasControl);
        }

        private static string MarkerName(GameplaySequenceMarkerAuthoring marker) =>
            marker is GameplaySequenceCueMarkerAuthoring cue ?
                (string.IsNullOrEmpty(cue.CueTag.Name) ? "Cue · 未指定" : cue.CueTag.Name) :
            marker is GameplaySequenceApplyEffectMarkerAuthoring effect ?
                EffectName(effect.Effect) :
            marker is GameplaySequenceSkillStepMarkerAuthoring step ?
                (step.Actions?.Length==1 && step.Actions[0] is GameplaySkillApplyEffectActionAuthoring apply ? EffectName(apply.Effect) : $"SkillStep · {step.Actions?.Length ?? 0} Actions") : "Marker";

        private static string EffectName(GameplayEffectDefinitionAsset effect)
        {
            if(effect==null) return "Effect · 未指定";
            if(effect.Executions?.Length!=1 || effect.Executions[0]==null) return effect.name;
            using(var so=new SerializedObject(effect)) {
                var execution=so.FindProperty("Executions").GetArrayElementAtIndex(0);
                return SkillAuthoring.GameplaySkillNodeDescriptors.Get(effect.Executions[0].GetType(),execution).DisplayName;
            }
        }

        private void HandleCanvasEvents(Rect area, Rect ruler, Rect content, float contentX, float scale,
            IReadOnlyList<GameplaySequenceMarkerLayout.Entry> markers, int canvasControl)
        {
            Event evt = Event.current;
            if (_drag != null)
            {
                if (evt.type == EventType.MouseDrag && GUIUtility.hotControl == _dragControl)
                {
                    _drag.Update((evt.mousePosition.x - _dragGrabOffsetPixels - contentX) / scale, _snap);
                    _selectedTime=_drag.PreviewTime;SetPlayhead(_selectedTime);
                    Validate(); evt.Use(); _repaint(); return;
                }
                if (evt.type == EventType.MouseUp || evt.type == EventType.Ignore)
                {
                    _drag.End(); _drag = null; _dragging = -1;
                    if (GUIUtility.hotControl == _dragControl) GUIUtility.hotControl = 0;
                    _dragControl = 0; _dragGrabOffsetPixels = 0f; evt.Use(); _repaint(); return;
                }
            }
            if (!area.Contains(evt.mousePosition)) return;
            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                int hit=GameplaySequenceMarkerLayout.HitTest(markers, evt.mousePosition, content);
                if (hit >= 0)
                {
                    SelectMarker(hit);SetPlayhead(_sequence.Markers[hit].Time);MarkerSelected?.Invoke(hit);
                    _dragging = _selected;
                    _dragGrabOffsetPixels = evt.mousePosition.x -
                        (contentX + _sequence.Markers[_selected].Time * scale);
                    _dragControl = canvasControl;
                    GUIUtility.hotControl = _dragControl;
                    _drag = GameplaySequenceTimelineModel.BeginDrag(_sequence, _selected,FramesPerSecond);
                }
                else if (evt.mousePosition.y <= ruler.yMax && evt.mousePosition.x >= ruler.x)
                    _playhead = Mathf.Clamp(GameplaySequenceTimelineModel.Snap((evt.mousePosition.x - contentX) / scale, _snap,FramesPerSecond), 0f, DisplayDuration);
                PlayheadChanged?.Invoke(_playhead); evt.Use(); _repaint();
            }
            else if (evt.type == EventType.MouseDrag && evt.button == 0 && evt.mousePosition.x >= ruler.x)
            {
                float time = (evt.mousePosition.x - contentX) / scale;
                _playhead = Mathf.Clamp(GameplaySequenceTimelineModel.Snap(time, _snap,FramesPerSecond), 0f, DisplayDuration);
                PlayheadChanged?.Invoke(_playhead); evt.Use(); _repaint();
            }
        }

        public void DrawInspector(float width = 320f, float availableHeight = 0f)
        {
            GUILayoutOption[] outer = availableHeight > 0f
                ? new[] { GUILayout.Width(width), GUILayout.Height(availableHeight) }
                : new[] { GUILayout.Width(width), GUILayout.ExpandHeight(true) };
            GUILayout.BeginVertical(EditorStyles.helpBox, outer);
            GUILayout.Label("检查器 / 校验", EditorStyles.boldLabel);
            _inspectorScroll = availableHeight > 0f
                ? GUILayout.BeginScrollView(_inspectorScroll, GUILayout.Height(Mathf.Max(80f, availableHeight - 30f)))
                : GUILayout.BeginScrollView(_inspectorScroll, GUILayout.ExpandHeight(true));
            if (_sequence != null)
            {
                var serialized = new SerializedObject(_sequence);
                serialized.Update();
                EditorGUILayout.PropertyField(serialized.FindProperty("definitionId"), new GUIContent("定义 ID"));
                EditorGUILayout.PropertyField(serialized.FindProperty("Duration"), new GUIContent("总时长（秒）"));
                if (_selected >= 0 && _selected < _sequence.Markers.Length)
                {
                    GUILayout.Space(8f);
                    _showMarker = EditorGUILayout.Foldout(_showMarker,
                        "标记 " + _selected + " · " + MarkerName(_sequence.Markers[_selected]), true);
                    if (_showMarker)
                    {
                        float currentTime=_sequence.Markers[_selected].Time;
                        float entered=EditorGUILayout.FloatField(_frames?"事件帧":"事件秒",_frames?currentTime*FramesPerSecond:currentTime);
                        float seconds=_frames?entered/FramesPerSecond:entered;
                        if(!Mathf.Approximately(seconds,currentTime)) {
                            if(seconds<0||seconds>_sequence.Duration) EditorGUILayout.HelpBox("事件超出正式总时长：不会自动改时长或裁剪此输入。",MessageType.Warning);
                            else {serialized.ApplyModifiedProperties();GameplaySequenceTimelineModel.SetTime(_sequence,_selected,seconds,false,FramesPerSecond);serialized=new SerializedObject(_sequence);}
                        }
                        var marker = serialized.FindProperty("Markers").GetArrayElementAtIndex(_selected);
                        EditorGUILayout.PropertyField(marker, true);
                        if (marker.managedReferenceValue is GameplaySequenceSkillStepMarkerAuthoring)
                            GameplaySequenceSkillStepEditor.DrawNodeMenus(marker);
                        GUILayout.BeginHorizontal();
                        if (GUILayout.Button("复制")) { serialized.ApplyModifiedProperties(); SelectMarker(GameplaySequenceTimelineModel.Duplicate(_sequence, _selected));MarkerSelected?.Invoke(_selected); serialized = new SerializedObject(_sequence); }
                        if (GUILayout.Button("删除")) { serialized.ApplyModifiedProperties(); GameplaySequenceTimelineModel.Delete(_sequence, _selected); _selected = -1; serialized = new SerializedObject(_sequence); }
                        GUILayout.EndHorizontal();
                    }
                }
                if (serialized.ApplyModifiedProperties()) { GameplayAuthoringSourceChanges.Notify(_sequence); EditorUtility.SetDirty(_sequence); Validate(); }
                GUILayout.Space(8f);
                _showEvents = EditorGUILayout.Foldout(_showEvents,
                    "标记列表 · " + _sequence.Markers.Length + "（重叠时在此选取）", true);
                if (_showEvents)
                    foreach (var pair in _sequence.Markers.Select((marker, index) => (marker, index))
                        .Where(pair => pair.marker != null).OrderBy(pair => pair.marker.Time).ThenBy(pair => pair.index))
                {
                    string label = (pair.marker.Time <= _playhead + .00001f ? "✓  " : "·  ") +
                        pair.marker.Time.ToString("0.000") + "s  " + MarkerName(pair.marker);
                    if (GUILayout.Toggle(_selected == pair.index, label, EditorStyles.miniButton) &&
                        _selected != pair.index)
                    { SelectMarker(pair.index);SetPlayhead(pair.marker.Time);MarkerSelected?.Invoke(pair.index); }
                }
                GUILayout.Space(8f);
                _showValidation = EditorGUILayout.Foldout(_showValidation,
                    "校验 · " + (_issues.Length == 0 ? "无错误" : _issues.Length + " 项错误"), true);
                if (_showValidation)
                    foreach (string issue in _issues) EditorGUILayout.HelpBox(issue, MessageType.Error);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }
    }
}
