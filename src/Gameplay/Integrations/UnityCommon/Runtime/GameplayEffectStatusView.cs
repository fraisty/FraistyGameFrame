using TMPro;
using UnityEngine;

namespace FRFrame.Gameplay.Unity
{
    /// <summary>A reusable pooled Effect view. The controller supplies actual layers and owns its lifetime.</summary>
    public sealed class GameplayEffectStatusView : MonoBehaviour
    {
        [SerializeField] private Transform badge;
        [SerializeField] private TMP_Text stackLabel;
        [SerializeField] private SpriteRenderer icon;
        [SerializeField] private Material fullStackMaterial;
        [SerializeField] private Color fullStackTextColor = new Color(.86f, .69f, 1);
        [SerializeField, Min(0)] private float fullStackPulseSeconds = .26f;
        [SerializeField, Min(0)] private float fullStackPulseScale = .34f;
        private ParticleSystem[] _particles;
        private Vector3 _normalScale;
        private Material _normalMaterial;
        private Color _normalTextColor;
        private float _pulseRemaining;

        private void Awake()
        {
            _particles = GetComponentsInChildren<ParticleSystem>(true);
            if (badge != null) _normalScale = badge.localScale;
            if (icon != null) _normalMaterial = icon.sharedMaterial;
            if (stackLabel != null) _normalTextColor = stackLabel.color;
        }

        internal void Begin()
        {
            _pulseRemaining = 0;
            if (badge != null) badge.localScale = _normalScale;
            foreach (var particle in _particles)
            {
                particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                particle.Play(false);
                particle.Pause(false);
            }
        }

        internal void SetStacks(int stacks, bool full, bool enteredFull)
        {
            if (stackLabel != null)
            {
                stackLabel.SetText("×{0}", stacks);
                stackLabel.color = full ? fullStackTextColor : _normalTextColor;
            }
            if (icon != null) icon.sharedMaterial = full && fullStackMaterial != null ? fullStackMaterial : _normalMaterial;
            if (enteredFull) _pulseRemaining = fullStackPulseSeconds;
            if (!full && _pulseRemaining > 0)
            {
                _pulseRemaining = 0;
                if (badge != null) badge.localScale = _normalScale;
            }
        }

        internal void Advance(float amount, bool showBadge)
        {
            if (badge != null)
            {
                if (badge.gameObject.activeSelf != showBadge) badge.gameObject.SetActive(showBadge);
                if (_pulseRemaining > 0)
                {
                    _pulseRemaining = Mathf.Max(0, _pulseRemaining - amount);
                    badge.localScale = _normalScale * (1 + fullStackPulseScale * Mathf.Sin(Mathf.PI * _pulseRemaining / fullStackPulseSeconds));
                }
            }
            foreach (var particle in _particles) particle.Simulate(amount, false, false, false);
        }
    }
}
