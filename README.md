# OVERRUN *(working codename)*

A first-person, 1–4 player co-op, round-based horde-survival roguelike.

Players drop into a hostile site, fight escalating waves, earn currency, unlock areas,
buy and find weapons, take synergistic run upgrades, uncover secrets, fight bosses, and
try to survive. A run should arc from *weak survivor* → *armed survivor* → *specialised
build* → *absurd power fantasy*.

> **Status: planning complete, no gameplay code yet.**
> This repository currently contains architecture and roadmap documents only.
> See [`Docs/DEVELOPMENT_ROADMAP.md`](Docs/DEVELOPMENT_ROADMAP.md) for what gets built first.

---

## Originality constraint

This project is **mechanically inspired** by classic round-based horde shooters. It must
not reproduce protected content from any of them — no characters, maps, terminology,
weapon names, story, UI layouts, audio, or art. All naming in these documents is original
placeholder naming and is subject to a clearance review before any public release.
See [`Docs/GAME_VISION.md`](Docs/GAME_VISION.md#originality-boundary).

The directory name `zombieclone` is a scratch name and should be renamed.

---

## Tech stack

| Concern | Choice |
| --- | --- |
| Engine | **Unity 6000.5** ([ADR-015](Docs/DECISIONS.md)) |
| Render pipeline | **URP** — chosen for 4-way split-screen cost ([ADR-018](Docs/DECISIONS.md)) |
| Language | C# |
| Networking | **Netcode for GameObjects 2.x** over Unity Transport, host/listen-server ([ADR-017](Docs/DECISIONS.md)) |
| Data assets | `ScriptableObject` definitions |
| Input | **Input System** package, `PlayerInputManager` + per-player `PlayerInput` |
| Split-screen | Cameras with viewport rects, assigned by `PlayerInputManager` |
| 3D content | Blender → FBX/glTF |

Rationale for every one of these is recorded in [`Docs/DECISIONS.md`](Docs/DECISIONS.md).

The Editor version is pinned in `ProjectSettings/ProjectVersion.txt`. **Do not let the Hub
silently upgrade it** — a Unity version bump is an architectural decision and gets an ADR.

---

## Getting set up

Unity Hub is installed as a user Flatpak:

```bash
flatpak run com.unity.UnityHub
```

Then, in the Hub UI:

1. **Sign in to your Unity account** — required before the Hub will install an Editor.
2. **Install Unity 6000.5.**
3. Add modules: **Linux Build Support (IL2CPP)**, plus **Windows Build Support (Mono)** if
   a Windows build is ever wanted.
4. Create the project from the **Universal 3D (URP)** template.

Packages to add via Package Manager. Nothing beyond these without a recorded decision:

| Package | Why |
| --- | --- |
| `com.unity.inputsystem` | Per-device routing and local multiplayer. Mandatory — see [ADR-007](Docs/DECISIONS.md). |
| `com.unity.netcode.gameobjects` | Server-authoritative replication. |
| `com.unity.transport` | Pulled in by Netcode. |
| `com.unity.ai.navigation` | Enemy pathfinding. |

### Running tests headlessly

```bash
DISPLAY=:0 flatpak run --command=/home/sage/Unity/Hub/Editor/6000.5.8f1/Editor/Unity com.unity.UnityHub -batchmode -runTests -testPlatform EditMode -projectPath "$PWD" -testResults Logs/editmode-results.xml -logFile -
```

> **Do not add `-nographics` to a `-runTests` invocation.** On this machine it hangs
> indefinitely at `[MODES] Loading mode Default` with the process at 0% CPU — it never
> reaches the test runner and never writes a results file. With a real display attached the
> same suite completes in about 30 seconds. `-nographics` is fine for plain compile checks
> and for `-executeMethod`; it is only the test runner that stalls.

### Platform caveat

Unity officially supports Ubuntu and CentOS on Linux. **CachyOS/Arch is not an officially
supported distribution.** The Flatpak bundles its own runtime, which removes most library
mismatch risk, but if the Editor misbehaves the vendor's answer will be "unsupported
platform."

Verified working on this machine: glibc 2.44, Mesa 26.1.6, RADV on an RX 6650 XT, Vulkan
available, 451 GB free. RAM is 15 GiB — enough, but Editor plus two test clients will be
tight; prefer a batchmode server when testing simulation only.

**Licensing** differs materially from a permissively-licensed engine. Unity Personal is free
below a revenue/funding threshold, above which a paid seat is required. Confirm current
terms against Unity's site before any commercial planning — they have changed more than
once. See [ADR-020](Docs/DECISIONS.md).

---

## Repository layout

The layout below is the *target* shape, created as milestones land — not all of it exists yet.
Each `Overrun.*` folder carries an **Assembly Definition** that makes the dependency
direction in [`Docs/ARCHITECTURE.md`](Docs/ARCHITECTURE.md) §2 a compile-time guarantee.

```
Docs/                          Architecture and planning documents
Assets/
  Overrun.Core/                PlayerId, RunSeed, StatBlock, TagMask.
                               No UnityEngine deps beyond ScriptableObject. EditMode-testable.
  Overrun.Data/                ScriptableObject definitions. Pure data, no behaviour.
  Overrun.Simulation/          Server-authoritative gameplay.
  Overrun.Net/                 Session, client↔player mapping, replication, RPC surface.
                               The only assembly that knows client ids exist.
  Overrun.Presentation/        Per-local-player rigs, HUD, VFX, audio, camera, feel.
  Content/
    Definitions/               .asset data assets
    Maps/                      Arena scenes
    Art/                       Greybox and placeholder art
  Tests/                       EditMode simulation tests
Packages/                      Package manifest — committed
ProjectSettings/               Unity project configuration — committed
```

---

## Documentation

| Document | What it covers |
| --- | --- |
| [`Docs/GAME_VISION.md`](Docs/GAME_VISION.md) | The game, its pillars, and the originality boundary |
| [`Docs/ARCHITECTURE.md`](Docs/ARCHITECTURE.md) | Layering, player identity, scene topology, system boundaries |
| [`Docs/NETWORKING.md`](Docs/NETWORKING.md) | Authority model, client-vs-player, replication, RPC conventions |
| [`Docs/GAMEPLAY_SYSTEMS.md`](Docs/GAMEPLAY_SYSTEMS.md) | Upgrades, stats, weapons, enemies, wave director, map components |
| [`Docs/DEVELOPMENT_ROADMAP.md`](Docs/DEVELOPMENT_ROADMAP.md) | Milestones, each independently playable |
| [`Docs/DECISIONS.md`](Docs/DECISIONS.md) | Architecture decision records |

**When a major architectural decision changes, update `DECISIONS.md` and every document it
touches in the same commit.**

> This project was briefly planned against Godot before switching to Unity. ADR-001 through
> ADR-012 are retained as **superseded** history rather than deleted — several record
> reasoning that still applies, and each superseding ADR points back at the one it replaces.
