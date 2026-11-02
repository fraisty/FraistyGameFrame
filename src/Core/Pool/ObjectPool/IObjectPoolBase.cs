namespace FRFrame
{
    public interface IObjectPoolBase
    {
        string Name { get; }
        int CountInactive { get; }
        int CountActive { get; }
        int CountAll { get; }
        int TotalCreated { get; }
        int TotalAcquired { get; }
        int TotalReleased { get; }
        int HitCount { get; }
        int MissCount { get; }
        string ObjectTypeName { get; }
        void Clear();
    }
}
