#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace FRFrame.Editor
{
    public sealed class FRFrameEventDiagnosticsWindow : EditorWindow
    {
        private Vector2 _scroll;
        private string _filter;

        [MenuItem("FRFrame/Diagnostics/Event Diagnostics")]
        public static void Open()
        {
            GetWindow<FRFrameEventDiagnosticsWindow>("Event Diagnostics");
        }

        private void OnGUI()
        {
            _filter = EditorGUILayout.TextField("Filter", _filter);
            if (GUILayout.Button("Refresh"))
                Repaint();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Event diagnostics are available in Play Mode.", MessageType.Info);
                return;
            }

            EventBusSnapshot snapshot = FRFM.Event.CreateSnapshot();
            EditorGUILayout.LabelField("Total Listeners", snapshot.TotalListenerCount.ToString());
            EditorGUILayout.LabelField("Event Types", snapshot.EventTypeCount.ToString());
            EditorGUILayout.LabelField("Owners", snapshot.OwnerCount.ToString());
            EditorGUILayout.LabelField("Post Queue", snapshot.PostQueueCount.ToString());

            if (GUILayout.Button("Clear Post Queue"))
                FRFM.Event.ClearPostQueue();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField("Listeners", EditorStyles.boldLabel);
            foreach (EventListenerSnapshot listener in snapshot.Listeners)
            {
                string name = listener.EventType?.FullName ?? string.Empty;
                if (!string.IsNullOrEmpty(_filter) && !name.Contains(_filter))
                    continue;
                EditorGUILayout.LabelField(name, $"Count:{listener.ListenerCount}, Once:{listener.OnceListenerCount}, Priority:{listener.MinPriority}-{listener.MaxPriority}");
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Owners", EditorStyles.boldLabel);
            foreach (EventOwnerSnapshot owner in snapshot.Owners)
            {
                string name = owner.OwnerName ?? string.Empty;
                if (!string.IsNullOrEmpty(_filter) && !name.Contains(_filter))
                    continue;
                EditorGUILayout.LabelField(name, $"{owner.OwnerType?.Name ?? string.Empty}, Count:{owner.SubscriptionCount}");
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
#endif
