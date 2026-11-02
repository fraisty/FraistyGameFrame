namespace FRFrame
{
    /// <summary>
    /// 资源下载流程状态机控制器。
    /// </summary>
    public class AssetsDownLoadFsmController : FsmTaskController
    {
        protected override void OnInitialize()
        {
            base.OnInitialize();
            AddState<PackageDownloadState>();
        }

        public void Start()
        {
            ChangeState<PackageDownloadState>();
        }
    }
}
