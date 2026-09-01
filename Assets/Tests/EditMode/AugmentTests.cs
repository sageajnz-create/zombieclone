using NUnit.Framework;
using UnityEngine;
using Overrun.Core;
using Overrun.Data;
using Overrun.Simulation;

namespace Overrun.Tests
{
    [TestFixture]
    public class AugmentTests
    {
        private const float Tol = 0.0001f;

        [Test]
        public void ApplyTo_AddsThreeLayerModifiersToStatBlock()
        {
            var def = ScriptableObject.CreateInstance<AugmentDefinition>();
            def.Modifiers = new[]
            {
                new AuthoredModifier { Stat = StatId.Damage, Op = StatOp.Flat, Value = 5f },
                new AuthoredModifier { Stat = StatId.Damage, Op = StatOp.Increased, Value = 0.30f },
                new AuthoredModifier { Stat = StatId.Damage, Op = StatOp.More, Value = 0.50f }
            };

            var stats = new StatBlock();
            stats.SetBase(StatId.Damage, 10f);
            def.ApplyTo(stats);

            // (10 + 5) * 1.30 * 1.50 = 29.25
            Assert.AreEqual(29.25f, stats.Resolve(StatId.Damage), Tol);
        }

        [Test]
        public void Roll_ReturnsUniqueOffers_AndIsDeterministic()
        {
            var pool = new AugmentDefinition[6];
            for (int i = 0; i < pool.Length; i++)
            {
                pool[i] = ScriptableObject.CreateInstance<AugmentDefinition>();
                pool[i].DefinitionId = i + 1;
                pool[i].DisplayName = "A" + i;
                pool[i].MaxStacks = 1;
            }

            var seed = new RunSeed(0xC0FFEEUL);
            var a = new AugmentDefinition[AugmentOfferer.Choices];
            var b = new AugmentDefinition[AugmentOfferer.Choices];

            int na = AugmentOfferer.Roll(pool, seed.Stream(RngStream.AugmentOffers, 1), null, a);
            int nb = AugmentOfferer.Roll(pool, seed.Stream(RngStream.AugmentOffers, 1), null, b);

            Assert.AreEqual(3, na);
            Assert.AreEqual(3, nb);
            Assert.AreEqual(a[0].DefinitionId, b[0].DefinitionId);
            Assert.AreEqual(a[1].DefinitionId, b[1].DefinitionId);
            Assert.AreEqual(a[2].DefinitionId, b[2].DefinitionId);

            Assert.AreNotEqual(a[0].DefinitionId, a[1].DefinitionId);
            Assert.AreNotEqual(a[0].DefinitionId, a[2].DefinitionId);
            Assert.AreNotEqual(a[1].DefinitionId, a[2].DefinitionId);
        }

        [Test]
        public void Roll_SkipsOwnedMaxStackAugments()
        {
            var pool = new AugmentDefinition[3];
            for (int i = 0; i < 3; i++)
            {
                pool[i] = ScriptableObject.CreateInstance<AugmentDefinition>();
                pool[i].DefinitionId = i + 1;
                pool[i].MaxStacks = 1;
            }

            var owned = new System.Collections.Generic.HashSet<int> { 1, 2 };
            var results = new AugmentDefinition[3];
            int n = AugmentOfferer.Roll(pool, new RunSeed(9).Stream(RngStream.AugmentOffers, 1), owned, results);

            Assert.AreEqual(1, n);
            Assert.AreEqual(3, results[0].DefinitionId);
        }

        [Test]
        public void TryApply_WritesToPlayerStatBlock_AndRejectsSecondStack()
        {
            var def = ScriptableObject.CreateInstance<AugmentDefinition>();
            def.DefinitionId = 42;
            def.MaxStacks = 1;
            def.Modifiers = new[]
            {
                new AuthoredModifier { Stat = StatId.MoveSpeed, Op = StatOp.Increased, Value = 0.20f }
            };

            var state = new PlayerState(new PlayerId(0, 0));
            float before = state.Stats.MoveSpeed;

            Assert.IsTrue(AugmentOfferer.TryApply(def, state));
            Assert.AreEqual(before * 1.20f, state.Stats.MoveSpeed, Tol);
            Assert.IsTrue(state.HoldsAugment(42));
            Assert.IsFalse(AugmentOfferer.TryApply(def, state));
        }
    }
}
