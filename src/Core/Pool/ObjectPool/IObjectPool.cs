namespace FRFrame
{
    /// <summary>
    /// 泛型对象池接口。
    /// </summary>
    public interface IObjectPool<T> : IObjectPoolBase where T : class
    {
        /// <summary>
        /// 获取对象。
        /// </summary>
        T Acquire();

        /// <summary>
        /// 回收对象。
        /// </summary>
        void Release(T obj);
    }
}
