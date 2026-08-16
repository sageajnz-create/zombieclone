using NUnit.Framework;
using Overrun.Core;
using UnityEngine;

namespace Overrun.Tests
{
    /// <summary>
    /// The guards from Docs/GAMEPLAY_SYSTEMS.md §4. These exist to stop an augment build
    /// from recursing the server to death, so they are worth testing before any augment
    /// content exists.
    /// </summary>
    [TestFixture]
    public class ProcBudgetTests
    {
        [Test]
        public void DepthBelowCap_IsAllowed()
        {
            var budget = new ProcBudget();
            budget.BeginTick();

            for (byte d = 0; d < ProcBudget.MaxProcDepth; d++)
            {
                Assert.IsTrue(budget.TrySpend(d), $"depth {d} should be allowed");
            }
        }

        [Test]
        public void DepthAtOrAboveCap_IsRefused()
        {
            var budget = new ProcBudget();
            budget.BeginTick();

            Assert.IsFalse(budget.TrySpend(ProcBudget.MaxProcDepth));
            Assert.IsFalse(budget.TrySpend((byte)(ProcBudget.MaxProcDepth + 1)));
            Assert.AreEqual(2, budget.DroppedThisTick);
        }

        [Test]
        public void PerTickBudget_IsEnforced_AndExcessIsDroppedNotQueued()
        {
            var budget = new ProcBudget(perTick: 3);
            budget.BeginTick();

            Assert.IsTrue(budget.TrySpend(0));
            Assert.IsTrue(budget.TrySpend(0));
            Assert.IsTrue(budget.TrySpend(0));
            Assert.IsFalse(budget.TrySpend(0), "fourth proc must be refused");

            Assert.AreEqual(3, budget.SpentThisTick);
            Assert.AreEqual(1, budget.DroppedThisTick);
        }

        [Test]
        public void BeginTick_ResetsBudgetButDoesNotCarryDebt()
        {
            var budget = new ProcBudget(perTick: 2);

            budget.BeginTick();
            budget.TrySpend(0);
            budget.TrySpend(0);
            Assert.IsFalse(budget.TrySpend(0));

            budget.BeginTick();
            Assert.AreEqual(0, budget.SpentThisTick);
            Assert.AreEqual(0, budget.DroppedThisTick);
            Assert.IsTrue(budget.TrySpend(0), "next tick starts fresh, no queued backlog");
        }

        [Test]
        public void SameEffect_SameSourceAndVictim_CannotRetriggerInsideWindow()
        {
            var budget = new ProcBudget(perTick: 64, minRetriggerSeconds: 0.1f);
            var source = new PlayerId(0, 0);

            Assert.IsTrue(budget.TryFire(source, victimId: 7, effectId: 1, now: 0f));
            Assert.IsFalse(budget.TryFire(source, victimId: 7, effectId: 1, now: 0.05f));
            Assert.IsTrue(budget.TryFire(source, victimId: 7, effectId: 1, now: 0.20f));
        }

        [Test]
        public void CooldownIsScopedPerVictimAndPerEffectAndPerSource()
        {
            var budget = new ProcBudget(perTick: 64, minRetriggerSeconds: 0.1f);
            var p0 = new PlayerId(0, 0);
            var p1 = new PlayerId(0, 1);   // same machine, different couch player

            Assert.IsTrue(budget.TryFire(p0, 7, 1, 0f));
            Assert.IsTrue(budget.TryFire(p0, 8, 1, 0f), "different victim is independent");
            Assert.IsTrue(budget.TryFire(p0, 7, 2, 0f), "different effect is independent");
            Assert.IsTrue(budget.TryFire(p1, 7, 1, 0f), "different local slot is a different player");
        }

        [Test]
        public void CreateProc_IncrementsDepthAndInheritsSource()
        {
            var ctx = new DamageContext();
            ctx.Set(new PlayerId(3, 1), Tag.Weapon | Tag.Hitscan, 50f, Vector3.zero);

            var child = ctx.CreateProc(Tag.Shock | Tag.Status, 10f);

            Assert.AreEqual(1, child.ProcDepth);
            Assert.AreEqual(ctx.Source, child.Source);
            Assert.AreEqual(Tag.Shock | Tag.Status, child.Tags);
            Assert.IsFalse(child.IsCritical, "a proc does not inherit the parent's crit");

            var grandchild = child.CreateProc(Tag.Shock, 5f);
            Assert.AreEqual(2, grandchild.ProcDepth);
        }

        [Test]
        public void ChainOfProcs_TerminatesAtTheDepthCap()
        {
            // Simulates chain-lightning triggering chain-lightning without bound.
            var budget = new ProcBudget(perTick: 1000);
            budget.BeginTick();

            var ctx = new DamageContext();
            ctx.Set(new PlayerId(0, 0), Tag.Weapon, 100f, Vector3.zero);

            int hops = 0;
            var current = ctx;
            while (budget.TrySpend(current.ProcDepth) && hops < 100)
            {
                current = current.CreateProc(Tag.Shock, current.Amount * 0.5f);
                hops++;
            }

            Assert.AreEqual(ProcBudget.MaxProcDepth, hops, "chain must stop at the depth cap, not run away");
        }
    }
}
