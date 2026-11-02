using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace FRFrame.Gameplay.Editor
{
    /// <summary>Editor-only selection of public external cost nodes. All edits use serialized properties and Undo.</summary>
    public static class GameplayAbilityCostEditor
    {
        public static IReadOnlyList<Type> Types() => GameplayAuthoringTypeCatalog.Types(typeof(GameplayAbilityCostAuthoring));

        public static void SetCost(GameplayAbilityDefinitionAsset asset, Type type)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            if (type != null && !Types().Contains(type)) throw new ArgumentException("Not a discoverable cost authoring type.", nameof(type));
            Undo.RecordObject(asset, "Change Ability Cost");
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("Cost").managedReferenceValue = type == null ? null : Activator.CreateInstance(type);
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(asset);
        }
    }

    [CustomEditor(typeof(GameplayAbilityDefinitionAsset))]
    public sealed class GameplayAbilityDefinitionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI() => GameplayAuthoringInspector.Draw(serializedObject);
    }
}
