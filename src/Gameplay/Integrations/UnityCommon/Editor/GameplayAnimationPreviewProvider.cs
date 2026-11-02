using System;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Editor.SkillAuthoring;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Gameplay.Unity.Editor
{
    public sealed class GameplayAnimationNodeDescriptor:IGameplaySkillNodeEditorDescriptorProvider
    {
        public bool Supports(Type type)=>type==typeof(GameplayPlayAnimationActionAuthoring)||type==typeof(GameplayPlayPresentationCueActionAuthoring)||type==typeof(GameplayShowCombatTextActionAuthoring)||type==typeof(GameplayFloatingTextAsset);
        public GameplaySkillNodeEditorDescriptor Describe(Type type,SerializedProperty property)
        {
            if(type==typeof(GameplayPlayAnimationActionAuthoring))return new GameplaySkillNodeEditorDescriptor(property?.FindPropertyRelative("Queue")?.boolValue==true?"排队动画":"播放动画","FRFrame/表现","播放正式动画资源；Queue在同一channel等待，取消施法释放当前与排队请求。",visualKind:GameplayNodeVisualKind.Presentation,isAnimation:true);
            if(type==typeof(GameplayShowCombatTextActionAuthoring)||type==typeof(GameplayFloatingTextAsset))return new GameplaySkillNodeEditorDescriptor("战斗飘字","FRFrame/表现","展示同来源已提交结果；复用Core UIWidget/Pool。不会决定伤害或另建规则时钟。",visualKind:GameplayNodeVisualKind.Presentation);
            var cue=property?.FindPropertyRelative("Cue")?.objectReferenceValue as GameplayPresentationCueAsset;
            return new GameplaySkillNodeEditorDescriptor(cue==null?"播放特效 / 音效":cue.Kind==GameplayPresentationCueAsset.CueKind.Vfx?"生成特效":"播放音效","FRFrame/表现","直接正式资源绑定；复用 FRFrame Pool/Audio，绑定当前施法并精确清理。",visualKind:GameplayNodeVisualKind.Presentation);
        }
    }
    /// <summary>SDK-neutral preview uses exactly the Runtime asset factory. It owns only presentation
    /// objects; seeking never installs or advances a Gameplay rule, Ability or Sequence.</summary>
    public sealed class GameplayAnimationPreviewProvider:IGameplaySkillPreviewProvider,IGameplayPlayableDocumentPreviewProvider
    {
        public string DisplayName=>"正式动画资源";
        public bool CanPreview(GameplaySkillDefinitionAsset skill)=>!EditorApplication.isPlayingOrWillChangePlaymode;
        public IGameplaySkillPreviewSession CreateSession(GameplaySkillDefinitionAsset skill)=>new GameplayAnimationPreviewSession();
    }
    public sealed partial class GameplayAnimationPreviewSession:IGameplaySkillPreviewSession,IGameplaySkillPreviewDiagnostics
    {
        private GameplayAnimationAsset _source;
        private GameplayPresentationCueAsset _cueSource;
        private GameplayFloatingTextAsset _textSource;
        private GameplayUnityCuePreview _cuePreview;
        private float _presentationStart;
        private GameObject _subject;
        private IGameplayAnimationPlayer _player;
        private PreviewRenderUtility _preview;
        private int _level=1;
        private float _time;
        private string _error;
        public string Error=>_error;
        public void Retry(){Release();Load();Seek(_time);}
        private string _sourceJson;
        private float _speed=1;
        private bool _captureVariantRoll;
        private GameplaySkillDocumentAsset _document;
        private GameplayAnimationData _values;
        private GameplayPresentationCueData _cueValues;
        private GameplayFloatingTextData _textValues;
        /// <summary>V2 preview consumes the identical embedded values used by compilation.
        /// No proxy SO, generated asset or runtime rule simulation is involved.</summary>
        public void UpdateValues(GameplaySkillDocumentAsset document, object values, float startSeconds)
        {
            Release(); _source=null; _cueSource=null; _textSource=null;
            _casterProfile=FRFrame.Gameplay.Editor.SkillAuthoring.GameplaySkillEditorState.PreviewProfile(document);
            _document=document; _values=values as GameplayAnimationData; _cueValues=values as GameplayPresentationCueData; _textValues=values as GameplayFloatingTextData;
            _presentationStart=startSeconds; Load(); Seek(0);
        }
        public float Duration=>_presentationStart+(_cuePreview!=null?_cuePreview.Duration:_player==null?0:(float)Math.Min(float.MaxValue,(double)_player.Duration/_speed));
        /// <summary>Temporary camera for preview export/diagnostics; the session retains ownership.</summary>
        public Camera PreviewCamera=>_cuePreview?.Camera??_preview?.camera;
        /// <summary>Temporary subject for actual SDK diagnostics/preview export; owned by this session.</summary>
        public GameObject PreviewSubject=>_cuePreview?.Subject??_subject;
        /// <summary>Preview-only level selection shared by the controls and export/diagnostics.
        /// A formal document's actual Ability MaxLevel remains the upper bound.</summary>
        public void SetLevel(int level)
        {
            int selected=Math.Max(1,Math.Min(level,Math.Max(1,_document?.EffectiveMaxLevel??int.MaxValue)));
            if(selected==_level)return;_level=selected;Release();Load();Seek(_time);
        }
        public void UpdateContext(GameplaySkillPreviewContext context)
        {
            _document=context?.Document;
            if(!_hasExplicitValueContext){_casterProfile=FRFrame.Gameplay.Editor.SkillAuthoring.GameplaySkillEditorState.PreviewProfile(_document);_targetProfile=null;}
            var source=context?.PresentationSource as GameplayAnimationAsset;
            var cue=context?.PresentationSource as GameplayPresentationCueAsset;
            var text=context?.PresentationSource as GameplayFloatingTextAsset;
            if(context?.Source is GameplayAnimationAsset direct){source=direct;cue=null;text=null;}
            else if(context?.Source is GameplayPresentationCueAsset directCue){cue=directCue;source=null;text=null;}
            else if(context?.Source is GameplayFloatingTextAsset directText){text=directText;source=null;cue=null;}
            if(context?.Source!=null&&!string.IsNullOrEmpty(context.PropertyPath))
                using(var so=new SerializedObject(context.Source))
                {
                    var property=so.FindProperty(context.PropertyPath);
                    if(property?.propertyType==SerializedPropertyType.ManagedReference){
                        if(property.managedReferenceValue is GameplayPlayAnimationActionAuthoring action){source=action.Animation;cue=null;text=null;}
                        else if(property.managedReferenceValue is GameplayPlayPresentationCueActionAuthoring cueAction){cue=cueAction.Cue;source=null;text=null;}
                        else if(property.managedReferenceValue is GameplayShowCombatTextActionAuthoring textAction){text=textAction.Source;source=null;cue=null;}
                    }
                }
            _document=context?.Document;
            int clamped=Math.Min(_level,Math.Max(1,_document?.EffectiveMaxLevel??int.MaxValue));bool levelChanged=clamped!=_level;_level=clamped;
            UnityEngine.Object actual=(UnityEngine.Object)source??cue??(UnityEngine.Object)text;
            string json=actual==null?null:EditorJsonUtility.ToJson(actual)+(_document==null?"":EditorJsonUtility.ToJson(_document))+(_casterProfile==null?"":EditorJsonUtility.ToJson(_casterProfile))+(_targetProfile==null?"":EditorJsonUtility.ToJson(_targetProfile));
            float start=context?.PresentationStartSeconds??0;
            if(source==_source&&cue==_cueSource&&text==_textSource&&json==_sourceJson&&!levelChanged&&start==_presentationStart)return;
            Release();_source=source;_cueSource=cue;_textSource=text;_presentationStart=start;_sourceJson=json;Load();Seek(_time);
        }
        private void Load()
        {
            _error=null;
            try
            {
                if(_source==null&&_values==null){_speed=1;if(_cueValues!=null||_textValues!=null)_cuePreview=new GameplayUnityCuePreview(_cueValues,_textValues,PreviewApplication());else if(_cueSource!=null||_textSource!=null)_cuePreview=new GameplayUnityCuePreview(_cueSource,_textSource,PreviewApplication());return;}
                _subject=(_values??_source.CaptureGraphValues() as GameplayAnimationData).CreatePreviewSubject();_subject.hideFlags=HideFlags.HideAndDontSave;
                _preview=new PreviewRenderUtility();_preview.AddSingleGO(_subject);
                (_values??_source.CaptureGraphValues() as GameplayAnimationData).ConfigurePreviewCamera(_preview.camera);
                _preview.camera.orthographic=true;_preview.camera.clearFlags=CameraClearFlags.Color;
                _preview.camera.nearClipPlane=.01f;_preview.camera.farClipPlane=100;
                _preview.camera.backgroundColor=new Color(.12f,.14f,.18f);
                _preview.lights[0].intensity=1;_preview.lights[0].transform.rotation=Quaternion.Euler(40,40,0);
                var application=PreviewApplication();var definition=_values!=null?_values.Build(new GameplayAuthoringBuildContext()):_source.Build(new GameplayAuthoringBuildContext());
                _captureVariantRoll=definition.CaptureVariantRoll;
                _speed=GameplayValueEvaluator.Evaluate(definition.Speed,application);
                if(_speed<=0)throw new ArithmeticException("Animation speed must be positive.");
                _player=definition.Create(_subject,application.Context);_player.Seek(0);
                var renderers=_subject.GetComponentsInChildren<Renderer>();
                // UI-compatible presentation materials may multiply by an inherited/default zero
                // clip rectangle. An isolated preview has no Canvas clipping parent. Override only
                // this temporary renderer's block; never edit the shared source material.
                foreach(var renderer in renderers){var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);block.SetVector("_ClipRect",new Vector4(-100000,-100000,100000,100000));renderer.SetPropertyBlock(block);}
                var bounds=new Bounds(_subject.transform.position,Vector3.one);
                if(renderers.Length>0){bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);}
                _preview.camera.transform.position=bounds.center+new Vector3(0,0,-10);
                _preview.camera.orthographicSize=Mathf.Max(.5f,bounds.extents.y*1.3f,bounds.extents.x*.85f);
            }
            catch(Exception ex){_error=ex.Message;Release();}
        }
        public void DrawControls()
        {
            if(_cuePreview!=null){
                SetLevel(EditorGUILayout.IntField("预览技能等级",_level));
                _cuePreview?.DrawControls();DrawValueControls();return;
            }
            if(_source==null&&_values==null){EditorGUILayout.HelpBox("选择「播放动画」节点以预览正式资源。",MessageType.Info);return;}
            EditorGUILayout.LabelField(_values!=null?_values.BackendName:_source.name+" · "+_source.BackendName);
            int level=Math.Max(1,Math.Min(EditorGUILayout.IntField("预览技能等级",_level),Math.Max(1,_document?.EffectiveMaxLevel??int.MaxValue)));
            SetLevel(level);
            DrawValueControls();
            if(!string.IsNullOrEmpty(_error))EditorGUILayout.HelpBox(_error,MessageType.Warning);
        }
        public void Seek(float seconds)
        {
            if(!float.IsFinite(seconds))throw new ArgumentOutOfRangeException(nameof(seconds));
            _time=Mathf.Max(0,seconds);float local=Mathf.Max(0,_time-_presentationStart);
            if(_cuePreview!=null)_cuePreview.Seek(local,_time>=_presentationStart);
            else _player?.Seek((float)Math.Min(float.MaxValue,(double)local*_speed));
        }
        public void Render(Rect viewport)
        {
            if(_cuePreview!=null){_cuePreview.Render(viewport);return;}
            if(_preview==null||Event.current.type!=EventType.Repaint)return;
            _preview.BeginPreview(viewport,GUIStyle.none);_preview.Render(true);
            GUI.DrawTexture(viewport,_preview.EndPreview(),ScaleMode.ScaleToFit,false);
        }
        private void Release()
        {
            _captureVariantRoll=false;
            _cuePreview?.Dispose();_cuePreview=null;
            _player?.Dispose();_player=null;
            _preview?.Cleanup();_preview=null;
            if(_subject!=null)UnityEngine.Object.DestroyImmediate(_subject);_subject=null;
        }
        public void Dispose(){Release();_source=null;_cueSource=null;_textSource=null;_values=null;_cueValues=null;_textValues=null;}
    }
}
