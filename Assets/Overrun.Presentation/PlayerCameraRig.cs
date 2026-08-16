using UnityEngine;
using UnityEngine.InputSystem;
using Overrun.Simulation;

namespace Overrun.Presentation
{
    /// <summary>
    /// One local player's view. Pure presentation: it follows the pawn, it never moves it.
    ///
    /// Pitch is local-only and never reaches the simulation — the server has no use for
    /// where you are looking vertically, and keeping it client-side means vertical aim is
    /// always frame-instant regardless of latency. Yaw is mirrored locally for the same
    /// reason, then reconciled from the authoritative pawn rotation.
    /// </summary>
    public sealed class PlayerCameraRig : MonoBehaviour
    {
        [SerializeField] private PlayerContext _context;
        [SerializeField] private Transform _cameraPivot;

        [SerializeField] private float _lookSensitivity = 0.12f;
        [SerializeField] private float _minPitch = -85f;
        [SerializeField] private float _maxPitch = 85f;

        private InputAction _look;
        private PlayerPawn _pawn;
        private float _pitch;
        private float _yaw;

        private void Awake()
        {
            if (_context == null) _context = GetComponentInParent<PlayerContext>();
            if (_cameraPivot == null && _context != null && _context.Camera != null)
                _cameraPivot = _context.Camera.transform;
        }

        private void OnEnable()
        {
            if (_context != null && _context.Input != null && _context.Input.actions != null)
                _look = _context.Input.actions.FindAction("Look", false);
        }

        /// <summary>Called when the server confirms which pawn this local player drives.</summary>
        public void Follow(PlayerPawn pawn)
        {
            _pawn = pawn;
            if (pawn != null) _yaw = pawn.Yaw;
        }

        private void LateUpdate()
        {
            if (_cameraPivot == null) return;

            Vector2 delta = _look != null ? _look.ReadValue<Vector2>() : Vector2.zero;

            _pitch = Mathf.Clamp(_pitch - delta.y * _lookSensitivity, _minPitch, _maxPitch);
            _yaw += delta.x * _lookSensitivity;

            if (_pawn != null)
            {
                // Snap to authoritative yaw when it diverges enough to matter. On a host
                // this is a no-op; over a network it is the reconciliation step.
                float authoritative = _pawn.Yaw;
                if (Mathf.Abs(Mathf.DeltaAngle(_yaw, authoritative)) > 10f) _yaw = authoritative;

                _cameraPivot.position = _pawn.Head.position;
            }

            _cameraPivot.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }
    }
}
