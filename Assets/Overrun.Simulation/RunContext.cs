using System;
using Overrun.Core;

namespace Overrun.Simulation
{
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
        public RunSeed Seed { get; }

        /// <summary>1-based. Round 0 means the run has not started.</summary>
        public int Round { get; private set; }

        /// <summary>Raised on the server when anything with Health dies. (killer, victim)</summary>
        public event Action<PlayerId, Health> Killed;

        /// <summary>Raised on the server when the round advances.</summary>
        public event Action<int> RoundStarted;

        public RunContext(RunSeed seed, PlayerRegistry players = null, ProcBudget procs = null)
        {
            Seed = seed;
            Players = players ?? new PlayerRegistry();
            Procs = procs ?? new ProcBudget();
        }

        public void AdvanceRound()
        {
            Round++;
            RoundStarted?.Invoke(Round);
        }

        /// <summary>
        /// Report a death. Scrip is awarded here rather than inside Health so that the
        /// economy stays in one place and Health remains reusable for players, enemies
        /// and destructibles alike.
        /// </summary>
        public void ReportKill(PlayerId killer, Health victim, int scripReward)
        {
            if (killer.IsValid && scripReward > 0 && Players.TryGet(killer, out PlayerState state))
            {
                state.AwardScrip(scripReward);
            }

            Killed?.Invoke(killer, victim);
        }
    }
}
