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

        [Header("Rig")]
        [Tooltip("Eye position the presentation camera follows. Not authoritative.")]
        [SerializeField] private Transform _head;

        [Header("Loadout")]
        [SerializeField] private WeaponRuntime _weapon;
        [SerializeField] private Overrun.Data.WeaponDefinition _startingWeapon;

        private CharacterController _controller;
        private RunContext _run;
        private Health _health;
        private Vector3 _velocity;
        private float _yaw;

        public StatBlock Stats { get; } = new StatBlock();

        /// <summary>Assigned by the server at spawn. Never derived from ownership.</summary>
        public PlayerId Id { get; private set; } = PlayerId.None;

        public Transform Head => _head != null ? _head : transform;
        public bool IsGrounded => _controller != null && _controller.isGrounded;
        public float Yaw => _yaw;

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

            if (_health != null)
            {
                _health.Configure(Stats.MaxHealth, Stats.Armor, true);
            }

            if (_weapon != null && state != null)
            {
                // The pawn's stats and the roster's stats must be the same object, or
                // augments picked between rounds would apply to only one of them.
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

            // --- Yaw. Pitch is presentation-only and never reaches the simulation.
            _yaw += frame.LookDelta.x * _lookSensitivity;
            _yaw = Mathf.Repeat(_yaw, 360f);
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);

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

            // Weapon resolution runs on the same authoritative step as movement, so a shot
            // is always traced from where the server thinks the player is.
            if (_weapon != null) _weapon.ServerTick(frame, Head, Time.time);
        }

        public void Teleport(Vector3 position, float yaw)
        {
            if (!IsServer) return;

            bool wasEnabled = _controller.enabled;
            _controller.enabled = false;          // CharacterController fights direct writes
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            _controller.enabled = wasEnabled;

            _yaw = yaw;
            _velocity = Vector3.zero;
        }
    }
}
