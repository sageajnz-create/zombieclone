using System;
using UnityEngine;
using Overrun.Core;

namespace Overrun.Simulation
{
    /// <summary>Anything damage can be applied to.</summary>
    public interface IDamageable
    {
        bool IsDead { get; }
        void ApplyDamage(DamageContext context);
    }

    /// <summary>
    /// Damage intake and death. Shared unchanged between enemies, player pawns and
    /// destructibles — composition rather than a subclass per entity type
    /// (Docs/ARCHITECTURE.md §8).
    ///
    /// Server-authoritative: damage only resolves where IsServerAuthority is set. Clients
    /// see the result through replicated state and never apply damage locally.
    /// </summary>
    public sealed class Health : MonoBehaviour, IDamageable
    {
        [SerializeField] private float _maxHealth = 100f;
        [SerializeField] private float _armor;

        public float MaxHealth => _maxHealth;
        public float Current { get; private set; }
        public bool IsDead => Current <= 0f;
        public float Normalised => _maxHealth > 0f ? Mathf.Clamp01(Current / _maxHealth) : 0f;

        /// <summary>Set by the owning networked component at spawn.</summary>
        public bool IsServerAuthority { get; set; }

        /// <summary>Server-side. (killer, victim, killing blow)</summary>
        public event Action<PlayerId, Health, DamageContext> Died;

        /// <summary>Server-side, every applied hit. Drives hit markers and damage numbers.</summary>
        public event Action<DamageContext> Damaged;

        private void Awake() => Current = _maxHealth;

        public void Configure(float maxHealth, float armor, bool serverAuthority)
        {
            _maxHealth = Mathf.Max(1f, maxHealth);
            _armor = Mathf.Max(0f, armor);
            IsServerAuthority = serverAuthority;
            Current = _maxHealth;
        }

        public void ApplyDamage(DamageContext context)
        {
            if (!IsServerAuthority || IsDead || context == null) return;

            // Flat armor reduction, floored so armor can never make a hit heal.
            float applied = Mathf.Max(0f, context.Amount - _armor);
            if (applied <= 0f) return;

            context.Amount = applied;
            Current -= applied;

            Damaged?.Invoke(context);

            if (Current > 0f) return;

            Current = 0f;
            Died?.Invoke(context.Source, this, context);
        }

        public void Heal(float amount)
        {
            if (!IsServerAuthority || IsDead || amount <= 0f) return;
            Current = Mathf.Min(_maxHealth, Current + amount);
        }

        /// <summary>Raise the cap and current health by the same amount (augment pick).</summary>
        public void IncreaseMax(float delta)
        {
            if (!IsServerAuthority || IsDead || delta <= 0f) return;
            _maxHealth += delta;
            Current += delta;
        }

        public void ResetHealth()
        {
            Current = _maxHealth;
        }

        public void ResetHealth(float maxHealth)
        {
            _maxHealth = Mathf.Max(1f, maxHealth);
            Current = _maxHealth;
        }
    }
}
