using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace FRFrame
{
    [Serializable]
    public sealed class UIRichTextStringEvent : UnityEvent<string>
    {
    }

    [Serializable]
    public sealed class UIRichTextLinkEvent : UnityEvent<UIRichTextClickContext>
    {
    }

    /// <summary>TMP 富文本主组件，提供项目友好标签解析、链接点击和动态模板参数。</summary>
    [RequireComponent(typeof(TMP_Text))]
    public sealed class UIRichText : MonoBehaviour
    {
        public TMP_Text targetText;
        public UIRichTextConfig config = new UIRichTextConfig();
        public bool parseOnEnable = true;
        public bool enableClick = true;
        public bool enableLongPress = true;
        public bool enableHover;
        public float longPressThreshold = 0.6f;
        public bool cacheParsedText = true;
        [Min(0)] public int maxCachedEntries = 128;
        public string sourceText;
        public UIRichTextLinkEvent onLinkClicked = new UIRichTextLinkEvent();
        public UIRichTextLinkEvent onLinkLongPressed = new UIRichTextLinkEvent();
        public UIRichTextLinkEvent onLinkHovered = new UIRichTextLinkEvent();
        public UIRichTextStringEvent onParseCompleted = new UIRichTextStringEvent();

        private readonly Dictionary<string, Action<UIRichTextClickContext>> _linkHandlers = new Dictionary<string, Action<UIRichTextClickContext>>();
        private readonly Dictionary<string, UIRichTextParseResult> _parseCache = new Dictionary<string, UIRichTextParseResult>();
        private UIRichTextParser _parser;
        private IUIRichTextSpriteResolver _spriteResolver;
        private UIRichTextClickHandler _clickHandler;
        private UIRichTextParseResult _lastParseResult;
        private UIRichTextConfig _cachedConfig;
        private int _cachedConfigHash;
        private int _spriteResolverGeneration;
        private int _cachedSpriteResolverGeneration = -1;

        public bool EnableClick => enableClick;
        public bool EnableLongPress => enableLongPress;
        public bool EnableHover => enableHover;
        public float LongPressThreshold => longPressThreshold;
        public UIRichTextParseResult LastParseResult => _lastParseResult;
        public int CacheCount => _parseCache.Count;

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnEnable()
        {
            EnsureInitialized();
            if (parseOnEnable && !string.IsNullOrEmpty(sourceText))
                Refresh();
        }

        private void OnValidate()
        {
            if (targetText == null)
                targetText = GetComponent<TMP_Text>();
            longPressThreshold = Mathf.Max(0.05f, longPressThreshold);
            maxCachedEntries = Mathf.Max(0, maxCachedEntries);
        }

        public void SetText(string source)
        {
            sourceText = source ?? string.Empty;
            Refresh();
        }

        public void SetText(string source, IReadOnlyDictionary<string, object> args)
        {
            SetText(UIRichTextUtility.FormatTemplate(source, args));
        }

        public void Refresh()
        {
            EnsureInitialized();
            if (targetText == null)
            {
                Debug.LogError("UIRichText 需要挂载 TMP_Text 组件。", this);
                enabled = false;
                return;
            }

            _lastParseResult = Parse(sourceText ?? string.Empty);
            targetText.text = _lastParseResult.ParsedText;
            targetText.ForceMeshUpdate();
            onParseCompleted.Invoke(_lastParseResult.ParsedText);
        }

        public void Clear()
        {
            sourceText = string.Empty;
            _lastParseResult = null;
            if (targetText != null)
                targetText.text = string.Empty;
        }

        public void RegisterLinkHandler(string scheme, Action<UIRichTextClickContext> handler)
        {
            if (string.IsNullOrEmpty(scheme) || handler == null)
                return;

            _linkHandlers[scheme] = handler;
        }

        public void UnregisterLinkHandler(string scheme)
        {
            if (!string.IsNullOrEmpty(scheme))
                _linkHandlers.Remove(scheme);
        }

        public void SetSpriteResolver(IUIRichTextSpriteResolver resolver)
        {
            _spriteResolver = resolver;
            _spriteResolverGeneration++;
            if (_parser != null)
                _parser.SetSpriteResolver(GetActiveSpriteResolver());
            InvalidateCache();
        }

        public void SetConfig(UIRichTextConfig value, bool refresh = true)
        {
            config = value;
            InvalidateCache(refresh);
        }

        public void InvalidateCache(bool refresh = false)
        {
            _parseCache.Clear();
            _cachedConfig = null;
            _cachedSpriteResolverGeneration = -1;
            if (_parser != null)
                _parser.SetSpriteResolver(GetActiveSpriteResolver());
            if (refresh && isActiveAndEnabled)
                Refresh();
        }

        public void DispatchLinkEvent(UIRichTextClickContext context)
        {
            if (context == null)
                return;

            switch (context.PointerEvent)
            {
                case UIRichTextLinkPointerEvent.LongPress:
                    onLinkLongPressed.Invoke(context);
                    break;
                case UIRichTextLinkPointerEvent.Hover:
                    onLinkHovered.Invoke(context);
                    break;
                default:
                    onLinkClicked.Invoke(context);
                    if (_linkHandlers.TryGetValue(context.Scheme, out Action<UIRichTextClickContext> handler))
                        handler.Invoke(context);
                    break;
            }
        }

        private UIRichTextParseResult Parse(string source)
        {
            EnsureCacheState();
            string cacheKey = source;
            if (cacheParsedText && _parseCache.TryGetValue(cacheKey, out UIRichTextParseResult cached))
                return cached;

            UIRichTextParseResult result = _parser.Parse(source, config);
            if (cacheParsedText && maxCachedEntries > 0)
            {
                if (_parseCache.Count >= maxCachedEntries)
                    _parseCache.Clear();
                _parseCache[cacheKey] = result;
            }
            return result;
        }

        private void EnsureCacheState()
        {
            int configHash = config != null ? config.ComputeContentHash() : 0;
            if (ReferenceEquals(_cachedConfig, config) && _cachedConfigHash == configHash &&
                _cachedSpriteResolverGeneration == _spriteResolverGeneration)
                return;

            _parseCache.Clear();
            _cachedConfig = config;
            _cachedConfigHash = configHash;
            _cachedSpriteResolverGeneration = _spriteResolverGeneration;
            if (_parser != null)
                _parser.SetSpriteResolver(GetActiveSpriteResolver());
        }

        private IUIRichTextSpriteResolver GetActiveSpriteResolver()
        {
            return _spriteResolver ?? new UIRichTextConfigSpriteResolver(config);
        }

        private void EnsureInitialized()
        {
            if (targetText == null)
                targetText = GetComponent<TMP_Text>();
            if (_parser == null)
                _parser = new UIRichTextParser(GetActiveSpriteResolver());

            if (_clickHandler == null)
            {
                _clickHandler = GetComponent<UIRichTextClickHandler>();
                if (_clickHandler == null)
                    _clickHandler = gameObject.AddComponent<UIRichTextClickHandler>();
            }

            _clickHandler.owner = this;
            _clickHandler.targetText = targetText;
            if (targetText != null)
                targetText.raycastTarget = enableClick || enableLongPress || enableHover;
        }
    }
}
