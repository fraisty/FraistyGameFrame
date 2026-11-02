using System.Collections.Generic;

namespace FRFrame
{
    public sealed class UIRichTextLinkInfo
    {
        public string Scheme;
        public string Payload;
        public string Text;
    }

    public sealed class UIRichTextSpriteInfo
    {
        public string Key;
        public string SpriteName;
        public int SpriteIndex;
    }

    public sealed class UIRichTextParseResult
    {
        public string SourceText { get; }
        public string ParsedText { get; }
        public IReadOnlyList<UIRichTextLinkInfo> Links { get; }
        public IReadOnlyList<UIRichTextSpriteInfo> Sprites { get; }
        public IReadOnlyList<string> Warnings { get; }

        public UIRichTextParseResult(string sourceText, string parsedText, List<UIRichTextLinkInfo> links, List<UIRichTextSpriteInfo> sprites, List<string> warnings)
        {
            SourceText = sourceText ?? string.Empty;
            ParsedText = parsedText ?? string.Empty;
            Links = links ?? new List<UIRichTextLinkInfo>();
            Sprites = sprites ?? new List<UIRichTextSpriteInfo>();
            Warnings = warnings ?? new List<string>();
        }
    }
}
