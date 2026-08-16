using UnityEngine;
using Overrun.Core;

namespace Overrun.Data
{
    public enum EnemyArchetype
    {
        Basic,
        Fast,
        Tank,
        Ranged,
        Exploder,
        Elite,
        MiniBoss,
        Boss
    }

    /// <summary>
    /// Immutable enemy data. Archetypes differ by which components the prefab carries and
    /// which definition feeds them — not by a subclass per enemy type
    /// (Docs/ARCHITECTURE.md §8).
    /// </summary>
    [CreateAssetMenu(fileName = "EnemyDef_", menuName = "Overrun/Enemy Definition")]
    public sealed class EnemyDefinition : ScriptableObject
    {
        [Header("Identity")]
        public int DefinitionId;
        public string DisplayName = "Sundered";
        public EnemyArchetype Archetype = EnemyArchetype.Basic;

        [Header("Survivability")]
        public float MaxHealth = 60f;
        public float Armor;

        [Header("Movement")]
        public float MoveSpeed = 3.2f;
        public float TurnSpeed = 240f;

        [Header("Attack")]
        public float AttackDamage = 12f;
        public float AttackRange = 1.9f;
        public float AttackInterval = 1.1f;
        public Tag AttackTags = Tag.Melee;

        [Header("Economy / Director")]
        [Tooltip("Scrip awarded to the killing player.")]
        public int ScripReward = 10;
        [Tooltip("Cost against the wave budget. Tougher enemies cost more.")]
        public float BudgetCost = 1f;
        [Tooltip("Relative likelihood of being chosen when the director spends budget.")]
        public float SelectionWeight = 1f;
    }
}
