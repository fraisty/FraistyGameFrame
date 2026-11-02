using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FRFrame.Gameplay
{
    /// <summary>Immutable marker schedule. Equal-time markers retain their authored array order.</summary>
    public sealed class GameplaySequenceDefinition
    {
        public float Duration { get; }
        public IReadOnlyList<GameplaySequenceMarkerDefinition> Markers { get; }

        public GameplaySequenceDefinition(float duration, IEnumerable<GameplaySequenceMarkerDefinition> markers)
        {
            if (!float.IsFinite(duration) || duration < 0f) throw new ArgumentOutOfRangeException(nameof(duration));
            if (markers == null) throw new ArgumentNullException(nameof(markers));
            Duration = duration;
            var ordered = new List<(GameplaySequenceMarkerDefinition marker, int index)>();
            foreach (var marker in markers)
            {
                if (marker == null) throw new ArgumentException("Markers cannot contain null.", nameof(markers));
                if (marker.Time > duration) throw new ArgumentException("Marker exceeds sequence duration.", nameof(markers));
                ordered.Add((marker, ordered.Count));
            }
            ordered.Sort((a, b) =>
            {
                int timeOrder = a.marker.Time.CompareTo(b.marker.Time);
                return timeOrder != 0 ? timeOrder : a.index.CompareTo(b.index);
            });
            var copy = new List<GameplaySequenceMarkerDefinition>(ordered.Count);
            foreach (var item in ordered) copy.Add(item.marker);
            Markers = new ReadOnlyCollection<GameplaySequenceMarkerDefinition>(copy);
        }
    }
}
