# Architecture Decision Records

Append-only. When a decision changes, add a superseding ADR rather than editing history,
and update every document the change touches in the same commit.

Status values: **Accepted** · **Proposed** · **Superseded** · **Open**

> **ADR-001 through ADR-014 were written against Godot 4.7**, before the project switched to
> Unity on the same day. They are kept verbatim as history. Some are superseded outright;
> several hold unchanged because the decision was about *architecture*, not about the
> engine, and those are annotated in place. The engine switch is ADR-015.

---

## ADR-001 — Godot 4.7 as the engine

**Status:** ~~Accepted~~ **Superseded by ADR-015** · 2026-08-16

**Context.** The project brief was written entirely in Unity vocabulary (`Camera.main`,
`FindObjectOfType()`, ScriptableObjects, "Unity's modern input architecture"). The
development machine has none of it: no Unity, no Unity Hub, no Unreal. It has Godot 4.7.1
installed via pacman on 2026-08-13 — three days before project start, which reads as
deliberate.

**Decision.** Godot 4.7.1.

**Rationale.** Beyond it being what is installed, Godot is a better structural fit for the
two hardest requirements:

- **Split-screen.** `SubViewport` gives genuinely per-player cameras. The `Camera.main`
  hazard the brief warns about does not exist in Godot, because cameras are inherently
  viewport-scoped.
- **Mixed local + online.** Godot's per-node authority model composes with a
  server-authoritative design (ADR-003). Unity's Netcode for GameObjects assumes one
  client = one player, so split-screen means working against its ownership model.

Practical factors: 15 GiB RAM is tight for the Unity editor and prohibitive for an Unreal
source build on Linux; Godot's editor is light. Blender is installed and the glTF path is
clean.

**Consequences.** Every Unity-specific instruction in the brief needs translation, recorded
in ADR-002. Smaller asset ecosystem than Unity. Godot's FPS and networking tooling is less
mature than Unreal's, so more is hand-built — accepted, and the reason this planning pass
is unusually detailed about the network layer.

> **Superseded.** The user elected to switch to Unity and installed 6000.5. The concerns
> raised here did not evaporate — the split-screen audio point in particular turned out
> *worse* under Unity (ADR-019). See ADR-015 for what was traded and why it is workable.

---

## ADR-002 — Unity concepts, translated

**Status:** ~~Accepted~~ **Superseded by ADR-016** · 2026-08-16 · followed ADR-001

| Brief (Unity) | This project (Godot) |
| --- | --- |
| ScriptableObject | `Resource` subclass, `.tres` |
| `Camera.main` (banned) | `PlayerContext.Camera` |
| `FindObjectOfType()` (banned) | explicit registry lookup / injection |
| Prefab | `PackedScene` |
| MonoBehaviour | `Node` subclass |
| New Input System | `InputMap` + per-device routing (ADR-007) |
| NetworkBehaviour | `MultiplayerSynchronizer` + `[Rpc]` |
| Assembly definitions | C# namespaces + folder discipline |

The *intent* behind each ban carries over exactly: no global "the player," no implicit
singleton lookups, no system that assumes one local player.

> **Superseded.** This table now runs in reverse and is mostly moot — the brief's original
> Unity vocabulary applies directly. The right-hand column is what needed translating.

---

## ADR-003 — Full server authority; clients send intent

**Status:** **Accepted** · 2026-08-16 · *survives the engine switch unchanged*

**Context.** The common Godot pattern is `pawn.SetMultiplayerAuthority(peerId)`, giving each
client ownership of its own pawn.

**Decision.** All simulation nodes stay at authority 1 (server). Clients send `InputFrame`
and request messages; the server simulates and replicates results.

**Rationale.** The tutorial pattern fails twice here. It is not server-authoritative — the
brief requires that it is. And it breaks under split-screen: two local players share a
`PeerId`, so authority cannot distinguish them. Godot's authority system has no concept of
a local slot.

Inverting to intent-based input makes split-screen free: a machine with two local players
sends two input streams tagged with different `LocalSlot` values, and nothing else changes.

**Consequences.** Client-side prediction becomes necessary for movement feel (VS004) rather
than optional. Lag compensation becomes necessary for hitscan fairness (VS010). Both are
scheduled, and the resolution code sits server-side from VS001 so adding rewind is an
insertion rather than a relocation.

> **Still Accepted under Unity, and the reasoning got *stronger*.** The identical argument
> holds with `ClientId` substituted for `PeerId` — NGO ownership has no concept of a local
> slot either. What changed is that this is now *with* the framework grain rather than
> against it: NGO's `NetworkVariable` defaults to server-write and `NetworkTransform`
> defaults to server authority. Mechanics in ADR-017 and [`NETWORKING.md`](NETWORKING.md) §2.
>
> This ADR is also why the netcode stack is swappable at all — nothing in the design leans
> on ownership semantics, so stack-specific surface is confined to `Overrun.Net`.

---

## ADR-004 — Listen-server, not dedicated

**Status:** **Accepted** · 2026-08-16 · *survives unchanged*

**Decision.** The host is a player and the authority. Dedicated servers are a supported
future mode, not a target.

**Rationale.** 4-player co-op with friends; dedicated infrastructure is cost with no player
benefit at this scale.

**Consequences.** Host advantage on latency — acceptable in PvE. No host migration
(ADR-005). Because simulation is server-gated and presentation is a separate subtree
(ARCHITECTURE §3), a dedicated server is later reachable as the same build launched
headless with zero local players — no rewrite. Under Unity this is `StartHost()` and a
batchmode build respectively.

---

## ADR-005 — No host migration

**Status:** **Accepted** · 2026-08-16 · *survives unchanged*

**Decision.** Host disconnect ends the run.

**Rationale.** Migration requires transferring full authoritative state — enemies, wave
state, all player state, RNG stream positions — mid-run. It is a large amount of work for a
rare case in a co-op game where sessions are one run long.

**Revisit if** play-testing shows host drops are common enough to hurt. The `RunSeed` +
stream-position model (ARCHITECTURE §9) would make state transfer more tractable than usual,
so this is not a permanently closed door.

---

## ADR-006 — Seed determinism covers content, not physics

**Status:** **Accepted** · 2026-08-16 · *survives unchanged*

**Decision.** `RunSeed` determines augment offers, loot, wave composition, shop inventory,
events, and modifier rolls. It does not attempt deterministic physics or lockstep.

**Rationale.** Bit-exact physics replay is not achievable with floats, variable frame
timing, and a physics engine not built for lockstep. Claiming determinism we cannot deliver
would produce desync bugs that look like network bugs. This is as true of PhysX as it was
of Godot Physics.

**Consequences.** Seeds are shareable for run *content*, not for full replay. Randomness
comes from named independent streams so consuming one does not shift another. The global
RNG is banned — `GD.Randi()` then, `UnityEngine.Random` now, for exactly the same reason:
shared global mutable sequence state couples every consumer.

---

## ADR-007 — Per-device input routing, not a global input singleton

**Status:** **Accepted** · 2026-08-16 · *decision survives; mechanism changed*

**Decision.** Each `PlayerContext` owns input bound to a specific device set. Gameplay never
reads a merged global input source.

**Rationale.** Godot's global `Input` merges all connected devices. With two gamepads, both
players move when either stick moves. This is not a polish issue — it is a correctness issue
that appears the moment a second controller is plugged in, which is why VS001 already
includes two local players.

> **Under Unity:** identical hazard, different API. Unity's *legacy* `Input` class
> (`Input.GetAxis`, `Input.GetKey`) merges devices exactly the same way and is banned
> outright. The replacement is the **Input System** package with one `PlayerInput` per local
> player, managed by `PlayerInputManager`.
>
> This is the clearest win from the switch: device pairing, join-on-button-press, hot-plug,
> and split-screen viewport assignment are all handled by the package instead of hand-rolled.

---

## ADR-008 — Split-screen sets the rendering budget

**Status:** **Accepted** · 2026-08-16 · *survives unchanged; see also ADR-018*

**Decision.** Art, lighting, and post-processing budgets are set by the 4-way split case.

**Rationale.** Four viewports render the world four times. The target GPU is an RX 6650 XT.
Building visuals against the solo case guarantees a painful retrofit.

**Consequences.** Favour cheap lighting, aggressive LODs, and restrained post. Measure
4-player split frame time from VS002 onward, not at the end. This constraint is the primary
driver of the render pipeline choice in ADR-018.

---

## ADR-009 — Per-viewport audio listeners

**Status:** ~~Proposed~~ **Superseded by ADR-019** · 2026-08-16

**Decision.** Each `SubViewport` gets its own `AudioListener3D`.

**Risk.** How Godot 4.7 mixes positional audio across multiple simultaneous listeners is
**not verified**. Possible outcomes range from clean per-viewport mixing to muddy overlap
requiring a custom strategy.

**Action.** Validate in VS002 as an early task, not a late one.

> **Superseded, and the news is bad.** Under Unity the question is settled and settled
> against us: Unity permits exactly one active `AudioListener`, full stop. The open question
> became a hard constraint. See ADR-019.

---

## ADR-010 — C# over GDScript

**Status:** ~~Accepted~~ **Superseded by ADR-015** · 2026-08-16

**Decision.** C#, via `godot-mono`. Target .NET 8.0+.

**Rationale.** Two of the brief's stated code-quality rules — "use clear namespaces" and
"use interfaces where they provide genuine value" — are things GDScript cannot do. GDScript
has no namespaces (only a flat global `class_name` registry, which degrades badly at content
scale) and no interfaces. C# also gives real generics for the stat and modifier pipeline,
better refactoring tools for a long-lived codebase, and meaningfully faster hot loops.

**Platform limits, confirmed against the Godot 4.7 docs:** C# projects *cannot* be exported
to web at all, and Android/iOS support is still marked experimental.

> **Moot under Unity**, which is C#-only. The reasoning was ultimately an argument *for* the
> kind of language Unity mandates, so nothing here is regretted. Unity has its own export
> constraints, but they are less severe on the desktop targets this project cares about.

---

## ADR-011 — Meta progression grants options, never power

**Status:** **Accepted** · 2026-08-16 · *survives unchanged*

**Decision.** Account unlocks add entries to pools — characters, starting loadouts, augment
pool members, artifacts, challenges, cosmetics, difficulty modifiers. They never increase
raw numbers.

**Rationale.** The brief forbids pay-to-win. It also protects run integrity: if meta unlocks
raised damage, balance would have to target the average unlock state and every run would be
balanced for nobody.

**Consequences.** Difficulty modifiers are player-selected, not player-earned-power. A client
declares its unlocks at lobby time and the server validates them against the run ruleset —
unlocks are never a channel for injecting arbitrary values.

---

## ADR-012 — NAT traversal unresolved

**Status:** **Open** · 2026-08-16 · *options changed under Unity*

**Context.** LAN is straightforward. Internet play needs traversal.

**Options under Godot.** UPnP via the linked `miniupnpc` (cheap, unreliable on consumer
routers) · a relay server (reliable, ongoing cost) · a platform SDK such as Steam networking
sockets (reliable, ties distribution to a platform).

> **Under Unity the default option improves.** **Unity Relay** is a first-party service that
> integrates directly with Unity Transport, which removes most of the engineering work — at
> the cost of a hosted-service dependency with its own pricing and an account requirement
> for players. Steam sockets and self-hosted relay remain alternatives.

**Decision.** Still deferred to VS010. Recorded now because it may influence distribution
strategy, and finding out late would be expensive.

---

## ADR-013 — `GAMEPLAY_SYSTEMS.md` written despite the brief's FIRST OBJECTIVE omitting it

**Status:** **Accepted** · 2026-08-16 · *survives unchanged*

**Context.** The brief's DOCUMENTATION section lists seven files to create and maintain,
including `README.md` and `Docs/GAMEPLAY_SYSTEMS.md`. Its FIRST OBJECTIVE section lists five
and omits both.

**Decision.** All seven were created.

**Rationale.** The stat/tag/modifier design is the project's highest-risk architectural
commitment — it is the thing that makes augments composable and the thing that, done wrong,
produces the giant switch statement the brief explicitly rejects. Leaving it undocumented
while writing five documents that depend on it would have made the set internally
incomplete.

---

## ADR-014 — Respawn model unresolved

**Status:** **Open** · 2026-08-16 · *survives unchanged*

**Context.** The brief lists "respawning between rounds if appropriate" among player systems,
and also specifies a roguelike run structure where death ends the run. These pull in opposite
directions.

**Working answer.** Downed-and-revived is the normal failure state. Full death respawns at
the start of the next round at a scrip penalty. A full team wipe ends the run.

**Why unresolved.** This preserves co-op forgiveness without erasing stakes, but it is
untested and it materially changes how threatening late rounds feel. Solo play makes it
sharper still — a solo player has nobody to revive them, so downed effectively equals dead
unless a self-revive item exists, which is itself an unmade decision.

**Action.** Play-test at VS006 and supersede this ADR with a decision.

---
---

# Unity era

## ADR-015 — Unity 6000.5 as the engine

**Status:** **Accepted** · 2026-08-16 · **supersedes ADR-001 and ADR-010**

**Context.** ADR-001 selected Godot largely because it was what the machine had and because
it fit split-screen and mixed local/online well. The user subsequently elected to switch to
Unity and installed 6000.5. Unity Hub 3.20.1 is installed as a user Flatpak.

**Decision.** Unity 6000.5, pinned in `ProjectSettings/ProjectVersion.txt`.

**Rationale.** This is a user directive rather than a technical re-derivation, and it is a
reasonable one: Unity brings a vastly larger FPS ecosystem, far more learning material,
better third-party tooling, and transferable skills. The brief was written in Unity
vocabulary to begin with, so the mental model and the engine now match.

**What is genuinely gained**

- **Assembly Definitions** turn the simulation/presentation boundary from a code-review
  convention into a **compile error** (ARCHITECTURE §2). This is strictly better than what
  Godot offered and it protects the project's most important structural rule.
- **`PlayerInputManager`** handles couch-co-op device assignment, join-on-press, and
  split-screen viewport rects natively — the single best-served requirement in the brief.
- **NGO's defaults are server-authoritative**, so ADR-003 now runs with the grain.

**What is genuinely lost**

- **Split-screen audio.** One `AudioListener` per game, full stop. This is a real, permanent
  downgrade with no clean fix (ADR-019).
- **Editor weight.** 15 GiB RAM is workable but tight with the Editor plus test clients.
- **Platform support.** CachyOS/Arch is not an officially supported Unity distro (ADR-020).
- **Licensing.** No longer a permissive MIT engine (ADR-020).

**Consequences.** `ARCHITECTURE.md`, `NETWORKING.md`, `README.md`, and `.gitignore` were
rewritten. `GAME_VISION.md` needed no changes at all — a useful confirmation that the design
work was properly engine-independent. `DEVELOPMENT_ROADMAP.md` and `GAMEPLAY_SYSTEMS.md`
needed only targeted edits.

**Open.** Whether 6000.5 is an LTS stream should be confirmed in the Hub. A multi-year
project on a non-LTS stream inherits an upgrade treadmill; if 6000.5 is not LTS, decide
deliberately whether to pin here or move to the nearest LTS before writing gameplay code.

---

## ADR-016 — Godot concepts, translated to Unity

**Status:** **Accepted** · 2026-08-16 · **supersedes ADR-002**

| Godot (superseded plan) | Unity (current) |
| --- | --- |
| `Resource` / `.tres` | `ScriptableObject` / `.asset` |
| `PackedScene` | Prefab |
| `Node` subclass | `MonoBehaviour` |
| `SubViewport` + `Camera3D` | `Camera` with viewport `rect` |
| `AudioListener3D` per viewport | **one** `AudioListener` per game (ADR-019) |
| `InputMap` + manual device filtering | Input System + `PlayerInput` / `PlayerInputManager` |
| `ENetMultiplayerPeer` | Unity Transport |
| `MultiplayerSynchronizer` | `NetworkVariable` / `NetworkTransform` |
| `MultiplayerSpawner` | `NetworkObject.Spawn()` + pooling |
| `Multiplayer.GetUniqueId()` | `NetworkManager.LocalClientId` |
| `SetMultiplayerAuthority()` | `NetworkObject` ownership (unused — see ADR-003) |
| `GetRemoteSenderId()` | `RpcParams.Receive.SenderClientId` |
| C# namespaces + folder discipline | **Assembly Definitions** (compiler-enforced) |
| `GD.Randi()` (banned) | `UnityEngine.Random` (banned) |

The banned-pattern *intent* is unchanged throughout: no global "the player," no implicit
singleton lookups, no system that assumes one local player.

---

## ADR-017 — Netcode for GameObjects

**Status:** **Accepted, with a defined revisit trigger** · 2026-08-16

**Decision.** Netcode for GameObjects 2.x over Unity Transport.

**Rationale.**

- **First-party**, so it adds no third-party dependency — the brief asks that dependencies
  be justified, and "the engine vendor's own netcode" is the lowest-justification-burden
  option available.
- **Its defaults match our model** (verified against the NGO 2.11 docs):
  `NetworkVariable` write permission defaults to `Server`; `NetworkTransform.AuthorityMode`
  defaults to `Server`; `NetworkObject.Spawn()` is server-only. ADR-003 is the framework's
  happy path, not a fight.
- Integrates with **Unity Relay** for the VS010 NAT problem (ADR-012).

**Known weakness.** NGO ships **neither client-side prediction nor lag compensation**. Both
are required by ADR-003 — prediction at VS004, hitbox rewind at VS010 — and both will be
hand-built. **FishNet** ships both as first-class features and is the strongest alternative;
Mirror is a third option with a large community but a similar prediction gap.

**Revisit trigger — deliberately concrete.** At the **VS004 prediction spike**. If
client-side prediction and reconciliation on NGO cost materially more than budgeted,
evaluate migrating to FishNet before VS010 compounds the problem with lag compensation.

**Why a later swap is survivable.** Per ADR-003, nothing in the design leans on ownership
semantics or stack-specific replication idioms. Stack-specific surface is confined to
`Overrun.Net`; `Overrun.Simulation` talks in `PlayerId` and `InputFrame`. That is deliberate
and should be preserved precisely so this door stays open.

**Note for implementers.** `[ServerRpc]`, `[ClientRpc]`, and `RequireOwnership` are
deprecated in NGO 2.x in favour of the unified `[Rpc(SendTo.…)]` attribute with
`InvokePermission`. Most tutorials still show the old form. Do not copy it.

---

## ADR-018 — Universal Render Pipeline

**Status:** **Accepted** · 2026-08-16 · follows ADR-008

**Decision.** URP, via the Universal 3D project template.

**Rationale.** ADR-008 establishes that 4-way split-screen sets the rendering budget. Four
cameras rendering the world on an RX 6650 XT rules out HDRP, whose per-camera cost and
deferred lighting model are built for a single high-fidelity view. Built-in is legacy and
receives no meaningful investment.

URP also suits the intended art direction — greybox to stylised, readable silhouettes,
strong colour-coded elemental effects — better than a photorealistic pipeline would.

**Consequences.** Some high-end lighting and post effects are unavailable. Accepted; the
game's legibility requirements (Pillar 5: always know what hit you) favour clarity over
fidelity anyway. Verify 4-camera frame time in VS002 rather than assuming URP makes it free.

---

## ADR-019 — One AudioListener; centroid strategy for split-screen

**Status:** **Accepted with known compromise** · 2026-08-16 · **supersedes ADR-009**

**Context.** ADR-009 planned a listener per viewport and flagged the mixing behaviour as
unverified. Under Unity it is not unverified — it is impossible. Unity permits exactly one
active `AudioListener`; more than one produces a warning and undefined spatialisation.

**Decision.** A single `AudioListener` in `Bootstrap`, positioned at the **centroid** of
living local players. Per-sound attenuation is computed against the **nearest** local player
rather than the listener transform. Player-specific audio — your reload, your hit confirm,
your ability — is played **2D and non-spatialised**, routed to that player's mixer group.

**Consequences, stated plainly.** Directional audio is approximate for everyone in
split-screen rather than correct for one player and wrong for the rest. This is a real
downgrade from the superseded plan and it constrains design:

> **No mechanic may depend on precisely localising a sound when 2+ local players are
> active.** An audio-cued enemy that must be located by ear is not shippable in split-screen.

**Action.** Validate the centroid approach in VS002 **before authoring any audio content**.
If it proves unacceptable, the fallback is fully manual per-player audio positioning, which
is substantially more work and should be costed before it is needed rather than after.

---

## ADR-020 — Unsupported distro and licensing exposure

**Status:** **Accepted, with monitoring** · 2026-08-16

**Context.** Two non-technical risks arrive with the engine switch, and neither shows up in
a code review.

**Distro support.** Unity officially supports Ubuntu and CentOS on Linux. **CachyOS/Arch is
not supported.** The Flatpak bundles its own runtime, which removes most library-mismatch
risk, and the machine checks out — glibc 2.44, Mesa 26.1.6, RADV on an RX 6650 XT, Vulkan
present. But if the Editor misbehaves, the vendor's answer is "unsupported platform," and
that is where external help stops.

**Licensing.** Godot is MIT: no terms, no seats, no thresholds. Unity is not. Unity Personal
is free below a revenue/funding threshold, above which paid seats are required. Unity has
changed its licensing terms materially more than once in recent years, including a proposal
that was withdrawn after industry backlash.

**Decision.** Both accepted. Neither blocks development.

**Action.** Confirm current licence terms against Unity's own site before any commercial
planning — do not rely on these documents or on recollection for that. Re-check at the point
of first public release.

---

## ADR-021 — Unity 6000.5 compiles at C# 9; write to that

**Status:** **Accepted** · 2026-08-16 · discovered during VS000

**Context.** Found by the compiler, not by planning. The first VS000 build failed with:

```
error CS8773: Feature 'record structs' is not available in C# 9.0.
error CS0518: Predefined type 'System.Runtime.CompilerServices.IsExternalInit' is not defined
```

Unity 6000.5 targets **C# 9** against netstandard2.1. `record struct` is a C# 10 feature,
and `init`-only setters — nominally C# 9 — require `IsExternalInit`, which netstandard2.1
does not ship. So records are effectively unavailable in both forms.

This invalidated code written directly into `ARCHITECTURE.md` §1, which specified
`public readonly record struct PlayerId(...)`. That was correct under the previous engine
(Godot's .NET 8 / C# 12) and silently wrong after ADR-015. The engine switch changed the
language version, and nothing flagged it until a real compile ran.

**Decision.** Write plain C# 9. No records, no `init`, no file-scoped namespaces, no global
usings. Value types get hand-written `IEquatable<T>`, `GetHashCode`, and `==`/`!=`.

**Rejected: raising the language version.** `langversion` can be forced via `csc.rsp`, and
`IsExternalInit` can be polyfilled with a hand-declared internal shim. Both are unsupported
configurations that Unity may break at any upgrade, in exchange for syntax sugar. Not worth
it for a project expected to live years across Editor versions.

**Consequences.** Slightly more boilerplate on value types — `PlayerId` is ~30 lines instead
of 3. Acceptable and localised; there are few such types.

**The transferable lesson:** code samples inside architecture documents are unverified
claims until something compiles them. The ADR-015 migration pass rewrote prose correctly but
carried a code sample across an engine boundary that invalidated it. Treat doc snippets as
claims to check, not as decisions already made.

---

## ADR-022 — `Tag` is 32-bit; Unity cannot serialise 64-bit enums

**Status:** **Accepted** · 2026-08-16 · discovered at runtime during VS001

**Context.** `GAMEPLAY_SYSTEMS.md` §2 originally specified `[Flags] enum Tag : ulong`, on
the reasoning that 64 tags gives room for a large content library. That compiled fine and
passed every EditMode test. It failed the moment the Editor tried to load a definition
asset:

```
Unsupported enum type 'Overrun.Core.Tag' used for field 'Tags' in class 'WeaponDefinition'
Unsupported enum type 'Overrun.Core.Tag' used for field 'AttackTags' in class 'EnemyDefinition'
```

Unity's serializer supports enums backed by 8/16/32-bit integer types only. A 64-bit
backing type is rejected outright, so the field does not serialise at all — every weapon
and enemy definition loses its tags, and the tag-filtered modifier system silently has
nothing to filter on.

**Decision.** `Tag : uint`. 32 flags maximum. 21 are currently used.

**Rejected: keeping ulong and hiding the field.** Marking the tags `[NonSerialized]` and
assigning them in code would defeat the point of data-driven definitions — the whole
reason tags exist is so designers can author augment interactions without touching C#.

**Rejected: pre-emptively building a 64-bit wrapper.** A serializable two-`uint` struct
with implicit conversion to a 64-bit runtime value does work, and is the escape hatch if we
ever need it. Building it now, with 11 bits still free, is exactly the premature
abstraction the brief warns against.

**Consequences.** A hard 32-tag ceiling, documented at the enum and in
`GAMEPLAY_SYSTEMS.md` §2 so it is not rediscovered by someone adding the 33rd tag. Note
that most future tags are *combinations* of existing ones rather than new bits, so the real
headroom is larger than 11 suggests.

**The transferable lesson, and it is the same one as ADR-021.** Compiling and passing tests
proved nothing here: the type was legal C#, the unit tests exercised it happily, and the
failure only appeared when Unity's asset pipeline touched a ScriptableObject field. Engine
serialisation constraints are not visible from either the compiler or headless tests —
they need an actual Editor load of actual assets.
