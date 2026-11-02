using System;
using UnityEngine;

namespace FRFrame
{
    /// <summary>
    /// 订阅扩展工具。
    /// </summary>
    public static class EventSubscriptionExtensions
    {
        /// <summary>
        /// 将一个 IDisposable 订阅句柄加入指定作用域，并返回原对象，便于链式调用。
        /// </summary>
        public static TDisposable AddTo<TDisposable>(
            this TDisposable disposable,
            EventSubscriptionScope scope)
            where TDisposable : IDisposable
        {
            if (disposable == null)
                throw new ArgumentNullException(nameof(disposable));

            if (scope == null)
                throw new ArgumentNullException(nameof(scope));

            scope.Add(disposable);
            return disposable;
        }

        public static TDisposable AddTo<TDisposable>(
            this TDisposable disposable,
            MonoBehaviour owner)
            where TDisposable : IDisposable
        {
            if (disposable == null)
                throw new ArgumentNullException(nameof(disposable));

            if (owner == null)
                throw new ArgumentNullException(nameof(owner));

            EventSubscriptionScopeComponent component = owner.GetComponent<EventSubscriptionScopeComponent>();
            if (component == null)
                component = owner.gameObject.AddComponent<EventSubscriptionScopeComponent>();

            component.Add(disposable);
            return disposable;
        }
    }
}
