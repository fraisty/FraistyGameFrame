namespace FRFrame
{
    internal readonly struct UIRichTextTag
    {
        public readonly string Name;
        public readonly string Argument;
        public readonly bool IsClosing;
        public readonly string RawText;
        public readonly int EndIndex;

        public UIRichTextTag(string name, string argument, bool isClosing, string rawText, int endIndex)
        {
            Name = name;
            Argument = argument;
            IsClosing = isClosing;
            RawText = rawText;
            EndIndex = endIndex;
        }
    }
}
