using UnityEngine;

namespace FRFrame.Gameplay.Unity
{
    /// <summary>Lets a renderer backend tint its visible model at the native color channel.</summary>
    public interface IGameplayVisualColorTarget
    {
        Renderer Renderer { get; }
        Color Color { get; set; }
    }
}
