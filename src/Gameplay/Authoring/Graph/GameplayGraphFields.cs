using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace FRFrame.Gameplay.Authoring.Graph
{
    /// <summary>Cached serialization metadata for existing polymorphic authoring values.
    /// This traverses only one in-memory payload; it never discovers assets or registers types.</summary>
    public static class GameplayGraphFields
    {
        private static readonly Dictionary<Type, FieldInfo[]> Fields = new Dictionary<Type, FieldInfo[]>();
        public static FieldInfo[] SerializedFields(Type type)
        {
            if (Fields.TryGetValue(type, out var result)) return result;
            var all = new List<FieldInfo>();
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
                all.AddRange(t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(f => !f.IsStatic && !f.IsNotSerialized && (f.IsPublic ||
                        f.IsDefined(typeof(SerializeField), false) || f.IsDefined(typeof(SerializeReference), false))));
            result = all.OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
            Fields.Add(type, result);
            return result;
        }

        /// <summary>Clones ordinary serialized values, preserving real Unity resource references.
        /// The clone is never added to an asset; it is the compiler's disposable parameter copy.</summary>
        public static object Clone(object value) => value == null ? null : JsonUtility.FromJson(JsonUtility.ToJson(value), value.GetType());

        public sealed class Slot
        {
            public object Owner;
            public FieldInfo Field;
            public int Index = -1;
            public string Path;
            public Type ValueType => Index >= 0 ? Field.FieldType.GetElementType() : Field.FieldType;
            public object Value
            {
                get => Index >= 0 ? ((Array)Field.GetValue(Owner)).GetValue(Index) : Field.GetValue(Owner);
                set { if (Index >= 0) ((Array)Field.GetValue(Owner)).SetValue(value, Index); else Field.SetValue(Owner, value); }
            }
        }

        public static List<Slot> ResourceSlots(object root)
        {
            var slots = new List<Slot>();
            Walk(root, "", new HashSet<object>(), (owner, field, path) =>
            {
                if (typeof(GameplayDefinitionAsset).IsAssignableFrom(field.FieldType))
                { slots.Add(new Slot { Owner = owner, Field = field, Path = path }); return false; }
                if (field.FieldType.IsArray && typeof(GameplayDefinitionAsset).IsAssignableFrom(field.FieldType.GetElementType()))
                {
                    var array = field.GetValue(owner) as Array;
                    for (int i = 0; i < (array?.Length ?? 0); i++) slots.Add(new Slot { Owner = owner, Field = field, Index = i, Path = path + ".Array.data[" + i + "]" });
                    return false;
                }
                return true;
            });
            return slots;
        }

        public static List<Slot> ActionArrays(object root)
        {
            var slots = new List<Slot>();
            Walk(root, "", new HashSet<object>(), (owner, field, path) =>
            {
                if (field.FieldType == typeof(GameplaySkillActionAuthoring[]))
                { slots.Add(new Slot { Owner = owner, Field = field, Path = path }); return false; }
                return !typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType);
            });
            return slots;
        }

        public static Dictionary<object, string> ObjectPaths(object root)
        {
            var result = new Dictionary<object, string>();
            if (root != null) result[root] = "";
            Walk(root, "", new HashSet<object>(), (owner, field, path) =>
            {
                object value = field.GetValue(owner);
                if (value != null && !value.GetType().IsValueType && !(value is string) && !(value is UnityEngine.Object))
                {
                    if (value is Array array) { for (int i = 0; i < array.Length; i++) if (array.GetValue(i) != null) result[array.GetValue(i)] = path + ".Array.data[" + i + "]"; }
                    else result[value] = path;
                }
                return true;
            });
            return result;
        }

        private static void Walk(object value, string path, HashSet<object> seen, Func<object, FieldInfo, string, bool> visit)
        {
            if (value == null || value is string || value is UnityEngine.Object) return;
            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum) return;
            if (!type.IsValueType && !seen.Add(value)) return;
            foreach (var field in SerializedFields(type))
            {
                string childPath = path.Length == 0 ? field.Name : path + "." + field.Name;
                if (!visit(value, field, childPath)) continue;
                object child = field.GetValue(value);
                if (child is Array array)
                {
                    for (int i = 0; i < array.Length; i++) Walk(array.GetValue(i), childPath + ".Array.data[" + i + "]", seen, visit);
                }
                else Walk(child, childPath, seen, visit);
            }
        }

        public static string NormalizePath(string path) =>
            System.Text.RegularExpressions.Regex.Replace(path ?? "", @"(?<!data)\[(\d+)\]", ".Array.data[$1]");

        /// <summary>These outputs map one-to-one to an actual action array. Then is compiled
        /// into the caller's ordered array; it does not introduce a runtime graph reader.</summary>
        public static string[] OutputPorts(GameplayAuthoringNode node)
        {
            if (node.Payload is GameplayRuleNodePayload) return new[] { "Actions" };
            if (node.Payload is GameplayActionNodePayload action && action.Action != null)
                return new[] { "Then" }.Concat(ActionArrays(action.Action).Select(s => s.Path)).ToArray();
            return Array.Empty<string>();
        }
    }
}
