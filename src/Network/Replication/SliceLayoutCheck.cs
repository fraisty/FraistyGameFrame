using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using LiteEntitySystem;
using LiteEntitySystem.Internal;

namespace FRFrame.Network.Replication
{
    /// <summary>POC-2 diagnostics for comparing the pinned SDK's movement layout across runtimes.</summary>
    public static class SliceLayoutCheck
    {
        private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic |
                                              BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        public static void Run(Action<string> log)
        {
            var manager = new ServerEntityManager(PocPlayerPawn.CreateTypes(), 1, 20, ServerSendRate.EqualToFPS);
            try
            {
                log($"LAYOUT runtime={RuntimeInformation.ProcessArchitecture} hash={manager.ClassDataHash}");
                var visited = new HashSet<Type>();
                var table = (Array)Read(manager, typeof(EntityManager), "ClassDataDict");
                for (var id = 1; id < table.Length; id++)
                {
                    var data = table.GetValue(id)!;
                    var type = (Type)Read(data, data.GetType(), "Type");
                    if (type == null)
                        continue;
                    log($"LAYOUT class={id} {type.Name} hash={Read(data, data.GetType(), "LayoutHash")} " +
                        $"flags={Read(data, data.GetType(), "Flags")} fixed={Read(data, data.GetType(), "FixedFieldsSize")} " +
                        $"predicted={Read(data, data.GetType(), "PredictedSize")}");
                    foreach (var field in (Array)Read(data, data.GetType(), "Fields"))
                    {
                        var ft = field.GetType();
                        log($"LAYOUT sync {Read(field, ft, "Name")} type={Read(field, ft, "FieldType")} " +
                            $"size={Read(field, ft, "IntSize")} flags={Read(field, ft, "Flags")} " +
                            $"wire={Read(field, ft, "FixedOffset")} predicted={Read(field, ft, "PredictedOffset")} native={Read(field, ft, "Offset")}");
                    }
                    for (var current = type; current != null && typeof(InternalEntity).IsAssignableFrom(current); current = current.BaseType)
                    {
                        var fields = current.GetFields(Declared);
                        Array.Sort(fields, (left, right) => string.CompareOrdinal(left.Name, right.Name));
                        foreach (var field in fields)
                        {
                            var ft = field.FieldType;
                            var generic = ft.IsGenericType ? ft.GetGenericTypeDefinition() : ft;
                            if (!field.IsStatic && generic != typeof(SyncVar<>) && !typeof(SyncableField).IsAssignableFrom(ft))
                                continue;
                            if (field.IsStatic && generic.Name != "RemoteCall" && generic.Name != "RemoteCall`1" &&
                                generic.Name != "RemoteCallSpan`1")
                                continue;
                            log($"LAYOUT member {id} {current.Name}.{field.Name} {ft}");
                            Walk(ft, visited, log);
                        }
                    }
                }
                var sdk = typeof(EntityManager).Assembly;
                foreach (var type in new[]
                {
                    typeof(PocPlayerInput), typeof(EntitySharedReference),
                    sdk.GetType("LiteEntitySystem.Internal.BaselineDataHeader", true)!,
                    sdk.GetType("LiteEntitySystem.Internal.DiffPartHeader", true)!,
                    sdk.GetType("LiteEntitySystem.Internal.LastPartData", true)!,
                    sdk.GetType("LiteEntitySystem.FirstInputHeader", true)!,
                    sdk.GetType("LiteEntitySystem.Internal.InputPacketHeader", true)!
                })
                {
                    var fields = Array.ConvertAll(type.GetFields(Declared & ~BindingFlags.Static),
                        field => $"{field.Name}@{Marshal.OffsetOf(type, field.Name)}");
                    log($"LAYOUT native {type.Name} size={Marshal.SizeOf(type)} {string.Join(",", fields)}");
                }
            }
            finally
            {
                manager.Reset();
            }
        }

        private static object Read(object instance, Type type, string name) =>
            type.GetField(name, Declared)?.GetValue(instance) ??
            type.GetProperty(name, Declared)?.GetValue(instance) ??
            throw new InvalidOperationException($"Layout probe could not read {type.Name}.{name}.");

        private static void Walk(Type type, HashSet<Type> visited, Action<string> log)
        {
            if (!visited.Add(type))
                return;
            if (type.HasElementType)
                Walk(type.GetElementType()!, visited, log);
            foreach (var argument in type.GetGenericArguments())
                Walk(argument, visited, log);
            if (type.IsEnum || !type.IsValueType || type.IsPrimitive)
                return;
            var layout = type.StructLayoutAttribute;
            var fields = type.GetFields(Declared & ~BindingFlags.Static);
            log($"LAYOUT struct {type} kind={layout?.Value} pack={layout?.Pack} size={layout?.Size} " +
                string.Join(",", Array.ConvertAll(fields, field => $"{field.Name}:{field.FieldType}")));
            foreach (var field in fields)
                Walk(field.FieldType, visited, log);
        }
    }
}
