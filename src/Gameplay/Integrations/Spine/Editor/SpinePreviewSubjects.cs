using System.Collections.Generic;
using Spine.Unity;

namespace FRFrame.Gameplay.Spine.Editor
{
    /// <summary>A transient character choice for a Spine preview, supplied by a game Editor assembly.</summary>
    public sealed class SpinePreviewSubject
    {
        public string DisplayName { get; }
        public SkeletonDataAsset Asset { get; }
        public bool IsDefault { get; }
        public string[] SkinNames { get; }
        public UnityEngine.Material Material { get; }

        public SpinePreviewSubject(string displayName, SkeletonDataAsset asset, bool isDefault = false, string[] skinNames = null, UnityEngine.Material material = null)
        {
            DisplayName = displayName;
            Asset = asset;
            IsDefault = isDefault;
            SkinNames = skinNames;
            Material = material;
        }
    }

    /// <summary>Optional game-side asset resolver and semantic animation hints. Rendering remains in the integration.</summary>
    public interface ISpinePreviewSubjectProvider
    {
        IReadOnlyList<SpinePreviewSubject> GetSubjects();
        string IdleAnimation(SkeletonDataAsset asset);
        string AnimationForCue(SkeletonDataAsset asset, string cueTag);
    }
    /// <summary>Scoped automatic selection; manual subject browsing remains unrestricted.</summary>
    public interface IScopedSpinePreviewSubjectProvider
    {
        string AuthoringScope { get; }
        bool SupportsPresentationSource(UnityEngine.Object source);
        SpinePreviewSubject Resolve(FRFrame.Gameplay.Editor.SkillAuthoring.GameplaySkillPreviewContext context);
        string Animation(FRFrame.Gameplay.Editor.SkillAuthoring.GameplaySkillPreviewContext context);
    }
}
