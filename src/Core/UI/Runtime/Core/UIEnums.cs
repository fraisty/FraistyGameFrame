using System;

namespace FRFrame
{
    /// <summary>
    /// UI层级类型。
    /// </summary>
    public enum UIGroupLayer
    {
        Background = 0,
        Normal = 1,
        Popup = 2,
        Top = 3,
        Overlay = 4,
        System = 5
    }

    /// <summary>
    /// 窗口关闭策略。
    /// </summary>
    public enum UIFormCloseMode
    {
        Destroy = 0,
        Cache = 1
    }

    /// <summary>
    /// 窗口状态。
    /// </summary>
    public enum UIFormState
    {
        None = 0,
        Loading = 1,
        Initialized = 2,
        Preloaded = 3,
        Opened = 4,
        Hidden = 5,
        Closing = 6,
        Closed = 7,
        Disposed = 8
    }

    /// <summary>
    /// 子组件状态。
    /// </summary>
    public enum UIWidgetState
    {
        None = 0,
        Initialized = 1,
        Opened = 2,
        Hidden = 3,
        Disposed = 4
    }

    /// <summary>
    /// 遮挡可见策略。
    /// </summary>
    public enum UICoverVisualMode
    {
        KeepVisible = 0,
        HideByCanvasGroup = 1,
        SetInactive = 2
    }

    public enum UIBlockingMode
    {
        None = 0,
        FullScreenOnly = 1,
        Always = 2
    }

    public enum UIOpenFailureReason
    {
        None = 0,
        InvalidFormId = 1,
        ConfigNotFound = 2,
        Canceled = 3,
        LoadFailed = 4,
        TypeNotFound = 5,
        CreateFailed = 6
    }

    public enum UINavigationFailureReason
    {
        None = 0,
        OpenFailed = 1,
        Canceled = 2
    }

    public enum UIEventListenMode
    {
        UntilDispose = 0,
        WhileOpen = 1,
        WhileVisible = 2
    }

    public enum UIMaskMode
    {
        None = 0,
        Transparent = 1,
        Dim = 2,
        CustomColor = 3,
        Blur = 4
    }

    public enum UIToastPosition
    {
        Top = 0,
        Center = 1,
        Bottom = 2
    }

    public enum UIDialogResult
    {
        None = 0,
        Confirm = 1,
        Cancel = 2,
        Close = 3,
        Timeout = 4
    }

    public enum UIPreloadMode
    {
        AssetOnly = 0,
        InstanceWarmup = 1,
        AssetAndInstance = 2
    }

    public enum UINavClosePolicy
    {
        HidePrevious = 0,
        ClosePrevious = 1,
        KeepPrevious = 2
    }

    public enum UIAnimationPreset
    {
        None = 0,
        Fade = 1,
        Scale = 2,
        FadeScale = 3,
        SlideFromLeft = 4,
        SlideFromRight = 5,
        SlideFromTop = 6,
        SlideFromBottom = 7,
        SlideToLeft = 8,
        SlideToRight = 9,
        SlideToTop = 10,
        SlideToBottom = 11
    }

    public enum UIAnimationEase
    {
        Linear = 0,
        OutQuad = 1,
        InQuad = 2,
        OutBack = 3,
        InBack = 4
    }
}
