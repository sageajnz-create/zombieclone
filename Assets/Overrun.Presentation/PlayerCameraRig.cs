using UnityEngine;
using Overrun.Simulation;

namespace Overrun.Presentation
{
    /// <summary>
    /// One local player's view. Pure presentation: it follows the pawn, it never moves it.
    ///
    /// Pitch is applied on the pawn Head from the same InputFrame the camera used to
    /// sample, so hitscan and view stay aligned on the host. Recoil is read from the
    /// weapon and added on top — presentation may not write simulation recoil.
    /// </summary>
    public sealed class PlayerCameraRig : MonoBehaviour
    {
        [SerializeField] private PlayerContext _context;
        [SerializeField] private Transform _cameraPivot;

        private PlayerPawn _pawn;

        private void Awake()
        {
            if (_context == null) _context = GetComponentInParent<PlayerContext>();
            if (_cameraPivot == null && _context != null && _context.Camera != null)
                _cameraPivot = _context.Camera.transform;
        }

        /// <summary>Called when the server confirms which pawn this local player drives.</summary>
        public void Follow(PlayerPawn pawn)
        {
            _pawn = pawn;
        }

        private void LateUpdate()
        {
            if (_cameraPivot == null) return;

            if (_pawn != null)
            {
                _cameraPivot.position = _pawn.Head.position;

                Quaternion recoil = Quaternion.identity;
                WeaponRuntime weapon = _pawn.Weapon;
                if (weapon != null)
                {
                    Vector2 kick = weapon.RecoilEuler;
                    recoil = Quaternion.Euler(-kick.x, kick.y, 0f);
                }

                _cameraPivot.rotation = _pawn.Head.rotation * recoil;
                return;
            }
        }
    }
}
