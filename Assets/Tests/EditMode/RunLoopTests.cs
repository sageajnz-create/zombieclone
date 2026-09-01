using NUnit.Framework;
using UnityEngine;
using Overrun.Core;
using Overrun.Data;
using Overrun.Simulation;

namespace Overrun.Tests
{
    [TestFixture]
    public class RunLoopTests
    {
        [Test]
        public void StartingRoom_IsUnlocked_RoomTwoIsNot()
        {
            var run = new RunContext(new RunSeed(1));
            Assert.IsTrue(run.IsRegionUnlocked(0));
            Assert.IsFalse(run.IsRegionUnlocked(1));
        }

        [Test]
        public void UnlockRegion_OpensRoomTwo_AndDoesNotRepeat()
        {
            var run = new RunContext(new RunSeed(1));
            int raised = 0;
            run.RegionUnlocked += _ => raised++;

            run.UnlockRegion(1);
            run.UnlockRegion(1);

            Assert.IsTrue(run.IsRegionUnlocked(1));
            Assert.AreEqual(1, raised);
        }

        [Test]
        public void TrySpendScrip_IsPerPlayer()
        {
            var a = new PlayerState(new PlayerId(0, 0));
            var b = new PlayerState(new PlayerId(0, 1));
            a.AwardScrip(80);
            b.AwardScrip(10);

            Assert.IsTrue(a.TrySpendScrip(80));
            Assert.AreEqual(0, a.Scrip);
            Assert.IsFalse(b.TrySpendScrip(80));
            Assert.AreEqual(10, b.Scrip);
        }

        [Test]
        public void LastLivingPlayerDeath_EndsTheRun()
        {
            var run = new RunContext(new RunSeed(1));
            var p0 = run.Players.Register(new PlayerId(0, 0));
            var p1 = run.Players.Register(new PlayerId(0, 1));

            run.NotifyPlayerDied(p0.Id);
            Assert.AreNotEqual(RunPhase.Ended, run.Phase);
            Assert.IsFalse(p0.IsAlive);
            Assert.IsTrue(p1.IsAlive);

            run.NotifyPlayerDied(p1.Id);
            Assert.AreEqual(RunPhase.Ended, run.Phase);
        }

        [Test]
        public void Reset_ClearsRoundScripAugmentsAndRelocksRegions()
        {
            var run = new RunContext(new RunSeed(1));
            var p = run.Players.Register(new PlayerId(0, 0));
            p.AwardScrip(50);
            p.RecordAugment(7);
            run.UnlockRegion(1);
            run.AdvanceRound();
            run.AdvanceRound();

            run.Reset(new RunSeed(99));

            Assert.AreEqual(0, run.Round);
            Assert.AreEqual(RunPhase.Playing, run.Phase);
            Assert.IsFalse(run.IsRegionUnlocked(1));
            Assert.IsTrue(run.IsRegionUnlocked(0));
            Assert.AreEqual(0, p.Scrip);
            Assert.IsFalse(p.HoldsAugment(7));
            Assert.IsTrue(p.IsAlive);
        }

        [Test]
        public void ChooseAugment_RequiresOfferPhaseAndValidIndex()
        {
            var run = new RunContext(new RunSeed(5));
            var p = run.Players.Register(new PlayerId(0, 0));

            Assert.IsFalse(run.TryChooseAugment(p.Id, 0), "no offers yet");
        }

        [Test]
        public void BetweenRounds_OfferAndChoose_AppliesModifier()
        {
            var pool = new AugmentDefinition[6];
            for (int i = 0; i < pool.Length; i++)
            {
                var def = ScriptableObject.CreateInstance<AugmentDefinition>();
                def.DefinitionId = i + 1;
                def.MaxStacks = 1;
                def.Modifiers = new[]
                {
                    new AuthoredModifier
                    {
                        Stat = StatId.Damage,
                        Op = StatOp.Increased,
                        Value = 0.10f * (i + 1)
                    }
                };
                pool[i] = def;
            }

            var run = new RunContext(new RunSeed(0xC0FFEEUL));
            run.AugmentPool = pool;
            var p = run.Players.Register(new PlayerId(0, 0));
            run.AdvanceRound();
            run.NotifyRoundCleared();

            Assert.AreEqual(RunPhase.OfferingAugments, run.Phase);
            Assert.IsTrue(p.HasPendingOffer);
            Assert.AreEqual(3, p.PendingOfferCount);

            float before = p.Stats.Resolve(StatId.Damage);
            Assert.IsTrue(run.TryChooseAugment(p.Id, 0));
            Assert.AreEqual(RunPhase.Playing, run.Phase);
            Assert.Greater(p.Stats.Resolve(StatId.Damage), before);
            Assert.IsFalse(p.HasPendingOffer);
        }
    }
}
