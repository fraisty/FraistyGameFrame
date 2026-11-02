using System;
using System.Linq;
using System.Reflection;
using FRFrame.Gameplay.Authoring;
using UnityEditor;
using UnityEngine;

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
        internal GameObject Subject {get;private set;}
        internal Camera Camera=>_preview?.camera;
        internal float Duration {get;private set;}
        internal GameplayUnityCuePreview(GameplayPresentationCueAsset cue,GameplayFloatingTextAsset text,GameplayEffectApplicationContext application)
            : this(cue?.CaptureGraphValues() as GameplayPresentationCueData, text?.CaptureGraphValues() as GameplayFloatingTextData, application) { }
        internal GameplayUnityCuePreview(GameplayPresentationCueData cue,GameplayFloatingTextData text,GameplayEffectApplicationContext application)
        {
            try{
                var build=new GameplayAuthoringBuildContext();
                if(cue!=null){_cue=cue.Build(build);Duration=GameplayValueEvaluator.Evaluate(_cue.Duration,application);}
                else{_text=text.Build(build);Duration=_text.Duration;}
                if(!float.IsFinite(Duration)||Duration<=0)throw new ArithmeticException("Presentation duration must be positive.");
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
                foreach(var particles in _particles){var main=particles.main;main.stopAction=ParticleSystemStopAction.None;particles.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);}
                if(_text!=null){
                    _widget=Subject.GetComponent<GameplayFloatingTextWidget>();
                    _widget.OpenStandalone(new GameplayFloatingTextContent("PREVIEW",text.DamageColor));
                    var canvas=Subject.GetComponent<Canvas>();if(canvas!=null)canvas.worldCamera=_preview.camera;
                }
                Seek(0,true);
                var bounds=new Bounds(_start,Vector3.one);
                foreach(var renderer in Subject.GetComponentsInChildren<Renderer>(true))bounds.Encapsulate(renderer.bounds);
                _preview.camera.transform.position=bounds.center+new Vector3(0,0,-10);
                _preview.camera.orthographicSize=Mathf.Max(.7f,bounds.extents.y*1.3f,bounds.extents.x);
            }catch{Dispose();throw;}
        }
        internal void Seek(float seconds,bool started)
        {
            if(Subject==null)return;
            bool visible=started&&seconds<Duration;Subject.SetActive(visible);if(!visible)return;
            foreach(var particles in _particles){particles.Simulate(seconds,false,true,true);particles.Pause(false);}
            if(_widget!=null){Subject.transform.position=_start+Vector3.up*(seconds*_text.RiseSpeed);_widget.SetOpacity(1-seconds/Duration);}
        }
        internal void DrawControls()
        {
            EditorGUILayout.LabelField("正式表现素材 / 持续秒",Duration.ToString("0.###"));
            if(_cue?.Kind==GameplayPresentationCueAsset.CueKind.Audio){
                EditorGUILayout.ObjectField("音频素材",_cue.Clip,typeof(AudioClip),false);
                EditorGUILayout.LabelField("运行音量 / 音高",_cue.Volume+" / "+_cue.Pitch);
                EditorGUILayout.LabelField("试听原Clip为标准音高；Play安装使用配置音量、音高和Loop。",EditorStyles.wordWrappedMiniLabel);
                if(GUILayout.Button("试听现有音频（一次）")){
                    var utility=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("UnityEditor.AudioUtil")).FirstOrDefault(t=>t!=null);
                    var play=utility?.GetMethod("PlayPreviewClip",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
                    if(play==null)throw new InvalidOperationException("Installed Editor audio audition is unavailable.");
                    play.Invoke(null,new object[]{_cue.Clip,0,false});
                }
            }
            else EditorGUILayout.LabelField(_text!=null?"飘字为样式预览；实际数字只来自已提交的战斗结果。":"独立素材预览；Origin / Follow 由实际 Runtime Host 绑定。",EditorStyles.wordWrappedMiniLabel);
        }
        internal void Render(Rect viewport)
        {
            if(_preview==null){EditorGUI.HelpBox(viewport,"选择实际音频素材；可试听或在真实Scene安装运行。",MessageType.Info);return;}
            if(Event.current.type!=EventType.Repaint)return;
            _preview.BeginPreview(viewport,GUIStyle.none);_preview.camera.Render();
            GUI.DrawTexture(viewport,_preview.EndPreview(),ScaleMode.ScaleToFit,false);
        }
        public void Dispose()
        {
            _preview?.Cleanup();_preview=null;
            if(Subject!=null)UnityEngine.Object.DestroyImmediate(Subject);Subject=null;
        }
    }
}
