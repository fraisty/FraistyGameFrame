namespace FRFrame
{
    public readonly struct UIOpenResult<TForm> where TForm : UIForm
    {
        public readonly bool Succeeded;
        public readonly TForm Form;
        public readonly UIOpenFailureReason FailureReason;
        public readonly string Message;

        public UIOpenResult(TForm form)
        {
            Succeeded = form != null;
            Form = form;
            FailureReason = form != null ? UIOpenFailureReason.None : UIOpenFailureReason.LoadFailed;
            Message = null;
        }

        public UIOpenResult(UIOpenFailureReason failureReason, string message = null)
        {
            Succeeded = false;
            Form = null;
            FailureReason = failureReason;
            Message = message;
        }

        public static UIOpenResult<TForm> Success(TForm form) => new UIOpenResult<TForm>(form);
        public static UIOpenResult<TForm> Failure(UIOpenFailureReason reason, string message = null) => new UIOpenResult<TForm>(reason, message);
    }
}
