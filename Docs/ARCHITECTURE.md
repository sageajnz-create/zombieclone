# Architecture

Engine: **Unity 6000.5**, render pipeline **URP**, language **C#**.

This document defines structural boundaries. Gameplay content design lives in
[`GAMEPLAY_SYSTEMS.md`](GAMEPLAY_SYSTEMS.md); the wire protocol lives in
[`NETWORKING.md`](NETWORKING.md).

---

## 1. The two hard boundaries

Everything else in this document follows from two rules.

### Boundary A — Simulation vs. Presentation

```
┌─────────────────────────────────────────────┐
│ SIMULATION            authoritative on server│
│ health, damage, spawning, waves, currency,   │
│ purchases, drops, interactions, revives,     │
│ death, augment acquisition, boss state, seed │
└─────────────────────────────────────────────┘
                     │ replication (one direction)
                     ▼
┌─────────────────────────────────────────────┐
│ PRESENTATION            local, per-player    │
│ camera, view bob, shake, sway, crosshair,    │
│ hit markers, HUD, audio, rumble, VFX, gore   │
└─────────────────────────────────────────────┘
```

**Presentation never writes simulation state.** It reads replicated state and reacts to
replicated events. This is what lets the feel work in Pillar 5 be pushed aggressively
without touching network correctness.

Unlike a folder convention, this boundary is **compiler-enforced** — see §2.

### Boundary B — Client is not Player

This is the single most important decision in the project.

```
ClientId   one machine / one network connection.  ulong. Host is 0.
LocalSlot  index of a player on one machine.      0..3.
PlayerId   (ClientId, LocalSlot)                  globally unique player handle.
```

Netcode for GameObjects identifies **connections**, not players.
`NetworkManager.LocalClientId`, `NetworkObject.OwnerClientId`, and
`NetworkManager.ConnectedClients[id].PlayerObject` are all per-connection — and that last
one is explicitly *singular*: NGO models one player object per client.

Two players on one couch therefore share a `ClientId`. Any system that treats client id as
player id silently breaks the moment split-screen exists, and it breaks in ways that look
like desyncs rather than like the actual bug.

**Therefore: no gameplay system ever takes a client id.** Systems take a `PlayerId` or a
`PlayerContext`. Client ids appear only inside `Overrun.Net`.

```csharp
public readonly struct PlayerId : IEquatable<PlayerId>
{
    public readonly ulong ClientId;
    public readonly byte  LocalSlot;

    public static PlayerId None => new PlayerId(ulong.MaxValue, 0);
    public bool IsValid => ClientId != ulong.MaxValue;
    // ... Equals / GetHashCode / == / != written out by hand
}
```

> **Not a `record struct`.** Unity 6000.5 compiles at **C# 9**. Record structs are C# 10,
> and `init` accessors need `System.Runtime.CompilerServices.IsExternalInit`, which
> netstandard2.1 does not provide. The compiler rejects both outright — see ADR-021.

Because NGO's built-in `PlayerObject` is single-per-client, **we do not use it.** Player
pawns are ordinary `NetworkObject`s tracked by our own roster (see
[`NETWORKING.md`](NETWORKING.md) §3). Reaching for `PlayerObject` anywhere is a bug.

**Banned patterns:**

| Banned | Why | Use instead |
| --- | --- | --- |
| `Camera.main` | returns *a* camera, not *this player's* | `playerContext.Camera` |
| `FindObjectOfType` / `FindFirstObjectByType` | assumes one of a thing | `PlayerRegistry.Get(playerId)` |
| `GameObject.Find` | stringly-typed global lookup | inject the reference |
| Static/singleton player state | one global player | `PlayerRegistry` |
| `Input.GetAxis` / `Input.GetKey` | legacy Input Manager merges all devices | `PlayerInput` action callbacks |
| A single global HUD `Canvas` | one screen | per-player Canvas on the rig |
| `ConnectedClients[id].PlayerObject` | one player per client | own roster lookup |

`Input.GetAxis` is the sharpest of these. Unity's legacy Input Manager merges every
connected device, so on a couch with two gamepads both players move when either one pushes
a stick. Device routing is mandatory, not a polish task — see §6.

**One clarification so the singleton ban is not misapplied:** `NetworkManager.Singleton` is
a *framework entry point*, not game state, and is fine to reference from `Overrun.Net`. The
ban is on singletons holding **gameplay or player state**.

---

## 2. Layers, enforced by Assembly Definitions

```
Overrun.Core/          PlayerId, RunSeed, StatBlock, TagMask, event bus primitives.
Overrun.Data/          ScriptableObject definitions. Pure data, no behaviour.
Overrun.Simulation/    Server-authoritative gameplay.
Overrun.Net/           Session lifecycle, client↔player mapping, replication, RPC surface.
                       The only assembly that knows client ids exist.
Overrun.Presentation/  Per-local-player rigs, HUD, VFX, audio, camera, feel.
```

Dependency direction is strictly downward:

```
Presentation ──▶ Net ──▶ Simulation ──▶ Data ──▶ Core
                                └──────────────▶ Core
```

Each folder carries an `.asmdef` listing **only** the assemblies it may reference.
`Overrun.Simulation.asmdef` does not reference `Overrun.Presentation`, so a stray
`using Overrun.Presentation;` in simulation code is a **compile error**, not something a
reviewer has to catch.

This is a genuine gain from the engine choice: Boundary A stops being a discipline problem
and becomes a build-time guarantee. Keeping the asmdef reference lists tight is therefore a
load-bearing maintenance task, not bookkeeping — **if someone widens one to "fix" a compile
error, the boundary is gone and nothing else will warn us.**

`Overrun.Core` references no other project assembly and no gameplay framework — no Netcode,
no Input System. It may use UnityEngine **value types** (`Vector2`, `Vector3`) and
`ScriptableObject`, but no scene types (`MonoBehaviour`, `GameObject`, `Component`). That
keeps it testable in EditMode without entering Play Mode.

---

## 3. Scene topology

Three scenes, loaded additively:

**`Bootstrap`** — persistent, loaded first.
```
NetworkManager            NGO + UnityTransport
NetSession                client lifecycle, player roster, RPC endpoints
AudioListenerRig          the ONLY AudioListener in the entire game (§4)
```

**`World`** — SIMULATION. Identical structure on every client.
```
Arena                     map instance: geometry, navmesh, spawn zones, fixtures
WaveDirector              server-only logic; inert on clients
EnemyRoot                 spawned enemies (pooled NetworkObjects)
PlayerPawns
  ├── Pawn_0_0            PlayerId(client 0, slot 0)
  ├── Pawn_0_1
  └── Pawn_7_0
RunState                  round, seed, budget, shared economy
```

**`LocalRigs`** — PRESENTATION. Differs per machine.
```
PlayerInputManager        Input System; split-screen enabled
Rig_Slot0
  ├── PlayerInput         bound to one device set
  ├── Camera              viewport rect assigned by PlayerInputManager
  └── HUD Canvas          Screen Space – Camera, this player's HUD only
Rig_Slot1
SharedUI                  pause, lobby, scoreboard
```

Key properties:

- **`World` is structurally identical on every client.** Only authority differs. The same
  code path runs everywhere and the server is not a special build.
- **Pawns live in the simulation scene, rigs live in the presentation scene.** A pawn does
  not own a camera. A rig *points at* a pawn. That decoupling makes spectating, hot-join,
  and death-cam trivial later: retarget the rig, don't restructure the world.
- **`WaveDirector` exists on clients but is inert.** It early-returns unless `IsServer`.
  Keeping the component present keeps scene structure identical.

### Split-screen

`PlayerInputManager` with split-screen enabled assigns each `PlayerInput`'s camera a
viewport `rect` automatically as players join, handling 1/2/3/4-player layouts. This is a
real convenience win — Unity's local-multiplayer support is the strongest part of this
engine for our requirements, and it is the piece that most directly serves the brief.

Each rig owns its own `Camera`, HUD `Canvas` (Screen Space – Camera, pointed at that rig's
camera), crosshair, and interaction raycast origin.

Cost note: 4-way split renders the world four times. Art and lighting budgets are set by
the 4-player case, not the solo case. See [`DECISIONS.md`](DECISIONS.md) ADR-008 and ADR-018.

---

## 4. Audio — the one place split-screen genuinely hurts

**Unity permits exactly one active `AudioListener`.** More than one produces a warning and
undefined spatialisation. There is no per-camera listener, and this cannot be solved by
attaching a listener per rig.

Strategy: a single `AudioListenerRig` in `Bootstrap`, positioned at the **centroid** of
living local players, with per-sound attenuation computed against the **nearest** local
player rather than against the listener transform. Directional panning is then approximate
for everyone, rather than correct for one player and wrong for the rest.

Consequences to design around:

- Positional audio cues are weaker in split-screen than in solo. **Do not make any mechanic
  depend on precisely localising a sound** when 2+ local players are active.
- Player-specific audio — your reload, your hit confirm, your ability — must be played
  **2D and non-spatialised**, routed to that player's mixer group, so it is unaffected.

This is an accepted downgrade rather than a solved problem. Validate the centroid approach
early in VS002. Recorded as ADR-019.

---

## 5. Player model

Three separate things, deliberately not merged:

**`PlayerContext`** — presentation-side, one per **local** player. Only exists on the
machine that player is sitting at.
> `PlayerInput`, camera, viewport rect, HUD canvas, crosshair, interaction targeting,
> mixer group, rumble target, local settings

**`PlayerState`** — simulation-side, one per player **in the run**, local or remote,
authoritative on the server.
> health, armor, downed/alive, scrip, weapon inventory, equipment, ability charge,
> augments, active status effects

**`PlayerPawn`** — the simulated body in the world: transform, collider, movement state.

A remote player has `PlayerState` + `PlayerPawn` on your machine but **no
`PlayerContext`** — you don't render their HUD or route their input. A local player has all
three. This split is what makes "2 local + 2 online" fall out of the architecture instead
of being a special case.

Lookup is always explicit:

```csharp
public sealed class PlayerRegistry            // Overrun.Simulation
{
    public PlayerState Get(PlayerId id);
    public IReadOnlyList<PlayerState> All { get; }
    public IReadOnlyList<PlayerState> AlivePlayers { get; }
}

public sealed class LocalPlayers               // Overrun.Presentation
{
    public IReadOnlyList<PlayerContext> Contexts { get; }   // 1..4, this machine only
}
```

---

## 6. Input

**Input System package**, one `PlayerInput` per local player.

Actions: `Move`, `Look`, `Fire`, `Aim`, `Reload`, `Interact`, `Jump`, `Sprint`, `Melee`,
`Ability`, `Equipment`, `Pause`, `Scoreboard`.

`PlayerInputManager` in **join-on-button-press** mode binds the pressing device to the next
free `LocalSlot` and instantiates that slot's rig. Device pairing, hot-plug, and
disconnection are handled by the package rather than by us.

Gameplay reads actions through its own `PlayerInput` only. The legacy `Input` class is
banned outright (§1).

The router's output is a plain struct, which is also exactly what gets sent to the server
(see [`NETWORKING.md`](NETWORKING.md) §4). It lives in `Overrun.Core` and has **no Netcode
dependency**:

```csharp
// Overrun.Core — unmanaged POD, no networking types.
public struct InputFrame
{
    public byte    LocalSlot;
    public Vector2 Move;        // normalised
    public Vector2 LookDelta;
    public uint    Held;        // button bitfield
    public uint    Pressed;
    public uint    ClientTick;
}
```

**Why it is not `INetworkSerializable`.** `Overrun.Simulation` consumes `InputFrame`
(`PlayerPawn.ProcessInput`), and `Overrun.Net` references `Overrun.Simulation`. Putting
`InputFrame` in `Overrun.Net` therefore forces `Simulation → Net`, producing a **circular
assembly reference that Unity rejects outright**. Implementing the Netcode interface in
`Overrun.Core` would instead drag networking into the layer that must reference nothing.

The resolution: `InputFrame` stays plain POD in `Core`, and `Overrun.Net` transmits it as
`ForceNetworkSerializeByMemcpy<InputFrame>` — the mechanism NGO provides specifically for
serialising structs the owning assembly cannot annotate. (Since NGO 1.0.0-pre.8, bare
unmanaged structs are no longer accepted as RPC parameters, so the wrapper is required, not
optional.)

This is a worked example of the asmdef boundary doing its job: the layering violation is a
build failure rather than a design smell someone might miss.

Making the input struct the network payload from day one is deliberate: local play and
networked play exercise the same path, so we cannot accidentally build a single-player
input flow that has to be rewritten for multiplayer.

---

## 7. Data-driven definitions

`ScriptableObject` subclasses, authored as `.asset`.

```
WeaponDefinition   damage, fire rate, magazine, reserve, reload, pellets, spread,
                   recoil, range, falloff, penetration, hitscan|projectile,
                   crit multiplier, element, rarity, attachment slots
EnemyDefinition    health, speed, armor, damage, archetype, abilities, resistances, budget cost
AugmentDefinition  rarity, tags, modifier list, prerequisites, exclusions
WaveProfile        budget curve, composition weights, special wave rules, boss rounds
ArenaDefinition    spawn zone groups, region graph, fixture placement
```

**Replication rule: definitions are never sent over the wire.** Content is identical on all
clients because they run the same build. The wire carries a definition **id** plus numeric
state. This keeps bandwidth flat as the content library grows, and it is why `Overrun.Data`
has no behaviour — anything that must run identically everywhere is code, not serialised
state.

Ids are stable `int` hashes assigned in a definition registry — not asset names, and not
`ScriptableObject` references. NGO cannot serialise an object reference across the wire, and
asset names change.

Definitions are immutable at runtime. Per-instance variation (a rolled weapon, an upgraded
weapon) is a separate `WeaponInstance` holding a definition id plus its rolled modifiers.

> **Never mutate a `ScriptableObject` at runtime.** In the Editor those mutations persist to
> disk, silently corrupting balance data in a way that does not reproduce in a build. This
> is the most common way a ScriptableObject-driven design goes wrong.

---

## 8. Composition over inheritance

Enemies, pawns, and fixtures are built from components rather than deep class trees:

```
Enemy (GameObject)
├── NetworkObject       identity + spawning
├── Health              damage intake, resistances, death
├── StatusReceiver      burning / frozen / shocked / poisoned stacks
├── NavAgent            NavMeshAgent wrapper
├── BrainX              one behaviour component per archetype
├── AttackX             melee / ranged / detonate
└── LootDropper         scrip and pickup rolls
```

`Health` and `StatusReceiver` are shared with `PlayerPawn` unchanged. The only thing that
differs between a basic enemy, a tank, and an exploder is which components are attached and
which `EnemyDefinition` feeds them — not a subclass per enemy type.

Because NGO requires network prefabs to be **pre-registered**, enemy variety comes from a
small set of registered prefabs configured at spawn by definition id, not one prefab per
enemy type. Same principle as §7, and it keeps the prefab list small.

Interfaces are used only where there is genuine polymorphism across unrelated types:
`IDamageable`, `IInteractable`, `IStatusReceiver`, `IModifierSource`. Not as a reflex.

---

## 9. Randomness and determinism

The server owns a 64-bit `RunSeed`. Randomness is drawn from **named independent streams**,
never a global RNG:

```csharp
var rng = runSeed.Stream(RngStream.AugmentOffers, round);
```

Each stream is a separately-seeded generator, so consuming augment offers cannot shift what
the loot table rolls.

**`UnityEngine.Random` is banned in gameplay code.** It is global mutable static state:
every consumer shares one sequence, so adding a particle effect that rolls a random value
silently changes the augment offers. Use owned `System.Random` instances or the project's
own stream type.

**Scope of determinism — be honest about this.** Seed determinism covers *content
selection*: augment offers, loot rolls, wave composition, shop inventory, event and modifier
choice. It does **not** cover physics or full lockstep simulation. PhysX, float behaviour,
and frame timing make bit-exact replay a non-goal. Replaying a seed reproduces the same
*run content*, not the same bullet trajectories. See ADR-006.

---

## 10. Meta vs. run state

| | Run progression | Account progression |
| --- | --- | --- |
| Lifetime | one run | permanent |
| Owner | server, in memory | each player, locally |
| Contents | augments, weapons, scrip, round | unlocked characters, starting loadouts, augment pool entries, artifacts, challenges, cosmetics |
| On death | discarded | retained |

Separate types in separate assemblies with no shared serialisation. Account data lives in
JSON under `Application.persistentDataPath`. A client's meta unlocks are *declared* to the
server at lobby time and validated against the run's ruleset; they never let a client inject
arbitrary power. Meta unlocks add options to pools — they do not raise numbers. See ADR-011.

---

## 11. Testing

`Overrun.Simulation` and `Overrun.Core` are written to run without a camera or canvas.
Damage pipeline, stat resolution, augment stacking, wave budgeting, and seed streams are
EditMode-testable — the practical payoff of Boundary A and the asmdef split.

Multi-client testing uses **Multiplayer Play Mode** where available, falling back to a built
player plus the Editor. At 15 GiB RAM, Editor plus two clients is tight; prefer a
batchmode server when testing pure simulation.
