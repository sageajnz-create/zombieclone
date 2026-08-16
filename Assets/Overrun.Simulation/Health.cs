using System;
using UnityEngine;
using Overrun.Core;

namespace Overrun.Simulation
{
    public interface IDamageable
    {
        void TakeDamage(float amount, PlayerId attacker);
    }

    /// <summary>
    /// Damage intake and death. Shared unchanged between enemies and player pawns —
    /// composition rather than a subclass per entity type (Docs/ARCHITECTURE.md §8).
    ///
    /// Server-authoritative: damage only resolves on the server. Clients see the result
    /// through replicated state, never by applying damage locally.
    /// </summary>
    public sealed class Health : MonoBehaviour, IDamageable
    {
        [SerializeField] private float _maxHealth = 100f;

        public float MaxHealth => _maxHealth;
        public float Current { get; private set; }
        public bool IsDead => Current <= 0f;

        /// <summary>Raised on the server when this entity dies. (killer, victim)</summary>
        public event Action<PlayerId, Health> Died;

        private bool _isServer;

        private void Awake() => Current = _maxHealth;

        /// <summary>Set by the owning networked component during spawn.</summary>
        public void SetServerAuthority(bool isServer) => _isServer = isServer;

        public void TakeDamage(float amount, PlayerId attacker)
        {
            if (!_isServer) return;          // damage resolves on the server only
            if (IsDead || amount <= 0f) return;

            Current -= amount;
            if (Current > 0f) return;

            Current = 0f;
            Died?.Invoke(attacker, this);
        }

        public void ResetHealth()
        {
            Current = _maxHealth;
        }
    }
}
