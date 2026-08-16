using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using Overrun.Core;
using Overrun.Data;

namespace Overrun.Simulation
{
    /// <summary>
    /// Server-driven melee enemy. Chases the nearest living player and attacks in range.
    ///
    /// Everything here runs on the server only; clients see position via NetworkTransform
    /// and death via despawn. The NavMeshAgent is disabled on clients so two machines never
    /// fight over the same transform.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(Health))]
    public sealed class Enemy : NetworkBehaviour
    {
        [SerializeField] private EnemyDefinition _definition;

        private NavMeshAgent _agent;
        private Health _health;
        private RunContext _run;

        private PlayerPawn _target;
        private float _nextRetarget;
        private float _nextAttack;

        public EnemyDefinition Definition => _definition;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _health = GetComponent<Health>();
        }

        /// <summary>Server-only. Applies definition data and wires the run context.</summary>
        public void ServerInitialise(EnemyDefinition definition, RunContext run)
        {
            if (!IsServer) return;

            _definition = definition != null ? definition : _definition;
            _run = run;

            if (_definition != null)
            {
                _health.Configure(_definition.MaxHealth, _definition.Armor, true);
                _agent.speed = _definition.MoveSpeed;
                _agent.angularSpeed = _definition.TurnSpeed;
                _agent.stoppingDistance = Mathf.Max(0.1f, _definition.AttackRange - 0.4f);
            }

            _health.Died += OnDied;
        }

        public override void OnNetworkSpawn()
        {
            // Only the server pathfinds. Leaving the agent enabled on clients would let it
            // steer a transform the server also owns.
            if (!IsServer && _agent != null) _agent.enabled = false;
            base.OnNetworkSpawn();
        }

        public override void OnNetworkDespawn()
        {
            if (_health != null) _health.Died -= OnDied;
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (!IsServer || _run == null || _health == null || _health.IsDead) return;

            float now = Time.time;

            if (now >= _nextRetarget)
            {
                _nextRetarget = now + 0.35f;      // retargeting every frame is wasted work
                _target = FindNearestLivingPlayer();
            }

            if (_target == null) return;

            Vector3 toTarget = _target.transform.position - transform.position;
            float distance = toTarget.magnitude;

            if (_agent.enabled && _agent.isOnNavMesh) _agent.SetDestination(_target.transform.position);

            if (_definition == null || distance > _definition.AttackRange || now < _nextAttack) return;

            _nextAttack = now + Mathf.Max(0.1f, _definition.AttackInterval);
            Attack(_target);
        }

        private void Attack(PlayerPawn target)
        {
            var victim = target.GetComponent<Health>();
            if (victim == null || victim.IsDead) return;

            var context = new DamageContext();
            // Source is None: an enemy is not a player, and kill attribution must never
            // credit a PlayerId that did not fire.
            context.Set(PlayerId.None, _definition.AttackTags, _definition.AttackDamage,
                        target.transform.position);

            victim.ApplyDamage(context);
        }

        private PlayerPawn FindNearestLivingPlayer()
        {
            PlayerPawn best = null;
            float bestSqr = float.MaxValue;

            var players = _run.Players.All;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerState state = players[i];
                if (!state.IsAlive || state.IsDowned || state.Pawn == null) continue;

                float sqr = (state.Pawn.transform.position - transform.position).sqrMagnitude;
                if (sqr >= bestSqr) continue;

                bestSqr = sqr;
                best = state.Pawn;
            }
            return best;
        }

        private void OnDied(PlayerId killer, Health victim, DamageContext killingBlow)
        {
            if (!IsServer) return;

            int reward = _definition != null ? _definition.ScripReward : 0;
            _run.ReportKill(killer, victim, reward);

            if (NetworkObject != null && NetworkObject.IsSpawned) NetworkObject.Despawn();
            else Destroy(gameObject);
        }
    }
}
