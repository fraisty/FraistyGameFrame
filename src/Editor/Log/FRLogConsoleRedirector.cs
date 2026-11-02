#if UNITY_EDITOR
using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Callbacks;

namespace FRFrame.Editor
{
    public static class FRLogConsoleRedirector
    {
        private static readonly Regex StackRegex = new Regex(@"\(at (?<path>.+\.cs):(?<line>\d+)\)", RegexOptions.Compiled);
        private static bool _isRedirecting;

        [OnOpenAsset(0)]
        public static bool OnOpenAsset(int instanceId, int line)
        {
            if (_isRedirecting)
            {
                return false;
            }

            string stackTrace = GetConsoleStackTrace();
            if (string.IsNullOrEmpty(stackTrace) || !stackTrace.Contains("FRFrame"))
            {
                return false;
            }

            MatchCollection matches = StackRegex.Matches(stackTrace);
            foreach (Match match in matches)
            {
                string path = match.Groups["path"].Value.Replace('\\', '/');
                if (IsLogInfrastructurePath(path))
                {
                    continue;
                }

                if (!int.TryParse(match.Groups["line"].Value, out int targetLine))
                {
                    continue;
                }

                string assetPath = ToAssetPath(path);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                    if (asset != null)
                    {
                        try
                        {
                            _isRedirecting = true;
                            AssetDatabase.OpenAsset(asset, targetLine);
                        }
                        finally
                        {
                            _isRedirecting = false;
                        }

                        return true;
                    }
                }
            }

            return false;
        }

        private static string GetConsoleStackTrace()
        {
            Type consoleWindowType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ConsoleWindow");
            EditorWindow consoleWindow = EditorWindow.focusedWindow;
            if (consoleWindow == null || consoleWindow.GetType() != consoleWindowType)
            {
                return null;
            }

            object activeText = consoleWindowType.GetField("m_ActiveText", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(consoleWindow);
            return activeText as string;
        }

        private static bool IsLogInfrastructurePath(string path)
        {
            string fileName = Path.GetFileName(path);
            return fileName == "FRLog.cs"
                || fileName == "FRLogger.cs"
                || fileName == "UnityFRLogHandler.cs"
                || fileName == "FileFRLogHandler.cs"
                || fileName == "FRLogStackTraceUtility.cs";
        }

        private static string ToAssetPath(string path)
        {
            string projectRoot = Directory.GetParent(UnityEngine.Application.dataPath).FullName.Replace('\\', '/');
            if (path.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                return path.Substring(projectRoot.Length + 1);
            }

            if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            return null;
        }
    }
}
#endif
