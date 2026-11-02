#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace FRFrame.Editor
{
    public static class FRLogScriptingDefineSymbols
    {
        private static readonly string[] LogDefines =
        {
            FRLogDefineSymbols.EnableLog,
            FRLogDefineSymbols.Debug,
            FRLogDefineSymbols.Info,
            FRLogDefineSymbols.Warning,
            FRLogDefineSymbols.Error,
            FRLogDefineSymbols.Fatal,
            FRLogDefineSymbols.DebugAndAbove,
            FRLogDefineSymbols.InfoAndAbove,
            FRLogDefineSymbols.WarningAndAbove,
            FRLogDefineSymbols.ErrorAndAbove,
            FRLogDefineSymbols.FatalAndAbove,
            FRLogDefineSymbols.File
        };

        [MenuItem("FRFrame/Log/Disable All Logs")]
        public static void DisableAllLogs()
        {
            SetLogDefines();
        }

        [MenuItem("FRFrame/Log/Enable All Logs")]
        public static void EnableAllLogs()
        {
            SetLogDefines(FRLogDefineSymbols.EnableLog);
        }

        [MenuItem("FRFrame/Log/Enable Debug And Above Logs")]
        public static void EnableDebugAndAboveLogs()
        {
            SetLogDefines(FRLogDefineSymbols.DebugAndAbove);
        }

        [MenuItem("FRFrame/Log/Enable Info And Above Logs")]
        public static void EnableInfoAndAboveLogs()
        {
            SetLogDefines(FRLogDefineSymbols.InfoAndAbove);
        }

        [MenuItem("FRFrame/Log/Enable Warning And Above Logs")]
        public static void EnableWarningAndAboveLogs()
        {
            SetLogDefines(FRLogDefineSymbols.WarningAndAbove);
        }

        [MenuItem("FRFrame/Log/Enable Error And Above Logs")]
        public static void EnableErrorAndAboveLogs()
        {
            SetLogDefines(FRLogDefineSymbols.ErrorAndAbove);
        }

        [MenuItem("FRFrame/Log/Enable Fatal And Above Logs")]
        public static void EnableFatalAndAboveLogs()
        {
            SetLogDefines(FRLogDefineSymbols.FatalAndAbove);
        }

        public static void SetLogDefines(params string[] targetDefines)
        {
            NamedBuildTarget namedBuildTarget = NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
            HashSet<string> defines = ReadDefines(namedBuildTarget);
            RemoveLogDefines(defines);
            foreach (string define in targetDefines)
            {
                if (!string.IsNullOrEmpty(define))
                {
                    defines.Add(define);
                }
            }

            PlayerSettings.SetScriptingDefineSymbols(namedBuildTarget, string.Join(";", defines.OrderBy(value => value)));
        }

        public static HashSet<string> ReadDefines(NamedBuildTarget namedBuildTarget)
        {
            string current = PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget);
            return new HashSet<string>(current.Split(';').Select(value => value.Trim()).Where(value => !string.IsNullOrEmpty(value)));
        }

        public static void RemoveLogDefines(HashSet<string> defines)
        {
            for (int i = 0; i < LogDefines.Length; i++)
            {
                defines.Remove(LogDefines[i]);
            }
        }
    }
}
#endif
