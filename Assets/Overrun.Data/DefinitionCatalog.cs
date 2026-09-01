using UnityEngine;

namespace Overrun.Data
{
    /// <summary>
    /// The VS001 content set, loadable without scene wiring via Resources.
    /// Definitions themselves live under Content/Definitions; this catalog points at them.
    /// </summary>
    [CreateAssetMenu(fileName = "DefinitionCatalog", menuName = "Overrun/Definition Catalog")]
    public sealed class DefinitionCatalog : ScriptableObject
    {
        public const string ResourcesPath = "Overrun/DefinitionCatalog";

        public WeaponDefinition Sidearm;
        public EnemyDefinition BasicEnemy;
        public AugmentDefinition[] Augments;

        public static DefinitionCatalog Load() =>
            Resources.Load<DefinitionCatalog>(ResourcesPath);
    }
}
