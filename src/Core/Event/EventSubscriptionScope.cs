using System;
using System.Collections.Generic;

namespace FRFrame
{
    /// <summary>
    /// 订阅作用域。
    /// 用于统一收集多个 IDisposable 订阅句柄，并在适当时机统一释放。
    ///
    /// 典型用法：
    /// var scope = new EventSubscriptionScope();
    /// bus.Subscribe<FooEvent>(OnFoo).AddTo(scope);
    /// ...
    /// scope.Dispose();
    /// </summary>
    public sealed class EventSubscriptionScope : IDisposable
    {
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>();
        private bool _disposed;

        /// <summary>
        /// 将一个订阅句柄加入当前作用域。
        /// </summary>
        /// <param name="subscription">订阅句柄，不能为 null。</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ObjectDisposedException"></exception>
        public void Add(IDisposable subscription)
        {
            if (subscription == null)
                throw new ArgumentNullException(nameof(subscription));

            if (_disposed)
                throw new ObjectDisposedException(nameof(EventSubscriptionScope));

            _subscriptions.Add(subscription);
        }

        /// <summary>
        /// 释放当前作用域中已收集的所有订阅。
        /// scope 本身仍可继续使用。
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < _subscriptions.Count; i++)
            {
                _subscriptions[i]?.Dispose();
            }

            _subscriptions.Clear();
        }

        /// <summary>
        /// 释放当前作用域中已收集的所有订阅，并使该 scope 进入终态。
        /// Dispose 后不允许继续 Add。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Clear();
        }
    }
}
