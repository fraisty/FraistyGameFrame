using UnityEditor;
using UnityEngine;

namespace FRFrame.Editor
{
    public sealed class FRFrameUIDiagnosticsWindow : EditorWindow
    {
        private Vector2 _scroll;
        private UIManagerSnapshot _snapshot;

        [MenuItem("FRFrame/Diagnostics/UI Diagnostics")]
        public static void Open()
        {
            GetWindow<FRFrameUIDiagnosticsWindow>("UI Diagnostics").Show();
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh", GUILayout.Width(100f)))
                    RefreshSnapshot();
                if (GUILayout.Button("Clear Snapshot", GUILayout.Width(120f)))
                    _snapshot = null;
            }

            if (_snapshot == null)
                RefreshSnapshot();

            if (_snapshot == null)
            {
                EditorGUILayout.HelpBox("FRFM.UI 尚未初始化，进入 Play Mode 并初始化框架后可查看运行时 UI 诊断。", MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawSummary();
            DrawServices();
            DrawGroups();
            DrawForms();
            DrawMasks();
            DrawPreloads();
            DrawLoadingForms();
            EditorGUILayout.EndScrollView();
        }

        private void RefreshSnapshot()
        {
            _snapshot = FRFM.UI != null ? FRFM.UI.CreateSnapshot() : null;
        }

        private void DrawSummary()
        {
            EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Opened", _snapshot.OpenedCount.ToString());
            EditorGUILayout.LabelField("Hidden", _snapshot.HiddenCount.ToString());
            EditorGUILayout.LabelField("Preloaded", _snapshot.PreloadedCount.ToString());
            EditorGUILayout.LabelField("Loading", _snapshot.LoadingCount.ToString());
            EditorGUILayout.LabelField("Navigation", _snapshot.NavigationCount.ToString());
            EditorGUILayout.Space();
        }

        private void DrawServices()
        {
            EditorGUILayout.LabelField("Services", EditorStyles.boldLabel);
            if (_snapshot.Toast != null)
                EditorGUILayout.LabelField("Toast", $"showing={_snapshot.Toast.Showing}, queue={_snapshot.Toast.QueueCount}, current={_snapshot.Toast.CurrentMessage}");
            if (_snapshot.Dialog != null)
                EditorGUILayout.LabelField("Dialog", $"running={_snapshot.Dialog.QueueRunning}, queue={_snapshot.Dialog.QueueCount}, current={_snapshot.Dialog.CurrentTitle}");
            if (_snapshot.Loading != null)
                EditorGUILayout.LabelField("Loading", $"visible={_snapshot.Loading.Visible}, refs={_snapshot.Loading.RefCount}, message={_snapshot.Loading.Message}, progress={_snapshot.Loading.Progress:0.00}");
            EditorGUILayout.Space();
        }

        private void DrawGroups()
        {
            EditorGUILayout.LabelField("Groups", EditorStyles.boldLabel);
            foreach (UIGroupSnapshot group in _snapshot.Groups)
                EditorGUILayout.LabelField(group.Layer.ToString(), $"forms={group.FormCount}, sortingBase={group.SortingOrderBase}");
            EditorGUILayout.Space();
        }

        private void DrawForms()
        {
            EditorGUILayout.LabelField("Forms", EditorStyles.boldLabel);
            foreach (UIFormSnapshot form in _snapshot.Forms)
            {
                EditorGUILayout.LabelField($"#{form.SerialId} {form.FormId}", $"state={form.State}, layer={form.Layer}, depth={form.Depth}, top={form.IsTopOfFormId}, order={form.SortingOrder}, pkg={form.PackageName}, visible={form.IsVisible}, mask={form.UseMask}/{form.MaskMode}");
            }
            EditorGUILayout.Space();
        }

        private void DrawMasks()
        {
            EditorGUILayout.LabelField("Masks", EditorStyles.boldLabel);
            foreach (UIMaskSnapshot mask in _snapshot.Masks)
                EditorGUILayout.LabelField(mask.Layer.ToString(), $"active={mask.Active}, order={mask.SortingOrder}, target={mask.TargetFormId}#{mask.TargetSerialId}, mode={mask.MaskMode}, closeOnClick={mask.CloseOnMaskClick}");
            EditorGUILayout.Space();
        }

        private void DrawPreloads()
        {
            EditorGUILayout.LabelField("Preloads", EditorStyles.boldLabel);
            foreach (UIPreloadSnapshot preload in _snapshot.Preloads)
                EditorGUILayout.LabelField(preload.FormId, $"mode={preload.Mode}, asset={preload.AssetPreloaded}, instance={preload.InstanceWarmed}, pkg={preload.PackageName}, path={preload.AssetPath}");
            EditorGUILayout.Space();
        }

        private void DrawLoadingForms()
        {
            EditorGUILayout.LabelField("Loading Operations", EditorStyles.boldLabel);
            foreach (UILoadingFormSnapshot loading in _snapshot.LoadingForms)
                EditorGUILayout.LabelField($"#{loading.SerialId} {loading.FormId}", $"cancel={loading.CancelRequested}, hasForm={loading.HasFormInstance}");
            EditorGUILayout.Space();
        }
    }
}
