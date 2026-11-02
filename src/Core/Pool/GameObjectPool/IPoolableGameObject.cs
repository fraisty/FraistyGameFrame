namespace FRFrame
{
    public interface IPoolSpawnHandler
    {
        void OnSpawnFromPool();
    }

    public interface IPoolRecycleHandler
    {
        void OnRecycleToPool();
    }

    public interface IPoolDestroyHandler
    {
        void OnDestroyFromPool();
    }

    public interface IPoolableGameObject : IPoolSpawnHandler, IPoolRecycleHandler, IPoolDestroyHandler
    {
    }
}
