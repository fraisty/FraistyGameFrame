using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace FRFrame
{
    [Serializable]
    public sealed class UIRichTextSpriteAlias
    {
        public string key;
        public TMP_SpriteAsset spriteAsset;
        public string spriteName;
        public int spriteIndex = -1;
        public float scale = 1f;
        public float yOffset;
    }

    [Serializable]
    public sealed class UIRichTextColorAlias
    {
        public string key;
        public Color color = Color.white;
    }

    [Serializable]
    public sealed class UIRichTextGradientAlias
    {
        public string key;
        public string materialPreset;
        public string fallbackColor = "#FFD37A";
    }

    /// <summary>富文本标签、颜色、图标和链接行为的集中配置。</summary>
    [Serializable]
    public sealed class UIRichTextConfig
    {
        public TMP_SpriteAsset defaultSpriteAsset;
        public List<UIRichTextSpriteAlias> spriteAliases = new List<UIRichTextSpriteAlias>();
        public List<UIRichTextColorAlias> colorAliases = new List<UIRichTextColorAlias>();
        public List<UIRichTextGradientAlias> gradientAliases = new List<UIRichTextGradientAlias>();
        public bool escapeUnknownTags = true;
        public bool underlineLinks = true;
        public Color defaultLinkColor = new Color(0.45f, 0.75f, 1f, 1f);
        public string missingSpriteText = "□";

        public int ComputeContentHash()
        {
            unchecked
            {
                int hash = 17;
                AddHash(ref hash, defaultSpriteAsset);
                AddHash(ref hash, spriteAliases != null ? spriteAliases.Count : -1);
                if (spriteAliases != null)
                {
                    for (int i = 0; i < spriteAliases.Count; i++)
                    {
                        UIRichTextSpriteAlias alias = spriteAliases[i];
                        AddHash(ref hash, alias != null);
                        if (alias == null)
                            continue;
                        AddHash(ref hash, alias.key);
                        AddHash(ref hash, alias.spriteAsset);
                        AddHash(ref hash, alias.spriteName);
                        AddHash(ref hash, alias.spriteIndex);
                        AddHash(ref hash, alias.scale);
                        AddHash(ref hash, alias.yOffset);
                    }
                }

                AddHash(ref hash, colorAliases != null ? colorAliases.Count : -1);
                if (colorAliases != null)
                {
                    for (int i = 0; i < colorAliases.Count; i++)
                    {
                        UIRichTextColorAlias alias = colorAliases[i];
                        AddHash(ref hash, alias != null);
                        if (alias == null)
                            continue;
                        AddHash(ref hash, alias.key);
                        AddHash(ref hash, alias.color);
                    }
                }

                AddHash(ref hash, gradientAliases != null ? gradientAliases.Count : -1);
                if (gradientAliases != null)
                {
                    for (int i = 0; i < gradientAliases.Count; i++)
                    {
                        UIRichTextGradientAlias alias = gradientAliases[i];
                        AddHash(ref hash, alias != null);
                        if (alias == null)
                            continue;
                        AddHash(ref hash, alias.key);
                        AddHash(ref hash, alias.materialPreset);
                        AddHash(ref hash, alias.fallbackColor);
                    }
                }

                AddHash(ref hash, escapeUnknownTags);
                AddHash(ref hash, underlineLinks);
                AddHash(ref hash, defaultLinkColor);
                AddHash(ref hash, missingSpriteText);
                return hash;
            }
        }

        public bool TryGetSpriteAlias(string key, out UIRichTextSpriteAlias alias)
        {
            alias = null;
            if (string.IsNullOrEmpty(key) || spriteAliases == null)
                return false;

            for (int i = 0; i < spriteAliases.Count; i++)
            {
                UIRichTextSpriteAlias item = spriteAliases[i];
                if (item != null && String.Equals(item.key, key, StringComparison.OrdinalIgnoreCase))
                {
                    alias = item;
                    return true;
                }
            }

            return false;
        }

        public bool TryGetColor(string key, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrEmpty(key) || colorAliases == null)
                return false;

            for (int i = 0; i < colorAliases.Count; i++)
            {
                UIRichTextColorAlias item = colorAliases[i];
                if (item != null && String.Equals(item.key, key, StringComparison.OrdinalIgnoreCase))
                {
                    color = item.color;
                    return true;
                }
            }

            return ColorUtility.TryParseHtmlString(key, out color);
        }

        public bool TryGetGradient(string key, out UIRichTextGradientAlias alias)
        {
            alias = null;
            if (string.IsNullOrEmpty(key) || gradientAliases == null)
                return false;

            for (int i = 0; i < gradientAliases.Count; i++)
            {
                UIRichTextGradientAlias item = gradientAliases[i];
                if (item != null && String.Equals(item.key, key, StringComparison.OrdinalIgnoreCase))
                {
                    alias = item;
                    return true;
                }
            }

            return false;
        }

        private static void AddHash(ref int hash, UnityEngine.Object value)
        {
            AddHash(ref hash, value != null ? value.GetInstanceID() : 0);
        }

        private static void AddHash(ref int hash, string value)
        {
            if (value == null)
            {
                AddHash(ref hash, -1);
                return;
            }

            AddHash(ref hash, value.Length);
            for (int i = 0; i < value.Length; i++)
                AddHash(ref hash, value[i]);
        }

        private static void AddHash(ref int hash, bool value)
        {
            AddHash(ref hash, value ? 1 : 0);
        }

        private static void AddHash(ref int hash, int value)
        {
            hash = hash * 31 + value;
        }

        private static void AddHash(ref int hash, float value)
        {
            AddHash(ref hash, value.GetHashCode());
        }

        private static void AddHash(ref int hash, Color value)
        {
            AddHash(ref hash, value.r);
            AddHash(ref hash, value.g);
            AddHash(ref hash, value.b);
            AddHash(ref hash, value.a);
        }
    }
}
