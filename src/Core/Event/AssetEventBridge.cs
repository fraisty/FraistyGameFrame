using System;

namespace FRFrame
{
    /// <summary>
    /// 资产事件桥接器。
    ///
    /// 第一版职责：
    /// - 单向桥接：AssetEvent -> EventBus
    /// - 显式绑定
    /// - Bind 返回 IDisposable，支持精确解绑
    /// - Bridge 自身也维护一个作用域，支持 Clear / Dispose 全量解绑
    /// </summary>
    public sealed class AssetEventBridge : IDisposable
    {
        private readonly EventBus _bus;
        private readonly EventSubscriptionScope _scope = new EventSubscriptionScope();
        private bool _disposed;

        public AssetEventBridge(EventBus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        }

        /// <summary>
        /// 将一个无参资产事件桥接为总线事件。
        /// 资产 Raise 时，立即 Publish(converter())。
        /// </summary>
        public IDisposable Bind<TEvent>(
            GameEventAsset asset,
            Func<TEvent> converter,
            object owner = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AssetEventBridge));

            if (asset == null)
                throw new ArgumentNullException(nameof(asset));

            if (converter == null)
                throw new ArgumentNullException(nameof(converter));

            IDisposable binding = asset.Register(() =>
            {
                _bus.Publish(converter());
            }, owner);

            _scope.Add(binding);
            return binding;
        }

        /// <summary>
        /// 将一个单参数资产事件桥接为总线事件。
        /// 资产 Raise(arg) 时，立即 Publish(converter(arg))。
        /// </summary>
        public IDisposable Bind<TArg, TEvent>(
            GameEventAsset<TArg> asset,
            Func<TArg, TEvent> converter,
            object owner = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AssetEventBridge));

            if (asset == null)
                throw new ArgumentNullException(nameof(asset));

            if (converter == null)
                throw new ArgumentNullException(nameof(converter));

            IDisposable binding = asset.Register(arg =>
            {
                _bus.Publish(converter(arg));
            }, owner);

            _scope.Add(binding);
            return binding;
        }

        /// <summary>
        /// 清除当前 Bridge 创建的全部桥接绑定。
        /// Bridge 本身仍可继续使用。
        /// </summary>
        public void Clear()
        {
            if (_disposed)
                return;

            _scope.Clear();
        }

        /// <summary>
        /// 清除当前 Bridge 创建的全部桥接绑定，并使 Bridge 进入终态。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _scope.Dispose();
        }
    }
}