using UnityEngine;
using Unity.Netcode;
using Overrun.Simulation;

namespace Overrun.Net
{
    /// <summary>
    /// Connects an arena to the session at runtime: player spawn points, enemy spawn
    /// zones, and the wave director.
    ///
    /// Exists because the session lives in Bootstrap and the arena lives in World. Unity
    /// cannot serialise a cross-scene object reference, so inspector fields pointing the
    /// other way would silently be null and the game would quietly fall back to defaults —
    /// the failure mode being "enemies never spawn" with no error anywhere.
    ///
    /// Lives in Overrun.Net rather than Overrun.Simulation because it must talk to
    /// NetSession, and Net references Simulation — the reverse would be a cycle.
    /// </summary>
    public sealed class ArenaRegistrar : MonoBehaviour
    {
        [SerializeField] private Transform[] _playerSpawns;
        [SerializeField] private Transform[] _enemySpawnZones;
        [SerializeField] private WaveDirector _waveDirector;

        private void Start()
        {
            NetSession session = NetSession.Find(NetworkManager.Singleton);
            if (session == null)
            {
                Debug.LogWarning("[Overrun] Arena loaded with no NetSession spawned; " +
                                 "spawns and wave director not registered.", this);
                return;
            }

            if (_playerSpawns != null && _playerSpawns.Length > 0)
            {
                session.SetSpawnPoints(_playerSpawns);
            }

            if (_waveDirector == null) return;

            bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
            _waveDirector.ServerInitialise(session.Run, isServer);
            _waveDirector.SetSpawnZones(_enemySpawnZones);

            Debug.Log($"[Overrun] Arena registered: {_playerSpawns?.Length ?? 0} player spawn(s), " +
                      $"{_enemySpawnZones?.Length ?? 0} enemy zone(s), server={isServer}");
        }
    }
}
