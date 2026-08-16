using Overrun.Core;

namespace Overrun.Simulation
{
    /// <summary>
    /// Simulation-side state for one player in the run — local or remote.
    /// Authoritative on the server only. Clients hold a replicated copy.
    ///
    /// Distinct from PlayerContext (presentation, local players only) and from
    /// PlayerPawn (the body in the world). See Docs/ARCHITECTURE.md §5.
    /// </summary>
    public sealed class PlayerState
    {
        public readonly PlayerId Id;
        public readonly StatBlock Stats = new StatBlock();

        public float Health;
        public float Armor;
        public bool IsDowned;
        public bool IsAlive = true;
        public int Scrip;

        /// <summary>The body this player drives. Null until the pawn spawns.</summary>
        public PlayerPawn Pawn;

        /// <summary>
        /// Most recent intent received from the owning client. The server consumes this on
        /// its own fixed step rather than acting inside the RPC, so a client cannot drive
        /// the simulation faster by sending more packets.
        /// </summary>
        public InputFrame PendingInput;
        public bool HasPendingInput;

        public PlayerState(PlayerId id)
        {
            Id = id;
            Health = Stats.MaxHealth;
        }

        public float MaxHealth => Stats.MaxHealth;

        public void AwardScrip(int amount)
        {
            if (amount <= 0) return;
            Scrip += UnityEngine.Mathf.RoundToInt(amount * Stats.Resolve(StatId.ScripGain, Tag.Economy));
        }

        public bool TrySpendScrip(int cost)
        {
            if (cost < 0 || Scrip < cost) return false;
            Scrip -= cost;
            return true;
        }
    }
}
