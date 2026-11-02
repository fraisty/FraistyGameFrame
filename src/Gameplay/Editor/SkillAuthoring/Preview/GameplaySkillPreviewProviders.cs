using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>Editor presentation projection. Implementations must never tick Gameplay Runtime or modify Skill assets.</summary>
    public interface IGameplaySkillPreviewProvider
    {
        string DisplayName { get; }
        bool CanPreview(GameplaySkillDefinitionAsset skill);
        IGameplaySkillPreviewSession CreateSession(GameplaySkillDefinitionAsset skill);
    }
    /// <summary>Optional preference for previews consuming the playable document's actual resources.</summary>
    public interface IGameplayPlayableDocumentPreviewProvider { }

    /// <summary>Disposable preview state, including game-specific subject selection and rendering.</summary>
    public interface IGameplaySkillPreviewSession : IDisposable
    {
        float Duration { get; }
        void UpdateContext(GameplaySkillPreviewContext context);
        void DrawControls();
        void Seek(float seconds);
        void Render(Rect viewport);
    }

    /// <summary>Optional recoverable backend diagnostics consumed by the V2 source navigator.</summary>
    public interface IGameplaySkillPreviewDiagnostics
    {
        string Error { get; }
        void Retry();
    }

    /// <summary>The currently selected authoring projection. Sequence is supplied by the Workbench, never guessed by a provider.</summary>
    public sealed class GameplaySkillPreviewContext
    {
        public GameplaySkillDefinitionAsset Skill { get; }
        public SkillNodeLocation? SelectedNode { get; }
        public GameplaySequenceDefinitionAsset Sequence { get; }
        public GameplaySkillDocumentAsset Document { get; }
        public string AuthoringScope { get; }
        public UnityEngine.Object Source { get; }
        public string PropertyPath { get; }
        public UnityEngine.Object PresentationSource { get; }
        /// <summary>Absolute sequence start of the selected presentation. Standalone resource previews start at zero.</summary>
        public float PresentationStartSeconds {get;}

        public GameplaySkillPreviewContext(GameplaySkillDefinitionAsset skill, SkillNodeLocation? selectedNode,
            GameplaySequenceDefinitionAsset sequence, GameplaySkillDocumentAsset document = null,
            UnityEngine.Object source = null, string propertyPath = null, UnityEngine.Object presentationSource = null,float presentationStartSeconds=0)
        {
            Skill = skill;
            SelectedNode = selectedNode;
            Sequence = sequence;
            Document = document; Source = source; PropertyPath = propertyPath; PresentationSource = presentationSource;
            PresentationStartSeconds=presentationStartSeconds;
            AuthoringScope = document == null ? GameplayAuthoringTypeCatalog.Scope(skill) : document.AuthoringGraph?.Scope;
        }
    }

    /// <summary>Deterministic Editor-only provider discovery with failure isolation.</summary>
    public static class GameplaySkillPreviewProviders
    {
        private static readonly Type[] Types = TypeCache.GetTypesDerivedFrom<IGameplaySkillPreviewProvider>()
            .Where(t => !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
            .OrderBy(t => t.AssemblyQualifiedName, StringComparer.Ordinal).ToArray();

        public static IReadOnlyList<IGameplaySkillPreviewProvider> For(GameplaySkillDefinitionAsset skill, bool playableDocument = false)
        {
            var result = new List<IGameplaySkillPreviewProvider>();
            foreach (Type type in Types)
                try
                {
                    var provider = (IGameplaySkillPreviewProvider)Activator.CreateInstance(type);
                    if (provider.CanPreview(skill)) result.Add(provider);
                }
                catch (Exception ex) { Debug.LogWarning("Skill preview provider " + type.FullName + ": " + ex.Message); }
            return playableDocument?result.OrderByDescending(p=>p is IGameplayPlayableDocumentPreviewProvider).ToArray():result;
        }
    }
}
