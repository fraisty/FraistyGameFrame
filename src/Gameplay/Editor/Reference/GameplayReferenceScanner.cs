using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FRFrame.Gameplay.Authoring;
using UnityEditor;

namespace FRFrame.Gameplay.Editor
{
    public enum GameplayReferenceKind { Tag, Attribute }

    /// <summary>One typed serialized reference, identified by asset and SerializedProperty path.</summary>
    public readonly struct GameplaySerializedReference
    {
        public readonly string AssetPath;
        public readonly string PropertyPath;
        public readonly string Value;
        public readonly GameplayReferenceKind Kind;
        public GameplaySerializedReference(string assetPath, string propertyPath, string value, GameplayReferenceKind kind)
        { AssetPath = assetPath; PropertyPath = propertyPath; Value = value; Kind = kind; }
    }

    public readonly struct GameplayCodeLiteralReference
    {
        public readonly string SourcePath;
        public readonly int Line;
        public readonly string Value;
        public readonly GameplayReferenceKind Kind;
        public GameplayCodeLiteralReference(string sourcePath, int line, string value, GameplayReferenceKind kind)
        { SourcePath = sourcePath; Line = line; Value = value; Kind = kind; }
    }

    /// <summary>A definition asset reference, including those inside external SerializeReference authoring nodes.</summary>
    public readonly struct GameplayDefinitionAssetReference
    {
        public readonly GameplayDefinitionAsset Source;
        public readonly GameplayDefinitionAsset Target;
        public readonly string PropertyPath;
        public GameplayDefinitionAssetReference(GameplayDefinitionAsset source, GameplayDefinitionAsset target, string path)
        { Source = source; Target = target; PropertyPath = path; }
    }

    /// <summary>Read-only typed serialized traversal and read-only production constructor literal scan.</summary>
    public static class GameplayReferenceScanner
    {
        private static readonly Regex LiteralPattern = new Regex(
            @"\bnew\s+Gameplay(Tag|AttributeId)\s*\(\s*""([^""\r\n]*)""\s*\)", RegexOptions.Compiled);

        public static IReadOnlyList<GameplayDefinitionAsset> FindDefinitionAssets()
        {
            var assets = new List<GameplayDefinitionAsset>();
            var guids = new HashSet<string>(StringComparer.Ordinal);
            foreach (Type type in TypeCache.GetTypesDerivedFrom<GameplayDefinitionAsset>())
            {
                if (type.IsAbstract) continue;
                foreach (string guid in AssetDatabase.FindAssets("t:" + type.Name, new[] { "Assets" }))
                    guids.Add(guid);
            }
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                assets.AddRange(AssetDatabase.LoadAllAssetsAtPath(path).OfType<GameplayDefinitionAsset>().Where(a=>!a.MigrationHistoryOnly));
            }
            assets.Sort((a, b) => StringComparer.Ordinal.Compare(AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b)));
            return assets;
        }

        public static IReadOnlyList<GameplaySerializedReference> ScanAssets(IEnumerable<GameplayDefinitionAsset> assets)
        {
            var result = new List<GameplaySerializedReference>();
            var roots = assets.ToArray();
            var reachable = new HashSet<GameplayDefinitionAsset>(roots.Where(x => x != null));
            foreach (var reference in ScanDefinitionReferences(roots)) reachable.Add(reference.Target);
            foreach (GameplayDefinitionAsset asset in reachable.OrderBy(x => AssetDatabase.GetAssetPath(x), StringComparer.Ordinal))
            {
                if (asset == null) continue;
                string assetPath = AssetDatabase.GetAssetPath(asset);
                var serialized = new SerializedObject(asset);
                SerializedProperty property = serialized.GetIterator();
                bool enterChildren = true;
                while (property.NextVisible(enterChildren))
                {
                    enterChildren = true;
                    GameplayReferenceKind kind;
                    string field;
                    if (property.type == nameof(GameplayTagReference))
                    { kind = GameplayReferenceKind.Tag; field = "Name"; }
                    else if (property.type == nameof(GameplayAttributeReference))
                    { kind = GameplayReferenceKind.Attribute; field = "Value"; }
                    else continue;
                    SerializedProperty child = property.FindPropertyRelative(field);
                    if (child != null && child.propertyType == SerializedPropertyType.String)
                        result.Add(new GameplaySerializedReference(assetPath, child.propertyPath, child.stringValue, kind));
                }
            }
            return result;
        }

        /// <summary>Read-only, cycle-safe traversal of referenced definition assets; does not build or save content.</summary>
        public static IReadOnlyList<GameplayDefinitionAssetReference> ScanDefinitionReferences(IEnumerable<GameplayDefinitionAsset> assets)
        {
            var result = new List<GameplayDefinitionAssetReference>();
            var seen = new HashSet<GameplayDefinitionAsset>();
            var pending = new Queue<GameplayDefinitionAsset>(assets);
            while (pending.Count > 0)
            {
                var asset = pending.Dequeue();
                if (asset == null || !seen.Add(asset)) continue;
                var serialized = new SerializedObject(asset);
                var property = serialized.GetIterator();
                while (property.NextVisible(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference ||
                        !(property.objectReferenceValue is GameplayDefinitionAsset target)) continue;
                    result.Add(new GameplayDefinitionAssetReference(asset, target, property.propertyPath));
                    pending.Enqueue(target);
                }
            }
            return result;
        }

        public static IReadOnlyList<GameplayCodeLiteralReference> ScanProductionCode()
        {
            var result = new List<GameplayCodeLiteralReference>();
            foreach (string file in ProductionSourceFiles())
            {
                string path = file.Replace('\\', '/');
                string source = File.ReadAllText(file);
                foreach (Match match in LiteralPattern.Matches(source))
                {
                    int line = 1;
                    for (int i = 0; i < match.Index; i++) if (source[i] == '\n') line++;
                    result.Add(new GameplayCodeLiteralReference(path, line, match.Groups[2].Value,
                        match.Groups[1].Value == "Tag" ? GameplayReferenceKind.Tag : GameplayReferenceKind.Attribute));
                }
            }
            return result;
        }

        public static int CountProductionSourceFiles() => ProductionSourceFiles().Count();

        private static IEnumerable<string> ProductionSourceFiles()
        {
            foreach (string root in new[] { "Assets/FRFrame/Gameplay", "Assets/Scripts/AzurLane" })
            {
                if (!Directory.Exists(root)) continue;
                foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
                {
                    string path = file.Replace('\\', '/');
                    if (!path.Contains("/Tests/") && !path.Contains("/Generated/")) yield return file;
                }
            }
        }
    }
}
