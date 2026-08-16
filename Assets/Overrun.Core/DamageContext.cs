using UnityEngine;

namespace Overrun.Core
{
    /// <summary>
    /// One damage instance travelling through the resolution pipeline. Mutable by design:
    /// each stage (base -> weapon stats -> augment modifiers -> crit -> resistances) reads
    /// and adjusts it, so every stage is inspectable and augments can hook any of them.
    ///
    /// See Docs/GAMEPLAY_SYSTEMS.md §4.
    /// </summary>
    public sealed class DamageContext
    {
        /// <summary>Who caused this. Always a PlayerId — never a ClientId.</summary>
        public PlayerId Source;

        /// <summary>What this damage *is*. Drives every tag-filtered modifier.</summary>
        public Tag Tags;

        public float Amount;
        public bool IsCritical;

        public Vector3 HitPoint;
        public Vector3 HitNormal;

        /// <summary>
        /// How many effect-spawned-effect hops produced this instance. A direct weapon hit
        /// is 0; chain lightning triggered by that hit is 1. Capped by
        /// <see cref="ProcBudget.MaxProcDepth"/> — without it, an effect that re-enters the
        /// pipeline and re-triggers itself is an unbounded recursion on the server.
        /// </summary>
        public byte ProcDepth;

        public void Reset()
        {
            Source = PlayerId.None;
            Tags = Tag.None;
            Amount = 0f;
            IsCritical = false;
            HitPoint = Vector3.zero;
            HitNormal = Vector3.zero;
            ProcDepth = 0;
        }

        public void Set(PlayerId source, Tag tags, float amount, Vector3 hitPoint, byte procDepth = 0)
        {
            Source = source;
            Tags = tags;
            Amount = amount;
            HitPoint = hitPoint;
            ProcDepth = procDepth;
            IsCritical = false;
            HitNormal = Vector3.zero;
        }

        /// <summary>Derive a child context for an effect spawned by this one.</summary>
        public DamageContext CreateProc(Tag procTags, float amount)
        {
            return new DamageContext
            {
                Source = Source,
                Tags = procTags,
                Amount = amount,
                HitPoint = HitPoint,
                HitNormal = HitNormal,
                ProcDepth = (byte)(ProcDepth + 1),
                IsCritical = false
            };
        }
    }
}
