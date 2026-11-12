using System;
using System.Linq;
using System.Reflection;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Editor;
using FRFrame.Gameplay.Editor.SkillAuthoring;
using UnityEditor;
using UnityEngine;
using PositionSource = FRFrame.Gameplay.Unity.GameplayPresentationCueAsset.PositionSource;

namespace FRFrame.Gameplay.Unity.Editor
{
    /// <summary>Disposable presentation-only preview of the actual cue/widget prefab. Uses the
    /// built Runtime source and absolute particle/text time, without a rule world, damage or clock.</summary>
    internal sealed class GameplayUnityCuePreview:IDisposable
    {
        private PreviewRenderUtility _preview;
        private GameplayPresentationCueDefinition _cue;
        private GameplayFloatingTextDefinition _text;
        private ParticleSystem[] _particles=Array.Empty<ParticleSystem>();
        private GameplayFloatingTextWidget _widget;
        private Vector3 _start;
        private GameplayPresentationCueData _values;
        private GameplaySourceHandle _source;
        private bool _shared, _auditioning;
        private bool _facingLeft = SessionState.GetBool("FRFrame.CuePreview.FacingLeft", false);
        private bool _spatial = SessionState.GetBool("FRFrame.CuePreview.Spatial", false);
        private Vector3 _owner = SessionState.GetVector3("FRFrame.CuePreview.Owner", Vector3.zero);
        private Vector3 _target = SessionState.GetVector3("FRFrame.CuePreview.Target", new Vector3(3, 0, 0));
        private Vector3 _point = SessionState.GetVector3("FRFrame.CuePreview.Point", new Vector3(3, 0, 0));
        private Vector3 _binding = SessionState.GetVector3("FRFrame.CuePreview.Binding", Vector3.up);
        private float _actorHeight = SessionState.GetFloat("FRFrame.CuePreview.ActorHeight", 1.65f);
        private float _intensity = 1;
        private string _valueNote;
        private Vector3 _materialCameraPosition;
        private float _materialCameraSize;
        private int _hotControl, _dragAxis;
        private Vector2 _dragMouse, _dragBasis;
        private Vector3 _draft, _dragStart;
        private Renderer[] _renderers;
        internal GameObject Subject {get;private set;}
        internal Camera Camera=>_preview?.camera;
        internal float Duration {get;private set;}
        internal GameplayUnityCuePreview(GameplayPresentationCueAsset cue,GameplayFloatingTextAsset text,GameplayEffectApplicationContext application)
            : this(cue?.CaptureGraphValues() as GameplayPresentationCueData, text?.CaptureGraphValues() as GameplayFloatingTextData, application) { }
        internal GameplayUnityCuePreview(GameplayPresentationCueData cue,GameplayFloatingTextData text,GameplayEffectApplicationContext application)
        {
            try{
                var build=new GameplayAuthoringBuildContext();
                if(cue!=null){
                    _values = cue;
                    _cue=cue.Build(build);
                    if (!_cue.RecycleWhenParticlesFinish)
                        Duration = Sample(_cue.Duration, cue.Duration, _cue.Clip ? _cue.Clip.length : 1, "时限", positive: true);
                    if (_cue.Intensity != null)
                        _intensity = Mathf.Max(0, Sample(_cue.Intensity, cue.Intensity, 1, "强度"));
                }
                else{_text=text.Build(build);Duration=_text.Duration;}
                if(_cue?.RecycleWhenParticlesFinish!=true&&(!float.IsFinite(Duration)||Duration<=0))
                    throw new ArithmeticException("Presentation duration must be positive.");
                if(_cue?.Kind==GameplayPresentationCueAsset.CueKind.Audio)return;
                var prefab=_cue!=null?_cue.Prefab:_text.Prefab;
                if(prefab==null)throw new InvalidOperationException("Select an existing presentation prefab.");
                Subject=UnityEngine.Object.Instantiate(prefab);Subject.hideFlags=HideFlags.HideAndDontSave;
                _start=_cue!=null?_cue.Offset:_text.Offset;Subject.transform.position=_start;
                _preview=new PreviewRenderUtility();_preview.AddSingleGO(Subject);
                _preview.camera.cameraType=CameraType.Game;_preview.camera.orthographic=true;
                _preview.camera.clearFlags=CameraClearFlags.Color;_preview.camera.backgroundColor=new Color(.12f,.14f,.18f);
                _preview.camera.nearClipPlane=.01f;_preview.camera.farClipPlane=100;
                _preview.lights[0].intensity=1;_preview.lights[0].transform.rotation=Quaternion.Euler(40,40,0);
                _particles=Subject.GetComponentsInChildren<ParticleSystem>(true);
                _renderers = Subject.GetComponentsInChildren<Renderer>(true);
                if(_cue?.RecycleWhenParticlesFinish==true)Duration=EstimateParticlePreviewDuration();
                foreach(var particles in _particles){var main=particles.main;main.stopAction=ParticleSystemStopAction.None;particles.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);}
                if(_text!=null){
                    _widget=Subject.GetComponent<GameplayFloatingTextWidget>();
                    _widget.OpenStandalone(new GameplayFloatingTextContent("PREVIEW",text.DamageColor));
                    var canvas=Subject.GetComponent<Canvas>();if(canvas!=null)canvas.worldCamera=_preview.camera;
                }
                _spatial = false;
                Seek(0,true);
                var bounds=new Bounds(_start,Vector3.one);
                foreach(var renderer in _renderers)bounds.Encapsulate(renderer.bounds);
                _materialCameraPosition = bounds.center + new Vector3(0,0,-10);
                _materialCameraSize = Mathf.Max(.7f,bounds.extents.y*1.3f,bounds.extents.x);
                _spatial = SessionState.GetBool("FRFrame.CuePreview.Spatial", false);
            }catch{Dispose();throw;}

            float Sample(IGameplayFloatValue value, GameplayFloatValueAuthoring source, float fallback, string label, bool positive = false)
            {
                try
                {
                    float result = GameplayValueEvaluator.Evaluate(value, application);
                    if (!positive || result > 0 || source is GameplayConstantFloatValueAuthoring) return result;
                    _valueNote += label + "在当前预览输入下不为正，示意使用 " + fallback + "。\n";
                }
                catch (Exception error) when (!(source is GameplayConstantFloatValueAuthoring) &&
                    (error is InvalidOperationException || error is ArithmeticException || error is System.Collections.Generic.KeyNotFoundException))
                {
                    _valueNote += label + "缺少有效预览输入，示意使用 " + fallback + "。\n";
                }
                return fallback;
            }
        }
        internal void BindSource(GameplaySourceHandle source, bool shared) { _source = source; _shared = shared; }

        private GameplaySkillDocumentAsset WritableDocument =>
            !_shared && !EditorApplication.isPlayingOrWillChangePlaymode &&
            _source?.Owner is GameplaySkillDocumentAsset document && !GameplaySkillAuthoringCommands.IsReadOnly(document)
                ? document : null;

        internal void Seek(float seconds,bool started)
        {
            if(Subject==null)return;
            var end = End;
            bool visible=started&&(_cue?.RecycleWhenParticlesFinish==true||seconds<Duration) &&
                (!_spatial || _cue == null || !end.HasValue || (end.Value - Origin).sqrMagnitude > .000001f);
            Subject.SetActive(visible);if(!visible)return;
            if (_cue != null)
            {
                if (_spatial)
                {
                    var pose = Pose;
                    Subject.transform.SetPositionAndRotation(pose.Position, pose.Rotation);
                    Subject.transform.localScale = pose.Scale;
                }
                else
                {
                    Subject.transform.SetPositionAndRotation(_start, Quaternion.identity);
                    Subject.transform.localScale = _cue.Prefab.transform.localScale;
                }
            }
            foreach(var particles in _particles){particles.Simulate(seconds,false,true,true);particles.Pause(false);}
            if(_widget!=null){Subject.transform.position=_start+Vector3.up*(seconds*_text.RiseSpeed);_widget.SetOpacity(1-seconds/Duration);}
        }
        // The standalone scrubber needs a finite range. Runtime uses actual particle completion.
        private float EstimateParticlePreviewDuration()
        {
            float duration=1;
            foreach(var particles in _particles){
                if(!particles.gameObject.activeInHierarchy)continue;
                var main=particles.main;
                var trails=particles.trails;
                float lifetime=Peak(main.startLifetime);
                if(trails.enabled&&!trails.dieWithParticles)lifetime+=lifetime*Peak(trails.lifetime);
                duration=Mathf.Max(duration,(Peak(main.startDelay)+main.duration+lifetime)/main.simulationSpeed);
            }
            return duration;

            float Peak(ParticleSystem.MinMaxCurve value)
            {
                if(value.mode==ParticleSystemCurveMode.Constant||value.mode==ParticleSystemCurveMode.TwoConstants)
                    return value.constantMax;
                float max=value.curveMax.keys.Select(k=>k.value).DefaultIfEmpty(0).Max();
                if(value.mode==ParticleSystemCurveMode.TwoCurves)
                    max=Mathf.Max(max,value.curveMin.keys.Select(k=>k.value).DefaultIfEmpty(0).Max());
                return max*value.curveMultiplier;
            }
        }
        internal void DrawControls()
        {
            if(_cue?.RecycleWhenParticlesFinish==true)
                EditorGUILayout.LabelField("播完回收 · 预览范围估算",Duration.ToString("0.###")+" 秒");
            else EditorGUILayout.LabelField("素材预览时长",Duration.ToString("0.###")+" 秒");
            if (_cue == null)
            {
                EditorGUILayout.LabelField("飘字为样式预览；实际数字来自已提交的战斗结果。",EditorStyles.wordWrappedMiniLabel);
                return;
            }
            if (!string.IsNullOrEmpty(_valueNote)) EditorGUILayout.HelpBox(_valueNote.TrimEnd(), MessageType.Info);
            if(_cue?.Kind==GameplayPresentationCueAsset.CueKind.Audio){
                using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField("音频素材",_cue.Clip,typeof(AudioClip),false);
                EditorGUILayout.LabelField("试听原 Clip 一次。实际播放使用配置音量、音高、循环及宿主音频混音。",EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField(_cue.OwnedByAbility ? "施放结束会停止本音频，包括仍在播放的尾音。" : "一次性尾音按 Cue 自己的回收时限播放。", EditorStyles.wordWrappedMiniLabel);
                if(GUILayout.Button("试听现有音频（一次）")){
                    var play=AudioMethod("PlayPreviewClip");
                    if(play==null)throw new InvalidOperationException("Installed Editor audio audition is unavailable.");
                    play.Invoke(null,new object[]{_cue.Clip,0,false});
                    _auditioning = true;
                }
            }
            else
            {
                bool spatial = GUILayout.Toolbar(_spatial ? 1 : 0, new[] { "素材预览", "场景相对预览" }) == 1;
                if (spatial != _spatial) { CancelInteraction(); _spatial = spatial; SessionState.SetBool("FRFrame.CuePreview.Spatial", spatial); }
                if (_spatial)
                {
                    EditorGUILayout.HelpBox("Editor 示意。修改正式参数后请保存，进入 Scene / 调试重新试装。", MessageType.Info);
                    _owner = AnchorField("模拟 Owner", _owner, "Owner");
                    _target = AnchorField("模拟 Target", _target, "Target");
                    if (_cue.Origin == PositionSource.ContextPoint) _point = AnchorField("模拟 Point", _point, "Point");
                    bool facingLeft = EditorGUILayout.Toggle("施法者朝左", _facingLeft);
                    if (facingLeft != _facingLeft) SessionState.SetBool("FRFrame.CuePreview.FacingLeft", _facingLeft = facingLeft);
                    float height = EditorGUILayout.Slider("角色参考高度", _actorHeight, .2f, 4);
                    if (height != _actorHeight) SessionState.SetFloat("FRFrame.CuePreview.ActorHeight", _actorHeight = height);
                    if (UsesBinding) _binding = AnchorField("模拟挂点偏移", _binding, "Binding");
                    if (End.HasValue)
                        EditorGUILayout.LabelField($"两点距离 {Vector3.Distance(Origin, End.Value):0.##}。Scale X 乘距离，Y 调宽度。\nCallbackSegment 的实机起点与命中点由回调提供。", EditorStyles.wordWrappedMiniLabel);
                    else if (_cue.Follow)
                        EditorGUILayout.LabelField(_cue.Origin == PositionSource.ContextPoint
                            ? "ContextPoint 是固定世界地点，没有跟随对象。"
                            : "跟随所选 Actor 或挂点。绿色虚线标出跟随锚点。", EditorStyles.wordWrappedMiniLabel);
                    if (WritableDocument) EditorGUILayout.LabelField("拖动中心十字调偏移，拖动 X / Y 方块调缩放。Esc 取消。", EditorStyles.wordWrappedMiniLabel);
                }
            }
            DrawSourceFields();
        }

        private static MethodInfo AudioMethod(string name) => typeof(AudioImporter).Assembly
            .GetType("UnityEditor.AudioUtil")?.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        private static Vector3 AnchorField(string title, Vector3 current, string key)
        {
            var value = EditorGUILayout.Vector3Field(title, current);
            if (value != current) SessionState.SetVector3("FRFrame.CuePreview." + key, value);
            return value;
        }

        private void DrawSourceFields()
        {
            if (!WritableDocument)
            {
                EditorGUILayout.LabelField(_shared ? "共享 Cue · 当前技能只读" : "当前来源只读", EditorStyles.wordWrappedMiniLabel);
                if (_shared && _source?.Owner is UnityEngine.Object asset && GUILayout.Button("定位共享 Cue 来源"))
                { Selection.activeObject = asset; EditorGUIUtility.PingObject(asset); }
                using (new EditorGUI.DisabledScope(true))
                {
                    if (_cue.Kind == GameplayPresentationCueAsset.CueKind.Vfx)
                    {
                        EditorGUILayout.Vector3Field("偏移", _cue.Offset);
                        EditorGUILayout.Vector3Field("缩放", _cue.Scale);
                        EditorGUILayout.Toggle("跟随", _cue.Follow);
                        EditorGUILayout.Toggle("朝向镜像", _cue.MirrorFacing);
                        EditorGUILayout.TextField("挂点", _cue.Binding ?? "");
                    }
                    else EditorGUILayout.LabelField($"音量 {_cue.Volume:0.##} / 音高 {_cue.Pitch:0.##} / 循环 {(_cue.AudioLoop ? "开" : "关")}");
                }
                return;
            }
            if (!_source.TryResolve(out string path)) return;
            var document = WritableDocument;
            using var serialized = new SerializedObject(document);
            var cue = serialized.FindProperty(path);
            var paths = new System.Collections.Generic.List<string>();
            Draw("Origin", "位置来源");
            if (_cue.Kind == GameplayPresentationCueAsset.CueKind.Vfx)
            {
                Draw("Offset", "偏移");
                Draw("Scale", "缩放");
                Draw("Binding", "挂点");
                using (new EditorGUI.DisabledScope(End.HasValue)) { Draw("Follow", "跟随"); Draw("MirrorFacing", "朝向镜像"); }
                Draw("RecycleWhenParticlesFinish", "粒子播完回收");
            }
            else
            {
                Draw("Volume", "音量"); Draw("Pitch", "音高"); Draw("AudioLoop", "循环");
            }
            Draw("OwnedByAbility", "随技能结束清理");
            if (!_cue.RecycleWhenParticlesFinish)
            {
                if (_values.Duration is GameplayConstantFloatValueAuthoring) Draw("Duration.Value", "回收时限（秒）");
                else EditorGUILayout.LabelField("回收时限来自数值表达式，请在业务参数中编辑其来源。", EditorStyles.wordWrappedMiniLabel);
            }
            if (paths.Count > 0) GameplaySkillFieldSchema.ApplyFields(serialized, document, false, paths);

            void Draw(string name, string label)
            {
                var property = cue.FindPropertyRelative(name);
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(property, new GUIContent(label));
                if (EditorGUI.EndChangeCheck()) paths.Add(property.propertyPath);
            }
        }

        private bool UsesBinding => !string.IsNullOrEmpty(_cue.Binding) &&
            (_cue.Origin == PositionSource.Owner || _cue.Origin == PositionSource.PrimaryTarget);
        private Vector3 Origin => _cue.Origin switch
        {
            PositionSource.ContextPoint => _point,
            PositionSource.TargetToOwner => _target,
            PositionSource.PrimaryTarget => _target + (UsesBinding ? _binding : Vector3.zero),
            _ => _owner + (UsesBinding ? _binding : Vector3.zero)
        };
        private Vector3? End => _cue?.Origin switch
        {
            PositionSource.CallbackSegment => _target,
            PositionSource.TargetToOwner => _owner,
            _ => null
        };
        private (Vector3 Position, Quaternion Rotation, Vector3 Scale) Pose => GameplayUnityAnimationController.ResolveCueGeometry(
            Origin, End, _hotControl != 0 && _dragAxis == 0 ? _draft : _cue.Offset,
            _hotControl != 0 && _dragAxis != 0 ? _draft : _cue.Scale, _cue.MirrorFacing && _facingLeft, _intensity);
        internal void Render(Rect viewport)
        {
            if(_preview==null){EditorGUI.HelpBox(viewport,"选择实际音频素材；可试听或在真实Scene安装运行。",MessageType.Info);return;}
            if (_spatial && _cue != null) DrawSpatial(viewport);
            else
            {
                _preview.camera.transform.position = _materialCameraPosition;
                _preview.camera.orthographicSize = _materialCameraSize;
                _preview.camera.ResetAspect();
            }
            if(Event.current.type!=EventType.Repaint || _preview == null)return;
            _preview.BeginPreview(viewport,GUIStyle.none);_preview.camera.Render();
            GUI.DrawTexture(viewport,_preview.EndPreview(),ScaleMode.StretchToFill,false);
            if (_spatial && _cue != null) DrawSpatialOverlay(viewport);
        }
        private void DrawSpatial(Rect viewport)
        {
            if (viewport.width <= 1 || viewport.height <= 1) return;
            var camera = _preview.camera;
            var pose = Pose;
            if (_hotControl == 0)
            {
                var frame = new Bounds(_owner, Vector3.one);
                frame.Encapsulate(_target); frame.Encapsulate(_owner + Vector3.up * _actorHeight);
                frame.Encapsulate(_target + Vector3.up * _actorHeight);
                frame.Encapsulate(Origin); frame.Encapsulate(pose.Position);
                camera.aspect = viewport.width / viewport.height;
                camera.transform.position = frame.center + Vector3.back * 10;
                camera.orthographicSize = Mathf.Max(1, frame.extents.y, frame.extents.x / camera.aspect) * 1.45f;
            }
            int control = GUIUtility.GetControlID(FocusType.Passive, viewport);
            var evt = Event.current;
            if (_hotControl != 0 && (GUIUtility.hotControl != _hotControl || evt.type == EventType.Ignore ||
                (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)))
            { CancelInteraction(); if (evt.type == EventType.KeyDown) evt.Use(); GUI.changed = true; return; }
            Vector2 center = Project(viewport, pose.Position);
            var xAxis = HandleAxis(viewport, pose.Rotation * Vector3.right);
            var yAxis = HandleAxis(viewport, pose.Rotation * Vector3.up);
            if (evt.type == EventType.MouseDown && evt.button == 0 && viewport.Contains(evt.mousePosition) && WritableDocument)
            {
                int axis = Vector2.Distance(evt.mousePosition, center) < 10 ? 0 :
                    Vector2.Distance(evt.mousePosition, center + xAxis * 34) < 9 ? 1 :
                    Vector2.Distance(evt.mousePosition, center + yAxis * 34) < 9 ? 2 : -1;
                if (axis < 0) return;
                _dragAxis = axis;
                _dragBasis = axis == 1 ? xAxis : yAxis;
                _dragStart = _draft = axis == 0 ? _cue.Offset : _cue.Scale;
                _dragMouse = evt.mousePosition;
                _hotControl = GUIUtility.hotControl = control;
                evt.Use();
            }
            else if (_hotControl == control && evt.type == EventType.MouseDrag)
            {
                float unitsPerPixel = 2 * camera.orthographicSize / viewport.height;
                var delta = evt.mousePosition - _dragMouse;
                _draft = _dragStart;
                if (_dragAxis == 0)
                    _draft += new Vector3(!End.HasValue && _cue.MirrorFacing && _facingLeft ? -delta.x : delta.x, -delta.y, 0) * unitsPerPixel;
                else
                {
                    float divisor = _dragAxis == 1 && End.HasValue ? Vector3.Distance(Origin, End.Value) : 1;
                    _draft[_dragAxis - 1] += Vector2.Dot(delta, _dragBasis) * unitsPerPixel / Mathf.Max(.001f, divisor);
                }
                evt.Use(); GUI.changed = true;
            }
            else if (_hotControl == control && evt.rawType == EventType.MouseUp && evt.button == 0)
            {
                var document = WritableDocument;
                string field = _dragAxis == 0 ? "Offset" : "Scale";
                var value = _draft;
                CancelInteraction(); evt.Use(); GUI.changed = true;
                if (document && value != _dragStart && _source.TryResolve(out string path))
                {
                    using var serialized = new SerializedObject(document);
                    var property = serialized.FindProperty(path).FindPropertyRelative(field);
                    property.vector3Value = value;
                    Undo.IncrementCurrentGroup();
                    Undo.SetCurrentGroupName("调整 Cue " + (field == "Offset" ? "偏移" : "缩放"));
                    GameplaySkillFieldSchema.ApplyFields(serialized, document, false, new[] { property.propertyPath });
                }
            }
        }

        private Vector2 Project(Rect viewport, Vector3 point)
        {
            var uv = _preview.camera.WorldToViewportPoint(point);
            return new Vector2(viewport.x + uv.x * viewport.width, viewport.y + (1 - uv.y) * viewport.height);
        }
        private Vector2 HandleAxis(Rect viewport, Vector3 axis) => (Project(viewport, axis) - Project(viewport, Vector3.zero)).normalized;

        private void DrawSpatialOverlay(Rect viewport)
        {
            if (viewport.width <= 1 || viewport.height <= 1) return;
            GUI.BeginGroup(viewport);
            viewport.position = Vector2.zero;
            Handles.BeginGUI();
            Color previous = Handles.color;
            var pose = Pose;
            var owner = Project(viewport, _owner);
            Mark(_owner, "Owner", new Color(.35f,.85f,1), _actorHeight);
            Mark(_target, "Target", new Color(1,.6f,.35f), _actorHeight);
            Mark(Origin, UsesBinding ? "模拟挂点" : "原点", Color.white);
            if (End.HasValue) { Handles.color = Color.yellow; Handles.DrawAAPolyLine(2, Project(viewport, Origin), Project(viewport, End.Value)); }
            Handles.color = Color.gray;
            Handles.DrawAAPolyLine(2, owner, Project(viewport, _owner + Vector3.right));
            Handles.DrawAAPolyLine(2, owner, Project(viewport, _owner + Vector3.up));
            GUI.Label(new Rect(owner.x + 6, owner.y + 3, 105, 18), "世界轴 · 1 单位", EditorStyles.miniLabel);
            var center = Project(viewport, pose.Position);
            Handles.color = Color.green;
            if (_cue.Follow && (_cue.Origin == PositionSource.Owner || _cue.Origin == PositionSource.PrimaryTarget))
                Handles.DrawDottedLine(Project(viewport, Origin), center, 4);
            Handles.DrawAAPolyLine(2, center - Vector2.right * 6, center + Vector2.right * 6);
            Handles.DrawAAPolyLine(2, center - Vector2.up * 6, center + Vector2.up * 6);
            if (Subject.activeSelf && _renderers.Length > 0)
            {
                var bounds = _renderers[0].bounds;
                foreach (var renderer in _renderers) bounds.Encapsulate(renderer.bounds);
                var min = Project(viewport, bounds.min);
                var max = Project(viewport, bounds.max);
                Handles.color = new Color(.7f, 1, .8f, .7f);
                Handles.DrawAAPolyLine(1, min, new Vector2(max.x,min.y), max, new Vector2(min.x,max.y), min);
            }
            if (WritableDocument)
            {
                DrawScaleHandle(pose.Rotation * Vector3.right, "X", new Color(1,.55f,.42f));
                DrawScaleHandle(pose.Rotation * Vector3.up, "Y", new Color(.6f,1,.55f));
            }
            GUI.Label(new Rect(5, 3, viewport.width - 10, 18), $"生成位置 {pose.Position.ToString("F2")}", EditorStyles.miniLabel);
            Handles.color = previous;
            Handles.EndGUI();
            GUI.EndGroup();

            void Mark(Vector3 point, string label, Color color, float height = 0)
            {
                var bottom = Project(viewport, point);
                var top = Project(viewport, point + Vector3.up * height);
                Handles.color = color;
                if (height > 0) Handles.DrawAAPolyLine(2, bottom, top);
                EditorGUI.DrawRect(new Rect(bottom.x - 3, bottom.y - 3, 6, 6), color);
                GUI.Label(new Rect(top.x + 5, top.y - 16, 80, 18), label, EditorStyles.miniLabel);
            }
            void DrawScaleHandle(Vector3 direction, string label, Color color)
            {
                var end = center + HandleAxis(viewport, direction) * 34;
                Handles.color = color;
                Handles.DrawAAPolyLine(1, center, end);
                EditorGUI.DrawRect(new Rect(end.x - 4,end.y - 4,8,8), color);
                GUI.Label(new Rect(end.x + 5,end.y - 8,18,18), label, EditorStyles.miniLabel);
            }
        }
        internal void CancelInteraction()
        {
            if (_hotControl == 0) return;
            if (GUIUtility.hotControl == _hotControl) GUIUtility.hotControl = 0;
            _hotControl = 0;
        }
        public void Dispose()
        {
            CancelInteraction();
            if (_auditioning) { AudioMethod("StopAllPreviewClips")?.Invoke(null, null); _auditioning = false; }
            _preview?.Cleanup();_preview=null;
            if(Subject!=null)UnityEngine.Object.DestroyImmediate(Subject);Subject=null;
        }
    }
}
