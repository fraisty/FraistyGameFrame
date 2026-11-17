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
        private string _auditionPath;
        private double _auditionEnd;
        private string _auditionError;
        private Material _tintMaterial;
        private (Animator Animator, AnimationClip Clip)[] _animators=Array.Empty<(Animator,AnimationClip)>();
        private bool _unsupportedAnimator;
        private bool _xz=SessionState.GetBool("FRFrame.CuePreview.XZ",false);
        private float _zoom=1;
        private Vector3 _pan;
        private int _panControl;
        private GameplayAnimationAsset _modelSource;
        private GameObject _ownerModel,_targetModel;
        private bool _bindingFound;
        private string _referenceError;
        private string _dragSourceKey;
        private uint _dragSourceHash;
        private float _sampleSeconds;
        private bool _sampleStarted;
        private bool _facingLeft = SessionState.GetBool("FRFrame.CuePreview.FacingLeft", false);
        private bool _spatial = SessionState.GetBool("FRFrame.CuePreview.Spatial", true);
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
                if (_cue?.Kind==GameplayPresentationCueAsset.CueKind.Tint)
                {
                    if (_cue.TintFadeIn+_cue.TintFadeOut>Duration+.000001f) throw new InvalidOperationException("淡入与淡出超过闪色时限。");
                    Subject=GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    UnityEngine.Object.DestroyImmediate(Subject.GetComponent<Collider>());
                    _tintMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit")??Shader.Find("Unlit/Color"));
                    Subject.GetComponent<Renderer>().sharedMaterial=_tintMaterial;
                }
                else
                {
                    var prefab=_cue!=null?_cue.Prefab:_text.Prefab;
                    if(prefab==null)throw new InvalidOperationException("Select an existing presentation prefab.");
                    Subject=UnityEngine.Object.Instantiate(prefab);
                }
                Subject.hideFlags=HideFlags.HideAndDontSave;
                _start=_cue!=null?_cue.Offset:_text.Offset;Subject.transform.position=_start;
                _preview=new PreviewRenderUtility();_preview.AddSingleGO(Subject);
                _preview.camera.cameraType=CameraType.Game;_preview.camera.orthographic=true;
                _preview.camera.clearFlags=CameraClearFlags.Color;_preview.camera.backgroundColor=new Color(.12f,.14f,.18f);
                _preview.camera.nearClipPlane=.01f;_preview.camera.farClipPlane=100;
                _preview.lights[0].intensity=1;_preview.lights[0].transform.rotation=Quaternion.Euler(40,40,0);
                _particles=Subject.GetComponentsInChildren<ParticleSystem>(true);
                _renderers = Subject.GetComponentsInChildren<Renderer>(true);
                var animators=new System.Collections.Generic.List<(Animator,AnimationClip)>();
                foreach(var animator in Subject.GetComponentsInChildren<Animator>(true))
                {
                    var clips=animator.runtimeAnimatorController?.animationClips;
                    if(clips?.Length==1)animators.Add((animator,clips[0]));
                    else if(clips?.Length>1)_unsupportedAnimator=true;
                    animator.enabled=false;
                }
                _animators=animators.ToArray();
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
                _spatial = SessionState.GetBool("FRFrame.CuePreview.Spatial", true);
                string reference=AssetDatabase.GUIDToAssetPath(SessionState.GetString("FRFrame.CuePreview.Model",""));
                if(!string.IsNullOrEmpty(reference))SetReference(AssetDatabase.LoadAssetAtPath<GameplayAnimationAsset>(reference));
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
        internal void BindSource(GameplaySourceHandle source, bool shared)
        {
            if (_source?.Key != source?.Key || _shared != shared) CancelInteraction();
            _source = source; _shared = shared;
        }

        private GameplaySkillDocumentAsset WritableDocument =>
            !_shared && !EditorApplication.isPlayingOrWillChangePlaymode &&
            _source?.Owner is GameplaySkillDocumentAsset document && !GameplaySkillAuthoringCommands.IsReadOnly(document)
                ? document : null;

        internal void Seek(float seconds,bool started)
        {
            _sampleSeconds=seconds;_sampleStarted=started;
            PositionSubject();
            if(!Subject || !Subject.activeSelf)return;
            foreach(var particles in _particles){particles.Simulate(seconds,false,true,true);particles.Pause(false);}
            foreach(var item in _animators)item.Clip.SampleAnimation(item.Animator.gameObject,Mathf.Min(seconds,item.Clip.length));
            if(_widget!=null){Subject.transform.position=_start+Vector3.up*(seconds*_text.RiseSpeed);_widget.SetOpacity(1-seconds/Duration);}
        }

        private void PositionSubject()
        {
            if(Subject==null)return;
            float seconds=_sampleSeconds;bool started=_sampleStarted;
            var end = End;
            bool visible=_tintMaterial || started&&(_cue?.RecycleWhenParticlesFinish==true||seconds<Duration) &&
                (!_spatial || _cue == null || !end.HasValue || (end.Value - Origin).sqrMagnitude > .000001f);
            Subject.SetActive(visible);if(!visible)return;
            if (_cue != null)
            {
                if (_tintMaterial)
                {
                    Subject.transform.position=(_spatial ? Origin : Vector3.zero)+Vector3.up*_actorHeight*.5f;
                    Subject.transform.localScale=Vector3.one*(_actorHeight*.5f);
                    float weight=started?GameplayUnityAnimationController.TintWeight(seconds,Duration,_cue.TintFadeIn,_cue.TintFadeOut):0;
                    _tintMaterial.color=Color.Lerp(new Color(.55f,.65f,.8f),_cue.TintColor,Mathf.Clamp01(weight*_intensity*_cue.TintColor.a));
                }
                else if (_spatial)
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
            if (_cue.Intensity != null) EditorGUILayout.LabelField("当前模拟强度", _intensity.ToString("0.###"));
            if(_cue?.Kind==GameplayPresentationCueAsset.CueKind.Audio){
                using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField("音频素材",_cue.Clip,typeof(AudioClip),false);
                float natural=_cue.Clip.length/_cue.Pitch;
                string end=_cue.AudioLoop || natural>Duration ? $"配置时限 {Duration:0.###}s" : $"自然播完 {natural:0.###}s";
                EditorGUILayout.LabelField(end+(_cue.OwnedByAbility?" · 技能更早结束时截断":""),EditorStyles.wordWrappedMiniLabel);
                using(new EditorGUILayout.HorizontalScope())
                {
                    if(GUILayout.Button("原 Clip"))Audition(false);
                    if(GUILayout.Button("按配置重播"))Audition(true);
                    if(GUILayout.Button("停止"))StopAudition();
                }
                if(_auditionError!=null)EditorGUILayout.HelpBox(_auditionError,MessageType.Warning);
            }
            else
            {
                bool spatial = GUILayout.Toolbar(_spatial ? 1 : 0, new[] { "素材预览", "场景相对预览" }) == 1;
                if (spatial != _spatial) { CancelInteraction(); _spatial = spatial; SessionState.SetBool("FRFrame.CuePreview.Spatial", spatial); }
                if (_spatial)
                {
                    bool xz=EditorGUILayout.Popup("观察平面",_xz?1:0,new[]{"XY","XZ"})==1;
                    if(xz!=_xz){CancelInteraction();_xz=xz;_pan=Vector3.zero;SessionState.SetBool("FRFrame.CuePreview.XZ",xz);}
                    UnityEngine.Object reference=EditorGUILayout.ObjectField("参考角色",_modelSource,typeof(GameplayAnimationAsset),false);
                    if(reference!=_modelSource)
                    {
                        SessionState.SetString("FRFrame.CuePreview.Model",reference?AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(reference)):"");
                        SetReference(reference as GameplayAnimationAsset);
                    }
                    if(_referenceError!=null)EditorGUILayout.HelpBox(_referenceError,MessageType.Warning);
                    EditorGUILayout.LabelField("Shift 拖动 Owner / Target · 滚轮缩放 · 中键平移",EditorStyles.wordWrappedMiniLabel);
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
            if(_unsupportedAnimator)EditorGUILayout.LabelField("多状态 Animator 需在实机检查；当前仅预览该对象的静态姿态。",EditorStyles.wordWrappedMiniLabel);
            DrawSourceFields();
        }

        private static MethodInfo AudioMethod(string name) => typeof(AudioImporter).Assembly
            .GetType("UnityEditor.AudioUtil")?.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        private void Audition(bool configured)
        {
            StopAudition();
            _auditionError=null;
            var play=AudioMethod("PlayPreviewClip");
            if(play==null){_auditionError="此 Editor 未提供音频试听。";return;}
            var clip=_cue.Clip;
            double length=clip.length;
            if(configured)
            {
                if(clip.loadState==AudioDataLoadState.Unloaded)clip.LoadAudioData();
                if(clip.loadState==AudioDataLoadState.Loading){_auditionError="音频正在载入，请再点一次按配置重播。";return;}
                length=Math.Min(12,_cue.AudioLoop?Duration:Math.Min(Duration,clip.length/_cue.Pitch));
                var input=new float[clip.samples*clip.channels];
                if(clip.loadState!=AudioDataLoadState.Loaded||!clip.GetData(input,0)){_auditionError="配置试听需要可读取 PCM 的素材。当前仍可试听原 Clip。";return;}
                int frames=Math.Max(1,Mathf.CeilToInt((float)length*clip.frequency));
                var output=new float[frames*clip.channels];
                float volume=Mathf.Clamp01(_cue.Volume*_intensity);
                for(int i=0;i<frames;i++)
                {
                    double position=i*(double)_cue.Pitch;
                    if(_cue.AudioLoop)position%=clip.samples;
                    int first=Math.Min((int)position,clip.samples-1);
                    int next=_cue.AudioLoop?(first+1)%clip.samples:Math.Min(first+1,clip.samples-1);
                    for(int channel=0;channel<clip.channels;channel++)
                        output[i*clip.channels+channel]=Mathf.Lerp(input[first*clip.channels+channel],input[next*clip.channels+channel],(float)(position-first))*volume;
                }
                // AudioUtil reloads samples from import data and silences non-imported AudioClips.
                _auditionPath="Assets/__FRFrameCueAudition_"+Guid.NewGuid().ToString("N")+".wav";
                try
                {
                    using(var wave=new System.IO.BinaryWriter(System.IO.File.Create(_auditionPath)))
                    {
                        int byteCount=output.Length*sizeof(short);
                        wave.Write(0x46464952); wave.Write(36+byteCount); wave.Write(0x45564157);
                        wave.Write(0x20746d66); wave.Write(16); wave.Write((short)1); wave.Write((short)clip.channels);
                        wave.Write(clip.frequency); wave.Write(clip.frequency*clip.channels*sizeof(short));
                        wave.Write((short)(clip.channels*sizeof(short))); wave.Write((short)16);
                        wave.Write(0x61746164); wave.Write(byteCount);
                        foreach(float sample in output)wave.Write((short)Mathf.RoundToInt(Mathf.Clamp(sample,-1,1)*short.MaxValue));
                    }
                    AssetDatabase.ImportAsset(_auditionPath,ImportAssetOptions.ForceSynchronousImport);
                    clip=AssetDatabase.LoadAssetAtPath<AudioClip>(_auditionPath);
                    if(!clip)throw new InvalidOperationException("Unity 无法载入配置试听的音频。");
                }
                catch(Exception error){StopAudition();_auditionError=error.Message;return;}
            }
            try{play.Invoke(null,new object[]{clip,0,false});_auditioning=true;}
            catch(TargetInvocationException error){StopAudition();_auditionError=error.InnerException?.Message??error.Message;return;}
            _auditionEnd=EditorApplication.timeSinceStartup+length;
            EditorApplication.update+=StopExpiredAudition;
            AssemblyReloadEvents.beforeAssemblyReload+=StopAudition;
        }

        private void StopExpiredAudition()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.timeSinceStartup>=_auditionEnd)StopAudition();
        }

        private void StopAudition()
        {
            if(_auditioning)AudioMethod("StopAllPreviewClips")?.Invoke(null,null);
            _auditioning=false;
            EditorApplication.update-=StopExpiredAudition;
            AssemblyReloadEvents.beforeAssemblyReload-=StopAudition;
            if(_auditionPath!=null)
            {
                if(!AssetDatabase.DeleteAsset(_auditionPath))System.IO.File.Delete(_auditionPath);
                _auditionPath=null;
            }
        }

        private void SetReference(GameplayAnimationAsset source)
        {
            if(_ownerModel)UnityEngine.Object.DestroyImmediate(_ownerModel);
            if(_targetModel)UnityEngine.Object.DestroyImmediate(_targetModel);
            _modelSource=source;
            _referenceError=null;
            if(!source)return;
            try
            {
                _ownerModel=source.CreatePreviewSubject();_targetModel=source.CreatePreviewSubject();
                _preview.AddSingleGO(_ownerModel);_preview.AddSingleGO(_targetModel);
            }
            catch(Exception error)
            {
                SetReference(null);_referenceError=error.Message;
            }
        }

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
                if (_shared && _source?.Owner is UnityEngine.Object asset && GUILayout.Button(new GUIContent("定位共享 Cue 来源","在 Project Inspector 编辑共享资产会影响它的所有引用。")))
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
            Draw("Kind", "表现类型");
            Draw("Origin", "位置来源");
            if (_cue.Kind == GameplayPresentationCueAsset.CueKind.Vfx)
            {
                Draw("Offset", "偏移");
                Draw("Scale", "缩放");
                Draw("Binding", "挂点");
                using (new EditorGUI.DisabledScope(End.HasValue)) { Draw("Follow", "跟随"); Draw("MirrorFacing", "朝向镜像"); }
                Draw("RecycleWhenParticlesFinish", "粒子播完回收");
            }
            else if (_cue.Kind == GameplayPresentationCueAsset.CueKind.Audio)
            {
                Draw("Volume", "音量"); Draw("Pitch", "音高"); Draw("AudioLoop", "循环");
            }
            else
            {
                Draw("Binding", "目标挂点");Draw("TintColor", "闪色");
                Draw("TintFadeIn", "淡入（秒）");Draw("TintFadeOut", "淡出（秒）");
            }
            if (_values.Intensity is GameplayConstantFloatValueAuthoring) Draw("Intensity.Value", "强度");
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
                if(property==null)return;
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(property, new GUIContent(label));
                if (EditorGUI.EndChangeCheck()) paths.Add(property.propertyPath);
            }
        }

        private bool UsesBinding => !string.IsNullOrEmpty(_cue.Binding) &&
            (_cue.Origin == PositionSource.Owner || _cue.Origin == PositionSource.PrimaryTarget);
        private Vector3 OwnerPoint => _hotControl!=0&&_dragAxis==4?_draft:_owner;
        private Vector3 TargetPoint => _hotControl!=0&&_dragAxis==5?_draft:_target;
        private Vector3 Origin => _cue.Origin switch
        {
            PositionSource.ContextPoint => _point,
            PositionSource.TargetToOwner => TargetPoint,
            PositionSource.PrimaryTarget => BoundPoint(_targetModel,TargetPoint),
            _ => BoundPoint(_ownerModel,OwnerPoint)
        };
        private Vector3 BoundPoint(GameObject model,Vector3 position)
        {
            if(!UsesBinding)return position;
            var anchor=model?model.transform.Find(_cue.Binding):null;
            _bindingFound=anchor;
            return anchor?anchor.position:position+_binding;
        }
        private Vector3? End => _cue?.Origin switch
        {
            PositionSource.CallbackSegment => TargetPoint,
            PositionSource.TargetToOwner => OwnerPoint,
            _ => null
        };
        private (Vector3 Position, Quaternion Rotation, Vector3 Scale) Pose => GameplayUnityAnimationController.ResolveCueGeometry(
            Origin, End, _hotControl != 0 && _dragAxis == 0 ? _draft : _cue.Offset,
            _hotControl != 0 && _dragAxis != 0 ? _draft : _cue.Scale, _cue.MirrorFacing && _facingLeft, _intensity);
        internal void Render(Rect viewport)
        {
            if(_preview==null){EditorGUI.HelpBox(viewport,"选择实际音频素材；可试听或在真实Scene安装运行。",MessageType.Info);return;}
            if (_ownerModel) { _ownerModel.SetActive(_spatial);_ownerModel.transform.position=OwnerPoint; }
            if (_targetModel) { _targetModel.SetActive(_spatial);_targetModel.transform.position=TargetPoint; }
            if (GUI.Button(new Rect(viewport.x+4,viewport.y+3,66,18),"重置视角",EditorStyles.miniButton))
            { _zoom=1;_pan=Vector3.zero; }
            viewport.yMin+=23;
            var evt=Event.current;
            int viewControl=GUIUtility.GetControlID(FocusType.Passive,viewport);
            if(evt.type==EventType.ScrollWheel&&viewport.Contains(evt.mousePosition))
            { _zoom=Mathf.Clamp(_zoom*Mathf.Exp(-evt.delta.y*.09f),.2f,8);evt.Use();GUI.changed=true; }
            if(evt.type==EventType.MouseDown&&evt.button==2&&viewport.Contains(evt.mousePosition))
            { _panControl=GUIUtility.hotControl=viewControl;evt.Use(); }
            else if(evt.type==EventType.MouseDrag&&_panControl==viewControl)
            {
                var transform=_preview.camera.transform;
                _pan+=(-transform.right*evt.delta.x+transform.up*evt.delta.y)*(2*_preview.camera.orthographicSize/viewport.height);
                evt.Use();GUI.changed=true;
            }
            else if(evt.rawType==EventType.MouseUp&&_panControl==viewControl)
            { _panControl=GUIUtility.hotControl=0;evt.Use(); }
            if (_spatial && _cue != null) DrawSpatial(viewport);
            else
            {
                _preview.camera.transform.SetPositionAndRotation(_materialCameraPosition+_pan,Quaternion.identity);
                _preview.camera.orthographicSize = _materialCameraSize/_zoom;
                _preview.camera.ResetAspect();
            }
            if(Event.current.type!=EventType.Repaint || _preview == null)return;
            if(_widget==null)PositionSubject();
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
                camera.transform.SetPositionAndRotation(frame.center+_pan+(_xz?Vector3.up:Vector3.back)*10,
                    _xz?Quaternion.Euler(90,0,0):Quaternion.identity);
                camera.orthographicSize = Mathf.Max(1, _xz?frame.extents.z:frame.extents.y, frame.extents.x / camera.aspect) * 1.45f/_zoom;
            }
            int control = GUIUtility.GetControlID(FocusType.Passive, viewport);
            var evt = Event.current;
            if (_hotControl != 0 && (GUIUtility.hotControl != _hotControl || evt.type == EventType.Ignore ||
                (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)))
            { CancelInteraction(); if (evt.type == EventType.KeyDown) evt.Use(); GUI.changed = true; return; }
            Vector2 center = Project(viewport, pose.Position);
            var xAxis = HandleAxis(viewport, pose.Rotation * Vector3.right);
            var yAxis = HandleAxis(viewport, pose.Rotation * (_xz?Vector3.forward:Vector3.up));
            if (evt.type == EventType.MouseDown && evt.button == 0 && viewport.Contains(evt.mousePosition))
            {
                int axis = evt.shift&&Vector2.Distance(evt.mousePosition,Project(viewport,_owner))<13?4:
                    evt.shift&&Vector2.Distance(evt.mousePosition,Project(viewport,_target))<13?5:
                    Vector2.Distance(evt.mousePosition, center) < 10 ? 0 :
                    Vector2.Distance(evt.mousePosition, center + xAxis * 34) < 9 ? 1 :
                    Vector2.Distance(evt.mousePosition, center + yAxis * 34) < 9 ? (_xz?3:2) :
                    Vector2.Distance(evt.mousePosition, center + (xAxis+yAxis).normalized * 43) < 9 ? 6 : -1;
                if (axis < 0) return;
                if (axis is not (4 or 5))
                {
                    if(!WritableDocument||!_source.TryResolve(out string current))return;
                    using var origin=new SerializedObject(_source.Owner);
                    _dragSourceKey=_source.Key;
                    _dragSourceHash=origin.FindProperty(current).contentHash;
                }
                _dragAxis = axis;
                _dragBasis = axis == 6 ? (xAxis+yAxis).normalized : axis == 1 ? xAxis : yAxis;
                _dragStart = _draft = axis==4?_owner:axis==5?_target:axis==0?_cue.Offset:_cue.Scale;
                _dragMouse = evt.mousePosition;
                _hotControl = GUIUtility.hotControl = control;
                evt.Use();
            }
            else if (_hotControl == control && evt.type == EventType.MouseDrag)
            {
                float unitsPerPixel = 2 * camera.orthographicSize / viewport.height;
                var delta = evt.mousePosition - _dragMouse;
                _draft = _dragStart;
                if (_dragAxis is 0 or 4 or 5)
                    _draft += (camera.transform.right*delta.x-camera.transform.up*delta.y)*unitsPerPixel;
                if (_dragAxis==0&&!End.HasValue&&_cue.MirrorFacing&&_facingLeft) _draft.x=2*_dragStart.x-_draft.x;
                else if(_dragAxis==6) _draft*=(1+Vector2.Dot(delta,_dragBasis)*unitsPerPixel);
                else if(_dragAxis is 1 or 2 or 3)
                {
                    float divisor = _dragAxis == 1 && End.HasValue ? Vector3.Distance(Origin, End.Value) : 1;
                    _draft[_dragAxis - 1] += Vector2.Dot(delta, _dragBasis) * unitsPerPixel / Mathf.Max(.001f, divisor);
                }
                evt.Use(); GUI.changed = true;
            }
            else if (_hotControl == control && evt.rawType == EventType.MouseUp && evt.button == 0)
            {
                var document = WritableDocument;
                int axis=_dragAxis;
                string field = _dragAxis == 0 ? "Offset" : "Scale";
                var value = _draft;
                CancelInteraction(); evt.Use(); GUI.changed = true;
                if(axis==4){_owner=value;SessionState.SetVector3("FRFrame.CuePreview.Owner",_owner);return;}
                if(axis==5){_target=value;SessionState.SetVector3("FRFrame.CuePreview.Target",_target);return;}
                if (document && value != _dragStart && _source.Key==_dragSourceKey && _source.TryResolve(out string path))
                {
                    using var serialized = new SerializedObject(document);
                    var source=serialized.FindProperty(path);
                    if(source.contentHash!=_dragSourceHash)return;
                    var property = source.FindPropertyRelative(field);
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
            Mark(OwnerPoint, "Owner", new Color(.35f,.85f,1), _actorHeight);
            Mark(TargetPoint, "Target", new Color(1,.6f,.35f), _actorHeight);
            Mark(Origin, UsesBinding ? _bindingFound ? "实际挂点" : "模拟挂点" : "原点", Color.white);
            if (End.HasValue) { Handles.color = Color.yellow; Handles.DrawAAPolyLine(2, Project(viewport, Origin), Project(viewport, End.Value)); }
            Handles.color = Color.gray;
            Handles.DrawAAPolyLine(2, owner, Project(viewport, _owner + Vector3.right));
            Handles.DrawAAPolyLine(2, owner, Project(viewport, _owner + (_xz?Vector3.forward:Vector3.up)));
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
                var up=pose.Rotation*(_xz?Vector3.forward:Vector3.up);
                DrawScaleHandle(up, _xz?"Z":"Y", new Color(.6f,1,.55f));
                Handles.color=Color.white;
                Vector2 uniform=center+(HandleAxis(viewport,pose.Rotation*Vector3.right)+HandleAxis(viewport,up)).normalized*43;
                Handles.DrawSolidDisc(uniform,Vector3.forward,4);
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
            if (GUIUtility.hotControl == _hotControl || GUIUtility.hotControl == _panControl) GUIUtility.hotControl = 0;
            _hotControl = _panControl = 0;
        }
        public void Dispose()
        {
            CancelInteraction();
            StopAudition();
            SetReference(null);
            _preview?.Cleanup();_preview=null;
            if(Subject!=null)UnityEngine.Object.DestroyImmediate(Subject);Subject=null;
            if(_tintMaterial)UnityEngine.Object.DestroyImmediate(_tintMaterial);_tintMaterial=null;
        }
    }
}
