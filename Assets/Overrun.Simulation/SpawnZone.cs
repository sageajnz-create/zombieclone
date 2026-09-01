using UnityEngine;

namespace Overrun.Simulation
{
    /// <summary>
    /// A tagged volume the wave director may spawn into. Region 0 is the starting room
    /// and begins unlocked; further regions open when a PurchasableDoor is bought.
    /// </summary>
    public sealed class SpawnZone : MonoBehaviour
    {
        [SerializeField] private int _regionId;
        [SerializeField] private bool _startsUnlocked = true;

        public int RegionId => _regionId;
        public bool StartsUnlocked => _startsUnlocked;
        public bool IsUnlocked { get; private set; }

        private void Awake() => IsUnlocked = _startsUnlocked;

        public void Configure(int regionId, bool startsUnlocked)
        {
            _regionId = regionId;
            _startsUnlocked = startsUnlocked;
            IsUnlocked = startsUnlocked;
        }

        public void ServerSetUnlocked(bool unlocked) => IsUnlocked = unlocked;

        public void ServerReset() => IsUnlocked = _startsUnlocked;
    }
}
