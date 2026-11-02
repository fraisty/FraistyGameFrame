namespace FRFrame
{
    public sealed class UIToastRequest
    {
        public string Message;
        public float Duration = 2f;
        public UIToastPosition Position = UIToastPosition.Bottom;
        public int Priority;
        public bool MergeSameMessage = true;
    }
}
