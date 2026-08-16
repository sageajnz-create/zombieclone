using UnityEngine;

namespace Overrun.Data
{
    [CreateAssetMenu(fileName = "EnemyDef_", menuName = "Overrun/Enemy Definition")]
    public class EnemyDefinition : ScriptableObject
    {
        public string Name;
        public float MaxHealth = 50f;
        public float MoveSpeed = 3.5f;
        public float AttackDamage = 10f;
        public float AttackRange = 2f;
        public float BudgetCost = 1.0f; // For WaveDirector
        public int DefinitionId;
    }
}
