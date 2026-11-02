using System;
using FRFrame.Gameplay.Authoring;
namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>Editor-owned lease on an existing scene host. Implementations install the
    /// immutable compiler output and never manufacture a workspace or another runtime clock.</summary>
    public interface IGameplaySkillHostSession:IDisposable { void Draw(); }
    public interface IGameplaySkillHost
    {
        bool Supports(GameplaySkillDocumentAsset document);
        string Unavailable(GameplaySkillDocumentAsset document);
        void DrawSetup(GameplaySkillDocumentAsset document);
        IGameplaySkillHostSession Install(GameplaySkillDocumentAsset document);
    }
}
