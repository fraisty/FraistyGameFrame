namespace FRFrame
{
    public enum TaskState
    {
        None = 0,
        Ing = 1,
        Succ = 2,
        Fail = 3,
        Running = Ing,
        Completed = Succ,
        Failed = Fail,
        Canceled = 4
    }
}
