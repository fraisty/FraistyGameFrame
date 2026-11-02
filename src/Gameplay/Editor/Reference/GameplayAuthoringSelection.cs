using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>The four serialized skill roles. Array order is the execution order; no edge data is stored.</summary>
    public enum SkillNodeRole { Trigger, Condition, Target, Action }

    public readonly struct SkillNodeLocation
    {
        public readonly SkillNodeRole Role;
        public readonly int Index;
        public readonly long Id;
        public readonly string Path;
        public readonly bool IsNull;
        public string Key => IsNull ? Role + ":null:" + Path : Role + ":" + Id;
        public SkillNodeLocation(SkillNodeRole role, int index, SerializedProperty property)
        {
            Role = role;
            Index = index;
            Id = property.managedReferenceId;
            Path = property.propertyPath;
            IsNull = property.managedReferenceValue == null;
        }
    }

}
