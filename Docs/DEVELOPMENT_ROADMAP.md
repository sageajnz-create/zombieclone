# Development Roadmap

Vertical slices. **Every milestone ends with something playable.** No milestone exists
purely to build framework.

Ordering principle: *correct architecture → playable → multiplayer-safe → extensible →
polished*. Where those conflict, correctness of the **boundaries** wins and breadth of
content loses — but only the boundaries. Nothing here builds a system wider than the
milestone needs.

---

## VS000 — Project bootstrap ✅ **COMPLETE** (2026-08-16)

**Not playable.** The only non-playable milestone, kept as small as possible.

> **Delivered on Unity 6000.5.8f1.** Packages pinned at URP 17.5.0, Input System 1.20.0,
> Netcode for GameObjects 2.13.1, AI Navigation 2.0.14. All five `Overrun.*` assemblies
> compile clean with no warnings. The boundary test passed: a deliberate
> `using Overrun.Presentation;` inside `Overrun.Simulation` produced
> `error CS0234 / CS0246`, and removing it returned the build to zero errors.
>
> Two things were discovered by building rather than planning, and both are recorded:
> the engine compiles at **C# 9** (ADR-021), and `InputFrame` had to move to
> `Overrun.Core` to break a circular assembly reference (ARCHITECTURE §6).

- Install Unity 6000.5 via the Hub (requires signing in); add Linux Build Support (IL2CPP)
- Create the project from the **Universal 3D (URP)** template
- Add packages: Input System, Netcode for GameObjects, AI Navigation
- Commit `ProjectSettings/` and `Packages/`; confirm `ProjectVersion.txt` pins 6000.5
- Create the five `Overrun.*` folders, each with an `.asmdef` whose reference list encodes
  the dependency direction in [`ARCHITECTURE.md`](ARCHITECTURE.md) §2 — **verify the
  boundary by deliberately adding a bad `using` and confirming it fails to compile**
- Input Actions asset with the 13 actions ([`ARCHITECTURE.md`](ARCHITECTURE.md) §6)
- `Bootstrap` / `World` / `LocalRigs` scenes; empty world boots and exits cleanly

**Done when:** the project opens, compiles, runs — and a deliberate cross-boundary
reference is a compile error.

---

## VS001 — First playable *(the current target)*

One greybox arena. One or two local players. It should be genuinely fun for ninety seconds.

**Scope**

- Greybox arena: two rooms joined by one purchasable door, one spawn zone per room
- FPS character controller: walk, sprint, jump, mouse + gamepad look
- 1–2 local players in split-screen from the very first build
- One weapon (sidearm, hitscan) with recoil, spread, reload, ammo
- One enemy archetype (basic melee) with nav, health, damage, death
- Wave director at minimum viability: round counter, fixed budget curve, active cap
- Currency awarded on kill; per-player scrip
- One `PurchasableDoor` unlocking room two and its spawn zone
- One augment-choice event between rounds, offering 3 of ~6 augments
- Death → results → restart

**Architectural requirements — these are the point of VS001**

- Runs as a **listen-server with one peer**. Not "singleplayer code we network later."
- `PlayerId` threaded through every system from the first line of gameplay code
- Input flows `InputRouter → InputFrame → server` even though the server is in-process
- Simulation and presentation already in separate subtrees
- Two local players via `PlayerInputManager` split-screen with routed input devices
- One `AugmentDefinition`, one `WeaponDefinition`, one `EnemyDefinition` as `.asset`
- `StatBlock` exists with all three layers, even though ~6 augments barely exercise it
- `ProcDepth` cap and per-tick proc budget present from the start
  ([`GAMEPLAY_SYSTEMS.md`](GAMEPLAY_SYSTEMS.md) §4)

**Explicitly out of scope:** networking beyond in-process, prediction, more than one
weapon/enemy, elites, bosses, meta progression, real art, real audio, VFX polish.

**Why split-screen is in the *first* slice.** It looks like scope that could wait. It
cannot. Two local players is the cheapest possible test that `PlayerId`, input routing,
and per-viewport presentation are actually correct. A solo-only VS001 would compile and
play fine while hiding every single-player assumption we swore not to make, and we would
find them all at once in VS002. Two players on one screen is the canary.

**Done when:** two people on one couch with two gamepads survive to round 5, buy the door,
pick an augment, die, and restart.

---

## VS002 — Local multiplayer hardening

- 3rd and 4th local players; horizontal, vertical, and quad layouts
- Press-to-join device binding; hot-plug and disconnect handling
- **Centroid `AudioListener` strategy — validate first, before any audio content.** Unity
  allows only one listener in the entire game; this is a hard constraint, not a tuning
  problem ([`ARCHITECTURE.md`](ARCHITECTURE.md) §4, [`DECISIONS.md`](DECISIONS.md) ADR-019)
- Per-player HUD, crosshair, interaction targeting, rumble
- Downed state and revive
- Couch drop-in mid-run as a roster delta ([`NETWORKING.md`](NETWORKING.md) §3)

**Done when:** four gamepads, one screen, one run, no crossed inputs.

---

## VS003 — LAN multiplayer

The first real proof of the architecture.

- `StartHost()` / `StartClient()` over Unity Transport; LAN discovery via UDP broadcast
- Build-hash validation on connect ([`NETWORKING.md`](NETWORKING.md) §6)
- `NetworkTransform` (server authority) and `NetworkVariable` on pawns and enemies; pooled
  `NetworkObject` spawning by definition id
- Remote entity interpolation on a render delay
- Server-validated interaction and purchase
- **Mixed local + online: 2 couch players + 2 remote, in one run**
- Bandwidth measured against the 64 kbit/s target

**Done when:** two machines, four players, one run — and the mixed-local case needed no
architectural change. If it did, the peer/player model is wrong and we fix it here, while
it is still cheap.

---

## VS004 — Roguelike depth

The milestone that makes it *this* game rather than a horde shooter.

- Full tag system and `StatBlock` tag-context resolution
- Event bus with the complete event set
- All five modifier types
- 20–25 augments across five rarities, authored purely as `.asset`
- Three intentionally supported build archetypes (shock/chain, fire/status, crit/glass cannon)
- Client-side prediction for own movement; server reconciliation
- Immediate local weapon feedback with server-confirmed hit markers

**Done when:** a new augment can be added with zero C#, and three distinct builds feel
distinct.

---

## VS005 — Weapon breadth

6–8 weapons covering hitscan, projectile, pellet, and explosive resolution; rarity rolls;
attachments; `RefinementBench`; `LotteryRack`; ammo economy.

**Done when:** a new weapon is one `.asset` and a placeholder mesh.

---

## VS006 — Enemy and director depth

All eight archetypes; elite modifier sets; special waves; active-enemy scaling by player
count; spawn-visibility fairness check; second arena.
**Play-test and resolve the respawn question** ([`GAMEPLAY_SYSTEMS.md`](GAMEPLAY_SYSTEMS.md) §9).

**Done when:** round 25 is hard for reasons other than enemy HP.

---

## VS007 — Map component library

Full fixture set: traps, teleporters, `Mains`/power, `InfusionRig`, secret switches, arena
lockdown, environmental hazards. Second arena rebuilt entirely from components.

**Done when:** a designer can build a playable arena with no new code.

---

## VS008 — Encounters and bosses

Mini-boss; one full boss with phases and lockdown; boss modifiers from the run seed;
side objectives; extraction as a win condition.

**Done when:** a run has an ending you can win.

---

## VS009 — Game feel

Hit reactions, stagger, gore, screen shake, weapon impact, punchy audio, controller
feedback, critical-hit feedback, damage numbers, death animations. Evaluate slide.

Concentrated here rather than spread across earlier slices *because* Boundary A holds —
this milestone should touch almost nothing in `Simulation/`. If it does, the boundary
leaked and that is the real finding.

**Done when:** shooting feels good with placeholder art.

---

## VS010 — Online

NAT traversal (UPnP, relay, or platform SDK — unresolved, ADR-012); lag compensation with
server-side hitbox rewind; connection quality handling; reconnection.

**Done when:** four players on four internet connections have a good time at 80 ms RTT.

---

## VS011 — Meta progression

Account profile; unlockable characters, starting loadouts, augment pool entries, artifacts,
challenges, cosmetics, difficulty modifiers. Strictly options, never power (ADR-011).

---

## VS012 — Content scale-up

Third arena, weapon and augment library expansion, enemy variants, secrets and quests,
audio and art pass.

---

## Sequencing rationale

**Local multiplayer before networked multiplayer** (VS002 → VS003) because split-screen
surfaces player-identity bugs with zero network noise in the way. Debugging a `PlayerId`
mistake across a socket is dramatically harder than debugging it on one machine.

**LAN before roguelike depth** (VS003 → VS004) because the augment system generates
enormous amounts of gameplay events, and validating replication against 6 augments is far
easier than against 25. Content built on a broken network model is content rebuilt.

**Feel late** (VS009) — but only because the boundary makes it safely deferrable. In a
codebase where presentation and simulation were tangled, deferring polish would mean
retrofitting it through networked code, and it would have to come earlier and cost more.

**Bosses after the director** (VS008 after VS006) because a boss is the director's hardest
customer, and building it against an immature director means building it twice.
