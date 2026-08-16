using NUnit.Framework;
using Overrun.Core;

namespace Overrun.Tests
{
    /// <summary>
    /// Boundary B: two couch players share a ClientId, so PlayerId equality must be
    /// driven by the slot as well. Ported from the pre-Unity Tests/CoreTests project.
    /// </summary>
    [TestFixture]
    public class PlayerIdTests
    {
        [Test]
        public void DifferentSlots_SameClient_AreNotEqual()
        {
            Assert.AreNotEqual(new PlayerId(100, 0), new PlayerId(100, 1));
        }

        [Test]
        public void SameSlot_SameClient_AreEqual()
        {
            Assert.AreEqual(new PlayerId(100, 0), new PlayerId(100, 0));
        }

        [Test]
        public void SameSlot_DifferentClients_AreNotEqual()
        {
            Assert.AreNotEqual(new PlayerId(100, 0), new PlayerId(101, 0));
        }

        [Test]
        public void EqualIds_ShareAHashCode()
        {
            Assert.AreEqual(new PlayerId(42, 2).GetHashCode(), new PlayerId(42, 2).GetHashCode());
        }

        [Test]
        public void Operators_MatchEquals()
        {
            var a = new PlayerId(5, 1);
            var b = new PlayerId(5, 1);
            var c = new PlayerId(5, 2);

            Assert.IsTrue(a == b);
            Assert.IsFalse(a != b);
            Assert.IsTrue(a != c);
        }

        [Test]
        public void None_IsNotValid_AndRealIdsAre()
        {
            Assert.IsFalse(PlayerId.None.IsValid);
            Assert.IsTrue(new PlayerId(0, 0).IsValid, "client 0 is the host, which is a real player");
        }
    }

    /// <summary>
    /// Seed determinism covers content selection (ADR-006). Ported and adapted to the
    /// rewritten 64-bit generator — the original tests called a parameterless Next().
    /// </summary>
    [TestFixture]
    public class RunSeedTests
    {
        [Test]
        public void DifferentStreams_ProduceDifferentSequences()
        {
            var seed = new RunSeed(12345);
            var a = seed.Stream(RngStream.AugmentOffers, 1);
            var b = seed.Stream(RngStream.LootRolls, 1);

            bool different = false;
            for (int i = 0; i < 10 && !different; i++)
            {
                if (a.NextULong() != b.NextULong()) different = true;
            }

            Assert.IsTrue(different, "different streams must not produce identical sequences");
        }

        [Test]
        public void SameStream_SameSeed_SameRound_IsDeterministic()
        {
            var seed = new RunSeed(12345);
            var s1 = seed.Stream(RngStream.AugmentOffers, 1);
            var s2 = seed.Stream(RngStream.AugmentOffers, 1);

            for (int i = 0; i < 8; i++) Assert.AreEqual(s1.NextULong(), s2.NextULong());
        }

        [Test]
        public void DifferentRounds_ProduceDifferentSequences()
        {
            var seed = new RunSeed(999);
            var r1 = seed.Stream(RngStream.WaveComposition, 1);
            var r2 = seed.Stream(RngStream.WaveComposition, 2);

            Assert.AreNotEqual(r1.NextULong(), r2.NextULong());
        }

        [Test]
        public void ConsumingOneStream_DoesNotShiftAnother()
        {
            // The reason streams exist at all: drawing augment offers must not change
            // what the loot table rolls.
            var seed = new RunSeed(777);

            var lootBaseline = seed.Stream(RngStream.LootRolls, 3).NextULong();

            var augments = seed.Stream(RngStream.AugmentOffers, 3);
            for (int i = 0; i < 50; i++) augments.NextULong();

            Assert.AreEqual(lootBaseline, seed.Stream(RngStream.LootRolls, 3).NextULong());
        }

        [Test]
        public void Next_StaysInRange()
        {
            var rng = new RunSeed(4242).Stream(RngStream.EventChoice, 1);
            for (int i = 0; i < 500; i++)
            {
                int v = rng.Next(7);
                Assert.GreaterOrEqual(v, 0);
                Assert.Less(v, 7);
            }
        }

        [Test]
        public void NextFloat_StaysInUnitInterval()
        {
            var rng = new RunSeed(31337).Stream(RngStream.ModifierChoice, 2);
            for (int i = 0; i < 500; i++)
            {
                float v = rng.NextFloat();
                Assert.GreaterOrEqual(v, 0f);
                Assert.Less(v, 1f);
            }
        }

        [Test]
        public void ZeroSeed_DoesNotDegenerate()
        {
            // xorshift64* collapses to all-zero if seeded with 0.
            var rng = new DeterministicRandom(0);
            Assert.AreNotEqual(0UL, rng.NextULong());
            Assert.AreNotEqual(0UL, rng.NextULong());
        }
    }
}
