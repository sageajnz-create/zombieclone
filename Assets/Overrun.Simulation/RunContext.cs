using System;
using System.Collections.Generic;
using Overrun.Core;
using Overrun.Data;

namespace Overrun.Simulation
{
    public enum RunPhase
    {
        Playing,
        OfferingAugments,
        Ended
    }

    /// <summary>
    /// Everything a run's simulation systems need, in one injected object.
    ///
    /// This exists so that WaveDirector, enemies and weapons can reach the player roster,
    /// the seeded RNG and the proc guards WITHOUT a static Instance. Systems receive a
    /// RunContext; they never look one up. See the banned-patterns table in
    /// Docs/ARCHITECTURE.md §1.
    ///
    /// Server-authoritative: only the server constructs or mutates one.
    /// </summary>
    public sealed class RunContext
    {
        public PlayerRegistry Players { get; }
        public ProcBudget Procs { get; }
        public RunSeed Seed { get; private set; }

        /// <summary>1-based. Round 0 means the run has not started.</summary>
        public int Round { get; private set; }

        public RunPhase Phase { get; private set; }

        public AugmentDefinition[] AugmentPool { get; set; }

        private readonly HashSet<int> _unlockedRegions = new HashSet<int> { 0 };
        private readonly AugmentDefinition[] _offerScratch = new AugmentDefinition[AugmentOfferer.Choices];

        /// <summary>Raised on the server when anything with Health dies. (killer, victim)</summary>
        public event Action<PlayerId, Health> Killed;

        /// <summary>Raised on the server when the round advances.</summary>
        public event Action<int> RoundStarted;

        public event Action<int> RoundCleared;
        public event Action<PlayerId, AugmentDefinition[]> AugmentOffered;
        public event Action OffersResolved;
        public event Action RunEnded;
        public event Action RunReset;
        public event Action<int> RegionUnlocked;

        public RunContext(RunSeed seed, PlayerRegistry players = null, ProcBudget procs = null)
        {
            Seed = seed;
            Players = players ?? new PlayerRegistry();
            Procs = procs ?? new ProcBudget();
            Phase = RunPhase.Playing;
        }

        public bool IsRegionUnlocked(int regionId) => _unlockedRegions.Contains(regionId);

        public void UnlockRegion(int regionId)
        {
            if (!_unlockedRegions.Add(regionId)) return;
            RegionUnlocked?.Invoke(regionId);
        }

        public void AdvanceRound()
        {
            Round++;
            Phase = RunPhase.Playing;
            RoundStarted?.Invoke(Round);
        }

        /// <summary>
        /// Report a death. Scrip is awarded here rather than inside Health so that the
        /// economy stays in one place and Health remains reusable for players, enemies
        /// and destructibles alike.
        /// </summary>
        public void ReportKill(PlayerId killer, Health victim, int scripReward)
        {
            if (killer.IsValid && Players.TryGet(killer, out PlayerState state))
            {
                if (scripReward > 0) state.AwardScrip(scripReward);
                state.Kills++;
            }

            Killed?.Invoke(killer, victim);
        }

        public void NotifyPlayerDied(PlayerId id)
        {
            if (Players.TryGet(id, out PlayerState state))
            {
                state.IsAlive = false;
                state.IsDowned = false;
                state.ClearOffers();
            }

            if (CountAlive() == 0) EndRun();
            else if (Phase == RunPhase.OfferingAugments) TryResolveOffers();
        }

        public void NotifyRoundCleared()
        {
            if (Phase == RunPhase.Ended) return;
            RoundCleared?.Invoke(Round);
            BeginAugmentOffers();
        }

        public void BeginAugmentOffers()
        {
            if (Phase == RunPhase.Ended) return;

            AugmentDefinition[] pool = AugmentPool;
            if (pool == null || pool.Length == 0)
            {
                Phase = RunPhase.Playing;
                OffersResolved?.Invoke();
                return;
            }

            Phase = RunPhase.OfferingAugments;
            int pending = 0;
            IReadOnlyList<PlayerState> all = Players.All;

            for (int i = 0; i < all.Count; i++)
            {
                PlayerState state = all[i];
                if (!state.IsAlive) continue;

                DeterministicRandom rng = Seed.Stream(RngStream.AugmentOffers, Round * 16 + state.Id.LocalSlot + 1);
                int n = AugmentOfferer.Roll(pool, rng, OwnedSet(state), _offerScratch);
                state.SetPendingOffers(_offerScratch, n);
                if (n > 0)
                {
                    pending++;
                    AugmentDefinition[] copy = new AugmentDefinition[n];
                    for (int k = 0; k < n; k++) copy[k] = _offerScratch[k];
                    AugmentOffered?.Invoke(state.Id, copy);
                }
                else
                {
                    state.MarkOfferChosen();
                }
            }

            if (pending == 0)
            {
                Phase = RunPhase.Playing;
                OffersResolved?.Invoke();
            }
        }

        public bool TryChooseAugment(PlayerId id, int offerIndex)
        {
            if (Phase != RunPhase.OfferingAugments) return false;
            if (!Players.TryGet(id, out PlayerState state) || !state.IsAlive) return false;
            if (!state.HasPendingOffer) return false;

            AugmentDefinition def = state.GetPendingOffer(offerIndex);
            if (def == null) return false;
            if (!AugmentOfferer.TryApply(def, state)) return false;

            state.MarkOfferChosen();
            TryResolveOffers();
            return true;
        }

        public void EndRun()
        {
            if (Phase == RunPhase.Ended) return;
            Phase = RunPhase.Ended;
            RunEnded?.Invoke();
        }

        public void Reset(RunSeed seed)
        {
            Seed = seed;
            Round = 0;
            Phase = RunPhase.Playing;
            Procs.Clear();
            _unlockedRegions.Clear();
            _unlockedRegions.Add(0);

            IReadOnlyList<PlayerState> all = Players.All;
            for (int i = 0; i < all.Count; i++) all[i].ResetForNewRun();

            RunReset?.Invoke();
        }

        private void TryResolveOffers()
        {
            IReadOnlyList<PlayerState> all = Players.All;
            for (int i = 0; i < all.Count; i++)
            {
                PlayerState s = all[i];
                if (s.IsAlive && s.HasPendingOffer) return;
            }

            Phase = RunPhase.Playing;
            OffersResolved?.Invoke();
        }

        private int CountAlive()
        {
            int n = 0;
            IReadOnlyList<PlayerState> all = Players.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].IsAlive && !all[i].IsDowned) n++;
            }
            return n;
        }

        private static HashSet<int> OwnedSet(PlayerState state)
        {
            var set = new HashSet<int>();
            IReadOnlyList<int> owned = state.OwnedAugments;
            for (int i = 0; i < owned.Count; i++) set.Add(owned[i]);
            return set;
        }
    }
}
