namespace FRFrame
{
    /// <summary>
    /// 可被引用池管理的对象接口。
    /// Release 时会调用 Clear 重置数据。
    /// </summary>
    public interface IReference
    {
        /// <summary>
        /// 清理对象状态。
        /// </summary>
        void Clear();
    }
}
