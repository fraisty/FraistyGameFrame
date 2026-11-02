using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Authoring;
using UnityEngine;

namespace FRFrame.Gameplay.Editor.Timeline
{
    /// <summary>One shared geometry result for marker rendering and mouse hit testing.</summary>
    public static class GameplaySequenceMarkerLayout
    {
        public struct Entry
        {
            public int Index;
            public int Track;
            public Rect Rect;
        }

        /// <summary>The standalone timeline may present a pending drag without changing its
        /// source. Drawing and hit testing consume that same optional presentation time.</summary>
        public static List<Entry> Build(GameplaySequenceMarkerAuthoring[] markers, Rect area,
            float contentX, float pixelsPerSecond, Func<int,float> presentedTime = null)
        {
            var result = new List<Entry>();
            if (markers == null) return result;
            var laneEnd = new float[GameplaySequenceTimelineModel.TrackCount, 2];
            for (int track = 0; track < GameplaySequenceTimelineModel.TrackCount; track++)
                for (int lane = 0; lane < 2; lane++) laneEnd[track, lane] = float.NegativeInfinity;
            for (int i = 0; i < markers.Length; i++)
            {
                var marker = markers[i];
                int track = GameplaySequenceTimelineModel.Track(marker);
                if (track < 0) continue;
                float x = contentX + (presentedTime==null?marker.Time:presentedTime(i)) * pixelsPerSecond;
                float left = x - 3f;
                int lane = left >= laneEnd[track, 0] + 2f ? 0 :
                    left >= laneEnd[track, 1] + 2f ? 1 :
                    laneEnd[track, 0] <= laneEnd[track, 1] ? 0 : 1;
                laneEnd[track, lane] = left + GameplayEditorStyles.MarkerWidth;
                result.Add(new Entry { Index = i, Track = track,
                    Rect = new Rect(left,
                        area.y + GameplayEditorStyles.RulerHeight + track * GameplayEditorStyles.TrackHeight +
                        2f + lane * (GameplayEditorStyles.MarkerHeight + 1f),
                        GameplayEditorStyles.MarkerWidth, GameplayEditorStyles.MarkerHeight) });
            }
            return result;
        }

        public static int HitTest(IReadOnlyList<Entry> entries, Vector2 point, Rect content)
        {
            if (!content.Contains(point)) return -1;
            for (int i = entries.Count - 1; i >= 0; i--)
                if (entries[i].Rect.Contains(point)) return entries[i].Index;
            return -1;
        }
    }
}
