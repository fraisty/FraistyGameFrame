using UnityEditor;
using UnityEngine;

namespace FRFrame.Gameplay.Editor
{
    /// <summary>Shared sizing and palette for the Skill Workbench and Sequence Timeline.</summary>
    public static class GameplayEditorStyles
    {
        public const float ToolbarHeight = 28f;
        public const float PaneHandle = 5f;
        public const float RowHeight = 24f;
        public const float RulerHeight = 32f;
        public const float TrackHeight = 42f;
        public const float TrackHeaderWidth = 142f;
        public const float MarkerWidth = 138f;
        public const float MarkerHeight = 18f;
        public static readonly Color Surface = new Color(.16f, .19f, .23f);
        public static readonly Color Panel = new Color(.19f, .22f, .27f);
        public static readonly Color TrackA = new Color(.18f, .21f, .26f);
        public static readonly Color TrackB = new Color(.15f, .18f, .23f);
        public static readonly Color Tail = new Color(.12f, .15f, .19f);
        public static readonly Color Grid = new Color(.29f, .33f, .39f);
        public static readonly Color Muted = new Color(.69f, .74f, .79f);
        public static readonly Color Accent = new Color(.31f, .72f, .91f);
        public static readonly Color Effect = new Color(.25f, .59f, .69f);
        public static readonly Color Cue = new Color(.70f, .44f, .28f);
        public static readonly Color Playhead = new Color(1f, .79f, .32f);
        public static readonly Color RuleEnd = new Color(.91f, .40f, .42f);
        public static readonly Color TriggerNode = new Color(.31f, .69f, .95f);
        public static readonly Color ConditionNode = new Color(.85f, .67f, .34f);
        public static readonly Color TargetNode = new Color(.48f, .79f, .64f);
        public static readonly Color ActionNode = new Color(.74f, .56f, .91f);
        public static readonly Color PreviewBackground = new Color(.12f, .16f, .21f);

        private static GUIStyle _markerText;
        private static GUIStyle _browserRow;
        public static GUIStyle BrowserRow => _browserRow ?? (_browserRow = new GUIStyle(EditorStyles.toolbarButton)
        {
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(8, 4, 0, 0)
        });
        public static GUIStyle MarkerText => _markerText ?? (_markerText = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleLeft,
            clipping = TextClipping.Clip,
            normal = { textColor = Color.white }
        });
    }
}
