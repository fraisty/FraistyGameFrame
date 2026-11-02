using Cysharp.Threading.Tasks;
using YooAsset;

namespace FRFrame
{
    /// <summary>
    /// 下载状态。
    /// </summary>
    public class PackageDownloadState : FsmState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            BeginDownloadAsync().Forget();
        }

        private async UniTask BeginDownloadAsync()
        {
            PackageConfig packageConfig = GetData("packageConfig") as PackageConfig;
            ResourceDownloaderOperation downloader = GetData("downloader") as ResourceDownloaderOperation;

            if (packageConfig == null)
            {
                FRLog.Error("PackageDownloadState failed, packageConfig is null.");
                FailPrepare();
                return;
            }

            if (downloader == null)
            {
                FRLog.Warning($"PackageDownloadState ignored, downloader is null : {packageConfig.name}");

                UniTaskCompletionSource<bool> nullDownloaderDone = GetData("prepareDone") as UniTaskCompletionSource<bool>;
                nullDownloaderDone?.TrySetResult(true);
                return;
            }

            downloader.DownloadError += data =>
            {
                FRLog.Error($"{data.FileName} download failed : {data.ErrorInfo}");
            };

            downloader.DownloadProgressChanged += data =>
            {
                // 你后续可以在这里接 UI 进度
                // FRLog.Info($"Download progress:{data.Progress}");
            };

            downloader.DownloadCompleted += data =>
            {
                FRLog.Info($"Downloader finished : {data.PackageName}, succeed : {data.Succeeded}");
            };

            downloader.DownloadFileStarted += data =>
            {
                FRLog.Info($"Begin download file : {data.FileName}, size : {data.FileSize}");
            };

            downloader.StartDownload();
            await downloader.ToUniTask();

            if (downloader.Status != EOperationStatus.Succeeded)
            {
                FRLog.Error($"Package download failed : {packageConfig.name}");
                FailPrepare();
                return;
            }

            await FRFM.Resource.YooExtension.ClearUnusedCacheFilesAsync(packageConfig.name);
            FRLog.Info($"Package download completed : {packageConfig.name}");

            UniTaskCompletionSource<bool> prepareDoneSignal = GetData("prepareDone") as UniTaskCompletionSource<bool>;
            prepareDoneSignal?.TrySetResult(true);
        }

        private void FailPrepare()
        {
            UniTaskCompletionSource<bool> prepareDone = GetData("prepareDone") as UniTaskCompletionSource<bool>;
            prepareDone?.TrySetResult(false);
        }
    }
}
