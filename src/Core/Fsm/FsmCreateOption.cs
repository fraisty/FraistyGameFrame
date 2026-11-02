namespace FRFrame
{
    public class FsmCreateOption
    {
        /// <summary>
        /// 仅对 FsmTaskController 生效：任务完成后自动移除
        /// </summary>
        public bool AutoRemoveWhenTaskDone { get; set; }

        /// <summary>
        /// 创建者/拥有者，可用于批量清理
        /// </summary>
        public object Owner { get; set; }

        /// <summary>
        /// 是否开启日志
        /// </summary>
        public bool EnableLog { get; set; }
    }
}
