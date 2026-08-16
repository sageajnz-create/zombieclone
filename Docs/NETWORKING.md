# Networking

**Netcode for GameObjects 2.x** over **Unity Transport**. **Host / listen-server**
topology: the host is also a player, and is the sole authority.

Read [`ARCHITECTURE.md`](ARCHITECTURE.md) §1 first — Boundary B (client ≠ player) is the
premise for everything here.

> **API note.** `[ServerRpc]`, `[ClientRpc]`, and `RequireOwnership` are **deprecated** in
> NGO 2.x. Most tutorials and Stack Overflow answers still use them. This project uses the
> unified `[Rpc(SendTo.…)]` attribute with `InvokePermission` throughout. Do not copy the
> old form.

---

## 1. Topology

```
        ┌──────────────────────────────────────┐
        │  HOST  (client id 0)                 │
        │  authority for all simulation state  │
        │  also runs 1..4 local players        │
        └──────────────────────────────────────┘
              ▲ intent            │ state
              │                   ▼
   ┌──────────────────┐   ┌──────────────────┐
   │ CLIENT id 1      │   │ CLIENT id 2      │
   │ 2 local players  │   │ 1 local player   │
   └──────────────────┘   └──────────────────┘
```

Started with `NetworkManager.Singleton.StartHost()`. Up to 4 players total across any
distribution of machines. The host having local players is not a special case — it runs the
same rig code as anyone else, it just skips the network hop for its own intent.

Compare against `NetworkManager.ServerClientId` rather than hardcoding `0`; the value is a
transport constant, not a guarantee.

A dedicated headless server is a supported future mode rather than a rewrite: because
`WaveDirector` and all simulation are already server-gated and presentation is a separate
scene, a dedicated server is the same build launched in batchmode with zero local players.
Not a milestone target. See [`DECISIONS.md`](DECISIONS.md) ADR-004.

---

## 2. Authority model

**All simulation state is server-authoritative. Clients send intent, never state.**

The good news is that this is **NGO's default behaviour**, not a fight against it:

| Mechanism | Default | Our use |
| --- | --- | --- |
| `NetworkVariable<T>` | read Everyone, **write Server** | unchanged |
| `NetworkTransform.AuthorityMode` | **Server** | unchanged |
| `NetworkObject.Spawn()` | server-only | unchanged |

We do **not** hand ownership of player pawns to their clients. Ownership stays with the
server, for two reasons:

1. **It is what server-authoritative means.** Client-owned transforms let clients declare
   their own position, which makes movement cheats trivial.
2. **Split-screen breaks the alternative.** Two local players share a `ClientId`. Giving
   both pawns ownership of client `1` means client 1 owns two pawns with no in-band way to
   say which input drives which. NGO ownership has no concept of a local slot.

So ownership is never used to identify a player — `PlayerId` is
([`ARCHITECTURE.md`](ARCHITECTURE.md) §1). This decision predates the engine switch and
survived it unchanged; see [`DECISIONS.md`](DECISIONS.md) ADR-003 for the original
reasoning and ADR-017 for the stack it now runs on.

---

## 3. Client ↔ player mapping

`Overrun.Net` is the only place client ids appear.

```csharp
// Server-side, replicated to all clients on change.
sealed class PlayerRoster
{
    // client 0 -> [slot 0, slot 1]
    // client 1 -> [slot 0]
    IReadOnlyDictionary<ulong, IReadOnlyList<PlayerId>> ByClient { get; }
    IReadOnlyList<PlayerId> AllPlayers { get; }
}
```

Player pawns are ordinary `NetworkObject`s registered here. **NGO's
`ConnectedClients[id].PlayerObject` is unused** — it models one player per connection and
cannot represent a couch pair.

Lifecycle:

| Event | Server action |
| --- | --- |
| Client connects | validate build hash + roster capacity via connection approval |
| Client requests N local slots | check total ≤ 4, create `PlayerId(clientId, slot)` per slot, spawn `PlayerState` + `PlayerPawn`, broadcast roster |
| Local player joins mid-session | same path — adding a couch player is a roster delta, not a reconnect |
| Client disconnects | despawn *all* that client's players, broadcast roster |
| Host disconnects | session ends (no host migration; see ADR-005) |

Note the third row. Because a machine can add a local player at any time, roster changes are
deltas rather than a fixed lobby snapshot. Designing for that now avoids a rewrite when
couch drop-in lands in VS002.

---

## 4. The wire

### Client → Server: intent only

```csharp
[Rpc(SendTo.Server)]
void SubmitInputRpc(InputFrame frame, RpcParams rpc = default)
{
    var playerId = new PlayerId(rpc.Receive.SenderClientId, frame.LocalSlot);
    if (!_roster.Owns(rpc.Receive.SenderClientId, frame.LocalSlot)) return;
    _simulation.EnqueueInput(playerId, frame);
}

[Rpc(SendTo.Server)]
void RequestInteractRpc(byte localSlot, ulong targetNetworkObjectId, RpcParams rpc = default);

[Rpc(SendTo.Server)]
void RequestAugmentChoiceRpc(byte localSlot, byte offerIndex, RpcParams rpc = default);
```

`SendTo.Server` defaults to `InvokePermission.Everyone`, which is what we want: these live
on server-owned objects, so an owner-only permission would block every client.

Every client→server RPC carries `localSlot`, and **the server always reconstructs
`PlayerId` from `RpcParams.Receive.SenderClientId` plus that slot** — never from a
client-supplied client id. A client can therefore only address slots on its own connection,
and the server validates that the slot exists in the roster for that client. That closes
the obvious spoofing hole for free.

Nothing a client sends is trusted. `RequestInteractRpc` names a target; the server
re-validates range, line of sight, cost, and fixture state against its own world before
acting. A client raycast decides *what the player is pointing at*, never *what happens*.

### Server → Client: state and events

Two mechanisms, chosen per data type:

**Continuous state → `NetworkVariable<T>` and `NetworkTransform`.** Pawn and enemy
transforms, health, ammo, scrip. Server-write is the default; leave it that way.

**Discrete events → broadcast RPC.** Damage numbers, kills, purchases, round transitions,
augment offers, boss phase changes. These drive presentation and must not be lost.

```csharp
[Rpc(SendTo.ClientsAndHost)]
void OnEnemyKilledRpc(ulong enemyNetworkObjectId, PlayerId killer, uint scripAwarded);
```

`SendTo.ClientsAndHost` rather than `SendTo.NotServer` matters: the host must run the same
presentation path as clients, or the host gets a different game feel and we ship two
codebases by accident.

For a message aimed at one player — an augment offer, a personal pickup — use
`SendTo.SpecifiedInParams` with `RpcTarget.Single`, addressing that player's **client id**
and letting the payload carry the `LocalSlot`.

### Spawning

`NetworkObject.Spawn()` server-side, with a `NetworkObjectPool` for enemies — spawning
dozens per wave without pooling will produce GC spikes on the server, which is the one
machine that cannot afford them.

Network prefabs must be **pre-registered and identical on every client**; a prefab-hash
mismatch fails the connection. Enemy variety therefore comes from a small registered prefab
set configured at spawn by definition id, per [`ARCHITECTURE.md`](ARCHITECTURE.md) §8.

### Bandwidth budget

Target ≤ 64 kbit/s down per client at 4 players with ~40 active enemies. Enemy transforms
dominate. Mitigations if exceeded: lower `NetworkTransform` send rate with client-side
interpolation, and relevancy culling by distance. Measure at VS003; do not pre-optimise.

---

## 5. Latency handling — staged, not deferred

Nothing here ships in VS001, but the shape is fixed now so it doesn't require restructuring.

| Concern | Plan | Milestone |
| --- | --- | --- |
| Remote entity motion | interpolate on a render delay (~100 ms) | VS003 |
| Own movement | client-side prediction + server reconciliation | VS004 |
| Hitscan fairness | server rewinds hitboxes to the shooter's view time | VS010 |
| Ability/fire response | fire local presentation immediately, reconcile on server confirm | VS004 |

The last row is the game-feel escape hatch and the reason Boundary A is drawn where it is.
Muzzle flash, sound, recoil, and shell eject fire on the client *instantly* on button press,
because they are presentation. The damage is server-side. The player experiences zero input
latency on the parts they can perceive, while the parts that matter stay authoritative. If a
shot is rejected, the client shows no hit marker — the shot still *felt* instant.

Hit markers are therefore server-confirmed, never client-predicted. A false-positive hit
marker is worse than a 60 ms late one.

**Lag compensation is the known-hard item**, and it is the weakest part of this stack. NGO
does not ship server-side hitbox rewind; we build it. Scheduled at VS010 because it needs a
stable hitbox history system, but hitscan resolution lives server-side from VS001 so adding
rewind is inserting a step, not relocating the logic. If prediction and lag compensation
prove more painful than budgeted, that is the trigger to revisit the netcode stack — see
ADR-017.

---

## 6. Session lifecycle

```
Boot → Main menu → [Host | Join] → Lobby (roster, device binding, ready)
     → Run start (server generates RunSeed, broadcasts) → Rounds
     → Run end (all dead | extract) → Results → Lobby
```

- The **server generates and owns `RunSeed`** and broadcasts it at run start. Clients use it
  only for cosmetic prediction, never for authoritative rolls.
- **Build-hash validation via NGO's connection approval callback.** Since definitions are
  resolved by id and never replicated (§4), a client with different content would silently
  desync. Reject mismatches at connect with a clear message. Cheap insurance, easy to
  forget.
- LAN discovery via UDP broadcast on a fixed port.
- Online play in VS010 needs NAT traversal. Unity Relay is the first-party answer and
  integrates with Unity Transport, at the cost of a hosted-service dependency. Unresolved;
  see ADR-012.

---

## 7. Rules

1. Simulation state changes only on the server.
2. No gameplay system takes a `ClientId`. Only `Overrun.Net` translates.
3. Client→server messages are *requests*. The server validates every one.
4. The server derives `PlayerId` from `RpcParams.Receive.SenderClientId`; never from a
   client-supplied id.
5. Presentation may read simulation state and subscribe to events. It may not write.
6. Definitions are resolved by id on both ends. Never serialise a `ScriptableObject` to the
   wire.
7. Randomness comes from the server's seeded streams. Clients never roll authoritative
   outcomes.
8. `SendTo.ClientsAndHost` for presentation events, so the host plays the same game.
9. Use `[Rpc(SendTo.…)]`. `[ServerRpc]` / `[ClientRpc]` / `RequireOwnership` are deprecated.
10. If a system cannot be expressed as "server simulates, client displays," it is a design
    bug — raise it before writing it.
