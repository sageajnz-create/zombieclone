using UnityEngine;
using Unity.Netcode;

namespace Overrun.Net
{
    /// <summary>
    /// Hands the arena's player spawn points to the session at runtime.
    ///
    /// Exists because the session lives in Bootstrap and the spawn points live in the
    /// arena scene: Unity cannot serialise a cross-scene object reference, so an inspector
    /// field on NetSession would silently be null and the game would quietly fall back to
    /// hardcoded coordinates. Runtime registration is the fix.
    ///
    /// Lives in Overrun.Net rather than Overrun.Simulation because it must talk to
    /// NetSession, and Net references Simulation — the reverse would be a cycle.
    /// </summary>
    public sealed class ArenaSpawnRegistrar : MonoBehaviour
    {
        [SerializeField] private Transform[] _spawnPoints;

        private void Start()
        {
            if (_spawnPoints == null || _spawnPoints.Length == 0)
            {
                CollectChildren();
            }

            NetSession session = NetSession.Find(NetworkManager.Singleton);
            if (session == null)
            {
                Debug.LogWarning("[Overrun] Arena loaded with no NetSession spawned; " +
                                 "spawn points not registered.", this);
                return;
            }

            session.SetSpawnPoints(_spawnPoints);
        }

        private void CollectChildren()
        {
            var found = new Transform[transform.childCount];
            for (int i = 0; i < transform.childCount; i++) found[i] = transform.GetChild(i);
            _spawnPoints = found;
        }
    }
}
