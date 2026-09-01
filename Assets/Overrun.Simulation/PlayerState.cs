using System.Collections.Generic;
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
        public int Kills;
        public int PeakScrip;

        /// <summary>The body this player drives. Null until the pawn spawns.</summary>
        public PlayerPawn Pawn;

        /// <summary>
        /// Most recent intent received from the owning client. The server consumes this on
        /// its own fixed step rather than acting inside the RPC, so a client cannot drive
        /// the simulation faster by sending more packets.
        /// </summary>
        public InputFrame PendingInput;
        public bool HasPendingInput;

        private readonly List<int> _ownedAugments = new List<int>();
        private readonly AugmentDefinitionSlot[] _pendingOffers = new AugmentDefinitionSlot[AugmentOfferer.Choices];
        private bool _hasPendingOffer;
        private bool _offerChosen;

        public IReadOnlyList<int> OwnedAugments => _ownedAugments;
        public bool HasPendingOffer => _hasPendingOffer && !_offerChosen;
        public bool OfferChosen => _offerChosen;

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
            if (Scrip > PeakScrip) PeakScrip = Scrip;
        }

        public bool TrySpendScrip(int cost)
        {
            if (cost < 0 || Scrip < cost) return false;
            Scrip -= cost;
            return true;
        }

        public bool HoldsAugment(int definitionId)
        {
            for (int i = 0; i < _ownedAugments.Count; i++)
            {
                if (_ownedAugments[i] == definitionId) return true;
            }
            return false;
        }

        public void RecordAugment(int definitionId) => _ownedAugments.Add(definitionId);

        public void SetPendingOffers(Overrun.Data.AugmentDefinition[] offers, int count)
        {
            for (int i = 0; i < _pendingOffers.Length; i++)
            {
                _pendingOffers[i] = i < count && offers != null
                    ? new AugmentDefinitionSlot(offers[i])
                    : default;
            }
            _hasPendingOffer = count > 0;
            _offerChosen = false;
        }

        public Overrun.Data.AugmentDefinition GetPendingOffer(int index)
        {
            if (index < 0 || index >= _pendingOffers.Length) return null;
            return _pendingOffers[index].Definition;
        }

        public int PendingOfferCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _pendingOffers.Length; i++)
                {
                    if (_pendingOffers[i].Definition != null) n++;
                }
                return n;
            }
        }

        public void MarkOfferChosen()
        {
            _offerChosen = true;
            _hasPendingOffer = false;
        }

        public void ClearOffers()
        {
            for (int i = 0; i < _pendingOffers.Length; i++) _pendingOffers[i] = default;
            _hasPendingOffer = false;
            _offerChosen = false;
        }

        public void ResetForNewRun()
        {
            Stats.ClearModifiers();
            Health = Stats.MaxHealth;
            Armor = 0f;
            IsDowned = false;
            IsAlive = true;
            Scrip = 0;
            Kills = 0;
            PeakScrip = 0;
            _ownedAugments.Clear();
            ClearOffers();
            HasPendingInput = false;
            PendingInput = default;
        }

        private readonly struct AugmentDefinitionSlot
        {
            public readonly Overrun.Data.AugmentDefinition Definition;
            public AugmentDefinitionSlot(Overrun.Data.AugmentDefinition definition) => Definition = definition;
        }
    }
}
