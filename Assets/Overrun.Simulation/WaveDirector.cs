using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Overrun.Core;
using Overrun.Data;

namespace Overrun.Simulation
{
    /// <summary>
    /// Owns round progression and enemy spawning. Server-only: the component exists on
    /// clients but early-returns, so scene structure stays identical everywhere
    /// (Docs/ARCHITECTURE.md §3).
    ///
    /// Budget-based rather than count-based, so difficulty can later scale by composition
    /// instead of by raw HP — a tank costing 4 and a basic costing 1 draw from the same
    /// pool (Docs/GAMEPLAY_SYSTEMS.md §7).
    /// </summary>
    public sealed class WaveDirector : MonoBehaviour
    {
        [Header("Content")]
        [SerializeField] private GameObject _enemyPrefab;
        [SerializeField] private EnemyDefinition[] _pool;

        [Header("Budget")]
        [SerializeField] private float _baseBudget = 6f;
        [SerializeField] private float _budgetGrowth = 1.22f;
        [SerializeField] private float _perPlayerScale = 0.6f;

        [Header("Pacing")]
        [SerializeField] private int _maxAlive = 16;
        [SerializeField] private float _spawnInterval = 0.9f;
        [SerializeField] private float _interRoundSeconds = 2.5f;

        private RunContext _run;
        private bool _isServer;
        private readonly List<SpawnZone> _spawnZones = new List<SpawnZone>();
        private readonly List<Enemy> _alive = new List<Enemy>();

        private float _budgetRemaining;
        private float _nextSpawnTime;
        private float _nextRoundTime;
        private bool _roundActive;
        private bool _waitingForAugments;

        public int AliveCount => _alive.Count;
        public bool RoundActive => _roundActive;
        public bool WaitingForAugments => _waitingForAugments;

        /// <summary>
        /// Server-only. Takes an explicit server flag rather than deriving it from
        /// NetworkBehaviour.IsServer: an in-scene NetworkObject only spawns when the scene
        /// is loaded through NetworkManager.SceneManager, and if it ever loaded through the
        /// plain fallback path the director would silently go inert and no enemies would
        /// ever appear.
        /// </summary>
        public void ServerInitialise(RunContext run, bool isServer)
        {
            if (_run != null)
            {
                _run.OffersResolved -= OnOffersResolved;
                _run.RunReset -= ServerReset;
                _run.RunEnded -= OnRunEnded;
            }

            _run = run;
            _isServer = isServer;

            if (_run != null && _isServer)
            {
                _run.OffersResolved += OnOffersResolved;
                _run.RunReset += ServerReset;
                _run.RunEnded += OnRunEnded;
            }
        }

        public void SetSpawnZones(IReadOnlyList<Transform> zones)
        {
            _spawnZones.Clear();
            if (zones == null) return;
            for (int i = 0; i < zones.Count; i++)
            {
                Transform t = zones[i];
                if (t == null) continue;
                SpawnZone zone = t.GetComponent<SpawnZone>();
                if (zone == null) zone = t.gameObject.AddComponent<SpawnZone>();
                _spawnZones.Add(zone);
            }
        }

        public void SetSpawnZones(IReadOnlyList<SpawnZone> zones)
        {
            _spawnZones.Clear();
            if (zones == null) return;
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i] != null) _spawnZones.Add(zones[i]);
            }
        }

        private void OnDestroy()
        {
            if (_run == null) return;
            _run.OffersResolved -= OnOffersResolved;
            _run.RunReset -= ServerReset;
            _run.RunEnded -= OnRunEnded;
        }

        private void Update()
        {
            if (!_isServer || _run == null) return;
            if (_run.Phase == RunPhase.Ended) return;

            PruneDead();

            if (_waitingForAugments) return;

            if (!_roundActive)
            {
                // Do not start round 1 until somebody is actually in the run, or the
                // director would burn through waves against an empty arena.
                if (_run.Players.Count == 0) return;
                if (Time.time < _nextRoundTime) return;

                BeginRound();
                return;
            }

            if (_budgetRemaining > 0f) TrySpawn();
            else if (_alive.Count == 0) EndRound();
        }

        private void BeginRound()
        {
            _run.AdvanceRound();

            int players = Mathf.Max(1, _run.Players.Count);
            float playerScale = 1f + (players - 1) * _perPlayerScale;

            _budgetRemaining = _baseBudget * Mathf.Pow(_budgetGrowth, _run.Round - 1) * playerScale;
            _roundActive = true;
            _waitingForAugments = false;
            _nextSpawnTime = 0f;

            Debug.Log($"[Overrun] Round {_run.Round} — budget {_budgetRemaining:0.#}, {players} player(s)");
        }

        private void EndRound()
        {
            _roundActive = false;
            _waitingForAugments = true;
            _run.Procs.Prune(Time.time);
            Debug.Log($"[Overrun] Round {_run.Round} cleared.");
            _run.NotifyRoundCleared();
        }

        private void OnOffersResolved()
        {
            _waitingForAugments = false;
            _nextRoundTime = Time.time + _interRoundSeconds;
        }

        private void OnRunEnded()
        {
            _roundActive = false;
            _waitingForAugments = false;
            DespawnAll();
        }

        public void ServerReset()
        {
            DespawnAll();
            _roundActive = false;
            _waitingForAugments = false;
            _budgetRemaining = 0f;
            _nextRoundTime = 0f;
            _nextSpawnTime = 0f;
        }

        public void DespawnAll()
        {
            for (int i = _alive.Count - 1; i >= 0; i--)
            {
                Enemy enemy = _alive[i];
                if (enemy == null) continue;
                NetworkObject netObj = enemy.NetworkObject;
                if (netObj != null && netObj.IsSpawned) netObj.Despawn();
                else Destroy(enemy.gameObject);
            }
            _alive.Clear();
        }

        private void TrySpawn()
        {
            if (_alive.Count >= _maxAlive) return;
            if (Time.time < _nextSpawnTime) return;
            if (_enemyPrefab == null || _pool == null || _pool.Length == 0) return;
            if (UnlockedZoneCount() == 0) return;

            EnemyDefinition definition = PickDefinition();
            if (definition == null || definition.BudgetCost > _budgetRemaining)
            {
                // Cannot afford anything left in the pool; close the round out.
                _budgetRemaining = 0f;
                return;
            }

            SpawnZone zone = PickZone();
            if (zone == null) return;

            Spawn(definition, zone.transform);

            _budgetRemaining -= definition.BudgetCost;
            _nextSpawnTime = Time.time + _spawnInterval;
        }

        private int UnlockedZoneCount()
        {
            int n = 0;
            for (int i = 0; i < _spawnZones.Count; i++)
            {
                if (IsZoneEligible(_spawnZones[i])) n++;
            }
            return n;
        }

        private bool IsZoneEligible(SpawnZone zone)
        {
            if (zone == null) return false;
            if (_run != null && !_run.IsRegionUnlocked(zone.RegionId)) return false;
            return zone.IsUnlocked;
        }

        private EnemyDefinition PickDefinition()
        {
            // Composition is drawn from the seeded content stream, so the same seed
            // produces the same waves (ADR-006).
            DeterministicRandom rng = _run.Seed.Stream(RngStream.WaveComposition, _run.Round);

            float total = 0f;
            for (int i = 0; i < _pool.Length; i++)
            {
                if (_pool[i] != null && _pool[i].BudgetCost <= _budgetRemaining) total += _pool[i].SelectionWeight;
            }
            if (total <= 0f) return null;

            float roll = rng.NextFloat() * total;
            for (int i = 0; i < _pool.Length; i++)
            {
                EnemyDefinition d = _pool[i];
                if (d == null || d.BudgetCost > _budgetRemaining) continue;

                roll -= d.SelectionWeight;
                if (roll <= 0f) return d;
            }
            return null;
        }

        private SpawnZone PickZone()
        {
            SpawnZone best = null;
            float bestScore = float.MinValue;

            for (int i = 0; i < _spawnZones.Count; i++)
            {
                SpawnZone zone = _spawnZones[i];
                if (!IsZoneEligible(zone)) continue;

                float nearest = NearestPlayerDistance(zone.transform.position);
                if (nearest < 6f) continue;                 // never spawn on top of a player

                if (nearest > bestScore) { bestScore = nearest; best = zone; }
            }

            if (best != null) return best;

            for (int i = 0; i < _spawnZones.Count; i++)
            {
                if (IsZoneEligible(_spawnZones[i])) return _spawnZones[i];
            }
            return null;
        }

        private float NearestPlayerDistance(Vector3 point)
        {
            float best = float.MaxValue;
            var players = _run.Players.All;

            for (int i = 0; i < players.Count; i++)
            {
                PlayerState s = players[i];
                if (!s.IsAlive || s.Pawn == null) continue;

                float d = Vector3.Distance(s.Pawn.transform.position, point);
                if (d < best) best = d;
            }
            return best == float.MaxValue ? 0f : best;
        }

        private void Spawn(EnemyDefinition definition, Transform zone)
        {
            GameObject go = Instantiate(_enemyPrefab, zone.position, zone.rotation);

            var netObj = go.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                Debug.LogError("[Overrun] Enemy prefab has no NetworkObject.", _enemyPrefab);
                Destroy(go);
                return;
            }

            netObj.Spawn(true);

            var enemy = go.GetComponent<Enemy>();
            enemy.ServerInitialise(definition, _run);
            _alive.Add(enemy);
        }

        private void PruneDead()
        {
            for (int i = _alive.Count - 1; i >= 0; i--)
            {
                if (_alive[i] == null) _alive.RemoveAt(i);
            }
        }
    }
}
