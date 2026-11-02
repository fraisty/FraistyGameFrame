using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace FRFrame
{
    /// <summary>把项目友好标签转换成 TMP 可直接渲染的富文本。</summary>
    public sealed class UIRichTextParser
    {
        private static readonly HashSet<string> InlineTmpTags = new HashSet<string> { "b", "i", "u", "s" };
        private IUIRichTextSpriteResolver _spriteResolver;

        public UIRichTextParser(IUIRichTextSpriteResolver spriteResolver = null)
        {
            _spriteResolver = spriteResolver;
        }

        public void SetSpriteResolver(IUIRichTextSpriteResolver spriteResolver)
        {
            _spriteResolver = spriteResolver;
        }

        public UIRichTextParseResult Parse(string source, UIRichTextConfig config)
        {
            List<UIRichTextLinkInfo> links = new List<UIRichTextLinkInfo>();
            List<UIRichTextSpriteInfo> sprites = new List<UIRichTextSpriteInfo>();
            List<string> warnings = new List<string>();
            string parsed = ParseRange(source ?? string.Empty, 0, (source ?? string.Empty).Length, config, links, sprites, warnings);
            return new UIRichTextParseResult(source, parsed, links, sprites, warnings);
        }

        private string ParseRange(string source, int start, int end, UIRichTextConfig config, List<UIRichTextLinkInfo> links, List<UIRichTextSpriteInfo> sprites, List<string> warnings)
        {
            StringBuilder builder = new StringBuilder(end - start);
            int index = start;
            while (index < end)
            {
                if (source[index] == '[' && TryReadTag(source, index, end, out UIRichTextTag tag))
                {
                    if (tag.IsClosing)
                    {
                        AppendEscaped(builder, source, index, tag.EndIndex);
                        index = tag.EndIndex;
                        continue;
                    }

                    if (TryAppendSelfClosingTag(builder, tag, config, sprites, warnings))
                    {
                        index = tag.EndIndex;
                        continue;
                    }

                    if (TryFindClosingTag(source, tag.EndIndex, end, tag.Name, out int contentEnd, out int closeEnd))
                    {
                        string content = ParseRange(source, tag.EndIndex, contentEnd, config, links, sprites, warnings);
                        AppendWrappedTag(builder, tag, content, config, links, warnings);
                        index = closeEnd;
                        continue;
                    }

                    if (config == null || config.escapeUnknownTags)
                        AppendEscaped(builder, source, index, tag.EndIndex);
                    else
                        builder.Append(source, index, tag.EndIndex - index);
                    index = tag.EndIndex;
                    continue;
                }

                AppendEscaped(builder, source[index]);
                index++;
            }

            return builder.ToString();
        }

        private bool TryAppendSelfClosingTag(StringBuilder builder, UIRichTextTag tag, UIRichTextConfig config, List<UIRichTextSpriteInfo> sprites, List<string> warnings)
        {
            if (tag.Name != "icon" && tag.Name != "emoji")
                return false;

            string key = tag.Argument;
            if (string.IsNullOrEmpty(key))
                key = tag.Name;

            IUIRichTextSpriteResolver resolver = _spriteResolver ?? new UIRichTextConfigSpriteResolver(config);
            if (resolver.TryResolve(key, out UIRichTextSpriteRef spriteRef))
            {
                builder.Append(UIRichTextUtility.BuildSpriteTag(spriteRef, key));
                sprites.Add(new UIRichTextSpriteInfo { Key = key, SpriteName = spriteRef.SpriteName, SpriteIndex = spriteRef.SpriteIndex });
                return true;
            }

            if (config != null && config.defaultSpriteAsset != null)
            {
                builder.Append("<sprite name=\"").Append(key).Append("\">");
                sprites.Add(new UIRichTextSpriteInfo { Key = key, SpriteName = key, SpriteIndex = -1 });
                return true;
            }

            warnings.Add($"未找到富文本图标：{key}");
            builder.Append(UIRichTextUtility.EscapeTmpText(config != null ? config.missingSpriteText : "□"));
            return true;
        }

        private void AppendWrappedTag(StringBuilder builder, UIRichTextTag tag, string content, UIRichTextConfig config, List<UIRichTextLinkInfo> links, List<string> warnings)
        {
            switch (tag.Name)
            {
                case "item":
                case "player":
                    AppendLink(builder, tag.Name, tag.Argument, content, config, links);
                    break;
                case "link":
                    AppendGenericLink(builder, tag.Argument, content, config, links);
                    break;
                case "color":
                    AppendColor(builder, tag.Argument, content, config);
                    break;
                case "gradient":
                    AppendGradient(builder, tag.Argument, content, config, warnings);
                    break;
                default:
                    if (InlineTmpTags.Contains(tag.Name))
                        builder.Append('<').Append(tag.Name).Append('>').Append(content).Append("</").Append(tag.Name).Append('>');
                    else
                        AppendUnknown(builder, tag, content, config);
                    break;
            }
        }

        private void AppendLink(StringBuilder builder, string scheme, string payload, string content, UIRichTextConfig config, List<UIRichTextLinkInfo> links)
        {
            string safePayload = UIRichTextUtility.EscapeTmpText(payload);
            string linkId = string.IsNullOrEmpty(safePayload) ? scheme : scheme + ":" + safePayload;
            links.Add(new UIRichTextLinkInfo { Scheme = scheme, Payload = payload, Text = content });

            builder.Append("<link=\"").Append(linkId).Append("\">");
            if (config == null || config.underlineLinks)
                builder.Append("<u>");
            builder.Append("<color=").Append(UIRichTextUtility.ToHtmlColor(config != null ? config.defaultLinkColor : Color.cyan)).Append('>').Append(content).Append("</color>");
            if (config == null || config.underlineLinks)
                builder.Append("</u>");
            builder.Append("</link>");
        }

        private void AppendGenericLink(StringBuilder builder, string linkId, string content, UIRichTextConfig config, List<UIRichTextLinkInfo> links)
        {
            string safeLinkId = UIRichTextUtility.EscapeTmpText(linkId);
            int splitIndex = linkId != null ? linkId.IndexOf(':') : -1;
            links.Add(new UIRichTextLinkInfo
            {
                Scheme = splitIndex >= 0 ? linkId.Substring(0, splitIndex) : linkId,
                Payload = splitIndex >= 0 ? linkId.Substring(splitIndex + 1) : string.Empty,
                Text = content
            });

            builder.Append("<link=\"").Append(safeLinkId).Append("\">");
            if (config == null || config.underlineLinks)
                builder.Append("<u>");
            builder.Append("<color=").Append(UIRichTextUtility.ToHtmlColor(config != null ? config.defaultLinkColor : Color.cyan)).Append('>').Append(content).Append("</color>");
            if (config == null || config.underlineLinks)
                builder.Append("</u>");
            builder.Append("</link>");
        }

        private void AppendColor(StringBuilder builder, string colorKey, string content, UIRichTextConfig config)
        {
            Color color;
            string colorText = config != null && config.TryGetColor(colorKey, out color) ? UIRichTextUtility.ToHtmlColor(color) : UIRichTextUtility.EscapeTmpText(colorKey);
            builder.Append("<color=").Append(colorText).Append('>').Append(content).Append("</color>");
        }

        private void AppendGradient(StringBuilder builder, string key, string content, UIRichTextConfig config, List<string> warnings)
        {
            if (config != null && config.TryGetGradient(key, out UIRichTextGradientAlias alias) && !string.IsNullOrEmpty(alias.materialPreset))
            {
                builder.Append("<gradient=\"").Append(UIRichTextUtility.EscapeTmpText(alias.materialPreset)).Append("\">").Append(content).Append("</gradient>");
                return;
            }

            string color = "#FFD37A";
            if (config != null && config.TryGetGradient(key, out alias) && !string.IsNullOrEmpty(alias.fallbackColor))
                color = alias.fallbackColor;
            else
                warnings.Add($"未找到富文本渐变：{key}");
            if (ContainsTmpTag(content))
            {
                if (!ColorUtility.TryParseHtmlString(color, out Color fallbackColor))
                    fallbackColor = new Color(1f, 0.82f, 0.38f, 1f);
                builder.Append("<color=").Append(UIRichTextUtility.ToHtmlColor(fallbackColor)).Append('>')
                    .Append(content).Append("</color>");
                warnings.Add($"富文本渐变包含 TMP 标签，已使用起始色回退：{key}");
                return;
            }

            AppendFallbackGradient(builder, content, color);
        }

        private static bool ContainsTmpTag(string content)
        {
            if (string.IsNullOrEmpty(content))
                return false;

            int open = content.IndexOf('<');
            return open >= 0 && content.IndexOf('>', open + 1) >= 0;
        }

        private void AppendFallbackGradient(StringBuilder builder, string content, string startColor)
        {
            if (!ColorUtility.TryParseHtmlString(startColor, out Color from))
                from = new Color(1f, 0.82f, 0.38f, 1f);

            Color to = new Color(1f, 0.25f, 0.18f, 1f);
            int length = string.IsNullOrEmpty(content) ? 0 : content.Length;
            for (int i = 0; i < length; i++)
            {
                float t = length <= 1 ? 0f : i / (float)(length - 1);
                builder.Append("<color=").Append(UIRichTextUtility.ToHtmlColor(Color.Lerp(from, to, t))).Append('>');
                AppendEscaped(builder, content[i]);
                builder.Append("</color>");
            }
        }

        private void AppendUnknown(StringBuilder builder, UIRichTextTag tag, string content, UIRichTextConfig config)
        {
            if (config == null || config.escapeUnknownTags)
            {
                builder.Append(UIRichTextUtility.EscapeTmpText(tag.RawText)).Append(content).Append(UIRichTextUtility.EscapeTmpText("[/" + tag.Name + "]"));
                return;
            }

            builder.Append(tag.RawText).Append(content).Append("[/").Append(tag.Name).Append(']');
        }

        private static bool TryReadTag(string source, int start, int end, out UIRichTextTag tag)
        {
            tag = default;
            int close = source.IndexOf(']', start + 1);
            if (close < 0 || close >= end)
                return false;

            string raw = source.Substring(start, close - start + 1);
            string body = source.Substring(start + 1, close - start - 1).Trim();
            if (string.IsNullOrEmpty(body))
                return false;

            bool closing = body[0] == '/';
            if (closing)
                body = body.Substring(1).Trim();

            int split = body.IndexOf(':');
            string name = split >= 0 ? body.Substring(0, split) : body;
            string argument = split >= 0 ? body.Substring(split + 1) : string.Empty;
            if (string.IsNullOrEmpty(name))
                return false;

            tag = new UIRichTextTag(name.ToLowerInvariant(), argument, closing, raw, close + 1);
            return true;
        }

        private static bool TryFindClosingTag(string source, int start, int end, string tagName, out int contentEnd, out int closeEnd)
        {
            contentEnd = -1;
            closeEnd = -1;
            if (tagName == "icon" || tagName == "emoji")
                return false;

            int depth = 1;
            int index = start;
            while (index < end)
            {
                int tagStart = source.IndexOf('[', index);
                if (tagStart < 0 || tagStart >= end)
                    return false;
                if (!TryReadTag(source, tagStart, end, out UIRichTextTag tag))
                {
                    index = tagStart + 1;
                    continue;
                }

                index = tag.EndIndex;
                if (!string.Equals(tag.Name, tagName, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                if (tag.IsClosing)
                {
                    depth--;
                    if (depth == 0)
                    {
                        contentEnd = tagStart;
                        closeEnd = tag.EndIndex;
                        return true;
                    }
                }
                else if (tag.Name != "icon" && tag.Name != "emoji")
                {
                    depth++;
                }
            }

            return false;
        }

        private static void AppendEscaped(StringBuilder builder, string source, int start, int end)
        {
            for (int i = start; i < end; i++)
                AppendEscaped(builder, source[i]);
        }

        private static void AppendEscaped(StringBuilder builder, char c)
        {
            switch (c)
            {
                case '<':
                    builder.Append("&lt;");
                    break;
                case '>':
                    builder.Append("&gt;");
                    break;
                case '&':
                    builder.Append("&amp;");
                    break;
                case '"':
                    builder.Append("&quot;");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }
    }
}
