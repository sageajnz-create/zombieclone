using UnityEngine;
using UnityEngine.InputSystem;
using Overrun.Core;
using Overrun.Net;

namespace Overrun.Presentation
{
    /// <summary>
    /// Samples ONE local player's devices and submits the result as intent.
    ///
    /// Reads only through its own PlayerInput — never the legacy UnityEngine.Input, which
    /// merges every connected device and would make both couch players move from one stick
    /// (ADR-007).
    ///
    /// The submission goes through the server RPC even when this machine IS the server.
    /// That is deliberate: local play and networked play exercise the same path, so we
    /// cannot accidentally build a single-player input flow that has to be rewritten for
    /// multiplayer (Docs/ARCHITECTURE.md §6).
    /// </summary>
    public sealed class LocalInputRouter : MonoBehaviour
    {
        [SerializeField] private PlayerContext _context;

        private NetSession _session;

        private InputAction _move, _look, _fire, _aim, _reload, _interact;
        private InputAction _jump, _sprint, _melee, _ability, _equipment;

        // Accumulated between fixed steps. Sampling edges in Update and consuming them in
        // FixedUpdate is what stops a tap landing between physics steps from being lost.
        private Vector2 _lookAccum;
        private uint _pressedAccum;
        private uint _tick;

        public void Bind(PlayerContext context, NetSession session)
        {
            _context = context;
            _session = session;
            CacheActions();
        }

        private void Awake()
        {
            if (_context == null) _context = GetComponentInParent<PlayerContext>();
        }

        private void OnEnable() => CacheActions();

        private void CacheActions()
        {
            PlayerInput input = _context != null ? _context.Input : null;
            if (input == null || input.actions == null) return;

            _move      = input.actions.FindAction("Move", false);
            _look      = input.actions.FindAction("Look", false);
            _fire      = input.actions.FindAction("Fire", false);
            _aim       = input.actions.FindAction("Aim", false);
            _reload    = input.actions.FindAction("Reload", false);
            _interact  = input.actions.FindAction("Interact", false);
            _jump      = input.actions.FindAction("Jump", false);
            _sprint    = input.actions.FindAction("Sprint", false);
            _melee     = input.actions.FindAction("Melee", false);
            _ability   = input.actions.FindAction("Ability", false);
            _equipment = input.actions.FindAction("Equipment", false);
        }

        private void Update()
        {
            if (_look != null) _lookAccum += _look.ReadValue<Vector2>();

            Accumulate(_fire,      InputButton.Fire);
            Accumulate(_aim,       InputButton.Aim);
            Accumulate(_reload,    InputButton.Reload);
            Accumulate(_interact,  InputButton.Interact);
            Accumulate(_jump,      InputButton.Jump);
            Accumulate(_sprint,    InputButton.Sprint);
            Accumulate(_melee,     InputButton.Melee);
            Accumulate(_ability,   InputButton.Ability);
            Accumulate(_equipment, InputButton.Equipment);
        }

        private void Accumulate(InputAction action, InputButton button)
        {
            if (action != null && action.WasPressedThisFrame()) _pressedAccum |= (uint)button;
        }

        private void FixedUpdate()
        {
            if (_session == null || _context == null) return;

            var frame = new InputFrame
            {
                LocalSlot = _context.LocalSlot,
                Move = _move != null ? _move.ReadValue<Vector2>() : Vector2.zero,
                LookDelta = _lookAccum,
                Held = ReadHeld(),
                Pressed = _pressedAccum,
                ClientTick = _tick++
            };

            _session.SubmitInputRpc(frame);

            _lookAccum = Vector2.zero;
            _pressedAccum = 0u;
        }

        private uint ReadHeld()
        {
            uint held = 0u;
            if (IsHeld(_fire))      held |= (uint)InputButton.Fire;
            if (IsHeld(_aim))       held |= (uint)InputButton.Aim;
            if (IsHeld(_reload))    held |= (uint)InputButton.Reload;
            if (IsHeld(_interact))  held |= (uint)InputButton.Interact;
            if (IsHeld(_jump))      held |= (uint)InputButton.Jump;
            if (IsHeld(_sprint))    held |= (uint)InputButton.Sprint;
            if (IsHeld(_melee))     held |= (uint)InputButton.Melee;
            if (IsHeld(_ability))   held |= (uint)InputButton.Ability;
            if (IsHeld(_equipment)) held |= (uint)InputButton.Equipment;
            return held;
        }

        private static bool IsHeld(InputAction action) => action != null && action.IsPressed();
    }
}
