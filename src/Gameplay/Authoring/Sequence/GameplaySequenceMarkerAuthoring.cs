using System;
using FRFrame.Gameplay;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>Serializable rule-time marker node; Build must return a static marker, never advance a runtime Sequence.</summary>
    [Serializable]
    public abstract class GameplaySequenceMarkerAuthoring
    {
        public float Time;
        public virtual void Validate(GameplayAuthoringValidationContext validation, string path)
        {
            validation.Finite(Time, path + ".Time");
            if (Time < 0f) validation.Error(path + ".Time", "Marker time must be nonnegative.");
        }
        public abstract GameplaySequenceMarkerDefinition Build(GameplayAuthoringBuildContext context);
    }
}
