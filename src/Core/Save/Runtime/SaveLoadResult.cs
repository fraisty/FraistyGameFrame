namespace FRFrame
{
    public readonly struct SaveLoadResult<T>
    {
        public readonly bool Success;
        public readonly bool FromCache;
        public readonly bool FromBackup;
        public readonly T Data;
        public readonly string Error;

        public SaveLoadResult(bool success, T data, bool fromCache = false, bool fromBackup = false, string error = null)
        {
            Success = success;
            Data = data;
            FromCache = fromCache;
            FromBackup = fromBackup;
            Error = error;
        }
    }
}
