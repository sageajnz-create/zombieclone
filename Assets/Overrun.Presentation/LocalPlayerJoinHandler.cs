using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Overrun.Net;
using Overrun.Simulation;

namespace Overrun.Presentation
{
    /// <summary>
    /// Receives join/leave callbacks from Unity's PlayerInputManager, seats each new local
    /// player, and wires its rig to the session.
    ///
    /// Named LocalPlayerJoinHandler rather than PlayerInputManager on purpose: the Input
    /// System already ships a PlayerInputManager component, and shadowing that name inside
    /// a file that also does `using UnityEngine.InputSystem` makes every reference
    /// ambiguous to read and to resolve.
    ///
    /// Subscribes to PlayerInputManager's C# events directly rather than through inspector
    /// UnityEvents — one less piece of scene wiring that can silently come unhooked.
    /// </summary>
    [RequireComponent(typeof(PlayerInputManager))]
    public sealed class LocalPlayerJoinHandler : MonoBehaviour
    {
        [SerializeField] private LocalPlayers _localPlayers;

        private NetSession _session;
        private PlayerInputManager _manager;

        private void Awake()
        {
            if (_localPlayers == null) _localPlayers = GetComponentInParent<LocalPlayers>();
            _manager = GetComponent<PlayerInputManager>();
        }

        private void OnEnable()
        {
            if (_manager == null) return;
            _manager.onPlayerJoined += OnPlayerJoined;
            _manager.onPlayerLeft += OnPlayerLeft;
        }

        private void OnDisable()
        {
            if (_manager == null) return;
            _manager.onPlayerJoined -= OnPlayerJoined;
            _manager.onPlayerLeft -= OnPlayerLeft;
        }

        private NetSession Session =>
            _session != null ? _session : (_session = NetSession.Find(NetworkManager.Singleton));

        public void OnPlayerJoined(PlayerInput playerInput)
        {
            if (playerInput == null) return;

            if (_localPlayers == null)
            {
                Debug.LogError("[Overrun] LocalPlayerJoinHandler has no LocalPlayers reference.", this);
                return;
            }

            var context = playerInput.GetComponentInChildren<PlayerContext>();
            if (context == null)
            {
                Debug.LogError($"[Overrun] Joined PlayerInput '{playerInput.name}' has no PlayerContext.", playerInput);
                return;
            }

            context.Bind(playerInput);

            int slot = _localPlayers.Add(context);
            if (slot < 0)
            {
                Debug.LogWarning($"[Overrun] Rejected local join: already at {LocalPlayers.MaxLocalPlayers} local players.");
                Destroy(playerInput.gameObject);
                return;
            }

            NetSession session = Session;
            if (session == null)
            {
                Debug.LogError("[Overrun] No NetSession spawned — start the host before joining players.", this);
                return;
            }

            // Inject the session rather than letting the rig look it up.
            var router = playerInput.GetComponentInChildren<LocalInputRouter>();
            if (router != null) router.Bind(context, session);

            session.LocalPawnAssigned -= OnLocalPawnAssigned;
            session.LocalPawnAssigned += OnLocalPawnAssigned;

            // Ask the server to seat this slot. Even as host this goes through the RPC.
            session.RequestJoinLocalPlayerRpc((byte)slot);

            Debug.Log($"[Overrun] Local player joined slot {slot} on: {string.Join(", ", playerInput.devices)}");
        }

        public void OnPlayerLeft(PlayerInput playerInput)
        {
            if (playerInput == null || _localPlayers == null) return;

            var context = playerInput.GetComponentInChildren<PlayerContext>();
            if (context != null) _localPlayers.Remove(context);
        }

        private void OnLocalPawnAssigned(byte localSlot, PlayerPawn pawn)
        {
            PlayerContext context = _localPlayers != null ? _localPlayers.GetBySlot(localSlot) : null;
            if (context == null) return;

            var rig = context.GetComponentInChildren<PlayerCameraRig>();
            if (rig != null) rig.Follow(pawn);
        }

        private void OnDestroy()
        {
            if (_session != null) _session.LocalPawnAssigned -= OnLocalPawnAssigned;
        }
    }
}
