using UnityEditor;
using UnityEngine;

namespace FRFrame.Editor
{
    public sealed class AudioDiagnosticsWindow : EditorWindow
    {
        private Vector2 _scroll;

        [MenuItem("FRFrame/Diagnostics/Audio Diagnostics")]
        public static void Open()
        {
            GetWindow<AudioDiagnosticsWindow>("Audio Diagnostics");
        }

        private void OnGUI()
        {
            if (!Application.isPlaying || FRFM.Audio == null)
            {
                EditorGUILayout.HelpBox("Audio Diagnostics is available in Play Mode.", MessageType.Info);
                return;
            }

            AudioDiagnosticsSnapshot snapshot = FRFM.Audio.CreateSnapshot();
            EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Playing", snapshot.PlayingCount.ToString());
            EditorGUILayout.LabelField("Loading", snapshot.LoadingCount.ToString());
            EditorGUILayout.LabelField("Preloaded", snapshot.PreloadedClipCount.ToString());
            EditorGUILayout.LabelField("Steal Count", snapshot.StealCount.ToString());
            EditorGUILayout.LabelField("Play Failure Count", snapshot.PlayFailureCount.ToString());

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Stop All"))
                FRFM.Audio.StopAll();
            if (GUILayout.Button("Refresh"))
                Repaint();
            EditorGUILayout.EndHorizontal();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < snapshot.Categories.Count; i++)
            {
                AudioCategorySnapshot category = snapshot.Categories[i];
                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField($"{category.Type} ({category.Name})", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Volume", category.Volume.ToString("0.00"));
                EditorGUILayout.LabelField("Mute", category.Mute.ToString());
                EditorGUILayout.LabelField("Paused", category.Paused.ToString());
                EditorGUILayout.LabelField("Agents", $"{category.AgentCount}  Idle:{category.IdleCount} Loading:{category.LoadingCount} Playing:{category.PlayingCount}");

                EditorGUI.indentLevel++;
                for (int j = 0; j < category.Agents.Count; j++)
                {
                    AudioAgentSnapshot agent = category.Agents[j];
                    if (agent.State == AudioAgentState.Idle)
                        continue;
                    EditorGUILayout.LabelField($"#{agent.Id} {agent.State}", $"{agent.PackageName} :: {agent.Location}  {agent.Elapsed:0.00}s");
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
