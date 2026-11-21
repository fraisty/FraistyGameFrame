using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

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
            UpdateManifestAsync().Forget(FailPrepare);
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
                FailPrepare();
                return;
            }

            ChangeState<PackageDownloaderCreateState>();
        }

        private void FailPrepare(Exception exception = null)
        {
            if (exception != null) Debug.LogException(exception);
            UniTaskCompletionSource<bool> prepareDone = GetData("prepareDone") as UniTaskCompletionSource<bool>;
            prepareDone?.TrySetResult(false);
        }
    }
}
