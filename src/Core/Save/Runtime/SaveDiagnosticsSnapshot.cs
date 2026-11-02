using System.Collections.Generic;

namespace FRFrame
{
    public sealed class SaveDiagnosticsSnapshot
    {
        public string RootPath;
        public int CacheCount;
        public int DirtyCount;
        public int SaveFailureCount;
        public int LoadFailureCount;
        public int BackupRestoreCount;
        public List<SaveDirtySnapshot> DirtyItems = new List<SaveDirtySnapshot>();
    }

    public sealed class SaveDirtySnapshot
    {
        public SaveKey Key;
        public string TypeName;
    }
}
