using System;
using System.Collections.Generic;

namespace FRFrame
{
    [Serializable]
    public sealed class SaveSlotInfo
    {
        public string SlotId;
        public string DisplayName;
        public string PackageName;
        public string UserId;
        public string AppVersion;
        public int DataVersion;
        public DateTime CreateTime;
        public DateTime UpdateTime;
        public string PreviewImagePath;
        public Dictionary<string, string> Tags = new Dictionary<string, string>();
    }
}
