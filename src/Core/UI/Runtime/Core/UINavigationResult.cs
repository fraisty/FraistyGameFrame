namespace FRFrame
{
    public readonly struct UINavigationResult<TForm> where TForm : UIForm
    {
        public readonly bool Succeeded;
        public readonly TForm Form;
        public readonly UINavigationFailureReason FailureReason;
        public readonly string Message;

        public UINavigationResult(TForm form)
        {
            Succeeded = form != null;
            Form = form;
            FailureReason = form != null ? UINavigationFailureReason.None : UINavigationFailureReason.OpenFailed;
            Message = null;
        }

        public UINavigationResult(UINavigationFailureReason failureReason, string message = null)
        {
            Succeeded = false;
            Form = null;
            FailureReason = failureReason;
            Message = message;
        }

        public static UINavigationResult<TForm> Success(TForm form) => new UINavigationResult<TForm>(form);
        public static UINavigationResult<TForm> Failure(UINavigationFailureReason reason, string message = null) => new UINavigationResult<TForm>(reason, message);
    }
}
