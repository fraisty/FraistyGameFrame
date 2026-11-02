using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine.Events;
using UnityEngine.UI;

namespace FRFrame
{
    public sealed class UIEventScopeSet : IDisposable
    {
        private readonly EventSubscriptionScope _disposeScope = new EventSubscriptionScope();
        private readonly EventSubscriptionScope _openScope = new EventSubscriptionScope();
        private readonly EventSubscriptionScope _visibleScope = new EventSubscriptionScope();

        public IDisposable Add(IDisposable subscription, UIEventListenMode mode)
        {
            if (subscription == null)
                return null;

            switch (mode)
            {
                case UIEventListenMode.WhileOpen:
                    subscription.AddTo(_openScope);
                    break;
                case UIEventListenMode.WhileVisible:
                    subscription.AddTo(_visibleScope);
                    break;
                default:
                    subscription.AddTo(_disposeScope);
                    break;
            }

            return subscription;
        }

        public void ClearOpen()
        {
            _openScope.Clear();
            _visibleScope.Clear();
        }

        public void ClearVisible()
        {
            _visibleScope.Clear();
        }

        public void Dispose()
        {
            _visibleScope.Dispose();
            _openScope.Dispose();
            _disposeScope.Dispose();
        }
    }

    public sealed class UnityEventSubscription : IDisposable
    {
        private readonly Action _removeAction;
        private bool _disposed;

        public UnityEventSubscription(Action removeAction)
        {
            _removeAction = removeAction;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _removeAction?.Invoke();
        }
    }

    public static class UIEventUtility
    {
        public static IDisposable AddButtonClick(Button button, UnityAction action)
        {
            if (button == null || action == null)
                return null;

            button.onClick.AddListener(action);
            return new UnityEventSubscription(() => button.onClick.RemoveListener(action));
        }

        public static IDisposable AddToggleChanged(Toggle toggle, UnityAction<bool> action)
        {
            if (toggle == null || action == null)
                return null;

            toggle.onValueChanged.AddListener(action);
            return new UnityEventSubscription(() => toggle.onValueChanged.RemoveListener(action));
        }

        public static IDisposable AddSliderChanged(Slider slider, UnityAction<float> action)
        {
            if (slider == null || action == null)
                return null;

            slider.onValueChanged.AddListener(action);
            return new UnityEventSubscription(() => slider.onValueChanged.RemoveListener(action));
        }

        public static IDisposable AddInputChanged(InputField input, UnityAction<string> action)
        {
            if (input == null || action == null)
                return null;

            input.onValueChanged.AddListener(action);
            return new UnityEventSubscription(() => input.onValueChanged.RemoveListener(action));
        }

        public static IDisposable AddInputChanged(TMP_InputField input, UnityAction<string> action)
        {
            if (input == null || action == null)
                return null;

            input.onValueChanged.AddListener(action);
            return new UnityEventSubscription(() => input.onValueChanged.RemoveListener(action));
        }

        public static IDisposable AddUIEvent(UnityEvent unityEvent, UnityAction action)
        {
            if (unityEvent == null || action == null)
                return null;

            unityEvent.AddListener(action);
            return new UnityEventSubscription(() => unityEvent.RemoveListener(action));
        }

        public static IDisposable AddUIEvent<T>(UnityEvent<T> unityEvent, UnityAction<T> action)
        {
            if (unityEvent == null || action == null)
                return null;

            unityEvent.AddListener(action);
            return new UnityEventSubscription(() => unityEvent.RemoveListener(action));
        }
    }
}
