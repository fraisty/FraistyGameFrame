using System;
using FRFrame.Gameplay.Authoring;
using UnityEditor;

namespace FRFrame.Gameplay.Editor
{
    /// <summary>Discovers exactly one project authoring catalog; never supplies Runtime identities.</summary>
    [InitializeOnLoad]
    public static class GameplayReferenceCatalogProvider
    {
        private static bool _cached;
        private static GameplayReferenceCatalogAsset _catalog;
        private static string _error;

        static GameplayReferenceCatalogProvider()
        {
            // Membership changes invalidate discovery. Editing catalog contents does not:
            // drawers read the live asset, so typing must never trigger another project scan.
            EditorApplication.projectChanged += Invalidate;
        }

        private static void Invalidate() => _cached = false;

        public static GameplayReferenceCatalogAsset Find(out string error)
        {
            if (_cached && (_error != null || _catalog != null))
            {
                error = _error;
                return _catalog;
            }
            GameplayReferenceCatalogAsset found = null;
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:GameplayReferenceCatalogAsset"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameplayReferenceCatalogAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null || asset.GetType() != typeof(GameplayReferenceCatalogAsset)) continue;
                count++;
                found = asset;
            }
            _error = count == 0 ? "No GameplayReferenceCatalogAsset found. Raw reference editing remains available." :
                count > 1 ? $"Found {count} GameplayReferenceCatalogAsset assets; choose one by removing duplicates." : null;
            _catalog = count == 1 ? found : null;
            _cached = true;
            error = _error;
            return _catalog;
        }
    }
}
