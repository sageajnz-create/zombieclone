using System;

namespace Overrun.Core
{
    /// <summary>
    /// Tags describe what an action, weapon, augment, or effect *is*. Modifiers filter on
    /// them, which is the mechanism that lets independently-authored augments interact
    /// without any of them knowing the others exist (Docs/GAMEPLAY_SYSTEMS.md §2).
    ///
    /// "Lightning Rounds" (Projectile|Elemental|Shock), "Conductive Blood" (Shock|Status)
    /// and "Overcharge" (Shock|Critical) form an electrical build purely because each
    /// filters on Shock. No code knows those three are related.
    /// </summary>
    [Flags]
    public enum Tag : ulong
    {
        None = 0UL,

        // --- Source
        Weapon     = 1UL << 0,
        Projectile = 1UL << 1,
        Hitscan    = 1UL << 2,
        Melee      = 1UL << 3,
        Ability    = 1UL << 4,
        Companion  = 1UL << 5,
        Trap       = 1UL << 6,
        Explosion  = 1UL << 7,

        // --- Elements
        Shock = 1UL << 8,
        Fire  = 1UL << 9,
        Frost = 1UL << 10,
        Toxic = 1UL << 11,
        Void  = 1UL << 12,

        // --- Roles
        Critical  = 1UL << 16,
        Status    = 1UL << 17,
        Defense   = 1UL << 18,
        Mobility  = 1UL << 19,
        Economy   = 1UL << 20,
        Support   = 1UL << 21,
        Elemental = 1UL << 22,
        Area      = 1UL << 23,

        AnyElement = Shock | Fire | Frost | Toxic | Void
    }

    /// <summary>
    /// A filter over <see cref="Tag"/>. A modifier declares one; the action declares its
    /// own tags; the modifier applies only when the filter matches.
    /// </summary>
    public readonly struct TagMask
    {
        /// <summary>All of these must be present. None = unconditional.</summary>
        public readonly Tag Required;

        /// <summary>Any of these present disqualifies the match.</summary>
        public readonly Tag Excluded;

        public TagMask(Tag required, Tag excluded = Tag.None)
        {
            Required = required;
            Excluded = excluded;
        }

        /// <summary>Matches everything. The default for unconditional modifiers.</summary>
        public static TagMask Any => new TagMask(Tag.None, Tag.None);

        public bool Matches(Tag context)
        {
            if ((context & Excluded) != Tag.None) return false;
            return (context & Required) == Required;
        }

        public override string ToString() =>
            Excluded == Tag.None ? $"[{Required}]" : $"[{Required} !{Excluded}]";
    }
}
