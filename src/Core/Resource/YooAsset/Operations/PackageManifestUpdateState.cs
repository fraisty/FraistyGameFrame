using Cysharp.Threading.Tasks;

namespace FRFrame
{
    /// <summary>
    /// 更新清单状态。
    /// </summary>
    public class PackageManifestUpdateState : FsmState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            UpdateManifestAsync().Forget();
        }

        private async UniTask UpdateManifestAsync()
        {
            PackageConfig packageConfig = GetData("packageConfig") as PackageConfig;
            if (packageConfig == null)
            {
                FRLog.Error("PackageManifestUpdateState failed, packageConfig is null.");
                FailPrepare();
                return;
            }

            string packageVersion = GetData("packageVersion") as string;
            if (string.IsNullOrEmpty(packageVersion))
            {
                FRLog.Error($"PackageManifestUpdateState failed, packageVersion is null : {packageConfig.name}");
                FailPrepare();
                return;
            }

            bool ok = await FRFM.Resource.YooExtension.UpdatePackageManifestAsync(packageConfig.name, packageVersion);
            if (!ok)
            {
                FRLog.Error($"Update package manifest failed : {packageConfig.name}");
                return;
            }

            ChangeState<PackageDownloaderCreateState>();
        }

        private void FailPrepare()
        {
            UniTaskCompletionSource<bool> prepareDone = GetData("prepareDone") as UniTaskCompletionSource<bool>;
            prepareDone?.TrySetResult(false);
        }
    }
}