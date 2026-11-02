namespace FRFrame
{
    public sealed class UIBuiltinConfig
    {
        public string ToastFormId = "UIToastForm";
        public string DialogFormId = "UIDialogForm";
        public string LoadingFormId = "UILoadingForm";
        public bool EnableFallbackView = true;

        public static UIBuiltinConfig Default => new UIBuiltinConfig();
    }
}
