using System;
using System.Collections.Generic;

namespace FRFrame
{
    [Serializable]
    public class FsmDebugInfo
    {
        public string Name;
        public string FsmName;
        public string TypeName;
        public string OwnerName;
        public string OwnerTypeName;
        public string CurrentStateName;
        public string CurrentStateTypeName;
        public float RunTime;
        public int StateCount;
        public bool IsDisposed;
        public bool IsPaused;
        public bool IsTask;
        public bool IsTaskFsm;
        public TaskState TaskState;
        public IReadOnlyList<string> StateNames;
        public IReadOnlyList<string> BlackboardKeys;
        public IReadOnlyList<FsmStateChangeRecord> StateHistory;
    }
}
