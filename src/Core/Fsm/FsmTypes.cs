using System;
using System.Collections.Generic;

namespace FRFrame
{
    public sealed class FsmTransitionRule
    {
        public Type From;
        public Type To;
    }

    public enum FsmTransitionPolicy
    {
        Free,
        WarningOnly,
        Strict
    }

    public sealed class FsmTaskResult
    {
        public TaskState State;
        public bool IsSuccess => State == TaskState.Completed;
        public bool IsCanceled => State == TaskState.Canceled;
        public bool IsFailed => State == TaskState.Failed;
        public string CompleteStateName;
        public string Error;
        public Exception Exception;
        public string CancelReason;
        public FsmTaskController Controller;
    }

    public sealed class FsmStateChangeRecord
    {
        public string FromState;
        public string ToState;
        public float Time;
        public int Frame;
    }

    public sealed class FsmManagerStatistics
    {
        public int ActiveCount;
        public int TotalCreated;
        public int TotalDisposed;
        public int TotalAutoRemoved;
        public int TaskFsmCount;
    }

    public sealed class FsmGraphConfig
    {
        public string InitialState;
        public List<FsmGraphTransition> Transitions;
    }

    public sealed class FsmGraphTransition
    {
        public string From;
        public string To;
    }
}
