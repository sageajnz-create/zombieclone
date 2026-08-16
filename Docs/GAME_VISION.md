# Game Vision

> Working codename: **OVERRUN**. All names in this document are placeholders.

## One line

A first-person co-op horde shooter where every run is a fresh, escalating experiment in
becoming absurdly overpowered — right up until the moment it isn't enough.

## The fantasy

Four survivors, one hostile site, no extraction guaranteed. You start with a sidearm and
no idea what the site holds. Twenty minutes later one player is a lightning conduit whose
kills chain across half the arena, another is a shotgun berserker healing off gore, and
the third is quietly running a turret economy. Then round 31 arrives and the site sends
something that does not care.

The joy is the *arc*, not any single moment. A run should be a story with a shape.

## Pillars

**1. The build is the story.**
Round-based horde shooters traditionally hand you power in fixed increments. Here, power
is *chosen* and *compounding*. Upgrades combine into builds that the designers did not
explicitly author. A player should be able to describe their run to a friend in one
sentence and have it sound different every time.

**2. Escalation is mechanical, not numerical.**
Later rounds must not be "the same enemy with more health." They change composition,
add modifiers, alter attack patterns, introduce hazards, and demand different play.
Difficulty that scales only through HP is a design failure, and the wave director is
built to make that easy to avoid.

**3. Co-op that is genuinely co-operative.**
Builds should have reasons to point at each other. A crowd-control player makes a
critical-hit player better. A support build is a real build, not a consolation prize.
Shared currency pressure and shared area unlocks give the team decisions, not just
parallel solo runs in one room.

**4. Playable together, however people actually play together.**
Two friends on one couch, two more online, all in the same run. This is a first-class
requirement that shapes the entire architecture, not a feature to be retrofitted.

**5. Weight and clarity.**
Weapons must feel heavy and legible. You should always know what hit you, what you hit,
whether it was a critical, and whether it died. Presentation is separated from simulation
so that this polish can be pushed hard without ever destabilising the network layer.

## Core loop

```
Explore → Kill → Earn → Unlock area → Arm up → Complete encounter
   → Choose upgrade → Refine build → Survive the next wave
   → Discover secret / trigger event → Fight elites → Fight boss
   → Extract, win, or die
```

Death ends the run. Meta progression persists; run power does not.

## Run shape

| Phase | Rounds | What it feels like |
| --- | --- | --- |
| Scramble | 1–5 | Sidearm, tight space, every coin matters |
| Footing | 6–12 | First real weapon, first upgrades, areas opening |
| Identity | 13–20 | Build has a clear direction; elites appear |
| Escalation | 21–30 | Synergies firing constantly; hazards and specials |
| Overrun | 31+ | Absurd power vs. absurd pressure; bosses; survival curve |

## What this game is not

- Not a story campaign. Narrative is environmental and optional.
- Not PvP. All competition is against the site.
- Not pay-to-win. Meta progression unlocks *options*, never raw power. See
  [`DECISIONS.md`](DECISIONS.md) ADR-011.
- Not a live-service content treadmill. Depth comes from combination, not volume.

---

## Originality boundary

This project is mechanically inspired by classic round-based horde survival shooters.
Mechanics and genre conventions are not protectable; specific expression is. The following
rules are binding on all content work.

**Do not use, reference, or near-miss:**

- Named characters, their likenesses, voices, or personalities
- Map names, layouts recreated from memory, or recognisable landmarks from existing games
- Franchise terminology for machines, perks, currencies, power-ups, or enemy types
- Real-world or franchise weapon names, and 1:1 recreations of distinctive weapon silhouettes
- Story elements, factions, lore, ciphers, or easter-egg quest structures
- UI layouts, fonts, HUD arrangements, or iconography copied from an existing game
- Audio: no sampled, transcribed, or deliberately imitative music, stingers, or voice lines
- Art: no traced, kitbashed, or datamined assets

**Do build original equivalents.** A vending device that grants a passive is a mechanic.
Its name, look, sound, jingle, and flavour must be ours.

**Working terminology (original, placeholder):**

| Concept | Placeholder term |
| --- | --- |
| The enemies | the **Sundered** |
| Currency | **Scrip** |
| Passive-granting station | **Infusion Rig** |
| Randomised weapon device | **Lottery Rack** |
| Weapon upgrade station | **Refinement Bench** |
| Site power system | **Mains** |
| Run upgrade | **Augment** |

These are working names chosen to be functional and non-infringing. They are not final and
must pass a trademark and prior-use clearance review before any public build ships.

**Process rule:** if a proposed name, asset, or mechanic's *presentation* was chosen because
it resembles something from an existing game, it is rejected. Resemblance driven by genre
convention (reload animations, wave counters, weapon wall-mounts) is fine.
