using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace FRFrame
{
    [Serializable]
    public sealed class UIAnimationNamedEvent : UnityEvent<string>
    {
    }

    [Serializable]
    public sealed class UIAnimationStringEvent : UnityEvent<string>
    {
    }

    [Serializable]
    public sealed class UIAnimationIntEvent : UnityEvent<int>
    {
    }

    [Serializable]
    public sealed class UIAnimationFloatEvent : UnityEvent<float>
    {
    }

    [Serializable]
    public sealed class UIAnimationObjectEvent : UnityEvent<UnityEngine.Object>
    {
    }

    public sealed class UIAnimationEventBridge : MonoBehaviour
    {
        private sealed class DisposableAction : IDisposable
        {
            private readonly Action _disposeAction;
            private bool _disposed;

            public DisposableAction(Action disposeAction)
            {
                _disposeAction = disposeAction;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                _disposeAction?.Invoke();
            }
        }

        public UnityEvent onTriggered = new UnityEvent();
        public UIAnimationNamedEvent onNamedTriggered = new UIAnimationNamedEvent();
        public UIAnimationStringEvent onStringTriggered = new UIAnimationStringEvent();
        public UIAnimationIntEvent onIntTriggered = new UIAnimationIntEvent();
        public UIAnimationFloatEvent onFloatTriggered = new UIAnimationFloatEvent();
        public UIAnimationObjectEvent onObjectTriggered = new UIAnimationObjectEvent();

        private readonly UIEventScopeSet _listeners = new UIEventScopeSet();

        public void Trigger()
        {
            onTriggered.Invoke();
        }

        public void TriggerName(string eventName)
        {
            onNamedTriggered.Invoke(eventName);
        }

        public void TriggerString(string value)
        {
            onStringTriggered.Invoke(value);
        }

        public void TriggerInt(int value)
        {
            onIntTriggered.Invoke(value);
        }

        public void TriggerFloat(float value)
        {
            onFloatTriggered.Invoke(value);
        }

        public void TriggerObject(UnityEngine.Object value)
        {
            onObjectTriggered.Invoke(value);
        }

        public IDisposable AddListener(string eventName, Action callback)
        {
            if (callback == null)
                return null;

            UnityAction<string> listener = value =>
            {
                if (value == eventName)
                    callback.Invoke();
            };
            onNamedTriggered.AddListener(listener);
            IDisposable subscription = new DisposableAction(() => onNamedTriggered.RemoveListener(listener));
            return _listeners.Add(subscription, UIEventListenMode.UntilDispose);
        }

        public IDisposable AddOnce(string eventName, Action callback)
        {
            IDisposable subscription = null;
            subscription = AddListener(eventName, () =>
            {
                subscription?.Dispose();
                callback?.Invoke();
            });
            return subscription;
        }

        private void OnDestroy()
        {
            _listeners.Dispose();
            onTriggered.RemoveAllListeners();
            onNamedTriggered.RemoveAllListeners();
            onStringTriggered.RemoveAllListeners();
            onIntTriggered.RemoveAllListeners();
            onFloatTriggered.RemoveAllListeners();
            onObjectTriggered.RemoveAllListeners();
        }
    }
}
