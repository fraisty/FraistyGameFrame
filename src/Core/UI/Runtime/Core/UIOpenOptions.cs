using System;
using System.Threading;
using UnityEngine;

namespace FRFrame
{
    public struct UIOpenOptions
    {
        public object UserData;
        public CancellationToken CancellationToken;
        public float Timeout;
        public bool? BringToFront;
        public bool PlayAnimation;
        public bool HasPlayAnimationValue;
        public bool SuppressCancelLog;
        public Type StrongParamType;
        public bool? CloseOnMaskClickOverride;
        public bool? UseMaskOverride;
        public UIMaskMode? MaskModeOverride;
        public Color? MaskColorOverride;

        public static UIOpenOptions Default => new UIOpenOptions
        {
            PlayAnimation = true,
            HasPlayAnimationValue = true,
            SuppressCancelLog = true
        };

        public static UIOpenOptions Normalize(UIOpenOptions options)
        {
            if (!options.HasPlayAnimationValue)
                options.PlayAnimation = true;

            options.HasPlayAnimationValue = true;
            return options;
        }

        public static UIOpenOptions FromUserData(object userData, CancellationToken cancellationToken = default)
        {
            UIOpenOptions options = Default;
            options.UserData = userData;
            options.CancellationToken = cancellationToken;
            return options;
        }
    }

    public interface IUIOpenParamReceiver<in TParam>
    {
        void SetOpenParam(TParam param);
    }

    public interface IUIPreloadHandler
    {
        Cysharp.Threading.Tasks.UniTask OnPreloadAsync(CancellationToken cancellationToken);
    }
}
