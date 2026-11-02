using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

namespace FRFrame
{
    public static class UIRichTextUtility
    {
        private static readonly Dictionary<char, string> EscapeMap = new Dictionary<char, string>
        {
            { '<', "&lt;" },
            { '>', "&gt;" },
            { '&', "&amp;" },
            { '"', "&quot;" }
        };

        /// <summary>转义外部输入，避免玩家名或聊天内容注入 TMP 标签。</summary>
        public static string EscapeTmpText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            StringBuilder builder = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                builder.Append(EscapeMap.TryGetValue(c, out string escaped) ? escaped : c.ToString());
            }

            return builder.ToString();
        }

        public static string ToHtmlColor(Color color)
        {
            return "#" + ColorUtility.ToHtmlStringRGBA(color);
        }

        public static string BuildSpriteTag(UIRichTextSpriteRef spriteRef, string fallbackName)
        {
            StringBuilder builder = new StringBuilder("<sprite");
            if (spriteRef.SpriteAsset != null)
                builder.Append("=\"").Append(spriteRef.SpriteAsset.name).Append("\"");
            if (!string.IsNullOrEmpty(spriteRef.SpriteName))
                builder.Append(" name=\"").Append(spriteRef.SpriteName).Append("\"");
            else if (spriteRef.SpriteIndex >= 0)
                builder.Append(" index=").Append(spriteRef.SpriteIndex);
            else if (!string.IsNullOrEmpty(fallbackName))
                builder.Append(" name=\"").Append(fallbackName).Append("\"");

            if (!Mathf.Approximately(spriteRef.Scale, 0f) && !Mathf.Approximately(spriteRef.Scale, 1f))
                builder.Append(" scale=").Append(spriteRef.Scale.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            if (!Mathf.Approximately(spriteRef.YOffset, 0f))
                builder.Append(" voffset=").Append(spriteRef.YOffset.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));

            builder.Append(">");
            return builder.ToString();
        }

        public static string FormatTemplate(string source, IReadOnlyDictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(source) || args == null)
                return source ?? string.Empty;

            string result = source;
            foreach (KeyValuePair<string, object> pair in args)
                result = result.Replace("{" + pair.Key + "}", EscapeTmpText(Convert.ToString(pair.Value)));
            return result;
        }

        public static Camera ResolveEventCamera(TMP_Text text)
        {
            if (text == null || text.canvas == null || text.canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                return null;
            return text.canvas.worldCamera != null ? text.canvas.worldCamera : Camera.main;
        }
    }
}
