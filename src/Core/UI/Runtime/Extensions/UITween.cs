using System;
using UnityEngine;

namespace FRFrame
{
    public static class UITween
    {
        private static IUITweenDriver _driver = new UIDOTweenDriver();

        public static IUITweenDriver Driver => _driver;

        public static void SetDriver(IUITweenDriver driver)
        {
            _driver = driver ?? new UIDOTweenDriver();
        }

        public static UITweenHandle Move(RectTransform target, Vector2 to, float duration, UIAnimationEase ease = UIAnimationEase.OutQuad, bool ignoreTimeScale = true)
        {
            return _driver.Move(target, to, duration, ease, ignoreTimeScale);
        }

        public static UITweenHandle Scale(Transform target, Vector3 to, float duration, UIAnimationEase ease = UIAnimationEase.OutQuad, bool ignoreTimeScale = true)
        {
            return _driver.Scale(target, to, duration, ease, ignoreTimeScale);
        }

        public static UITweenHandle Fade(CanvasGroup target, float to, float duration, UIAnimationEase ease = UIAnimationEase.OutQuad, bool ignoreTimeScale = true)
        {
            return _driver.Fade(target, to, duration, ease, ignoreTimeScale);
        }

        public static UITweenHandle Value(float from, float to, float duration, Action<float> onUpdate, UIAnimationEase ease = UIAnimationEase.OutQuad, bool ignoreTimeScale = true)
        {
            return _driver.Value(from, to, duration, onUpdate, ease, ignoreTimeScale);
        }
    }
}
