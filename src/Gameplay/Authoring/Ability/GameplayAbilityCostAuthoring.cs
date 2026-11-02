using System;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>
    /// Static game resource payment configuration. Concrete game nodes must validate references via
    /// validation.Asset, build them with the supplied session, and use Preserve for Player stripping.
    /// Runtime costs may inspect a host, but must not store per-grant mutable state in this authoring object.
    /// </summary>
    [Serializable]
    public abstract class GameplayAbilityCostAuthoring
    {
        public abstract void Validate(GameplayAuthoringValidationContext validation, string path);
        public abstract IGameplayAbilityCost Build(GameplayAuthoringBuildContext context);
    }
}
