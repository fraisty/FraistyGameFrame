using TMPro;

namespace FRFrame
{
    public struct UIRichTextSpriteRef
    {
        public TMP_SpriteAsset SpriteAsset;
        public string SpriteName;
        public int SpriteIndex;
        public float Scale;
        public float YOffset;
    }

    /// <summary>业务可接入的图标解析接口，用于把 icon key 映射到 TMP Sprite。</summary>
    public interface IUIRichTextSpriteResolver
    {
        bool TryResolve(string key, out UIRichTextSpriteRef spriteRef);
    }

    public sealed class UIRichTextConfigSpriteResolver : IUIRichTextSpriteResolver
    {
        private readonly UIRichTextConfig _config;

        public UIRichTextConfigSpriteResolver(UIRichTextConfig config)
        {
            _config = config;
        }

        public bool TryResolve(string key, out UIRichTextSpriteRef spriteRef)
        {
            spriteRef = default;
            if (_config == null || !_config.TryGetSpriteAlias(key, out UIRichTextSpriteAlias alias))
                return false;

            spriteRef = new UIRichTextSpriteRef
            {
                SpriteAsset = alias.spriteAsset != null ? alias.spriteAsset : _config.defaultSpriteAsset,
                SpriteName = alias.spriteName,
                SpriteIndex = alias.spriteIndex,
                Scale = alias.scale <= 0f ? 1f : alias.scale,
                YOffset = alias.yOffset
            };
            return true;
        }
    }
}
