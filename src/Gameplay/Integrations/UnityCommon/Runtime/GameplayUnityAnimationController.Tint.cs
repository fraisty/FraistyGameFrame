using System;
using System.Collections.Generic;
using UnityEngine;

namespace FRFrame.Gameplay.Unity
{
    public sealed partial class GameplayUnityAnimationController
    {
        private CueLease _activeTint;

        /// <summary>Envelope shared by the live timed Cue and its editor preview.</summary>
        public static float TintWeight(float elapsed, float duration, float fadeIn, float fadeOut)
        {
            if (elapsed < 0 || elapsed >= duration) return 0;
            float weight = fadeIn > 0 ? Mathf.Min(1, elapsed / fadeIn) : 1;
            return fadeOut > 0 ? Mathf.Min(weight, (duration - elapsed) / fadeOut) : weight;
        }

        private static Action<float> CaptureTint(Transform binding, Color color)
        {
            var writers = new List<Action<float>>();
            var nativeRenderers = new HashSet<Renderer>();
            foreach (var component in binding.GetComponentsInChildren<MonoBehaviour>())
            {
                if (component is not IGameplayVisualColorTarget native || !native.Renderer.enabled) continue;
                nativeRenderers.Add(native.Renderer);
                Color original = native.Color;
                writers.Add(weight => { if (component) native.Color = Blend(original, weight); });
            }
            foreach (var renderer in binding.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || nativeRenderers.Contains(renderer)) continue;
                if (renderer is SpriteRenderer sprite)
                {
                    Color original = sprite.color;
                    writers.Add(weight => { if (sprite) sprite.color = Blend(original, weight); });
                }
                else if (renderer is MeshRenderer or SkinnedMeshRenderer)
                {
                    var materials = renderer.sharedMaterials;
                    for (int index = 0; index < materials.Length; index++)
                    {
                        var material = materials[index];
                        if (!material) continue;
                        int property = Shader.PropertyToID(material.HasProperty("_BaseColor") ? "_BaseColor" : "_Color");
                        if (!material.HasProperty(property)) continue;
                        int slot = index;
                        var original = new MaterialPropertyBlock();
                        var working = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(original, slot);
                        renderer.GetPropertyBlock(working, slot);
                        if (original.isEmpty) renderer.GetPropertyBlock(working);
                        Color baseColor = working.HasColor(property) ? working.GetColor(property) : material.GetColor(property);
                        writers.Add(weight =>
                        {
                            if (!renderer) return;
                            if (weight == 0) { renderer.SetPropertyBlock(original.isEmpty ? null : original, slot); return; }
                            working.SetColor(property, Blend(baseColor, weight));
                            renderer.SetPropertyBlock(working, slot);
                        });
                    }
                }
            }
            if (writers.Count == 0) return null;
            return weight => { foreach (var write in writers) write(weight); };

            Color Blend(Color original, float weight) => Color.Lerp(original,
                new Color(color.r, color.g, color.b, original.a), Mathf.Clamp01(weight * color.a));
        }
    }
}
