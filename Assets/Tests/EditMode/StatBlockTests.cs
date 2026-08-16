using NUnit.Framework;
using Overrun.Core;

namespace Overrun.Tests
{
    /// <summary>
    /// Locks in the resolution formula from Docs/GAMEPLAY_SYSTEMS.md §1:
    ///     final = (base + sum Flat) * (1 + sum Increased) * product(1 + More)
    /// </summary>
    [TestFixture]
    public class StatBlockTests
    {
        private const float Tol = 0.0001f;

        [Test]
        public void BareStat_ReturnsBase()
        {
            var stats = new StatBlock();
            stats.SetBase(StatId.Damage, 10f);

            Assert.AreEqual(10f, stats.Resolve(StatId.Damage), Tol);
        }

        [Test]
        public void AllThreeLayers_ApplyInDocumentedOrder()
        {
            var stats = new StatBlock();
            stats.SetBase(StatId.Damage, 10f);

            stats.Add(new StatModifier(StatId.Damage, StatOp.Flat, 5f));
            stats.Add(new StatModifier(StatId.Damage, StatOp.Increased, 0.30f));
            stats.Add(new StatModifier(StatId.Damage, StatOp.More, 0.50f));
            stats.Add(new StatModifier(StatId.Damage, StatOp.More, 0.50f));

            // (10 + 5) * (1 + 0.30) * 1.5 * 1.5 = 43.875
            Assert.AreEqual(43.875f, stats.Resolve(StatId.Damage), Tol);
        }

        [Test]
        public void IncreasedStacksAdditively_NotMultiplicatively()
        {
            // This is the whole point of splitting Increased from More: repeated
            // Increased must suffer diminishing relative value, or linear upgrade
            // acquisition produces runaway scaling.
            var stats = new StatBlock();
            stats.SetBase(StatId.Damage, 100f);
            stats.Add(new StatModifier(StatId.Damage, StatOp.Increased, 0.30f));
            stats.Add(new StatModifier(StatId.Damage, StatOp.Increased, 0.30f));

            Assert.AreEqual(160f, stats.Resolve(StatId.Damage), Tol, "expected additive 1+0.6");
            Assert.AreNotEqual(169f, stats.Resolve(StatId.Damage), "must not compound like More");
        }

        [Test]
        public void MoreStacksMultiplicatively()
        {
            var stats = new StatBlock();
            stats.SetBase(StatId.Damage, 100f);
            stats.Add(new StatModifier(StatId.Damage, StatOp.More, 0.50f));
            stats.Add(new StatModifier(StatId.Damage, StatOp.More, 0.50f));

            Assert.AreEqual(225f, stats.Resolve(StatId.Damage), Tol);
        }

        [Test]
        public void TagFilteredModifier_OnlyAppliesToMatchingContext()
        {
            var stats = new StatBlock();
            stats.SetBase(StatId.Damage, 100f);

            // "+40% increased Damage with Shock" — data, not a code path.
            stats.Add(new StatModifier(StatId.Damage, StatOp.Increased, 0.40f,
                                       new TagMask(Tag.Shock)));

            Assert.AreEqual(140f, stats.Resolve(StatId.Damage, Tag.Shock | Tag.Projectile), Tol);
            Assert.AreEqual(100f, stats.Resolve(StatId.Damage, Tag.Fire | Tag.Projectile), Tol);
            Assert.AreEqual(100f, stats.Resolve(StatId.Damage), Tol);
        }

        [Test]
        public void ExcludedTag_BlocksOtherwiseMatchingModifier()
        {
            var stats = new StatBlock();
            stats.SetBase(StatId.Damage, 100f);
            stats.Add(new StatModifier(StatId.Damage, StatOp.Increased, 1f,
                                       new TagMask(Tag.Projectile, Tag.Explosion)));

            Assert.AreEqual(200f, stats.Resolve(StatId.Damage, Tag.Projectile), Tol);
            Assert.AreEqual(100f, stats.Resolve(StatId.Damage, Tag.Projectile | Tag.Explosion), Tol);
        }

        [Test]
        public void RequiredTags_AreAllOf_NotAnyOf()
        {
            var stats = new StatBlock();
            stats.SetBase(StatId.Damage, 100f);
            stats.Add(new StatModifier(StatId.Damage, StatOp.Increased, 1f,
                                       new TagMask(Tag.Shock | Tag.Critical)));

            Assert.AreEqual(200f, stats.Resolve(StatId.Damage, Tag.Shock | Tag.Critical), Tol);
            Assert.AreEqual(100f, stats.Resolve(StatId.Damage, Tag.Shock), Tol, "partial match must not apply");
        }

        [Test]
        public void RemovedModifier_NoLongerApplies_AndCacheIsInvalidated()
        {
            var stats = new StatBlock();
            stats.SetBase(StatId.Damage, 100f);
            var handle = stats.Add(new StatModifier(StatId.Damage, StatOp.Increased, 1f));

            Assert.AreEqual(200f, stats.Resolve(StatId.Damage), Tol);   // populates cache

            Assert.IsTrue(stats.Remove(handle));
            Assert.AreEqual(100f, stats.Resolve(StatId.Damage), Tol, "stale cache after Remove");
        }

        [Test]
        public void SetBase_InvalidatesCache()
        {
            var stats = new StatBlock();
            stats.SetBase(StatId.Damage, 10f);
            Assert.AreEqual(10f, stats.Resolve(StatId.Damage), Tol);

            stats.SetBase(StatId.Damage, 20f);
            Assert.AreEqual(20f, stats.Resolve(StatId.Damage), Tol, "stale cache after SetBase");
        }

        [Test]
        public void DistinctTagContexts_DoNotShareCacheEntries()
        {
            // Regression guard: a packed scalar cache key could collide across
            // (stat, tags) pairs and silently return another context's value.
            var stats = new StatBlock();
            stats.SetBase(StatId.Damage, 100f);
            stats.Add(new StatModifier(StatId.Damage, StatOp.Increased, 1f, new TagMask(Tag.Shock)));

            Assert.AreEqual(200f, stats.Resolve(StatId.Damage, Tag.Shock), Tol);
            Assert.AreEqual(100f, stats.Resolve(StatId.Damage, Tag.Fire), Tol);
            Assert.AreEqual(100f, stats.Resolve(StatId.Damage, Tag.AnyElement & ~Tag.Shock), Tol);
            Assert.AreEqual(200f, stats.Resolve(StatId.Damage, Tag.Shock), Tol);
        }

        [Test]
        public void ModifierOnOneStat_DoesNotLeakToAnother()
        {
            var stats = new StatBlock();
            stats.SetBase(StatId.Damage, 100f);
            stats.SetBase(StatId.MoveSpeed, 5f);
            stats.Add(new StatModifier(StatId.Damage, StatOp.Increased, 1f));

            Assert.AreEqual(5f, stats.Resolve(StatId.MoveSpeed), Tol);
        }

        // --- ResolveFor: modifiers applied to an externally-owned base ------------------

        [Test]
        public void ResolveFor_AppliesModifiersToTheSuppliedBase()
        {
            var stats = new StatBlock();
            stats.SetBase(StatId.Damage, 999f);           // must be ignored by ResolveFor
            stats.Add(new StatModifier(StatId.Damage, StatOp.Flat, 5f));
            stats.Add(new StatModifier(StatId.Damage, StatOp.Increased, 0.5f));

            // (10 + 5) * 1.5 = 22.5, using the weapon's base rather than the block's.
            Assert.AreEqual(22.5f, stats.ResolveFor(10f, StatId.Damage), Tol);
        }

        [Test]
        public void ResolveFor_DifferentBases_DoNotContaminateEachOther()
        {
            // Two weapons, one player. The shared modifier cache stores layers, not
            // results — if it stored results, the second weapon would inherit the first's.
            var stats = new StatBlock();
            stats.Add(new StatModifier(StatId.Damage, StatOp.Increased, 1f));

            Assert.AreEqual(20f, stats.ResolveFor(10f, StatId.Damage), Tol);
            Assert.AreEqual(200f, stats.ResolveFor(100f, StatId.Damage), Tol);
            Assert.AreEqual(20f, stats.ResolveFor(10f, StatId.Damage), Tol, "first weapon changed after second resolved");
        }

        [Test]
        public void ResolveFor_RespectsTagFiltering()
        {
            var stats = new StatBlock();
            stats.Add(new StatModifier(StatId.Damage, StatOp.Increased, 1f, new TagMask(Tag.Shock)));

            Assert.AreEqual(50f, stats.ResolveFor(25f, StatId.Damage, Tag.Shock), Tol);
            Assert.AreEqual(25f, stats.ResolveFor(25f, StatId.Damage, Tag.Fire), Tol);
        }

        [Test]
        public void ResolveFor_AndResolve_AgreeWhenBasesMatch()
        {
            var stats = new StatBlock();
            stats.SetBase(StatId.Damage, 40f);
            stats.Add(new StatModifier(StatId.Damage, StatOp.More, 0.25f));

            Assert.AreEqual(stats.Resolve(StatId.Damage), stats.ResolveFor(40f, StatId.Damage), Tol);
        }

        [Test]
        public void ResolveFor_SeesModifiersAddedAfterAnEarlierResolve()
        {
            var stats = new StatBlock();
            Assert.AreEqual(10f, stats.ResolveFor(10f, StatId.Damage), Tol);   // populates cache

            stats.Add(new StatModifier(StatId.Damage, StatOp.Increased, 1f));
            Assert.AreEqual(20f, stats.ResolveFor(10f, StatId.Damage), Tol, "stale layer cache after Add");
        }
    }
}
