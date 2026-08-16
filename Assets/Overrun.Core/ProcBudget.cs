using System.Collections.Generic;

namespace Overrun.Core
{
    /// <summary>
    /// The three guards that keep the augment system from eating the server.
    ///
    /// Effects re-enter the damage pipeline: a chain-lightning proc emits its own hit,
    /// which can trigger chain lightning. With 40 enemies and a status build this is not
    /// hypothetical — it is a guaranteed frame-time cliff or stack overflow, on the one
    /// machine that cannot afford either, taking down every player at once.
    ///
    /// These are cheap now and near-impossible to retrofit once fifty augments have been
    /// authored assuming unbounded chaining. Present from VS001 deliberately.
    /// See Docs/GAMEPLAY_SYSTEMS.md §4.
    /// </summary>
    public sealed class ProcBudget
    {
        /// <summary>Guard 1: an effect spawned from an effect stops here.</summary>
        public const byte MaxProcDepth = 3;

        private readonly int _perTick;
        private readonly float _minRetriggerSeconds;
        private readonly Dictionary<ProcKey, float> _lastFired = new Dictionary<ProcKey, float>();

        private int _spent;
        private int _dropped;

        public ProcBudget(int perTick = 64, float minRetriggerSeconds = 0.1f)
        {
            _perTick = perTick;
            _minRetriggerSeconds = minRetriggerSeconds;
        }

        public int SpentThisTick => _spent;

        /// <summary>Effects refused this tick. Surface it — silent dropping reads as "fine".</summary>
        public int DroppedThisTick => _dropped;

        public void BeginTick()
        {
            _spent = 0;
            _dropped = 0;
        }

        /// <summary>
        /// Guards 1 and 2. Returns false if the chain is too deep or the tick's effect
        /// budget is exhausted. Excess is dropped, never queued — queueing just moves the
        /// spike to the next tick and hides it.
        /// </summary>
        public bool TrySpend(byte procDepth)
        {
            if (procDepth >= MaxProcDepth) { _dropped++; return false; }
            if (_spent >= _perTick)        { _dropped++; return false; }

            _spent++;
            return true;
        }

        /// <summary>
        /// Guard 3: the same effect from the same source may not retrigger on the same
        /// victim within the minimum interval. Stops two entities ping-ponging one proc.
        /// </summary>
        public bool TryFire(PlayerId source, int victimId, int effectId, float now)
        {
            var key = new ProcKey(source, victimId, effectId);

            if (_lastFired.TryGetValue(key, out float last) && now - last < _minRetriggerSeconds)
            {
                _dropped++;
                return false;
            }

            _lastFired[key] = now;
            return true;
        }

        /// <summary>Drop cooldown entries older than the retrigger window. Call between rounds.</summary>
        public void Prune(float now)
        {
            if (_lastFired.Count == 0) return;

            var stale = new List<ProcKey>();
            foreach (var kv in _lastFired)
            {
                if (now - kv.Value >= _minRetriggerSeconds) stale.Add(kv.Key);
            }
            for (int i = 0; i < stale.Count; i++) _lastFired.Remove(stale[i]);
        }

        public void Clear()
        {
            _lastFired.Clear();
            _spent = 0;
            _dropped = 0;
        }

        private readonly struct ProcKey : System.IEquatable<ProcKey>
        {
            private readonly PlayerId _source;
            private readonly int _victimId;
            private readonly int _effectId;

            public ProcKey(PlayerId source, int victimId, int effectId)
            {
                _source = source;
                _victimId = victimId;
                _effectId = effectId;
            }

            public bool Equals(ProcKey other) =>
                _source.Equals(other._source) && _victimId == other._victimId && _effectId == other._effectId;

            public override bool Equals(object obj) => obj is ProcKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = _source.GetHashCode();
                    h = (h * 397) ^ _victimId;
                    h = (h * 397) ^ _effectId;
                    return h;
                }
            }
        }
    }
}
