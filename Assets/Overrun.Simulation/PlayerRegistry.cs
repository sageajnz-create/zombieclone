using System.Collections.Generic;
using Overrun.Core;

namespace Overrun.Simulation
{
    /// <summary>
    /// The mapping from PlayerId to simulation state for every player in the run.
    ///
    /// Deliberately a plain class with NO static Instance. A singleton here would
    /// reintroduce exactly the "one global player" assumption that Boundary B exists
    /// to prevent, and it is listed in the banned-patterns table in
    /// Docs/ARCHITECTURE.md §1. Systems receive this by injection from the object that
    /// owns the run, and address players by PlayerId.
    /// </summary>
    public sealed class PlayerRegistry
    {
        private readonly Dictionary<PlayerId, PlayerState> _players = new Dictionary<PlayerId, PlayerState>();
        private readonly List<PlayerState> _all = new List<PlayerState>();

        public IReadOnlyList<PlayerState> All => _all;
        public int Count => _all.Count;

        public PlayerState Register(PlayerId id)
        {
            if (_players.TryGetValue(id, out var existing)) return existing;

            var state = new PlayerState(id);
            _players.Add(id, state);
            _all.Add(state);
            return state;
        }

        public bool Unregister(PlayerId id)
        {
            if (!_players.TryGetValue(id, out var state)) return false;
            _players.Remove(id);
            _all.Remove(state);
            return true;
        }

        public bool TryGet(PlayerId id, out PlayerState state) => _players.TryGetValue(id, out state);

        public PlayerState Get(PlayerId id) => _players.TryGetValue(id, out var s) ? s : null;

        /// <summary>Removes every player belonging to one connection. Used on disconnect.</summary>
        public void UnregisterClient(ulong clientId)
        {
            for (int i = _all.Count - 1; i >= 0; i--)
            {
                if (_all[i].Id.ClientId == clientId) Unregister(_all[i].Id);
            }
        }

        public void GetAlive(List<PlayerState> results)
        {
            results.Clear();
            for (int i = 0; i < _all.Count; i++)
            {
                if (_all[i].IsAlive && !_all[i].IsDowned) results.Add(_all[i]);
            }
        }
    }
}
