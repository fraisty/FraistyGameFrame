using UnityEngine;
using UnityEngine.Video;

namespace FRFrame
{
    [CreateAssetMenu(menuName = "FRFrame/Game/Game Package Config", fileName = "GamePackageConfig")]
    public sealed class GamePackageConfig : ScriptableObject
    {
        public string gameId;
        public string displayName;
        public string entryScene;
        public UIConfigGroup uiConfigGroup;
        public Sprite icon;
        public VideoClip backgroundVideo;
    }
}
