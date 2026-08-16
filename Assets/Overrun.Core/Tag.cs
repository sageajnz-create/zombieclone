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
    ///
    /// UNDERLYING TYPE IS uint, NOT ulong — 32 tags maximum.
    /// Unity's serializer rejects enums with a 64-bit backing type outright:
    ///     "Unsupported enum type 'Overrun.Core.Tag' used for field 'Tags'"
    /// A ulong-backed Tag cannot appear on a ScriptableObject field, which would break the
    /// entire data-driven definition layer. 21 of 32 bits are used; if we ever approach
    /// the ceiling the fix is a serializable two-uint struct with implicit conversion to a
    /// 64-bit runtime value, NOT widening this enum.
    /// </summary>
    [Flags]
    public enum Tag : uint
    {
        None = 0u,

        // --- Source
        Weapon     = 1u << 0,
        Projectile = 1u << 1,
        Hitscan    = 1u << 2,
        Melee      = 1u << 3,
        Ability    = 1u << 4,
        Companion  = 1u << 5,
        Trap       = 1u << 6,
        Explosion  = 1u << 7,

        // --- Elements
        Shock = 1u << 8,
        Fire  = 1u << 9,
        Frost = 1u << 10,
        Toxic = 1u << 11,
        Void  = 1u << 12,

        // --- Roles
        Critical  = 1u << 16,
        Status    = 1u << 17,
        Defense   = 1u << 18,
        Mobility  = 1u << 19,
        Economy   = 1u << 20,
        Support   = 1u << 21,
        Elemental = 1u << 22,
        Area      = 1u << 23,

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
