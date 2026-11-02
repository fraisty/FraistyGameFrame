using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FRFrame
{
    public sealed class UIFontTextureRefresh : MonoBehaviour
    {
        public Transform targetRoot;
        public bool includeInactive = true;

        private readonly HashSet<Font> _pendingFonts = new HashSet<Font>();
        private readonly HashSet<Font> _processingFonts = new HashSet<Font>();
        private readonly HashSet<Font> _notifyingFonts = new HashSet<Font>();
        private Text[] _targets = new Text[0];

        public int PendingFontCount => _pendingFonts.Count;
        public int LastScannedCount { get; private set; }
        public int LastRefreshedCount { get; private set; }

        private void OnEnable()
        {
            Font.textureRebuilt += OnFontTextureRebuilt;
            RebuildTargets();
        }

        private void OnDisable()
        {
            Font.textureRebuilt -= OnFontTextureRebuilt;
            _pendingFonts.Clear();
            _processingFonts.Clear();
            _notifyingFonts.Clear();
        }

        private void LateUpdate()
        {
            if (_pendingFonts.Count == 0)
                return;

            _processingFonts.Clear();
            _processingFonts.UnionWith(_pendingFonts);
            _pendingFonts.Clear();

            LastScannedCount = _targets.Length;
            LastRefreshedCount = 0;
            _notifyingFonts.UnionWith(_processingFonts);
            try
            {
                for (int i = 0; i < _targets.Length; i++)
                {
                    Text text = _targets[i];
                    if (text == null || !_processingFonts.Contains(text.font))
                        continue;

                    text.FontTextureChanged();
                    LastRefreshedCount++;
                }
            }
            finally
            {
                _notifyingFonts.Clear();
                _processingFonts.Clear();
            }
        }

        public void RequestRefresh(Font font)
        {
            if (font != null)
                _pendingFonts.Add(font);
        }

        public int Refresh(Font font)
        {
            LastScannedCount = _targets.Length;
            LastRefreshedCount = RefreshTargets(font);
            return LastRefreshedCount;
        }

        public int RefreshAll()
        {
            LastScannedCount = _targets.Length;
            LastRefreshedCount = 0;
            for (int i = 0; i < _targets.Length; i++)
            {
                Text text = _targets[i];
                if (text != null && text.font != null)
                    _notifyingFonts.Add(text.font);
            }

            try
            {
                for (int i = 0; i < _targets.Length; i++)
                {
                    Text text = _targets[i];
                    if (text == null)
                        continue;

                    text.FontTextureChanged();
                    LastRefreshedCount++;
                }
            }
            finally
            {
                _notifyingFonts.Clear();
            }
            return LastRefreshedCount;
        }

        public void RebuildTargets()
        {
            Transform root = targetRoot != null ? targetRoot : transform;
            _targets = root.GetComponentsInChildren<Text>(includeInactive);
        }

        private int RefreshTargets(Font font)
        {
            if (font == null)
                return 0;

            int refreshedCount = 0;
            _notifyingFonts.Add(font);
            try
            {
                for (int i = 0; i < _targets.Length; i++)
                {
                    Text text = _targets[i];
                    if (text == null || text.font != font)
                        continue;

                    text.FontTextureChanged();
                    refreshedCount++;
                }
            }
            finally
            {
                _notifyingFonts.Remove(font);
            }
            return refreshedCount;
        }

        private void OnFontTextureRebuilt(Font font)
        {
            if (!_notifyingFonts.Contains(font))
                RequestRefresh(font);
        }
    }
}
