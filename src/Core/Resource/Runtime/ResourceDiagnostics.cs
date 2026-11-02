using System.Collections.Generic;
using System.Text;

namespace FRFrame
{
    public sealed class ResourceDiagnostics
    {
        private readonly ResourceManager _resourceManager;

        public ResourceDiagnostics(ResourceManager resourceManager)
        {
            _resourceManager = resourceManager;
        }

        public ResourceStatistics GetStatistics()
        {
            return _resourceManager.GetStatistics();
        }

        public ResourceDiagnosticSnapshot CreateSnapshot()
        {
            var snapshot = new ResourceDiagnosticSnapshot
            {
                Statistics = GetStatistics(),
                Assets = new List<AssetEntrySnapshot>(_resourceManager.GetAssetSnapshots()),
                Scenes = new List<SceneEntrySnapshot>(_resourceManager.GetSceneSnapshots()),
                Packages = new List<PackageResourceSnapshot>(_resourceManager.GetPackageResourceSnapshots())
            };

            for (int i = 0; i < snapshot.Assets.Count; i++)
            {
                var asset = snapshot.Assets[i];
                if (asset.State == ResourceEntryState.Loaded && !asset.HandleValid)
                    snapshot.Warnings.Add($"Asset loaded but handle invalid : {asset.Key}");
                if (asset.State == ResourceEntryState.Failed)
                    snapshot.Warnings.Add($"Asset failed entry still cached : {asset.Key}");
                if (_resourceManager.GetPackageState(asset.PackageName) != PackageRuntimeState.Initialized)
                    snapshot.Warnings.Add($"Asset package not initialized : {asset.Key}");
            }

            var buckets = FRFM.Pool?.GameObjectPool?.GetBucketSnapshots();
            if (buckets != null)
            {
                for (int i = 0; i < buckets.Count; i++)
                {
                    var bucket = buckets[i];
                    if (_resourceManager.GetAssetEntry(bucket.Key) == null)
                        snapshot.Warnings.Add($"GameObjectPool bucket has no asset entry : {bucket.Key}");
                    if (!string.IsNullOrEmpty(bucket.Warning))
                        snapshot.Warnings.Add($"GameObjectPool bucket warning : {bucket.Key}, {bucket.Warning}");
                }
            }

            return snapshot;
        }

        public IReadOnlyList<AssetEntrySnapshot> GetAssetSnapshots(string packageName = null)
        {
            return _resourceManager.GetAssetSnapshots(packageName);
        }

        public IReadOnlyList<SceneEntrySnapshot> GetSceneSnapshots(string packageName = null)
        {
            return _resourceManager.GetSceneSnapshots(packageName);
        }

        public string DumpSummary()
        {
            var stats = GetStatistics();
            var sb = new StringBuilder();
            sb.AppendLine("==== Resource Diagnostics ====");
            sb.AppendLine(stats.ToString());
            return sb.ToString();
        }

        public string DumpDetail()
        {
            var snapshot = CreateSnapshot();
            var sb = new StringBuilder();
            sb.AppendLine(DumpSummary());
            sb.AppendLine("---- Packages ----");
            for (int i = 0; i < snapshot.Packages.Count; i++)
            {
                var package = snapshot.Packages[i];
                sb.AppendLine($"{package.PackageName} Assets:{package.AssetCount} Scenes:{package.SceneCount} Loading:{package.LoadingAssetCount}");
            }
            sb.AppendLine("---- Assets ----");
            for (int i = 0; i < snapshot.Assets.Count; i++)
            {
                var asset = snapshot.Assets[i];
                sb.AppendLine($"{asset.Key} Ref:{asset.RefCount} State:{asset.State} Residency:{asset.Residency} Handle:{asset.HandleValid}");
            }
            sb.AppendLine("---- Warnings ----");
            for (int i = 0; i < snapshot.Warnings.Count; i++)
                sb.AppendLine(snapshot.Warnings[i]);
            return sb.ToString();
        }
    }
}
