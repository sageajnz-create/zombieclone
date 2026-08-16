namespace Overrun.Core
{
    /// <summary>
    /// Named RNG streams. Each stream advances independently, so consuming augment
    /// offers cannot shift what the loot table rolls. See Docs/ARCHITECTURE.md §9.
    /// </summary>
    public enum RngStream
    {
        AugmentOffers,
        LootRolls,
        WaveComposition,
        ShopInventory,
        EventChoice,
        ModifierChoice,
        EnemyModifiers,
        BossModifiers
    }

    /// <summary>
    /// 64-bit run seed owned by the server. Determines *content selection* only —
    /// not physics, not lockstep. See Docs/DECISIONS.md ADR-006.
    /// </summary>
    public readonly struct RunSeed
    {
        public readonly ulong Value;

        public RunSeed(ulong value) => Value = value;

        /// <summary>
        /// Derive an independent generator for one stream in one round.
        /// Mixed through SplitMix64 so adjacent (stream, round) pairs do not produce
        /// correlated sequences.
        /// </summary>
        public DeterministicRandom Stream(RngStream stream, int round)
        {
            ulong s = Value;
            s = Mix(s ^ ((ulong)stream + 1UL) * 0x9E3779B97F4A7C15UL);
            s = Mix(s ^ ((ulong)(uint)round + 1UL) * 0xBF58476D1CE4E5B9UL);
            return new DeterministicRandom(s);
        }

        private static ulong Mix(ulong x)
        {
            x += 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }
    }

    /// <summary>
    /// Deterministic 64-bit PRNG (xorshift64*).
    ///
    /// Deliberately not System.Random: its constructor takes an int, which would
    /// discard half of a 64-bit stream seed and let distinct streams collide.
    /// Deliberately not UnityEngine.Random: that is global mutable static state, so
    /// any unrelated caller rolling a value would shift authoritative outcomes.
    /// </summary>
    public sealed class DeterministicRandom
    {
        private ulong _state;

        public DeterministicRandom(ulong seed)
        {
            // xorshift64* degenerates to all-zero if seeded with 0.
            _state = seed == 0UL ? 0x9E3779B97F4A7C15UL : seed;
        }

        public ulong NextULong()
        {
            ulong x = _state;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            _state = x;
            return x * 0x2545F4914F6CDD1DUL;
        }

        /// <summary>Uniform in [0, maxExclusive). Rejection-sampled to avoid modulo bias.</summary>
        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 1) return 0;

            ulong bound = (ulong)maxExclusive;
            ulong limit = ulong.MaxValue - (ulong.MaxValue % bound) - 1UL;

            ulong r;
            do { r = NextULong(); } while (r > limit);

            return (int)(r % bound);
        }

        /// <summary>Uniform in [minInclusive, maxExclusive).</summary>
        public int Range(int minInclusive, int maxExclusive) =>
            minInclusive + Next(maxExclusive - minInclusive);

        /// <summary>Uniform in [0, 1).</summary>
        public float NextFloat() => (NextULong() >> 40) * (1.0f / 16777216.0f);

        public bool NextBool() => (NextULong() & 1UL) == 1UL;

        /// <summary>True with the given probability in [0, 1].</summary>
        public bool Chance(float probability) => NextFloat() < probability;
    }
}
