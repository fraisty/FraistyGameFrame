using System;
using System.Collections.Generic;

namespace FRFrame.Gameplay.Editor
{
    /// <summary>Ephemeral display projection; only full paths in the catalog are identities.</summary>
    public sealed class GameplayReferenceTreeNode
    {
        public string Segment { get; }
        public string FullPath { get; }
        public bool IsDefined { get; internal set; }
        public SortedDictionary<string, GameplayReferenceTreeNode> Children { get; } =
            new SortedDictionary<string, GameplayReferenceTreeNode>(StringComparer.Ordinal);
        internal GameplayReferenceTreeNode(string segment, string fullPath) { Segment = segment; FullPath = fullPath; }
    }

    public static class GameplayReferenceTree
    {
        public static GameplayReferenceTreeNode Project(IEnumerable<string> canonicalTags)
        {
            if (canonicalTags == null) throw new ArgumentNullException(nameof(canonicalTags));
            var root = new GameplayReferenceTreeNode("", "");
            foreach (string tag in canonicalTags)
            {
                if (string.IsNullOrEmpty(tag)) continue;
                GameplayReferenceTreeNode node = root;
                foreach (string segment in tag.Split('.'))
                {
                    string path = node.FullPath.Length == 0 ? segment : node.FullPath + "." + segment;
                    if (!node.Children.TryGetValue(segment, out GameplayReferenceTreeNode child))
                    {
                        child = new GameplayReferenceTreeNode(segment, path);
                        node.Children.Add(segment, child);
                    }
                    node = child;
                }
                node.IsDefined = true;
            }
            return root;
        }
    }
}
