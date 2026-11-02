using System;
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using UnityEngine;

namespace FRFrame.Live2D
{
    [Serializable]
    public class Live2DMotionRequest
    {
        public Live2DLayerId layerId = Live2DLayerId.Base;
        public Live2DInterruptPolicy interruptPolicy = Live2DInterruptPolicy.ImmediateCrossFade;
        public Live2DMotionReference motion;
        public string groupId;
        public Live2DParameterAnimationAsset parameterAnimation;
        public Live2DParameterApplyMode parameterApplyMode = Live2DParameterApplyMode.AsTrack;
        public string source;
    }

    public class Live2DMotionController : MonoBehaviour, ICubismUpdatable
    {
        private const int MotionExecutionOrder = 125;

        [Header("References")]
        public CubismModel cubismModel;
        public Live2DMotionLibrary motionLibrary;
        public Live2DInteractionProfile interactionProfile;
        public MonoBehaviour audioEventHandler;
        public MonoBehaviour externalStateProvider;

        [Header("Startup")]
        public bool rebuildLibraryOnAwake = true;
        public bool playIdleOnStart = true;
        public string startupIdleGroupId = "idle_base";
        public bool playEffectOnStart = false;
        public string startupEffectGroupId = "effect";

        [Header("Apply")]
        public bool resetToDefaultValuesBeforeApply = true;
        public float defaultFadeIn = 0.25f;
        public float defaultFadeOut = 0.25f;

        [Header("Default Policies")]
        public Live2DInterruptPolicy baseInterruptPolicy = Live2DInterruptPolicy.QueueUntilFinish;
        public Live2DInterruptPolicy overlayInterruptPolicy = Live2DInterruptPolicy.ImmediateCrossFade;
        public Live2DInterruptPolicy effectInterruptPolicy = Live2DInterruptPolicy.ImmediateCrossFade;

        [Header("Debug")]
        public bool debugLog;

        private readonly Live2DMotionMixer _mixer = new Live2DMotionMixer();
        private readonly System.Collections.Generic.Dictionary<string, Live2DMotionRequest> _afterMotionRequests = new System.Collections.Generic.Dictionary<string, Live2DMotionRequest>(StringComparer.OrdinalIgnoreCase);
        private Live2DMotionRequest _pendingBaseRequest;
        private ILive2DAudioEventHandler _audioHandler;
        private ILive2DExternalStateProvider _externalStateProvider;
        private CubismUpdateController _cubismUpdateController;
        private bool _isInitialized;

        public ILive2DExternalStateProvider ExternalStateProvider => _externalStateProvider;
        public Live2DMotionMixer Mixer => _mixer;
        public int ExecutionOrder => MotionExecutionOrder;
        public bool NeedsUpdateOnEditing => false;
        public bool HasUpdateController { get; set; }
        public string CurrentBaseClipId => _mixer.GetCurrentClipId(Live2DLayerId.Base);
        public string CurrentOverlayClipId => _mixer.GetCurrentClipId(Live2DLayerId.Overlay);
        public string CurrentOverlayExpressionClipId => _mixer.GetCurrentClipId(Live2DLayerId.OverlayExpression);
        public string CurrentOverlayBodyClipId => _mixer.GetCurrentClipId(Live2DLayerId.OverlayBody);
        public string CurrentOverlayEffectClipId => _mixer.GetCurrentClipId(Live2DLayerId.OverlayEffect);
        public string CurrentEffectClipId => _mixer.GetCurrentClipId(Live2DLayerId.Effect);
        public bool IsOverlayPlaying => !string.IsNullOrEmpty(CurrentOverlayClipId) || !string.IsNullOrEmpty(CurrentOverlayExpressionClipId) || !string.IsNullOrEmpty(CurrentOverlayBodyClipId) || !string.IsNullOrEmpty(CurrentOverlayEffectClipId);

        private void Awake()
        {
            if (cubismModel == null) cubismModel = GetComponent<CubismModel>() ?? GetComponentInChildren<CubismModel>(true);
            if (cubismModel == null)
            {
                Debug.LogError("Live2DMotionController requires CubismModel.", this);
                enabled = false;
                return;
            }

            if (interactionProfile != null && motionLibrary == null) motionLibrary = interactionProfile.motionLibrary;
            if (motionLibrary != null && rebuildLibraryOnAwake) motionLibrary.BuildRuntimeCache();

            _audioHandler = audioEventHandler as ILive2DAudioEventHandler;
            _externalStateProvider = externalStateProvider as ILive2DExternalStateProvider;
            _mixer.ResetToDefaultBeforeApply = resetToDefaultValuesBeforeApply;
            _mixer.DefaultFadeIn = defaultFadeIn;
            _mixer.DefaultFadeOut = defaultFadeOut;
            _mixer.Build(cubismModel);
            _mixer.MotionCompleted += OnMotionCompleted;
            _mixer.PlayableEventTriggered += OnPlayableEventTriggered;

            _cubismUpdateController = cubismModel.GetComponent<CubismUpdateController>();
            HasUpdateController = _cubismUpdateController != null;
            _isInitialized = true;
        }

        private void Start()
        {
            // Register after every component has completed Awake/OnEnable. This keeps
            // custom motion evaluation inside Cubism's deterministic r5 update chain.
            if (_cubismUpdateController != null)
                _cubismUpdateController.Refresh();

            if (playEffectOnStart) PlayGroup(startupEffectGroupId, Live2DLayerId.Effect, effectInterruptPolicy, "StartupEffect");
            if (playIdleOnStart) PlayGroup(startupIdleGroupId, Live2DLayerId.Base, baseInterruptPolicy, "StartupIdle");
        }

        private void OnDestroy()
        {
            _isInitialized = false;
            _mixer.MotionCompleted -= OnMotionCompleted;
            _mixer.PlayableEventTriggered -= OnPlayableEventTriggered;
        }

        private void LateUpdate()
        {
            if (!HasUpdateController)
                OnLateUpdate();
        }

        public void OnLateUpdate()
        {
            if (!_isInitialized || !isActiveAndEnabled) return;
            _mixer.ResetToDefaultBeforeApply = resetToDefaultValuesBeforeApply;
            _mixer.DefaultFadeIn = defaultFadeIn;
            _mixer.DefaultFadeOut = defaultFadeOut;
            _mixer.Tick(Time.deltaTime);
        }

        public void ResetRuntimeState(bool playStartupMotions = false)
        {
            _pendingBaseRequest = null;
            _afterMotionRequests.Clear();
            _mixer.ResetRuntimeState();
            if (!playStartupMotions || !enabled) return;
            if (playEffectOnStart)
                PlayGroup(startupEffectGroupId, Live2DLayerId.Effect, effectInterruptPolicy, "StartupEffect");
            if (playIdleOnStart)
                PlayGroup(startupIdleGroupId, Live2DLayerId.Base, baseInterruptPolicy, "StartupIdle");
        }

        public bool PlayMotion(Live2DMotionReference motion, Live2DLayerId layerId, Live2DInterruptPolicy interruptPolicy, string source = null)
        {
            if (!motion.IsValid) return false;
            if (motion.library != motionLibrary && motion.library != null) motion.library.BuildRuntimeCache();
            var library = motion.library != null ? motion.library : motionLibrary;
            var runtime = library != null ? library.GetRuntimeData(motion.motionId) : null;
            if (runtime == null) return false;
            return PlayClip(runtime, motion.motionId, layerId, interruptPolicy, source);
        }

        public bool PlayMotion(string motionId, Live2DLayerId layerId, Live2DInterruptPolicy interruptPolicy, string source = null)
        {
            if (motionLibrary == null || string.IsNullOrEmpty(motionId)) return false;
            var runtime = motionLibrary.GetRuntimeData(motionId);
            if (runtime == null) return false;
            return PlayClip(runtime, motionId, layerId, interruptPolicy, source);
        }

        public bool PlayGroup(string groupId, Live2DLayerId layerId, Live2DInterruptPolicy interruptPolicy, string source = null)
        {
            if (motionLibrary == null || string.IsNullOrEmpty(groupId)) return false;
            string motionId = motionLibrary.PickMotionFromGroup(groupId);
            return !string.IsNullOrEmpty(motionId) && PlayMotion(motionId, layerId, interruptPolicy, source);
        }

        public bool PlayParameterAnimation(Live2DParameterAnimationAsset asset, Live2DLayerId layerId, Live2DInterruptPolicy interruptPolicy, Live2DParameterApplyMode applyMode, string source = null)
        {
            if (asset == null) return false;
            var clip = new Live2DParameterAnimationClip(asset);
            if (applyMode == Live2DParameterApplyMode.Immediate)
            {
                clip.ApplyImmediate(_mixer.BindingCache);
                return true;
            }
            return PlayClip(clip, asset.animationId, layerId, interruptPolicy, source);
        }

        public bool ExecuteAction(Live2DInteractionAction action, string source = null)
        {
            if (action == null || !enabled) return false;
            switch (action.actionType)
            {
                case Live2DInteractionActionType.PlayMotion:
                    return PlayMotionAction(action, source);
                case Live2DInteractionActionType.PlayMotionGroup:
                    return PlayGroup(action.groupId, action.layerId, action.interruptPolicy, source);
                case Live2DInteractionActionType.PlayParameterAnimation:
                    return PlayParameterAnimation(action.parameterAnimation, action.layerId, action.interruptPolicy, action.parameterApplyMode, source);
                case Live2DInteractionActionType.StopLayer:
                    _mixer.Stop(action.layerId, defaultFadeOut);
                    return true;
                case Live2DInteractionActionType.PlayAudio:
                    DispatchAudio(action.audioKey, action.fallbackClip, action.volume, action.pitch);
                    return true;
                case Live2DInteractionActionType.SetParameterValue:
                {
                    _mixer.BindingCache.TryGetParameterValue(action.parameterId, out float beforeValue);
                    bool applied = _mixer.BindingCache.SetPersistentParameter(action.parameterId, action.parameterValue, Live2DParameterBlendMode.Override);
                    _mixer.BindingCache.TryGetParameterValue(action.parameterId, out float afterValue);
                    if (debugLog)
                        Debug.Log($"[Live2D] SetParameterValue id={action.parameterId}, value={action.parameterValue}, applied={applied}, before={beforeValue}, after={afterValue}, source={source}", this);
                    return applied;
                }
                case Live2DInteractionActionType.ToggleParameterValue:
                {
                    bool found = _mixer.BindingCache.TryGetParameterValue(action.parameterId, out float currentValue);
                    if (!found)
                    {
                        if (debugLog) Debug.Log($"[Live2D] ToggleParameterValue id={action.parameterId}, found=false, source={source}", this);
                        return false;
                    }

                    float nextValue = currentValue >= action.toggleThreshold ? action.toggleOffValue : action.toggleOnValue;
                    bool applied = _mixer.BindingCache.SetPersistentParameter(action.parameterId, nextValue, Live2DParameterBlendMode.Override);
                    _mixer.BindingCache.TryGetParameterValue(action.parameterId, out float afterValue);
                    if (debugLog)
                        Debug.Log($"[Live2D] ToggleParameterValue id={action.parameterId}, current={currentValue}, next={nextValue}, threshold={action.toggleThreshold}, applied={applied}, after={afterValue}, source={source}", this);
                    return applied;
                }
                case Live2DInteractionActionType.SendEvent:
                    Debug.Log($"[Live2D] Event: {action.eventName}, payload={action.payload}", this);
                    return true;
                default:
                    return false;
            }
        }

        private bool PlayMotionAction(Live2DInteractionAction action, string source)
        {
            if (!PlayMotion(action.motion, action.layerId, action.interruptPolicy, source)) return false;
            if (action.playAfterMotionOnComplete && action.afterMotion.IsValid)
            {
                _afterMotionRequests[MakeAfterMotionKey(action.layerId, action.motion.motionId)] = new Live2DMotionRequest
                {
                    layerId = action.afterMotionLayerId,
                    interruptPolicy = action.afterMotionInterruptPolicy,
                    motion = action.afterMotion,
                    source = source
                };
            }
            return true;
        }

        private bool PlayClip(ILive2DPlayableClip clip, string clipId, Live2DLayerId layerId, Live2DInterruptPolicy interruptPolicy, string source)
        {
            var layer = _mixer.GetLayer(layerId);
            if (layer.Current != null)
            {
                if (interruptPolicy == Live2DInterruptPolicy.IgnoreIfPlaying) return false;
                if (layerId == Live2DLayerId.Base && interruptPolicy == Live2DInterruptPolicy.QueueUntilFinish && !layer.Current.IsNaturallyFinished() && !layer.Current.Clip.Loop)
                {
                    _pendingBaseRequest = new Live2DMotionRequest { layerId = layerId, interruptPolicy = Live2DInterruptPolicy.ImmediateCrossFade, source = source };
                    if (clip is Live2DMotionRuntimeData data)
                        _pendingBaseRequest.motion = new Live2DMotionReference { library = motionLibrary, motionId = data.MotionId };
                    return true;
                }
            }

            _mixer.Play(layerId, clip);
            if (debugLog) Debug.Log($"[Live2D] Play {clipId} on {layerId}, source={source}", this);
            return true;
        }

        private void OnMotionCompleted(Live2DMotionEvent e)
        {
            if (_afterMotionRequests.TryGetValue(MakeAfterMotionKey(e.LayerId, e.ClipId), out var afterRequest))
            {
                _afterMotionRequests.Remove(MakeAfterMotionKey(e.LayerId, e.ClipId));
                PlayMotion(afterRequest.motion, afterRequest.layerId, afterRequest.interruptPolicy, afterRequest.source);
                return;
            }

            if (e.LayerId != Live2DLayerId.Base) return;
            if (_pendingBaseRequest != null)
            {
                var req = _pendingBaseRequest;
                _pendingBaseRequest = null;
                PlayMotion(req.motion, Live2DLayerId.Base, Live2DInterruptPolicy.ImmediateCrossFade, req.source);
                return;
            }
            PlayGroup(startupIdleGroupId, Live2DLayerId.Base, Live2DInterruptPolicy.ImmediateCrossFade, "IdleFallback");
        }

        private static string MakeAfterMotionKey(Live2DLayerId layerId, string clipId) => $"{layerId}:{clipId}";

        private void OnPlayableEventTriggered(Live2DPlayableEvent e)
        {
            if (e.EventType == Live2DPlayableEventType.PlayAudio)
                DispatchAudio(e.AudioKey, e.ObjectReference as AudioClip, 1f, 1f);
        }

        private void DispatchAudio(string audioKey, AudioClip fallbackClip, float volume, float pitch)
        {
            if (_audioHandler != null)
                _audioHandler.PlayAudio(new Live2DAudioEventData(audioKey, fallbackClip, volume, pitch));
            else if (debugLog)
                Debug.Log($"[Live2D] PlayAudio requested: key={audioKey}, clip={(fallbackClip ? fallbackClip.name : "null")}", this);
        }
    }
}
