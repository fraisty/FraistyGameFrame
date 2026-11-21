using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace FRFrame
{
    /// <summary>
    /// 包初始化状态。
    /// 负责根据 PackageConfig.playMode 创建 InitializePackageOptions 并初始化 package。
    /// </summary>
    public class PackageInitState : FsmState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            InitializePackageAsync().Forget(FailPrepare);
        }

        private async UniTask InitializePackageAsync()
        {
            PackageConfig packageConfig = GetData("packageConfig") as PackageConfig;
            if (packageConfig == null)
            {
                FRLog.Error("PackageInitState failed, packageConfig is null.");
                FailPrepare();
                return;
            }

            YooAssetExtension yoo = FRFM.Resource.YooExtension;
            InitializePackageOptions initParams = yoo.CreateInitializeParameters(packageConfig);
            if (initParams == null)
            {
                FRLog.Error($"PackageInitState failed, init params create failed : {packageConfig.name}");
                FailPrepare();
                return;
            }

            bool ok = await yoo.InitializePackageAsync(
                packageConfig.name,
                initParams,
                packageConfig.isDefaultPackage);

            if (!ok)
            {
                FRLog.Error($"PackageInitState failed : {packageConfig.name}");
                FailPrepare();
                return;
            }

            ChangeState<PackageVersionUpdateState>();
        }

        private void FailPrepare(Exception exception = null)
        {
            if (exception != null) Debug.LogException(exception);
            UniTaskCompletionSource<bool> prepareDone = GetData("prepareDone") as UniTaskCompletionSource<bool>;
            prepareDone?.TrySetResult(false);
        }
    }
}
