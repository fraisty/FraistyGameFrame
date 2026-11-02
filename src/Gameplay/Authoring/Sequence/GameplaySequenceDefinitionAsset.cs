using System;
using System.Collections.Generic;
using FRFrame.Gameplay;
using UnityEngine;

namespace FRFrame.Gameplay.Authoring
{
    [CreateAssetMenu(menuName = "FRFrame Gameplay/Sequence Definition")]
    public sealed class GameplaySequenceDefinitionAsset : GameplayDefinitionAsset
    {
        public float Duration;
        [SerializeReference] public GameplaySequenceMarkerAuthoring[] Markers =
            Array.Empty<GameplaySequenceMarkerAuthoring>();

        public override object BuildSharedDefinition(GameplayAuthoringBuildContext context) => Build(context);
        protected override void ValidateContent(GameplayAuthoringValidationContext validation, string path)
        {
            validation.Finite(Duration, path + ".Duration");
            if (Duration < 0f) validation.Error(path + ".Duration", "Sequence duration must be nonnegative.");
            if (Markers == null) { validation.Error(path + ".Markers", "Marker list is null."); return; }
            for (int i = 0; i < Markers.Length; i++)
            {
                var marker = Markers[i];
                string markerPath = path + $".Markers[{i}]";
                if (marker == null) { validation.Error(markerPath, "Null marker."); continue; }
                marker.Validate(validation, markerPath);
                if (marker.Time > Duration) validation.Error(markerPath + ".Time", "Marker exceeds sequence duration.");
            }
        }

        public GameplaySequenceDefinition Build(GameplayAuthoringBuildContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return context.Build(this, () =>
            {
                var markers = new List<GameplaySequenceMarkerDefinition>();
                foreach (var marker in Markers) markers.Add(marker.Build(context));
                return new GameplaySequenceDefinition(Duration, markers);
            });
        }
    }
}
