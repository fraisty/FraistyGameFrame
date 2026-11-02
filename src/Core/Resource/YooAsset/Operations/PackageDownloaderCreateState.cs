using Cysharp.Threading.Tasks;
using YooAsset;

namespace FRFrame
{
    /// <summary>
    /// 创建下载器状态。
    /// </summary>
    public class PackageDownloaderCreateState : FsmState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            CreateDownloader();
        }

        private void CreateDownloader()
        {
            PackageConfig packageConfig = GetData("packageConfig") as PackageConfig;
            if (packageConfig == null)
            {
                FRLog.Error("PackageDownloaderCreateState failed, packageConfig is null.");
                FailPrepare();
                return;
            }

            ResourceDownloaderOperation downloader = FRFM.Resource.YooExtension.CreateDownloader(
                packageConfig.name,
                YooAssetConfigs.DownloadingMaxNum,
                YooAssetConfigs.FailedTryAgain);

            if (downloader == null)
            {
                FRLog.Error($"Create downloader failed : {packageConfig.name}");
                FailPrepare();
                return;
            }

            if (downloader.TotalDownloadCount == 0)
            {
                FRLog.Info($"No files need download : {packageConfig.name}");

                UniTaskCompletionSource<bool> prepareDoneSignal = GetData("prepareDone") as Cysharp.Threading.Tasks.UniTaskCompletionSource<bool>;
                prepareDoneSignal?.TrySetResult(true);
                return;
            }

            SetData("downloader", downloader);

            // 如果存在下载 FSM，则交给下载 FSM
            AssetsDownLoadFsmController downloadController = FRFM.Fsm.CreateFsm<AssetsDownLoadFsmController>(
                new FsmCreateOption
                {
                    Owner = this,
                    AutoRemoveWhenTaskDone = true,
                    EnableLog = true,
                });

            // 透传数据
            downloadController.SetData("packageConfig", packageConfig);
            downloadController.SetData("downloader", downloader);
            downloadController.SetData("prepareDone", GetData("prepareDone"));
            downloadController.Start();
        }

        private void FailPrepare()
        {
            UniTaskCompletionSource<bool> prepareDone = GetData("prepareDone") as UniTaskCompletionSource<bool>;
            prepareDone?.TrySetResult(false);
        }
    }
}