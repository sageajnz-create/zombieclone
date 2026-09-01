using UnityEngine;
using Unity.Netcode;
using Overrun.Core;
using Overrun.Data;
using Overrun.Simulation;

namespace Overrun.Net
{
    /// <summary>
    /// Owns the run's session state and is the single translation point between Netcode
    /// connection identity (ClientId) and game identity (PlayerId).
    ///
    /// This is the ONLY layer permitted to know client ids exist. Everything downstream
    /// takes a PlayerId. See Docs/ARCHITECTURE.md Boundary B and Docs/NETWORKING.md §3.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetSession : NetworkBehaviour
    {
        public const int MaxPlayers = 4;

        [SerializeField] private GameObject _pawnPrefab;
        [SerializeField] private Transform[] _spawnPoints;
        [SerializeField] private AugmentDefinition[] _augments;

        /// <summary>
        /// Server-side truth for the whole run. Created here and injected into simulation
        /// systems; there is deliberately no static accessor.
        /// </summary>
        public RunContext Run { get; private set; }

        public PlayerRegistry Players => Run.Players;

        private void Awake()
        {
            // VS001 seeds from a fixed value so runs are reproducible while iterating.
            // Restart draws a new seed. The lobby-time seed lands in VS003 (NETWORKING.md §6).
            Run = new RunContext(new RunSeed(0xC0FFEEUL));
            BindAugmentPool();
        }

        /// <summary>Raised on a client when one of ITS local players receives a pawn.</summary>
        public event System.Action<byte, PlayerPawn> LocalPawnAssigned;

        /// <summary>
        /// Locate the session without FindObjectOfType, by asking Netcode's own spawn
        /// registry. NetworkManager.Singleton is a framework entry point rather than
        /// gameplay state, so it is outside the singleton ban (ARCHITECTURE §1); the
        /// session it returns is then *injected* into consumers rather than looked up
        /// repeatedly. Called once per local join.
        /// </summary>
        public static NetSession Find(NetworkManager manager)
        {
            if (manager == null || manager.SpawnManager == null) return null;

            foreach (var kv in manager.SpawnManager.SpawnedObjects)
            {
                if (kv.Value != null && kv.Value.TryGetComponent(out NetSession session)) return session;
            }
            return null;
        }

        public bool TryGetLocalPlayer(byte slot, out PlayerState state)
        {
            state = null;
            if (NetworkManager == null || Run == null) return false;
            var id = new PlayerId(NetworkManager.LocalClientId, slot);
            return Run.Players.TryGet(id, out state);
        }

        private void BindAugmentPool()
        {
            if (_augments != null && _augments.Length > 0)
            {
                Run.AugmentPool = _augments;
                return;
            }

            DefinitionCatalog catalog = DefinitionCatalog.Load();
            if (catalog != null && catalog.Augments != null && catalog.Augments.Length > 0)
            {
                Run.AugmentPool = catalog.Augments;
            }
        }

        // ------------------------------------------------------------- client -> server

        /// <summary>
        /// Ask the server to seat a local player in <paramref name="localSlot"/>.
        /// The slot is scoped to the sender's own connection, so a client can only ever
        /// add players to its own machine.
        /// </summary>
        [Rpc(SendTo.Server)]
        public void RequestJoinLocalPlayerRpc(byte localSlot, RpcParams rpc = default)
        {
            ulong clientId = rpc.Receive.SenderClientId;
            var id = new PlayerId(clientId, localSlot);

            if (localSlot >= MaxPlayers) return;
            if (Players.TryGet(id, out _)) return;                 // already seated
            if (Players.Count >= MaxPlayers) return;               // run is full

            PlayerState state = Players.Register(id);
            SpawnPawnFor(state);
        }

        /// <summary>
        /// One tick of intent. Stored, not acted on immediately — the server consumes it on
        /// its own fixed step so a client cannot drive the simulation faster by flooding.
        /// </summary>
        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
        public void SubmitInputRpc(NetInputFrame wire, RpcParams rpc = default)
        {
            InputFrame frame = wire;

            // PlayerId is ALWAYS rebuilt from the sender's own client id. Never trust a
            // client-supplied one — this is what closes the impersonation hole.
            var id = new PlayerId(rpc.Receive.SenderClientId, frame.LocalSlot);

            if (!Players.TryGet(id, out PlayerState state)) return;

            // Preserve edge-triggered presses that arrived since the last fixed step,
            // otherwise a jump landing between ticks is silently swallowed.
            uint carried = state.HasPendingInput ? state.PendingInput.Pressed : 0u;
            frame.Pressed |= carried;

            state.PendingInput = frame;
            state.HasPendingInput = true;
        }

        [Rpc(SendTo.Server)]
        public void RequestAugmentChoiceRpc(byte localSlot, byte offerIndex, RpcParams rpc = default)
        {
            var id = new PlayerId(rpc.Receive.SenderClientId, localSlot);
            Run.TryChooseAugment(id, offerIndex);
        }

        [Rpc(SendTo.Server)]
        public void RequestRestartRpc(byte localSlot, RpcParams rpc = default)
        {
            var id = new PlayerId(rpc.Receive.SenderClientId, localSlot);
            if (!Players.TryGet(id, out _)) return;
            if (Run.Phase != RunPhase.Ended) return;
            ServerRestart();
        }

        [Rpc(SendTo.Server)]
        public void RequestInteractRpc(byte localSlot, ulong targetNetworkObjectId, RpcParams rpc = default)
        {
            // VS001 interacts from the pawn's own Head raycast inside Tick. This RPC is the
            // documented surface for later client-hinted targeting; the server still owns
            // the outcome and ignores unknown targets.
            var id = new PlayerId(rpc.Receive.SenderClientId, localSlot);
            if (!Players.TryGet(id, out PlayerState state) || state.Pawn == null) return;
            if (targetNetworkObjectId == 0) return;
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject netObj))
                return;

            var interactable = netObj.GetComponent<IInteractable>();
            if (interactable == null || !interactable.IsAvailable) return;

            float range = interactable.InteractRange;
            if (Vector3.Distance(state.Pawn.Head.position, netObj.transform.position) > range + 0.5f) return;
            interactable.TryInteract(state);
        }

        // ------------------------------------------------------------- server -> client

        [Rpc(SendTo.SpecifiedInParams)]
        private void PawnAssignedRpc(byte localSlot, NetworkObjectReference pawnRef, RpcParams rpc)
        {
            if (!pawnRef.TryGet(out NetworkObject netObj)) return;

            var pawn = netObj.GetComponent<PlayerPawn>();
            if (pawn != null) LocalPawnAssigned?.Invoke(localSlot, pawn);
        }

        // -------------------------------------------------------------------- server sim

        private void FixedUpdate()
        {
            if (!IsServer) return;

            float dt = Time.fixedDeltaTime;
            Run.Procs.BeginTick();          // reset the per-tick effect budget
            var all = Players.All;

            for (int i = 0; i < all.Count; i++)
            {
                PlayerState state = all[i];
                if (state.Pawn == null) continue;

                if (state.HasPendingInput)
                {
                    InputFrame frame = state.PendingInput;

                    if (Run.Phase == RunPhase.Ended &&
                        (frame.WasPressed(InputButton.Jump) || frame.WasPressed(InputButton.Fire)))
                    {
                        ServerRestart();
                        return;
                    }

                    state.Pawn.Tick(frame, dt);

                    // Consume edges; hold state persists until the client says otherwise.
                    state.PendingInput.Pressed = 0u;
                    state.PendingInput.LookDelta = Vector2.zero;
                    state.HasPendingInput = false;
                }
                else
                {
                    // No packet this step: still integrate gravity, or a player standing
                    // still would hang in the air.
                    state.Pawn.Tick(default, dt);
                }
            }
        }

        public void ServerRestart()
        {
            if (!IsServer) return;

            ulong seed = (ulong)System.DateTime.UtcNow.Ticks;
            if (seed == 0UL) seed = 0xC0FFEEUL;
            Run.Reset(new RunSeed(seed));
            BindAugmentPool();

            var all = Players.All;
            for (int i = 0; i < all.Count; i++)
            {
                PlayerState state = all[i];
                if (state.Pawn == null) continue;
                GetSpawnPose(i, out Vector3 pos, out float yaw);
                state.Pawn.Teleport(pos, yaw);
                state.Pawn.ServerResetLoadout();
            }

            Debug.Log("[Overrun] Run restarted.");
        }

        // ------------------------------------------------------------------------ spawn

        private void SpawnPawnFor(PlayerState state)
        {
            if (!IsServer || _pawnPrefab == null) return;

            GetSpawnPose(Players.Count - 1, out Vector3 pos, out float yaw);

            GameObject go = Instantiate(_pawnPrefab, pos, Quaternion.Euler(0f, yaw, 0f));
            var netObj = go.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                Debug.LogError("[Overrun] Pawn prefab has no NetworkObject.", _pawnPrefab);
                Destroy(go);
                return;
            }

            netObj.Spawn(true);

            var pawn = go.GetComponent<PlayerPawn>();
            pawn.ServerInitialise(state.Id, Run, state);
            state.Pawn = pawn;

            go.name = $"Pawn_{state.Id.ClientId}_{state.Id.LocalSlot}";

            PawnAssignedRpc(state.Id.LocalSlot,
                            new NetworkObjectReference(netObj),
                            RpcTarget.Single(state.Id.ClientId, RpcTargetUse.Temp));
        }

        /// <summary>
        /// Registered by the arena at runtime (see ArenaSpawnRegistrar). Not an inspector
        /// field on this component, because the arena is in a different scene and Unity
        /// cannot serialise cross-scene references.
        /// </summary>
        public void SetSpawnPoints(Transform[] points)
        {
            if (points == null || points.Length == 0) return;
            _spawnPoints = points;
        }

        public void SetAugmentPool(AugmentDefinition[] augments)
        {
            _augments = augments;
            if (Run != null) Run.AugmentPool = augments;
        }

        private void GetSpawnPose(int index, out Vector3 position, out float yaw)
        {
            if (_spawnPoints != null && _spawnPoints.Length > 0)
            {
                Transform t = _spawnPoints[Mathf.Clamp(index, 0, _spawnPoints.Length - 1)];
                position = t.position;
                yaw = t.eulerAngles.y;
                return;
            }

            // Fallback so the slice is playable before spawn points are placed.
            position = new Vector3(index * 2f, 1.2f, 0f);
            yaw = 0f;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) Players.UnregisterClient(NetworkManager.LocalClientId);
            base.OnNetworkDespawn();
        }
    }
}
