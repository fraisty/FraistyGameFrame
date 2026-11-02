namespace FRFrame
{
    public sealed class ObjectPoolSnapshot
    {
        public string Name;
        public string ObjectTypeName;
        public int CountAll;
        public int CountActive;
        public int CountInactive;
        public int TotalCreated;
        public int TotalAcquired;
        public int TotalReleased;
        public int HitCount;
        public int MissCount;
    }
}
