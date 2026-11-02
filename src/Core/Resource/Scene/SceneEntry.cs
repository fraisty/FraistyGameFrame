using YooAsset;

namespace FRFrame
{
    /// <summary>
    /// 场景资源条目。
    /// 场景与普通资源分开管理。
    /// </summary>
    public sealed class SceneEntry : ResourceEntryBase
    {
        /// <summary>
        /// YooAsset 场景句柄。
        /// </summary>
        public SceneHandle Handle;

        /// <summary>
        /// 是否主场景。
        /// 对于 Single 模式加载的场景一般可标记为主场景。
        /// </summary>
        public bool IsMainScene;
    }
}
