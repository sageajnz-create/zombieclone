using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using Overrun.Data;
using Overrun.Simulation;

namespace Overrun.Net
{
    /// <summary>
    /// Connects an arena to the session at runtime: player spawn points, enemy spawn
    /// zones, the wave director, the site bulkhead, and the augment pool.
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
        [SerializeField] private PurchasableDoor _door;
        [SerializeField] private ArenaNavMesh _navMesh;
        [SerializeField] private AugmentDefinition[] _augments;

        private RunContext _run;

        private void Start()
        {
            NetSession session = NetSession.Find(NetworkManager.Singleton);
            if (session == null)
            {
                Debug.LogWarning("[Overrun] Arena loaded with no NetSession spawned; " +
                                 "spawns and wave director not registered.", this);
                return;
            }

            _run = session.Run;

            if (_playerSpawns != null && _playerSpawns.Length > 0)
            {
                session.SetSpawnPoints(_playerSpawns);
            }

            EnsureSpawnZones();
            EnsureDoor();
            BindNavMesh();
            BindAugments(session);

            if (_run != null)
            {
                _run.RegionUnlocked += OnRegionUnlocked;
                _run.RunReset += OnRunReset;
            }

            if (_waveDirector == null) return;

            bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
            _waveDirector.ServerInitialise(session.Run, isServer);
            _waveDirector.SetSpawnZones(_enemySpawnZones);

            Debug.Log($"[Overrun] Arena registered: {_playerSpawns?.Length ?? 0} player spawn(s), " +
                      $"{_enemySpawnZones?.Length ?? 0} enemy zone(s), server={isServer}");
        }

        private void OnDestroy()
        {
            if (_run == null) return;
            _run.RegionUnlocked -= OnRegionUnlocked;
            _run.RunReset -= OnRunReset;
        }

        private void BindAugments(NetSession session)
        {
            AugmentDefinition[] pool = _augments;
            if (pool == null || pool.Length == 0)
            {
                DefinitionCatalog catalog = DefinitionCatalog.Load();
                if (catalog != null) pool = catalog.Augments;
            }
            if (pool != null && pool.Length > 0) session.SetAugmentPool(pool);
        }

        private void EnsureSpawnZones()
        {
            if (_enemySpawnZones == null) return;
            for (int i = 0; i < _enemySpawnZones.Length; i++)
            {
                Transform t = _enemySpawnZones[i];
                if (t == null) continue;

                SpawnZone zone = t.GetComponent<SpawnZone>();
                if (zone == null) zone = t.gameObject.AddComponent<SpawnZone>();

                bool roomB = t.name.IndexOf("_B_") >= 0;
                zone.Configure(roomB ? 1 : 0, !roomB);
            }
        }

        private void EnsureDoor()
        {
            if (_door != null)
            {
                if (_run != null) _door.ServerInitialise(_run, _door.Cost, _door.UnlocksRegion, null);
                return;
            }

            Scene scene = gameObject.scene;
            Transform arena = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "Arena") { arena = root.transform; break; }
            }
            if (arena == null) return;

            Transform existing = arena.Find("SiteBulkhead");
            GameObject go;
            if (existing != null) go = existing.gameObject;
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "SiteBulkhead";
                go.transform.SetParent(arena, false);
                go.transform.position = new Vector3(0f, 1.5f, 11f);
                go.transform.localScale = new Vector3(3.6f, 3f, 0.35f);
                var renderer = go.GetComponent<Renderer>();
                if (renderer != null) renderer.material.color = new Color(0.55f, 0.18f, 0.14f);
            }

            _door = go.GetComponent<PurchasableDoor>();
            if (_door == null) _door = go.AddComponent<PurchasableDoor>();
            if (_run != null) _door.ServerInitialise(_run, 80, 1, "Site Bulkhead");
        }

        private void BindNavMesh()
        {
            if (_navMesh != null) return;
            Scene scene = gameObject.scene;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name != "Arena") continue;
                _navMesh = root.GetComponent<ArenaNavMesh>();
                break;
            }
        }

        private void OnRegionUnlocked(int region)
        {
            if (_enemySpawnZones == null) return;
            for (int i = 0; i < _enemySpawnZones.Length; i++)
            {
                Transform t = _enemySpawnZones[i];
                if (t == null) continue;
                SpawnZone zone = t.GetComponent<SpawnZone>();
                if (zone != null && zone.RegionId == region) zone.ServerSetUnlocked(true);
            }

            if (_navMesh != null) _navMesh.Rebuild();
        }

        private void OnRunReset()
        {
            if (_door != null) _door.ServerReset();
            if (_enemySpawnZones != null)
            {
                for (int i = 0; i < _enemySpawnZones.Length; i++)
                {
                    Transform t = _enemySpawnZones[i];
                    if (t == null) continue;
                    SpawnZone zone = t.GetComponent<SpawnZone>();
                    if (zone != null) zone.ServerReset();
                }
            }
            if (_navMesh != null) _navMesh.Rebuild();
        }
    }
}
