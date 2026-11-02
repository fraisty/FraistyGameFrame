using System.Collections.Generic;
using FRFrame.Gameplay.Editor.SkillAuthoring;
using UnityEngine;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>Editor capability describing groups of canonical fields. No mirrored authoring state is retained.</summary>
    public interface IGameplayBusinessInspectorProvider
    {
        string AuthoringScope { get; }
        bool Supports(GameplayAuthoringLocation node);
        IEnumerable<GameplayBusinessInspectorGroup> Groups(GameplayAuthoringLocation node);
    }
    /// <summary>All paths belong to Source; the common Inspector owns SerializedObject, Undo and source notifications.</summary>
    public sealed class GameplayBusinessInspectorGroup
    {
        public readonly Object Source;
        public readonly string Title, Note;
        public readonly string[] Paths;
        /// <summary>Optional ordered integer choices, declared by the owning game provider, never stored as rule data.</summary>
        public readonly Dictionary<string,string[]> Choices=new Dictionary<string,string[]>();
        public readonly HashSet<string> ReadOnly=new HashSet<string>();
        public readonly Dictionary<string,string> Labels=new Dictionary<string,string>();
        /// <summary>Complete arrays or extension fields are drawn only after an explicit
        /// foldout is opened; the fields still edit the same Source.</summary>
        public bool Advanced;
        public GameplayBusinessInspectorGroup(Object source,string title,string note,params string[] paths)
        {Source=source;Title=title;Note=note;Paths=paths;}
    }
}
