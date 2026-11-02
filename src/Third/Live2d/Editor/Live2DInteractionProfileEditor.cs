#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Live2D.Editor
{
    [CustomEditor(typeof(Live2DInteractionProfile))]
    public class Live2DInteractionProfileEditor : UnityEditor.Editor
    {
        private readonly List<string> _issues = new List<string>();

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            if (GUILayout.Button("Validate Profile")) Validate();

            if (_issues.Count == 0) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);
            foreach (string issue in _issues)
                EditorGUILayout.HelpBox(issue, MessageType.Warning);
        }

        private void Validate()
        {
            _issues.Clear();
            var profile = (Live2DInteractionProfile)target;
            if (profile.motionLibrary == null) _issues.Add("Motion Library is not assigned.");
            if (profile.layouts == null || profile.layouts.Count == 0) _issues.Add("No interaction layouts configured.");

            var layoutIds = new HashSet<string>();
            if (profile.layouts != null)
            {
                foreach (var layout in profile.layouts)
                {
                    if (layout == null) continue;
                    if (string.IsNullOrEmpty(layout.layoutId)) _issues.Add("Layout has empty layoutId.");
                    else if (!layoutIds.Add(layout.layoutId)) _issues.Add($"Duplicate layoutId: {layout.layoutId}");

                    if (layout.areas == null) continue;
                    foreach (var area in layout.areas)
                    {
                        if (area == null) continue;
                        if (string.IsNullOrEmpty(area.areaId)) _issues.Add($"Layout {layout.layoutId} has area with empty areaId.");
                        if (area.shapeType == Live2DTouchShapeType.Polygon && (area.normalizedPoints == null || area.normalizedPoints.Count < 3))
                            _issues.Add($"Area {area.areaId} uses Polygon but has fewer than 3 points.");
                        ValidateBindings(profile, area);
                    }
                }
            }

            if (!string.IsNullOrEmpty(profile.defaultLayoutId) && !layoutIds.Contains(profile.defaultLayoutId))
                _issues.Add($"Default layout not found: {profile.defaultLayoutId}");

            if (profile.layoutBindings != null)
            {
                foreach (var binding in profile.layoutBindings)
                {
                    if (binding == null || string.IsNullOrEmpty(binding.layoutId)) continue;
                    if (!layoutIds.Contains(binding.layoutId)) _issues.Add($"Layout binding references missing layout: {binding.layoutId}");
                }
            }

            if (_issues.Count == 0) _issues.Add("No issues found.");
        }

        private void ValidateBindings(Live2DInteractionProfile profile, Live2DTouchArea area)
        {
            if (area.bindings == null) return;
            foreach (var binding in area.bindings)
            {
                if (binding == null || binding.actions == null) continue;
                foreach (var action in binding.actions)
                {
                    if (action == null) continue;
                    switch (action.actionType)
                    {
                        case Live2DInteractionActionType.PlayMotion:
                            var library = action.motion.library != null ? action.motion.library : profile.motionLibrary;
                            if (library == null || string.IsNullOrEmpty(action.motion.motionId) || !library.HasMotion(action.motion.motionId))
                                _issues.Add($"Area {area.areaId} references missing motion: {action.motion.motionId}");
                            break;
                        case Live2DInteractionActionType.PlayMotionGroup:
                            if (profile.motionLibrary == null || profile.motionLibrary.GetGroup(action.groupId) == null)
                                _issues.Add($"Area {area.areaId} references missing group: {action.groupId}");
                            break;
                        case Live2DInteractionActionType.PlayParameterAnimation:
                            if (action.parameterAnimation == null) _issues.Add($"Area {area.areaId} has empty parameter animation action.");
                            break;
                    }
                }
            }
        }
    }
}
#endif
