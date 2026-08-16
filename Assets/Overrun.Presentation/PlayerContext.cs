using UnityEngine;
using UnityEngine.InputSystem;

namespace Overrun.Presentation
{
    /// <summary>
    /// Everything one LOCAL player owns on this machine. One per local player; remote
    /// players have no PlayerContext. See Docs/ARCHITECTURE.md §5.
    ///
    /// Gameplay must read input through this context's PlayerInput, never through the
    /// legacy UnityEngine.Input class — that merges every connected device, so on a couch
    /// with two gamepads both players would move when either stick moves (ADR-007).
    /// </summary>
    public sealed class PlayerContext : MonoBehaviour
    {
        [SerializeField] private Camera _camera;
        [SerializeField] private Canvas _hud;
        [SerializeField] private Transform _interactionOrigin;
        [SerializeField] private PlayerInput _input;

        /// <summary>Index of this player on this machine (0..3). Not a network identity.</summary>
        public byte LocalSlot { get; private set; }

        public Camera Camera => _camera;
        public Canvas Hud => _hud;
        public Transform InteractionOrigin => _interactionOrigin;
        public PlayerInput Input => _input;

        public void AssignSlot(byte slot) => LocalSlot = slot;

        public void Bind(PlayerInput input) => _input = input;
    }
}
