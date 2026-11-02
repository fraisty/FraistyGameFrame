using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Editor
{
    public sealed class SaveDiagnosticsWindow : EditorWindow
    {
        private Vector2 _scroll;

        [MenuItem("FRFrame/Diagnostics/Save Diagnostics")]
        public static void Open()
        {
            GetWindow<SaveDiagnosticsWindow>("Save Diagnostics");
        }

        private void OnGUI()
        {
            if (!Application.isPlaying || FRFM.Save == null)
            {
                EditorGUILayout.HelpBox("Save Diagnostics is available in Play Mode.", MessageType.Info);
                return;
            }

            SaveDiagnosticsSnapshot snapshot = FRFM.Save.CreateSnapshot();
            EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Root Path", snapshot.RootPath ?? string.Empty);
            EditorGUILayout.LabelField("Cache Count", snapshot.CacheCount.ToString());
            EditorGUILayout.LabelField("Dirty Count", snapshot.DirtyCount.ToString());
            EditorGUILayout.LabelField("Save Failure Count", snapshot.SaveFailureCount.ToString());
            EditorGUILayout.LabelField("Load Failure Count", snapshot.LoadFailureCount.ToString());
            EditorGUILayout.LabelField("Backup Restore Count", snapshot.BackupRestoreCount.ToString());

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Flush"))
                FRFM.Save.FlushAsync().Forget();
            if (GUILayout.Button("Open Folder") && !string.IsNullOrEmpty(snapshot.RootPath))
                EditorUtility.RevealInFinder(snapshot.RootPath);
            if (GUILayout.Button("Refresh"))
                Repaint();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Dirty Items", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < snapshot.DirtyItems.Count; i++)
            {
                SaveDirtySnapshot item = snapshot.DirtyItems[i];
                EditorGUILayout.LabelField(item.Key.ToString(), item.TypeName);
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
