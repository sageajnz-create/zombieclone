using System.Collections.Generic;
using Overrun.Core;
using Overrun.Data;

namespace Overrun.Simulation
{
    /// <summary>
    /// Draws between-round augment offers from the seeded content stream.
    /// Pure functions — EditMode-testable, no scene types.
    /// </summary>
    public static class AugmentOfferer
    {
        public const int Choices = 3;

        /// <summary>
        /// Pick <see cref="Choices"/> unique augments the player does not already hold at
        /// max stacks. Falls short only if the pool is smaller than that.
        /// </summary>
        public static int Roll(AugmentDefinition[] pool, DeterministicRandom rng,
                               ICollection<int> ownedIds, AugmentDefinition[] results)
        {
            if (results == null) return 0;
            for (int i = 0; i < results.Length; i++) results[i] = null;
            if (pool == null || rng == null) return 0;

            // Copy eligible entries into a scratch list so we can draw without replacement.
            var eligible = new List<AugmentDefinition>(pool.Length);
            for (int i = 0; i < pool.Length; i++)
            {
                AugmentDefinition def = pool[i];
                if (def == null) continue;
                if (ownedIds != null && ownedIds.Contains(def.DefinitionId) && def.MaxStacks <= 1)
                    continue;
                eligible.Add(def);
            }

            int take = Choices < results.Length ? Choices : results.Length;
            if (take > eligible.Count) take = eligible.Count;

            for (int i = 0; i < take; i++)
            {
                int pick = rng.Next(eligible.Count);
                results[i] = eligible[pick];
                eligible.RemoveAt(pick);
            }

            return take;
        }

        public static bool TryApply(AugmentDefinition def, PlayerState state)
        {
            if (def == null || state == null) return false;
            if (state.HoldsAugment(def.DefinitionId) && def.MaxStacks <= 1) return false;

            float oldMax = state.Stats.MaxHealth;
            def.ApplyTo(state.Stats);
            state.RecordAugment(def.DefinitionId);

            float gained = state.Stats.MaxHealth - oldMax;
            if (gained > 0f && state.Pawn != null)
            {
                Health health = state.Pawn.Health;
                if (health != null) health.IncreaseMax(gained);
            }

            return true;
        }
    }
}
