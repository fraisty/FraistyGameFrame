using System;
using System.Collections.Generic;
using UnityEngine;

namespace FRFrame
{
    public sealed class EventSubscriptionScopeComponent : MonoBehaviour
    {
        private readonly EventSubscriptionScope _scope = new EventSubscriptionScope();

        public void Add(IDisposable subscription)
        {
            _scope.Add(subscription);
        }

        private void OnDestroy()
        {
            _scope.Dispose();
        }
    }
}
