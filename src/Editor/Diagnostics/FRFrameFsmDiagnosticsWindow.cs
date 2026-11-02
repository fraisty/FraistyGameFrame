#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Editor
{
    public sealed class FRFrameFsmDiagnosticsWindow : EditorWindow
    {
        private readonly List<FsmDebugInfo> _debugInfos = new List<FsmDebugInfo>();
        private Vector2 _scroll;
        private string _filter;

        [MenuItem("FRFrame/Diagnostics/FSM Diagnostics")]
        public static void Open()
        {
            GetWindow<FRFrameFsmDiagnosticsWindow>("FSM Diagnostics");
        }

        private void OnGUI()
        {
            _filter = EditorGUILayout.TextField("Filter", _filter);
            if (GUILayout.Button("Refresh"))
                Repaint();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("FSM diagnostics are available in Play Mode.", MessageType.Info);
                return;
            }

            FsmManager manager = FRFM.Fsm;
            FsmManagerStatistics statistics = manager.GetStatistics();
            EditorGUILayout.LabelField("Active", statistics.ActiveCount.ToString());
            EditorGUILayout.LabelField("Total Created", statistics.TotalCreated.ToString());
            EditorGUILayout.LabelField("Total Disposed", statistics.TotalDisposed.ToString());
            EditorGUILayout.LabelField("Total Auto Removed", statistics.TotalAutoRemoved.ToString());
            EditorGUILayout.LabelField("Task FSM", statistics.TaskFsmCount.ToString());

            manager.GetAllDebugInfo(_debugInfos);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < _debugInfos.Count; i++)
            {
                FsmDebugInfo info = _debugInfos[i];
                if (!string.IsNullOrEmpty(_filter) && !ContainsFilter(info))
                    continue;

                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField(info.Name, EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Type", info.TypeName ?? string.Empty);
                EditorGUILayout.LabelField("Owner", $"{info.OwnerName} {info.OwnerTypeName}");
                EditorGUILayout.LabelField("Current State", info.CurrentStateName ?? string.Empty);
                EditorGUILayout.LabelField("Run Time", info.RunTime.ToString("F2"));
                EditorGUILayout.LabelField("State Count", info.StateCount.ToString());
                EditorGUILayout.LabelField("Paused", info.IsPaused.ToString());
                EditorGUILayout.LabelField("Task", info.IsTask.ToString());
                EditorGUILayout.LabelField("Task State", info.TaskState.ToString());
                DrawList("States", info.StateNames);
                DrawList("Blackboard", info.BlackboardKeys);
                DrawHistory(info.StateHistory);
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
        }

        private bool ContainsFilter(FsmDebugInfo info)
        {
            return (info.Name != null && info.Name.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (info.TypeName != null && info.TypeName.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (info.OwnerName != null && info.OwnerName.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (info.CurrentStateName != null && info.CurrentStateName.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static void DrawList(string title, IReadOnlyList<string> values)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            if (values == null)
                return;

            for (int i = 0; i < values.Count; i++)
                EditorGUILayout.LabelField("  ", values[i]);
        }

        private static void DrawHistory(IReadOnlyList<FsmStateChangeRecord> records)
        {
            EditorGUILayout.LabelField("State History", EditorStyles.boldLabel);
            if (records == null)
                return;

            for (int i = 0; i < records.Count; i++)
            {
                FsmStateChangeRecord record = records[i];
                EditorGUILayout.LabelField($"  {record.Frame}", $"{record.FromState} -> {record.ToState} @ {record.Time:F2}");
            }
        }
    }
}
#endif
