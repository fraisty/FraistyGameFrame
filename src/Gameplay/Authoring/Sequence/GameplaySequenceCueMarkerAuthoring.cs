using System;
using FRFrame.Gameplay;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Authoring
{
    [Serializable, Preserve]
    public sealed class GameplaySequenceCueMarkerAuthoring : GameplaySequenceMarkerAuthoring
    {
        public GameplayTagReference CueTag;
        public bool HasMagnitude;
        public float Magnitude;
        public bool HasDuration;
        public float Duration;

        public override void Validate(GameplayAuthoringValidationContext validation, string path)
        {
            base.Validate(validation, path);
            validation.Tag(CueTag, path + ".CueTag");
            if (HasMagnitude) validation.Finite(Magnitude, path + ".Magnitude");
            if (HasDuration)
            {
                validation.Finite(Duration, path + ".Duration");
                if (Duration <= 0f) validation.Error(path + ".Duration", "Cue duration must be positive.");
            }
        }

        public override GameplaySequenceMarkerDefinition Build(GameplayAuthoringBuildContext context) =>
            new GameplaySequenceCueMarkerDefinition(Time, CueTag.Build(),
                HasMagnitude ? Magnitude : (float?)null, HasDuration ? Duration : (float?)null);
    }
}
