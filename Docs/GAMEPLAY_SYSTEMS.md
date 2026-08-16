# Gameplay Systems

How the mechanics are built so that content scales without code scaling. Structural
context is in [`ARCHITECTURE.md`](ARCHITECTURE.md).

> Included per the brief's DOCUMENTATION section. Note that the FIRST OBJECTIVE list omits
> this file — see [`DECISIONS.md`](DECISIONS.md) ADR-013 for why it was written anyway.

---

## 1. Stat pipeline

Every gameplay number resolves through one formula. No system computes its own stacking.

```
final = (base + Σ flat) × (1 + Σ increased) × Π (1 + more)
```

| Layer | Stacks | Use for |
| --- | --- | --- |
| `Flat` | additive | +5 damage, +20 max health |
| `Increased` | additive with each other | +30% damage — the common case |
| `More` | multiplicative with each other | ×1.5 damage — rare, powerful, deliberately scarce |

Three layers is the whole design. `Increased` additive stacking means the tenth +30% is
worth less than the first, which is what stops linear upgrade acquisition from producing
runaway scaling. `More` is the escape valve reserved for Legendary augments and build
capstones, because it is the only layer that compounds — and that is exactly the "absurd
power fantasy" lever, kept behind a rarity gate.

```csharp
public sealed class StatBlock
{
    public float Resolve(StatId stat, TagMask context);
    public ModifierHandle Add(StatModifier mod);
    public void Remove(ModifierHandle handle);
}
```

`Resolve` takes a **tag context**, so "+40% increased damage with Shock" is a data row, not
a code path. The modifier declares which tags it applies to; the caller declares which tags
the current action has. See §2.

Stats are recomputed on modifier change and cached, not per-frame.

---

## 2. Tags

```csharp
[Flags]
public enum Tag : ulong
{
    None = 0,
    // Sources
    Weapon = 1 << 0,  Projectile = 1 << 1,  Hitscan = 1 << 2,  Melee = 1 << 3,
    Ability = 1 << 4,  Companion = 1 << 5,  Trap = 1 << 6,  Explosion = 1 << 7,
    // Elements
    Shock = 1 << 8,  Fire = 1 << 9,  Frost = 1 << 10,  Toxic = 1 << 11,  Void = 1 << 12,
    // Roles
    Critical = 1 << 16,  Status = 1 << 17,  Defense = 1 << 18,  Mobility = 1 << 19,
    Economy = 1 << 20,  Support = 1 << 21,  Elemental = 1 << 22,  Area = 1 << 23,
}
```

A `TagMask` wraps this with `Matches(required, excluded)`. Everything carries tags:
weapons, augments, damage events, status effects, abilities.

This is the mechanism that delivers the brief's "future upgrades interact without bespoke
code." *Lightning Rounds* (`Projectile|Elemental|Shock`), *Conductive Blood*
(`Shock|Status`), and *Overcharge* (`Shock|Critical`) form an electrical build because each
one's modifiers filter on `Shock` — none of them knows the others exist.

---

## 3. Augments

```csharp
[CreateAssetMenu(menuName = "Overrun/Augment")]
public sealed class AugmentDefinition : ScriptableObject
{
    [SerializeField] private int        _id;            // stable hash, not the asset name
    [SerializeField] private Rarity     _rarity;        // Common → Legendary
    [SerializeField] private Tag        _tags;
    [SerializeField] private Modifier[] _modifiers;
    [SerializeField] private Tag        _requiresAnyTag; // build-gated offers
    [SerializeField] private int[]      _excludedBy;
    [SerializeField] private int        _maxStacks;
}
```

An augment is **rarity + tags + a list of modifiers**. There are only a few modifier
*types*; there can be unlimited augment *content*.

```
Modifier (abstract ScriptableObject)
├── StatModifier          adjust a stat, filtered by tag context
├── EventEffectModifier   on event + tag match + condition → spawn an effect
├── ProcModifier          chance-gated EventEffectModifier
├── StatusModifier        change status application, duration, stacking, or potency
└── ResourceModifier      ammo, scrip, cooldown, charge economy
```

Adding *Chain Lightning* is authoring one `.asset`: `EventEffectModifier` on `OnHit`,
requiring `Shock`, spawning a `ChainEffect`. No new C# unless a genuinely new *kind* of
behaviour is needed. That is the "no giant switch statement" requirement discharged.

Modifiers are `ScriptableObject`s, so they are **shared, immutable assets**. Per-player
stack counts and runtime state live in the player's `StatBlock`, never on the asset —
mutating a `ScriptableObject` at runtime writes to disk in the Editor and corrupts balance
data ([`ARCHITECTURE.md`](ARCHITECTURE.md) §7).

Offers are drawn server-side from `RunSeed.Stream(RngStream.AugmentOffers, round)`,
weighted by rarity and filtered by the player's current tags — so a build in progress gets
offered things that talk to it, which is what makes builds feel intentional rather than
accidental.

---

## 4. The event bus and the proc problem

Server-side, per run. Typed events, ordered handlers.

```
OnHit          attacker, victim, DamageContext
OnKill         attacker, victim, DamageContext
OnCrit         attacker, victim, DamageContext
OnDamageTaken  victim, DamageContext
OnStatusApplied victim, status, stacks
OnReload / OnWaveStart / OnWaveEnd / OnPurchase / OnRevive
```

```csharp
public sealed class DamageContext
{
    public PlayerId Source;
    public Tag      Tags;
    public float    Amount;
    public bool     IsCritical;
    public Vector3  HitPoint;
    public byte     ProcDepth;      // ← the important field
}
```

Damage resolution is an ordered pipeline over the context — base → weapon stats → augment
stat modifiers → crit roll → resistances → final — so every stage is inspectable and
augments can hook any stage.

### The proc problem

Effects re-enter the pipeline. A chain-lightning proc emits its own `OnHit`, which can
trigger chain lightning. With 40 enemies and a status-effect build this is not a theoretical
concern — it is a guaranteed frame-time cliff or stack overflow, on the server, taking down
everyone's game at once.

Three guards, in from day one, not retrofitted:

1. **`ProcDepth` cap** — effects spawned from an effect carry depth+1; hard stop at 3.
2. **Per-tick proc budget** — a global cap on effect spawns per simulation tick; excess is
   dropped, not queued, and logged.
3. **Per-source cooldown** — an effect cannot retrigger on the same victim from the same
   source within a minimum interval.

These are cheap to add now and near-impossible to add cleanly later, once fifty augments
assume unbounded chaining. Flagged as a top technical risk.

---

## 5. Weapons

`WeaponDefinition` (see [`ARCHITECTURE.md`](ARCHITECTURE.md) §6) drives one `WeaponRuntime`
implementation. Fire modes are data — pellet count 8 with wide spread *is* the shotgun;
there is no `Shotgun` class.

```
WeaponInstance = definition id + rarity roll + attachments + refinement level
```

Only the instance is per-player state; the definition is shared and never replicated.
Weapon stats resolve through the same `StatBlock` pipeline as everything else, so a
`+30% increased damage with Projectile` augment applies to weapons with no weapon-specific
code.

Hitscan and projectile are two resolution strategies behind one interface. Both resolve on
the server; both fire presentation immediately on the client
(see [`NETWORKING.md`](NETWORKING.md) §5).

Archetypes to cover: sidearm, SMG, assault rifle, shotgun, LMG, marksman, sniper, launcher,
energy, experimental. All original designs per the
[originality boundary](GAME_VISION.md#originality-boundary).

---

## 6. Enemies

Component composition, not subclasses ([`ARCHITECTURE.md`](ARCHITECTURE.md) §7).

| Archetype | Distinguishing components |
| --- | --- |
| Basic | melee attack, ground nav |
| Fast | high speed, lunge, low health |
| Tank | armor layer, stagger resistance, slow |
| Ranged | projectile attack, kiting brain |
| Exploder | proximity detonate, death AoE |
| Elite | any base + elite modifier set |
| Mini-boss | elite + ability set + phase logic |
| Boss | scripted phases, arena lockdown |

**Elites and modifiers are the scaling mechanism.** An elite is a base enemy plus a modifier
set drawn from the run seed — armored, hasted, shielded, volatile, regenerating. This gives
composition-driven escalation from existing content, which is what keeps Pillar 2 ("mechanical,
not numerical") achievable without authoring a hundred enemy types.

Late-round difficulty adjusts speed, armor, resistances, abilities, attack patterns,
composition, special frequency, and hazards — HP is one lever among eight and deliberately
not the primary one.

---

## 7. Wave Director

Server-only. Budget-based.

```
budget(round, players) = base × roundCurve(round) × playerScale(players)
```

Spend budget on `EnemyDefinition`s by cost and weight from `WaveProfile`, drawn from
`RunSeed.Stream(RngStream.WaveComposition, round)`.

Responsibilities: current round, enemy budget, spawn pacing, composition, special waves,
elite injection, boss rounds, active-enemy cap, difficulty scaling, player-count scaling.

### Spawn zone selection

Spawning is **never** at arbitrary world positions. `SpawnZone` is a trigger `Collider`
with a region tag. A zone is eligible when it is unlocked (region purchased and powered), within a
distance band of at least one living player, and **not currently visible to any player's
camera**.

That last check needs every player's view frustum on the server — including remote players.
It is the reason the server replicates a low-rate approximate look direction for each pawn
(~10 Hz), which would otherwise look like unnecessary bandwidth. Recording it here so it
does not get optimised away later.

Fairness rule: if no zone passes all filters, relax *visibility* last — spawning slightly
too close beats spawning in front of a player's face, and both beat stalling the wave.

---

## 8. Map components

Reusable prefabs, not per-map scripting. Each implements `IInteractable`; interaction is
always client-requests → server-validates ([`NETWORKING.md`](NETWORKING.md) §4).

| Component | Behaviour |
| --- | --- |
| `PurchasableDoor` | scrip cost, unlocks a region, opens spawn zones |
| `PurchasableBarrier` | debris clear, cheaper shortcut variant |
| `PowerNode` / `Mains` | gates powered fixtures site-wide |
| `WeaponPurchasePoint` | fixed weapon + ammo resupply |
| `LotteryRack` | seeded random weapon, escalating cost |
| `InfusionRig` | grants a passive, one slot per rig |
| `RefinementBench` | upgrades a held weapon instance |
| `Trap` | timed hazard, scrip-activated |
| `Teleporter` | linked pair, optional cooldown |
| `SecretSwitch` | quest step trigger |
| `ArenaLockdown` | seals a region, drives an encounter |
| `SpawnZone` | tagged spawn volume (§7) |
| `EnvironmentalHazard` | persistent area damage |

A map is a scene composed of these plus geometry and navmesh. Map-specific logic is a
smell; if a map needs behaviour, it becomes a component.

---

## 9. Player systems

Health → armor/shield → **downed** → death. Downed players can be revived by a teammate
holding `Interact` for a duration; a solo player downing is a run loss (or consumes a
self-revive item, if that mechanic survives design review).

Movement: walk, sprint, jump, crouch, slide. Slide is a maybe — evaluated for feel in
VS009, not assumed.

Each player carries: weapon inventory (2 slots initially), equipment (throwable), one
active ability, passive augments, status effects.

**Open design question — respawn.** The brief lists "respawning between rounds if
appropriate," which sits awkwardly with roguelike permadeath. Current working answer:
downed-and-revived is the normal failure state; full death respawns at the start of the
next round at a scrip penalty, and a full team wipe ends the run. This preserves co-op
forgiveness without erasing stakes, but it is unvalidated and should be play-tested at
VS006 rather than treated as settled. Tracked in ADR-014.

---

## 10. Status effects

| Status | Effect | Interaction hook |
| --- | --- | --- |
| Burning | damage over time | spreads on death |
| Frozen | slow → freeze at max stacks | shatter bonus vs. frozen |
| Shocked | chain conductivity | chain damage scales with stacks |
| Toxic | DoT + healing reduction | stacks intensity, not duration |
| Bleed | DoT scaling with movement | melee synergy |

Statuses are stack-based with per-status stacking rules, applied through `StatusModifier`
so augments can alter duration, potency, stack cap, and application chance generically.
Cross-status interactions (shatter a frozen target, spread burning on death) are authored
as `EventEffectModifier` content — the same mechanism augments use, so an augment can add
a new interaction without engine changes.
