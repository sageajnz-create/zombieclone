# OVERRUN *(working codename)*

A first-person, 1–2 player (this slice) co-op, round-based horde-survival roguelike.

Players drop into a hostile site, fight escalating waves, earn **scrip**, unlock a second
room, pick **augments** between rounds, and try to survive. Death ends the run; restart
from the results screen.

> **Status: VS001 first playable.** Greybox arena, listen-server host, split-screen,
> hitscan sidearm, melee **Sundered**, wave director, one purchasable bulkhead, six
> augments, death → results → restart.
> See [`Docs/DEVELOPMENT_ROADMAP.md`](Docs/DEVELOPMENT_ROADMAP.md). Do not implement VS002+
> until this slice has been play-tested.

The directory name `zombieclone` is a scratch name. It must not appear in product strings,
UI, or scene names — the game is **Overrun**.

---

## Originality constraint

This project is **mechanically inspired** by classic round-based horde shooters. It must
not reproduce protected content from any of them — no characters, maps, terminology,
weapon names, story, UI layouts, audio, or art. All naming in these documents is original
placeholder naming and is subject to a clearance review before any public release.
See [`Docs/GAME_VISION.md`](Docs/GAME_VISION.md#originality-boundary).

---

## Tech stack

| Concern | Choice |
| --- | --- |
| Engine | **Unity 6000.5.8f1** ([ADR-015](Docs/DECISIONS.md)) |
| Render pipeline | **URP** |
| Language | C# 9 (Unity 6000.5 default — [ADR-021](Docs/DECISIONS.md)) |
| Networking | Netcode for GameObjects 2.x, listen-server (`StartHost`) |
| Input | Input System + `PlayerInputManager` split-screen |
| AI | AI Navigation (NavMesh) |

The Editor version is pinned in `ProjectSettings/ProjectVersion.txt`. **Do not let the Hub
silently upgrade it.**

---

## Open in Unity Hub (Windows) — Sage

Your clone lives wherever you put it (for example `C:\Users\sagea\Projects\zombieclone`).
This repo does not depend on that path.

1. Install **Unity Hub** from [unity.com/download](https://unity.com/download) and sign in.
2. In Hub, **Installs → Install Editor**.
3. Install **Unity 6000.5.8f1** specifically. If Hub hides older versions, use the archive:
   [unity.com/releases/editor/archive](https://unity.com/releases/editor/archive) → 6000.5.8f1.
4. Add modules: **Windows Build Support (Mono)** is enough to Play in the Editor. IL2CPP is optional.
5. **Projects → Add** → select the folder that contains `Assets`, `Packages`, and
   `ProjectSettings` (the repo root).
6. Open the project with **6000.5.8f1**. First import takes several minutes. Wait until the
   console is quiet.
7. Confirm the title bar / About window says **6000.5.8f1**. If Hub upgraded you, switch
   the project's Editor version back.

Play Mode is forced to start from `Assets/Content/Scenes/Bootstrap.unity` (menu
**Overrun → Always Start Play From Bootstrap**). You do **not** need to have Bootstrap
open in the Hierarchy first.

### Play

1. Press **Play**.
2. You should see the greybox rooms from above and an on-screen prompt.
3. Press any keyboard key or a gamepad face button to join as player 1.
4. Plug in a second gamepad (or use a second device) and press a button to join as player 2
   (split-screen). VS001 caps local players at **2**.
5. Shoot the **Sundered**, earn scrip, buy the **Site Bulkhead** in the corridor (**E** /
   gamepad West / X), pick an augment after each round (**1 / 2 / 3** or gamepad X / Y / B).
6. When everyone is down, **Fire** or **Jump** restarts the run.

| Action | Keyboard / mouse | Gamepad |
| --- | --- | --- |
| Move | WASD | Left stick |
| Look | Mouse | Right stick |
| Fire | Left mouse | Right trigger |
| Reload | R | North / Y |
| Interact | E | West / X |
| Jump | Space | South / A |
| Sprint | Left Shift | Left stick press |
| Join | Any bound button | Any face button |
| Augment pick | 1 / 2 / 3 | West / North / East (X / Y / B) |

If Play is a blank screen: stop Play, menu **Overrun → Always Start Play From Bootstrap**
should be checked, then Play again. If enemies never appear, menu
**Overrun → Repair Network Prefab Hashes** then Play.

Optional rebuild of greybox content (idempotent): **Overrun → Run VS001 (All)**.

### Headless EditMode tests (optional)

Needs a Unity Editor on PATH. From the repo root:

```bat
Unity.exe -batchmode -nographics -projectPath "%CD%" -executeMethod Overrun.EditorTools.VS001Bootstrap.RunEverything
```

`-nographics` is fine for `-executeMethod`. For `-runTests` on some Linux setups it hangs;
on Windows Hub installs, EditMode tests from **Window → General → Test Runner** are the
reliable path.

---

## What VS001 includes

- Listen-server: `NetworkManager.StartHost()`, input is `InputRouter → InputFrame → server RPC` even in-process
- `PlayerId` (client + local slot) on pawns, weapons, scrip, augments, interaction
- Two rooms, corridor **Site Bulkhead** (80 scrip) unlocking room two spawn zones
- Hitscan **Service Sidearm**: recoil, spread, reload, ammo
- One melee **Sundered** enemy: navmesh, health, damage, death, scrip on kill
- Wave director: round counter, budget curve, active cap 16
- Between rounds: 3 of 6 augments, applied through `StatBlock` (Flat / Increased / More)
- `ProcDepth` cap and per-tick proc budget (no proc content yet; the guards exist)
- Death → results overlay → restart

**Out of scope (VS002+):** 3rd/4th local players, LAN, prediction, more weapons/enemies,
elites, art, audio polish.

---

## Repository layout

```
Docs/                          Architecture and planning
Assets/
  Overrun.Core/                PlayerId, RunSeed, StatBlock, Tag, ProcBudget, InputFrame
  Overrun.Data/                ScriptableObject definitions (no behaviour)
  Overrun.Simulation/          Server-authoritative gameplay
  Overrun.Net/                 Session, roster, RPCs (the only layer that knows ClientId)
  Overrun.Presentation/        Rigs, HUD, cameras, input routing
  Content/Scenes/              Bootstrap, World, LocalRigs
  Content/Definitions/         .asset weapons, enemies, augments
  Tests/EditMode/              Stat, proc, identity, augment, run-loop tests
```

Each `Overrun.*` folder has an Assembly Definition. Simulation cannot reference
Presentation — that is a compile error, on purpose.

---

## Documentation

| Document | What it covers |
| --- | --- |
| [`Docs/GAME_VISION.md`](Docs/GAME_VISION.md) | The game, pillars, originality boundary |
| [`Docs/ARCHITECTURE.md`](Docs/ARCHITECTURE.md) | Layering, player identity, scenes |
| [`Docs/NETWORKING.md`](Docs/NETWORKING.md) | Authority, RPCs, client vs player |
| [`Docs/GAMEPLAY_SYSTEMS.md`](Docs/GAMEPLAY_SYSTEMS.md) | Stats, augments, director, fixtures |
| [`Docs/DEVELOPMENT_ROADMAP.md`](Docs/DEVELOPMENT_ROADMAP.md) | Milestones |
| [`Docs/DECISIONS.md`](Docs/DECISIONS.md) | ADRs |

**When a major architectural decision changes, update `DECISIONS.md` and every document it
touches in the same commit.**
