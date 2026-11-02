using System;

namespace FRFrame.Gameplay
{
    /// <summary>
    /// 为一个 Gameplay 运行域顺序分配非零来源标识。
    /// 同一运行域中的所有运行时来源必须共享同一个实例，不可与手写 ID 或其他分配器产生的 ID 混用。
    /// 分配器按单线程同步使用，已发出的标识不会回收或复用；耗尽 long 正数范围时抛出 OverflowException。
    /// </summary>
    public sealed class GameplaySourceIdAllocator
    {
        private long _lastValue;

        /// <summary>
        /// 返回一个从未由此分配器发出过的新 GameplaySourceId；正数范围耗尽时抛出 OverflowException。
        /// </summary>
        public GameplaySourceId Allocate()
        {
            long value = checked(_lastValue + 1);
            _lastValue = value;
            return new GameplaySourceId(value);
        }
    }
}
