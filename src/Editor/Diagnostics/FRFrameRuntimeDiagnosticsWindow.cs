#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace FRFrame.Editor
{
    public sealed class FRFrameRuntimeDiagnosticsWindow : EditorWindow
    {
        private int _tab;
        private Vector2 _scroll;
        private string _filter;

        [MenuItem("FRFrame/Diagnostics/Runtime Diagnostics")]
        public static void Open()
        {
            GetWindow<FRFrameRuntimeDiagnosticsWindow>("FRFrame Diagnostics");
        }

        private void OnGUI()
        {
            _tab = GUILayout.Toolbar(_tab, new[] { "Resource", "Packages", "GameObject Pools", "Object Pools", "Reference Pools", "Warnings" });
            _filter = EditorGUILayout.TextField("Filter", _filter);

            if (GUILayout.Button("Refresh"))
                Repaint();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            switch (_tab)
            {
                case 0:
                    DrawResourceAssets();
                    break;
                case 1:
                    DrawPackages();
                    break;
                case 2:
                    DrawGameObjectPools();
                    break;
                case 3:
                    DrawObjectPools();
                    break;
                case 4:
                    DrawReferencePools();
                    break;
                case 5:
                    DrawWarnings();
                    break;
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawResourceAssets()
        {
            var resource = FRFM.Resource;
            if (resource == null || resource.Diagnostics == null) return;
            var assets = resource.Diagnostics.GetAssetSnapshots();
            for (int i = 0; i < assets.Count; i++)
            {
                var asset = assets[i];
                if (!PassFilter(asset.Key.ToString())) continue;
                EditorGUILayout.LabelField(asset.Key.ToString(), $"Ref:{asset.RefCount} {asset.State} {asset.Residency}");
            }
        }

        private void DrawPackages()
        {
            var resource = FRFM.Resource;
            if (resource == null) return;
            var packages = resource.GetAllPackageInfos();
            for (int i = 0; i < packages.Count; i++)
            {
                var package = packages[i];
                if (!PassFilter(package.PackageName)) continue;
                EditorGUILayout.LabelField(package.PackageName, $"{package.State} Default:{package.IsDefaultPackage} Version:{package.Version}");
            }
        }

        private void DrawGameObjectPools()
        {
            var pool = FRFM.Pool?.GameObjectPool;
            if (pool == null) return;
            var buckets = pool.GetBucketSnapshots();
            for (int i = 0; i < buckets.Count; i++)
            {
                var bucket = buckets[i];
                if (!PassFilter(bucket.Key.ToString())) continue;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(bucket.Key.ToString(), $"Inactive:{bucket.InactiveCount} Max:{bucket.MaxInactiveCount}");
                if (GUILayout.Button("Clear", GUILayout.Width(60))) pool.Clear(bucket.Key);
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawObjectPools()
        {
            var objectPool = FRFM.Pool?.ObjectPool;
            if (objectPool == null) return;
            var snapshots = objectPool.GetSnapshots();
            for (int i = 0; i < snapshots.Count; i++)
            {
                var snapshot = snapshots[i];
                if (!PassFilter(snapshot.Name)) continue;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(snapshot.Name, $"Active:{snapshot.CountActive} Inactive:{snapshot.CountInactive} Hit:{snapshot.HitCount} Miss:{snapshot.MissCount}");
                if (GUILayout.Button("Clear", GUILayout.Width(60))) objectPool.ClearPool(snapshot.Name);
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawReferencePools()
        {
            var referencePool = FRFM.Pool?.ReferencePool;
            if (referencePool == null) return;
            var snapshots = referencePool.GetSnapshots();
            for (int i = 0; i < snapshots.Count; i++)
            {
                var snapshot = snapshots[i];
                string name = snapshot.Type != null ? snapshot.Type.FullName : string.Empty;
                if (!PassFilter(name)) continue;
                EditorGUILayout.LabelField(name, $"Cached:{snapshot.CachedCount} Max:{snapshot.MaxCachedCount}");
            }
        }

        private void DrawWarnings()
        {
            var resource = FRFM.Resource;
            if (resource == null || resource.Diagnostics == null) return;
            var warnings = resource.Diagnostics.CreateSnapshot().Warnings;
            for (int i = 0; i < warnings.Count; i++)
            {
                if (!PassFilter(warnings[i])) continue;
                EditorGUILayout.HelpBox(warnings[i], MessageType.Warning);
            }
        }

        private bool PassFilter(string text)
        {
            return string.IsNullOrEmpty(_filter) || (!string.IsNullOrEmpty(text) && text.ToLower().Contains(_filter.ToLower()));
        }
    }
}
#endif
