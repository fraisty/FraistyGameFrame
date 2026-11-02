namespace FRFrame
{
    /// <summary>
    /// 资源准备流程状态机控制器。
    /// 以旧版 AssetsProcess 为准：
    /// PackageInit -> PackageVersionUpdate -> PackageManifestUpdate -> PackageDownloaderCreate
    /// </summary>
    public class AssetsPrepareFsmController : FsmTaskController
    {
        protected override void OnInitialize()
        {
            base.OnInitialize();
            AddState<PackageInitState>();
            AddState<PackageVersionUpdateState>();
            AddState<PackageManifestUpdateState>();
            AddState<PackageDownloaderCreateState>();
        }

        public void Start()
        {
            ChangeState<PackageInitState>();
        }
    }
}
