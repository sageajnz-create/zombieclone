using UnityEngine;
using Unity.Netcode;
using Overrun.Core;

namespace Overrun.Simulation
{
    /// <summary>
    /// The simulated body in the world. Server-authoritative: the server owns this object
    /// and drives it entirely from InputFrames submitted by the owning client. Clients
    /// never write position — they see the result through NetworkTransform.
    ///
    /// Note this does NOT reference Overrun.Net. InputFrame lives in Overrun.Core precisely
    /// so Simulation can consume player intent without depending on the networking layer,
    /// which references Simulation. See Docs/ARCHITECTURE.md §6.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerPawn : NetworkBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float _gravity = -22f;
        [SerializeField] private float _airControl = 0.35f;
        [SerializeField] private float _lookSensitivity = 0.12f;
        [SerializeField] private float _interactRange = 3.2f;

        [Header("Rig")]
        [Tooltip("Eye position the presentation camera follows. Authoritative aim origin.")]
        [SerializeField] private Transform _head;

        [Header("Loadout")]
        [SerializeField] private WeaponRuntime _weapon;
        [SerializeField] private Overrun.Data.WeaponDefinition _startingWeapon;

        private CharacterController _controller;
        private RunContext _run;
        private PlayerState _state;
        private Health _health;
        private Vector3 _velocity;
        private float _yaw;
        private float _pitch;

        /// <summary>
        /// Shared with PlayerState.Stats after ServerInitialise. Movement, weapon and
        /// augments must mutate the same block or a pick would apply to only one of them.
        /// </summary>
        public StatBlock Stats { get; private set; } = new StatBlock();

        /// <summary>Assigned by the server at spawn. Never derived from ownership.</summary>
        public PlayerId Id { get; private set; } = PlayerId.None;

        public Transform Head => _head != null ? _head : transform;
        public bool IsGrounded => _controller != null && _controller.isGrounded;
        public float Yaw => _yaw;
        public float Pitch => _pitch;
        public Health Health => _health;
        public WeaponRuntime Weapon => _weapon;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _health = GetComponent<Health>();
            _yaw = transform.eulerAngles.y;
            if (_weapon == null) _weapon = GetComponentInChildren<WeaponRuntime>();
        }

        /// <summary>
        /// Server-only. Binds this pawn to a player slot and arms it.
        /// Identity comes from the roster, never from NetworkObject ownership — two couch
        /// players share one owner, so ownership cannot tell them apart.
        /// </summary>
        public void ServerInitialise(PlayerId id, RunContext run, PlayerState state)
        {
            if (!IsServer) return;

            Id = id;
            _run = run;
            _state = state;
            if (state != null) Stats = state.Stats;

            if (_health != null)
            {
                _health.Died -= OnDied;
                _health.Configure(Stats.MaxHealth, Stats.Armor, true);
                _health.Died += OnDied;
            }

            if (_weapon != null && state != null)
            {
                _weapon.ServerInitialise(_startingWeapon, state.Stats, id, run);
            }
        }

        /// <summary>
        /// Server-only. Applies one fixed step of client intent.
        /// The client's frame is a *request*: the server decides what actually happens.
        /// </summary>
        public void Tick(InputFrame frame, float deltaTime)
        {
            if (!IsServer || _controller == null || deltaTime <= 0f) return;

            if (_run != null && _run.Phase == RunPhase.Ended)
            {
                ApplyLook(frame);
                return;
            }

            ApplyLook(frame);

            if (_state != null && (!_state.IsAlive || _health != null && _health.IsDead))
            {
                ApplyGravityOnly(deltaTime);
                return;
            }

            if (_run != null && _run.Phase == RunPhase.OfferingAugments)
            {
                ApplyGravityOnly(deltaTime);
                return;
            }

            // --- Horizontal intent, clamped so a crafted frame cannot exceed 1.0.
            Vector2 move = frame.Move;
            if (move.sqrMagnitude > 1f) move.Normalize();

            Vector3 wish = transform.right * move.x + transform.forward * move.y;

            float speed = Stats.MoveSpeed;
            if (frame.IsHeld(InputButton.Sprint) && move.y > 0.1f) speed *= Stats.SprintMultiplier;

            Vector3 horizontal = wish * speed;

            if (_controller.isGrounded)
            {
                if (_velocity.y < 0f) _velocity.y = -2f;   // keep it pinned to the ground

                if (frame.WasPressed(InputButton.Jump))
                {
                    _velocity.y = Mathf.Sqrt(-2f * _gravity * Mathf.Max(0.01f, Stats.JumpHeight));
                }

                _velocity.x = horizontal.x;
                _velocity.z = horizontal.z;
            }
            else
            {
                // Partial air control: steer, don't teleport.
                _velocity.x = Mathf.Lerp(_velocity.x, horizontal.x, _airControl * deltaTime * 10f);
                _velocity.z = Mathf.Lerp(_velocity.z, horizontal.z, _airControl * deltaTime * 10f);
            }

            _velocity.y += _gravity * deltaTime;

            _controller.Move(_velocity * deltaTime);

            if (frame.WasPressed(InputButton.Interact)) TryInteract();

            // Weapon resolution runs on the same authoritative step as movement, so a shot
            // is always traced from where the server thinks the player is looking.
            if (_weapon != null) _weapon.ServerTick(frame, Head, Time.time, deltaTime);
        }

        private void ApplyLook(InputFrame frame)
        {
            _yaw += frame.LookDelta.x * _lookSensitivity;
            _yaw = Mathf.Repeat(_yaw, 360f);
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);

            // Pitch lives on the Head so hitscan aims where the player is looking.
            // Presentation also applies LookDelta locally for the same frame; on a host
            // those two stay in lockstep. See PlayerCameraRig.
            _pitch = Mathf.Clamp(_pitch - frame.LookDelta.y * _lookSensitivity, -85f, 85f);
            if (_head != null) _head.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private void ApplyGravityOnly(float deltaTime)
        {
            if (_controller.isGrounded && _velocity.y < 0f) _velocity.y = -2f;
            _velocity.x = 0f;
            _velocity.z = 0f;
            _velocity.y += _gravity * deltaTime;
            _controller.Move(_velocity * deltaTime);
        }

        private void TryInteract()
        {
            if (_state == null || _run == null) return;

            Vector3 origin = Head.position;
            Vector3 direction = Head.forward;
            if (!Physics.Raycast(origin, direction, out RaycastHit hit, _interactRange, ~0,
                                 QueryTriggerInteraction.Ignore))
                return;

            var interactable = hit.collider.GetComponentInParent<IInteractable>();
            if (interactable == null || !interactable.IsAvailable) return;
            if (hit.distance > interactable.InteractRange) return;

            interactable.TryInteract(_state);
        }

        public void Teleport(Vector3 position, float yaw)
        {
            if (!IsServer) return;

            bool wasEnabled = _controller.enabled;
            _controller.enabled = false;          // CharacterController fights direct writes
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            _controller.enabled = wasEnabled;

            _yaw = yaw;
            _pitch = 0f;
            _velocity = Vector3.zero;
            if (_head != null) _head.localRotation = Quaternion.identity;
        }

        public void ServerResetLoadout()
        {
            if (!IsServer) return;
            if (_health != null) _health.ResetHealth(Stats.MaxHealth);
            if (_weapon != null) _weapon.ServerResetAmmo();
        }

        private void OnDied(PlayerId killer, Health victim, DamageContext killingBlow)
        {
            if (!IsServer || _run == null) return;
            _run.NotifyPlayerDied(Id);
        }

        public override void OnNetworkDespawn()
        {
            if (_health != null) _health.Died -= OnDied;
            base.OnNetworkDespawn();
        }
    }
}
