using Cysharp.Threading.Tasks;

namespace FRFrame
{
    /// <summary>
    /// 请求包版本状态。
    /// 旧版逻辑是 RequestPackageVersionAsync。
    /// </summary>
    public class PackageVersionUpdateState : FsmState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            UpdatePackageVersionAsync().Forget();
        }

        private async UniTask UpdatePackageVersionAsync()
        {
            PackageConfig packageConfig = GetData("packageConfig") as PackageConfig;
            if (packageConfig == null)
            {
                FRLog.Error("PackageVersionUpdateState failed, packageConfig is null.");
                FailPrepare();
                return;
            }

            string version = await FRFM.Resource.YooExtension.RequestPackageVersionAsync(packageConfig.name);
            if (string.IsNullOrEmpty(version))
            {
                FRLog.Error($"Request package version failed : {packageConfig.name}");
                FailPrepare();
                return;
            }

            FRLog.Info($"Request package version success : {packageConfig.name}, version : {version}");
            SetData("packageVersion", version);

            ChangeState<PackageManifestUpdateState>();
        }

        private void FailPrepare()
        {
            UniTaskCompletionSource<bool> prepareDone = GetData("prepareDone") as UniTaskCompletionSource<bool>;
            prepareDone?.TrySetResult(false);
        }
    }
}