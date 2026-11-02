using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace FRFrame
{
    internal static class UIBuiltinFontProvider
    {
        private const string DefaultFontLocation = "Assets/GameRes/Common/Font/font_main_zht SDF.asset";
        private static TMP_FontAsset _font;
        private static UniTask<TMP_FontAsset> _loadingTask;
        private static bool _hasLoadingTask;

        public static TMP_FontAsset Font => _font;

        public static UniTask<TMP_FontAsset> PreloadDefaultFontAsync()
        {
            if (_font != null)
                return UniTask.FromResult(_font);

            if (_hasLoadingTask)
                return _loadingTask;

            _hasLoadingTask = true;
            _loadingTask = LoadDefaultFontAsync();
            return _loadingTask;
        }

        private static async UniTask<TMP_FontAsset> LoadDefaultFontAsync()
        {
            if (FRFM.Resource == null)
                return null;

            _font = await FRFM.Resource.LoadAssetAsync<TMP_FontAsset>(DefaultFontLocation, new ResourceLoadOptions
            {
                PackageName = "DefaultPackage",
                Residency = ResourceResidency.Resident,
                AllowCache = true,
                TrackReference = true
            });
            return _font;
        }

        public static void Apply(TMP_Text text)
        {
            if (text != null && _font != null)
                text.font = _font;
        }
    }
}
