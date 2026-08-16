namespace Overrun.Core
{
    /// <summary>Every gameplay number that an augment can touch.</summary>
    public enum StatId
    {
        MaxHealth,
        Armor,
        HealthRegen,
        Lifesteal,

        MoveSpeed,
        SprintMultiplier,
        JumpHeight,

        Damage,
        FireRate,
        CritChance,
        CritMultiplier,
        Range,
        Penetration,
        AreaRadius,

        MagazineSize,
        ReserveAmmo,
        ReloadSpeed,

        StatusChance,
        StatusDuration,
        StatusPotency,

        ScripGain,
        AbilityCooldown,
        PickupRadius
    }

    /// <summary>
    /// How a modifier combines with others of the same layer.
    ///
    /// The split exists so that stacking behaviour is a property of the data, not of code.
    /// Increased is additive with other Increased, which is what stops linear upgrade
    /// acquisition from producing runaway scaling. More is multiplicative and therefore
    /// the only layer that truly compounds — kept scarce and gated behind high rarity.
    /// </summary>
    public enum StatOp
    {
        Flat,
        Increased,
        More
    }

    /// <summary>
    /// One tag-filtered adjustment to one stat. Augments are just bundles of these
    /// (Docs/GAMEPLAY_SYSTEMS.md §3).
    /// </summary>
    public readonly struct StatModifier
    {
        public readonly StatId Stat;
        public readonly StatOp Op;
        public readonly float Value;
        public readonly TagMask Filter;

        public StatModifier(StatId stat, StatOp op, float value, TagMask filter)
        {
            Stat = stat;
            Op = op;
            Value = value;
            Filter = filter;
        }

        public StatModifier(StatId stat, StatOp op, float value)
            : this(stat, op, value, TagMask.Any) { }

        public override string ToString() => $"{Op} {Value:+0.##;-0.##} {Stat} {Filter}";
    }

    /// <summary>Opaque receipt for removing a modifier that was added.</summary>
    public readonly struct ModifierHandle
    {
        public readonly int Id;
        public ModifierHandle(int id) => Id = id;
        public bool IsValid => Id != 0;
        public static ModifierHandle None => new ModifierHandle(0);
    }
}
