using System;
using System.Collections.Generic;
using UnityEngine;

namespace FRFrame.Live2D
{
    [CreateAssetMenu(fileName = "Live2DMotionLibrary", menuName = "FRFrame/Live2D/Motion Library")]
    public class Live2DMotionLibrary : ScriptableObject
    {
        public string modelName;
        public TextAsset model3Json;
        public string motionFolderPath;
        public List<Live2DMotionClip> clips = new List<Live2DMotionClip>();
        public List<Live2DMotionGroup> groups = new List<Live2DMotionGroup>();

        private readonly Dictionary<string, Live2DMotionClip> _clipMap = new Dictionary<string, Live2DMotionClip>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Live2DMotionGroup> _groupMap = new Dictionary<string, Live2DMotionGroup>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Live2DMotionRuntimeData> _runtimeMap = new Dictionary<string, Live2DMotionRuntimeData>(StringComparer.OrdinalIgnoreCase);

        public void BuildRuntimeCache()
        {
            _clipMap.Clear();
            _groupMap.Clear();
            _runtimeMap.Clear();

            foreach (var clip in clips)
            {
                if (clip == null || string.IsNullOrEmpty(clip.motionId)) continue;
                if (!_clipMap.ContainsKey(clip.motionId)) _clipMap.Add(clip.motionId, clip);
                if (clip.motionAsset == null) continue;
                var runtime = Live2DMotionRuntimeData.FromTextAsset(clip.motionAsset, clip.motionId);
                if (runtime == null) continue;
                runtime.ResolvedLoop = ResolveLoop(clip, runtime.JsonLoop);
                if (clip.fadeIn >= 0f) runtime.MotionFadeInTime = clip.fadeIn;
                if (clip.fadeOut >= 0f) runtime.MotionFadeOutTime = clip.fadeOut;
                runtime.CustomEvents = clip.events;
                _runtimeMap[clip.motionId] = runtime;
            }

            foreach (var group in groups)
            {
                if (group == null || string.IsNullOrEmpty(group.groupId)) continue;
                if (!_groupMap.ContainsKey(group.groupId)) _groupMap.Add(group.groupId, group);
            }
        }

        public bool HasMotion(string motionId) => !string.IsNullOrEmpty(motionId) && _clipMap.ContainsKey(motionId);

        public Live2DMotionClip GetClip(string motionId)
        {
            if (string.IsNullOrEmpty(motionId)) return null;
            _clipMap.TryGetValue(motionId, out var clip);
            return clip;
        }

        public Live2DMotionRuntimeData GetRuntimeData(string motionId)
        {
            if (string.IsNullOrEmpty(motionId)) return null;
            _runtimeMap.TryGetValue(motionId, out var data);
            return data;
        }

        public Live2DMotionGroup GetGroup(string groupId)
        {
            if (string.IsNullOrEmpty(groupId)) return null;
            _groupMap.TryGetValue(groupId, out var group);
            return group;
        }

        public bool IsMotionInGroup(string motionId, string groupId)
        {
            var group = GetGroup(groupId);
            if (group == null || group.candidates == null || string.IsNullOrEmpty(motionId)) return false;
            foreach (var candidate in group.candidates)
            {
                if (candidate != null && string.Equals(candidate.motion.motionId, motionId, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public string PickMotionFromGroup(string groupId)
        {
            var group = GetGroup(groupId);
            if (group == null) return null;
            if (Time.unscaledTime < group.cooldownUntil) return PickMotionFromGroup(group.fallbackGroupId);
            if (group.candidates == null || group.candidates.Count == 0) return PickMotionFromGroup(group.fallbackGroupId);

            string picked;
            switch (group.selectionMode)
            {
                case Live2DSelectionMode.First:
                    picked = PickFirst(group);
                    break;
                case Live2DSelectionMode.Sequence:
                    picked = PickSequence(group);
                    break;
                case Live2DSelectionMode.WeightedRandom:
                    picked = PickWeighted(group);
                    break;
                case Live2DSelectionMode.ShuffleBag:
                    picked = PickShuffleBag(group);
                    break;
                default:
                    picked = PickRandom(group);
                    break;
            }

            if (string.IsNullOrEmpty(picked)) return PickMotionFromGroup(group.fallbackGroupId);
            group.cooldownUntil = Time.unscaledTime + Mathf.Max(0f, group.groupCooldown);
            return picked;
        }

        public static string NormalizeMotionId(string rawName)
        {
            if (string.IsNullOrEmpty(rawName)) return string.Empty;
            return rawName.Replace(".motion3.json", string.Empty).Replace(".motion3", string.Empty).Replace(".json", string.Empty).Trim();
        }

        public static Live2DMotionCategory GuessCategory(string motionId)
        {
            if (string.IsNullOrEmpty(motionId)) return Live2DMotionCategory.Unknown;
            string n = motionId.ToLowerInvariant();
            if (n.StartsWith("effect")) return Live2DMotionCategory.Effect;
            if (n.StartsWith("touch_drag")) return Live2DMotionCategory.Drag;
            if (n.StartsWith("touch_idle")) return Live2DMotionCategory.TouchIdle;
            if (n.StartsWith("touch_")) return Live2DMotionCategory.Touch;
            if (n.StartsWith("idle") || n.StartsWith("main")) return Live2DMotionCategory.Idle;
            if (n.StartsWith("change_")) return Live2DMotionCategory.Transition;
            return Live2DMotionCategory.Base;
        }

        public static Live2DLayerId GuessDefaultLayer(Live2DMotionCategory category)
        {
            switch (category)
            {
                case Live2DMotionCategory.Effect: return Live2DLayerId.Effect;
                case Live2DMotionCategory.Touch:
                case Live2DMotionCategory.Drag:
                case Live2DMotionCategory.TouchIdle: return Live2DLayerId.Overlay;
                default: return Live2DLayerId.Base;
            }
        }

        private static bool ResolveLoop(Live2DMotionClip clip, bool sourceLoop)
        {
            switch (clip.loopMode)
            {
                case Live2DLoopMode.ForceLoop: return true;
                case Live2DLoopMode.ForceOnce: return false;
                default: return sourceLoop;
            }
        }

        private bool IsCandidateAvailable(Live2DMotionGroup group, Live2DMotionCandidate c)
        {
            if (c == null || !c.enabled || string.IsNullOrEmpty(c.motion.motionId)) return false;
            var library = c.motion.library != null ? c.motion.library : this;
            if (!library.HasMotion(c.motion.motionId)) return false;
            if (group.candidateCooldownUntil.TryGetValue(c.motion.motionId, out float until) && Time.unscaledTime < until) return false;
            if (group.avoidRepeatLast && group.repeatBlockCount > 0 && string.Equals(c.motion.motionId, group.lastMotionId, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private void MarkPicked(Live2DMotionGroup group, Live2DMotionCandidate candidate)
        {
            string motionId = candidate.motion.motionId;
            group.lastMotionId = motionId;
            group.candidateCooldownUntil[motionId] = Time.unscaledTime + Mathf.Max(0f, candidate.cooldown);
        }

        private string PickFirst(Live2DMotionGroup group)
        {
            foreach (var c in group.candidates)
            {
                if (!IsCandidateAvailable(group, c)) continue;
                MarkPicked(group, c);
                return c.motion.motionId;
            }
            return null;
        }

        private string PickRandom(Live2DMotionGroup group)
        {
            var available = new List<Live2DMotionCandidate>();
            foreach (var c in group.candidates)
                if (IsCandidateAvailable(group, c)) available.Add(c);
            if (available.Count == 0) return null;
            var picked = available[UnityEngine.Random.Range(0, available.Count)];
            MarkPicked(group, picked);
            return picked.motion.motionId;
        }

        private string PickWeighted(Live2DMotionGroup group)
        {
            float total = 0f;
            foreach (var c in group.candidates)
                if (IsCandidateAvailable(group, c))
                    total += Mathf.Max(0f, c.weight);
            if (total <= 0f) return PickRandom(group);
            float r = UnityEngine.Random.value * total;
            foreach (var c in group.candidates)
            {
                if (!IsCandidateAvailable(group, c)) continue;
                r -= Mathf.Max(0f, c.weight);
                if (r <= 0f)
                {
                    MarkPicked(group, c);
                    return c.motion.motionId;
                }
            }
            return PickRandom(group);
        }

        private string PickSequence(Live2DMotionGroup group)
        {
            for (int i = 0; i < group.candidates.Count; i++)
            {
                int index = (group.sequenceIndex + i) % group.candidates.Count;
                var c = group.candidates[index];
                if (!IsCandidateAvailable(group, c)) continue;
                group.sequenceIndex = (index + 1) % group.candidates.Count;
                MarkPicked(group, c);
                return c.motion.motionId;
            }
            return null;
        }

        private string PickShuffleBag(Live2DMotionGroup group)
        {
            if (group.shuffleBag.Count == 0)
            {
                for (int i = 0; i < group.candidates.Count; i++)
                    if (IsCandidateAvailable(group, group.candidates[i])) group.shuffleBag.Add(i);
            }
            if (group.shuffleBag.Count == 0) return null;
            int bagIndex = UnityEngine.Random.Range(0, group.shuffleBag.Count);
            int candidateIndex = group.shuffleBag[bagIndex];
            group.shuffleBag.RemoveAt(bagIndex);
            var picked = group.candidates[candidateIndex];
            MarkPicked(group, picked);
            return picked.motion.motionId;
        }
    }
}
