#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace FRFrame.Editor
{
    public sealed class FRFrameLogDiagnosticsWindow : EditorWindow
    {
        private Vector2 _scroll;

        [MenuItem("FRFrame/Diagnostics/Log Diagnostics")]
        public static void Open()
        {
            GetWindow<FRFrameLogDiagnosticsWindow>("Log Diagnostics");
        }

        private void OnGUI()
        {
            if (GUILayout.Button("Refresh"))
                Repaint();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            FRLogRuntimeConfigSnapshot config = FRLog.GetRuntimeConfigSnapshot();
            FRLogStatistics statistics = FRLog.GetStatistics();

            EditorGUILayout.LabelField("Minimum Level", config.MinimumLevel.ToString());
            EditorGUILayout.LabelField("Unity Output", config.UnityOutputEnabled.ToString());
            EditorGUILayout.LabelField("File Output", config.FileOutputEnabled.ToString());
            EditorGUILayout.LabelField("Capture StackTrace", config.CaptureStackTrace.ToString());
            EditorGUILayout.LabelField("StackTrace Level", config.StackTraceLevel.ToString());
            EditorGUILayout.LabelField("Handler Count", config.HandlerCount.ToString());
            EditorGUILayout.LabelField("Log Directory", config.FileDirectory ?? string.Empty);
            EditorGUILayout.LabelField("Current Log File", statistics.CurrentLogFilePath ?? string.Empty);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Debug", statistics.DebugCount.ToString());
            EditorGUILayout.LabelField("Info", statistics.InfoCount.ToString());
            EditorGUILayout.LabelField("Warning", statistics.WarningCount.ToString());
            EditorGUILayout.LabelField("Error", statistics.ErrorCount.ToString());
            EditorGUILayout.LabelField("Fatal", statistics.FatalCount.ToString());
            EditorGUILayout.LabelField("Exception", statistics.ExceptionCount.ToString());
            EditorGUILayout.LabelField("Dropped", statistics.DroppedCount.ToString());
            EditorGUILayout.LabelField("File Queue", statistics.FileQueueCount.ToString());

            if (GUILayout.Button("Open Log Directory") && !string.IsNullOrEmpty(config.FileDirectory))
                EditorUtility.RevealInFinder(config.FileDirectory);

            if (GUILayout.Button("Copy Log Directory"))
                EditorGUIUtility.systemCopyBuffer = config.FileDirectory ?? string.Empty;

            if (GUILayout.Button("Copy Current Log File Path"))
                EditorGUIUtility.systemCopyBuffer = statistics.CurrentLogFilePath ?? string.Empty;

            if (GUILayout.Button("Flush"))
                FRLogger.Flush();

            if (GUILayout.Button("Reset Statistics"))
                FRLog.ResetStatistics();

            EditorGUILayout.EndScrollView();
        }
    }
}
#endif
