using UnityEngine;
using Overrun.Core;

namespace Overrun.Data
{
    /// <summary>
    /// Immutable weapon data. Fire behaviour is configuration, not code — pellet count 8
    /// with wide spread IS the shotgun; there is no Shotgun class
    /// (Docs/GAMEPLAY_SYSTEMS.md §5).
    ///
    /// Never mutated at runtime: in the Editor those writes persist to disk and silently
    /// corrupt balance data. Per-instance variation belongs on WeaponInstance.
    /// </summary>
    [CreateAssetMenu(fileName = "WeaponDef_", menuName = "Overrun/Weapon Definition")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable id used on the wire. Never send the asset itself.")]
        public int DefinitionId;
        public string DisplayName = "Sidearm";

        [Tooltip("Drives every tag-filtered augment. A hitscan pistol is Weapon|Hitscan.")]
        public Tag Tags = Tag.Weapon | Tag.Hitscan;

        [Header("Damage")]
        public float Damage = 24f;
        public float CritChance = 0.05f;
        public float CritMultiplier = 2f;
        public float HeadshotMultiplier = 2f;

        [Header("Fire")]
        [Tooltip("Seconds between shots.")]
        public float FireInterval = 0.16f;
        [Tooltip("Projectiles per trigger pull. >1 makes it a spread weapon.")]
        public int PelletCount = 1;
        [Tooltip("Cone half-angle in degrees.")]
        public float Spread = 0.6f;
        public float Range = 120f;
        public bool IsHitscan = true;

        [Header("Ammo")]
        public int MagazineSize = 12;
        public int ReserveAmmo = 120;
        public float ReloadSeconds = 1.4f;

        [Header("Recoil")]
        [Tooltip("Degrees of upward kick per shot, applied to the aim origin.")]
        public float RecoilPitch = 1.4f;
        [Tooltip("Degrees of random yaw kick per shot.")]
        public float RecoilYaw = 0.45f;
        [Tooltip("Degrees per second the kick recovers.")]
        public float RecoilRecovery = 12f;

        public float SecondsPerShot => Mathf.Max(0.01f, FireInterval);
    }
}
