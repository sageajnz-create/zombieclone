using UnityEngine;

namespace Overrun.Data
{
    [CreateAssetMenu(fileName = "WeaponDef_", menuName = "Overrun/Weapon Definition")]
    public class WeaponDefinition : ScriptableObject
    {
        public string Name;
        public float Damage = 10f;
        public float FireRate = 0.2f;
        public int MagazineSize = 30;
        public int ReserveSize = 90;
        public float ReloadTime = 2.0f;
        public float RecoilX = 0.1f;
        public float RecoilY = 0.2f;
        public float Spread = 0.02f;
        public float Range = 100f;
        public bool IsHitscan = true;
        public float CritMultiplier = 2.0f;
        public int DefinitionId; // Assigned by Registry
    }
}
