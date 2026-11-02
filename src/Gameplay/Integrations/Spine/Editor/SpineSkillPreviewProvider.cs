using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Editor.SkillAuthoring;
using Spine.Unity;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Gameplay.Spine.Editor
{
    /// <summary>Optional framework Spine integration discovered through the generic Editor preview seam.</summary>
    public sealed class SpineSkillPreviewProvider : IGameplaySkillPreviewProvider
    {
        public string DisplayName => "Spine";
        public bool CanPreview(GameplaySkillDefinitionAsset skill) =>
            !EditorApplication.isPlayingOrWillChangePlaymode;
        public IGameplaySkillPreviewSession CreateSession(GameplaySkillDefinitionAsset skill)
        {
            var session = new SpineSkillPreviewSession();
            session.UpdateContext(new GameplaySkillPreviewContext(skill, null, null));
            return session;
        }
    }

    /// <summary>Owns Spine subject/animation selection and a disposable presentation viewport.</summary>
    public sealed class SpineSkillPreviewSession : IGameplaySkillPreviewSession
    {
        private sealed class Entry
        {
            public readonly SpinePreviewSubject Subject;
            public readonly ISpinePreviewSubjectProvider Provider;
            public Entry(SpinePreviewSubject subject, ISpinePreviewSubjectProvider provider)
            { Subject = subject; Provider = provider; }
        }

        private readonly SpinePreviewRenderer _renderer = new SpinePreviewRenderer();
        private readonly List<Entry> _subjects = new List<Entry>();
        private GameplaySkillPreviewContext _context;
        private ISpinePreviewSubjectProvider _subjectProvider;
        private SkeletonDataAsset _asset;
        private string[] _animations = Array.Empty<string>();
        private int _subjectIndex, _animationIndex;
        private float _time;
        private string _warning, _cue;
        private bool _actionCue, _followSequenceCues = true;
        private string _automaticContextKey;
        private SpinePreviewSubject _resolvedSubject;

        public SkeletonDataAsset SkeletonDataAsset => _asset;
        public IReadOnlyList<string> AnimationNames => _animations;
        public string CurrentCueTag => _cue;
        public bool FollowSequenceCues
        {
            get => _followSequenceCues;
            set { _followSequenceCues = value; Seek(Mathf.Min(_time, Duration)); }
        }
        /// <summary>Presentation range includes the selected animation and every mapped Cue animation after its start time.</summary>
        public float Duration
        {
            get
            {
                float duration = Mathf.Max(_context?.Sequence?.Duration ?? 0f, SelectedAnimation?.Duration ?? 0f);
                if (!_followSequenceCues) return SelectedAnimation?.Duration ?? 0f;
                if (_asset == null || _subjectProvider == null || _context?.Sequence?.Markers == null) return duration;
                foreach (var cue in _context.Sequence.Markers.OfType<GameplaySequenceCueMarkerAuthoring>())
                {
                    string name = _subjectProvider.AnimationForCue(_asset, cue.CueTag.Name);
                    var animation = string.IsNullOrEmpty(name) ? null : _asset.GetSkeletonData(false)?.FindAnimation(name);
                    if (animation != null) duration = Mathf.Max(duration, cue.Time + animation.Duration);
                }
                return duration;
            }
        }
        public string CurrentAnimationName => _animationIndex < _animations.Length ? _animations[_animationIndex] : null;
        private global::Spine.Animation SelectedAnimation => _asset?.GetSkeletonData(false)?.FindAnimation(
            _animationIndex < _animations.Length ? _animations[_animationIndex] : string.Empty);

        public SpineSkillPreviewSession()
        {
            foreach (Type type in TypeCache.GetTypesDerivedFrom<ISpinePreviewSubjectProvider>()
                .Where(type => !type.IsAbstract && type.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(type => type.AssemblyQualifiedName, StringComparer.Ordinal))
            {
                try
                {
                    var provider = (ISpinePreviewSubjectProvider)Activator.CreateInstance(type);
                    foreach (var subject in provider.GetSubjects() ?? Array.Empty<SpinePreviewSubject>())
                        if (subject?.Asset != null) _subjects.Add(new Entry(subject, provider));
                }
                catch (Exception ex) { Debug.LogWarning("Spine preview subjects " + type.FullName + ": " + ex.Message); }
            }
        }

        public void UpdateContext(GameplaySkillPreviewContext context)
        {
            _context = context;
            var candidates=_subjects.Select(e=>e.Provider).Distinct().OfType<IScopedSpinePreviewSubjectProvider>().ToArray();
            var scoped=context?.PresentationSource==null ? null : candidates.FirstOrDefault(p=>p.SupportsPresentationSource(context.PresentationSource));
            scoped=scoped ?? candidates.FirstOrDefault(p=>p.AuthoringScope==context?.AuthoringScope);
            string key = (context?.Document == null ? "" : context.Document.GetInstanceID().ToString()) + ":" + context?.AuthoringScope +
                ":" + context?.PresentationSource?.GetInstanceID() + ":" + scoped?.Animation(context);
            if (key != _automaticContextKey)
            {
                _automaticContextKey = key;
                _resolvedSubject = scoped?.Resolve(context);
                if (_resolvedSubject != null)
                {
                    var provider = (ISpinePreviewSubjectProvider)scoped;
                    int index = _subjects.FindIndex(e => e.Provider.GetType() == provider.GetType() && e.Subject.Asset == _resolvedSubject.Asset);
                    _subjectIndex = index + 1; LoadAsset(_resolvedSubject.Asset, provider);
                    string animation = scoped.Animation(context); if (!string.IsNullOrEmpty(animation)) SetAnimation(animation);
                }
                else if (!string.IsNullOrEmpty(context?.AuthoringScope)) { _resolvedSubject = null; SelectSubject(0); }
            }
            Seek(_time);
        }

        public void DrawControls()
        {
            string[] names = new[] { "手动选择 Spine" }.Concat(_subjects.Select(entry => entry.Subject.DisplayName)).ToArray();
            int index = EditorGUILayout.Popup("预览角色", _subjectIndex, names);
            if (index != _subjectIndex) SelectSubject(index);
            var selected = (SkeletonDataAsset)EditorGUILayout.ObjectField("Spine Asset", _asset,
                typeof(SkeletonDataAsset), false);
            if (selected != _asset) SetSkeletonDataAsset(selected);
            if (_animations.Length > 0)
            {
                int animation = EditorGUILayout.Popup("Animation", _animationIndex, _animations);
                if (animation != _animationIndex) SetAnimation(_animations[animation]);
            }
            if (_context?.Sequence != null)
            {
                bool follow = EditorGUILayout.ToggleLeft("跟随 Sequence Cue（关闭后单独预览所选动画）", _followSequenceCues);
                if (follow != _followSequenceCues) FollowSequenceCues = follow;
            }
            if (SelectedAnimation != null)
                EditorGUILayout.LabelField("动画长度", SelectedAnimation.Duration.ToString("0.###") + "s" +
                    (Duration > SelectedAnimation.Duration ? "  ·  含 Cue 后续至 " + Duration.ToString("0.###") + "s" : ""));
            if (!string.IsNullOrEmpty(_warning)) EditorGUILayout.HelpBox(_warning, MessageType.Warning);
        }

        private void SelectSubject(int index)
        {
            _subjectIndex = index;
            var entry = index > 0 && index <= _subjects.Count ? _subjects[index - 1] : null;
            _resolvedSubject = entry?.Subject;
            LoadAsset(entry?.Subject.Asset, entry?.Provider);
        }

        /// <summary>Direct framework use is possible without any game subject provider.</summary>
        public void SetSkeletonDataAsset(SkeletonDataAsset asset)
        {
            _subjectIndex = 0;
            _resolvedSubject = null;
            LoadAsset(asset, null);
        }

        public void SetAnimation(string name)
        {
            int index = Array.IndexOf(_animations, name);
            if (index < 0) return;
            _animationIndex = index;
            Seek(Mathf.Min(_time, Duration));
        }

        private void LoadAsset(SkeletonDataAsset asset, ISpinePreviewSubjectProvider provider)
        {
            _asset = asset;
            _subjectProvider = provider;
            _warning = null;
            _animations = asset?.GetSkeletonData(false)?.Animations.Select(animation => animation.Name).ToArray()
                ?? Array.Empty<string>();
            string idle = provider?.IdleAnimation(asset);
            if (string.IsNullOrEmpty(idle))
                idle = new[] { "normal", "stand", "stand2", "idle", "sit" }
                    .FirstOrDefault(name => _animations.Contains(name));
            _animationIndex = Mathf.Max(0, Array.IndexOf(_animations, idle));
            try { _renderer.Load(asset, _resolvedSubject?.SkinNames, _resolvedSubject?.Material); Seek(Mathf.Min(_time, Duration)); }
            catch (Exception ex) { _warning = ex.Message; }
        }

        public void Seek(float seconds)
        {
            _time = Mathf.Max(0f, seconds);
            var cue = (_followSequenceCues ? _context?.Sequence?.Markers : null)?.OfType<GameplaySequenceCueMarkerAuthoring>()
                .Where(marker => marker.Time <= _time + .00001f)
                .OrderBy(marker => marker.Time).LastOrDefault();
            _cue = cue?.CueTag.Name;
            string actionName = _subjectProvider?.AnimationForCue(_asset, _cue);
            var action = string.IsNullOrEmpty(actionName) ? null : _asset?.GetSkeletonData(false)?.FindAnimation(actionName);
            _actionCue = action != null;
            _renderer.Seek(action ?? SelectedAnimation, _actionCue && cue != null ? _time - cue.Time : _time);
        }

        public void Render(Rect viewport)
        {
            if (_asset == null) { EditorGUI.HelpBox(viewport, "选择 SkeletonDataAsset 以预览 Spine。", MessageType.Info); return; }
            if (!string.IsNullOrEmpty(_warning)) { EditorGUI.HelpBox(viewport, _warning, MessageType.Warning); return; }
            _renderer.Render(viewport);
            if (Event.current.type == EventType.Repaint && !string.IsNullOrEmpty(_cue))
            {
                EditorGUI.DrawRect(new Rect(viewport.x, viewport.y, viewport.width, 25f),
                    _actionCue ? new Color(.58f, .26f, .17f, .9f) : new Color(.20f, .38f, .59f, .9f));
                GUI.Label(new Rect(viewport.x + 5f, viewport.y + 3f, viewport.width - 10f, 20f),
                    "Cue · " + _cue, EditorStyles.whiteMiniLabel);
            }
        }

        public void Dispose() => _renderer.Dispose();
    }
}
