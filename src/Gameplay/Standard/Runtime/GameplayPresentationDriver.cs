using System;

namespace FRFrame.Gameplay.Standard
{
    /// <summary>Optional presentation sampling over the host's existing delta. Does not own a rule
    /// scheduler or advance Effect/Ability/Sequence. Sample existing views before new rule markers,
    /// so a view created in this step cannot consume time preceding its creation.</summary>
    public interface IGameplayPresentationDriver : IDisposable { void Advance(float amount); }
}
