using System.Collections.Generic;

namespace Overrun.Core
{
    /// <summary>
    /// Resolves gameplay numbers through one formula, for players and enemies alike:
    ///
    ///     final = (base + sum Flat) * (1 + sum Increased) * product(1 + More)
    ///
    /// No system computes its own stacking. Adding an augment means adding
    /// <see cref="StatModifier"/>s; it never means editing the code that reads the stat.
    /// See Docs/GAMEPLAY_SYSTEMS.md §1.
    ///
    /// Resolution is tag-filtered, so "+40% increased Damage with Shock" is a data row
    /// rather than a code path: the caller passes the tags of the action being resolved.
    /// </summary>
    public sealed class StatBlock
    {
        private struct Entry
        {
            public int Handle;
            public StatModifier Mod;
        }

        private readonly Dictionary<StatId, float> _bases = new Dictionary<StatId, float>();
        private readonly List<Entry> _mods = new List<Entry>();
        // Keyed by the full (stat, tag-context) pair. A packed scalar key would have to
        // squeeze a 64-bit Tag plus a StatId into one word, and a collision there would
        // silently return another stat's value — the worst possible failure mode.
        private readonly Dictionary<(StatId Stat, Tag Context), float> _cache =
            new Dictionary<(StatId, Tag), float>();

        private int _nextHandle = 1;

        public StatBlock()
        {
            // Defaults. Definitions overwrite these via SetBase at spawn.
            SetBase(StatId.MaxHealth, 100f);
            SetBase(StatId.MoveSpeed, 5f);
            SetBase(StatId.SprintMultiplier, 1.6f);
            SetBase(StatId.JumpHeight, 1.1f);
            SetBase(StatId.Damage, 10f);
            SetBase(StatId.FireRate, 1f);
            SetBase(StatId.CritChance, 0.05f);
            SetBase(StatId.CritMultiplier, 2f);
            SetBase(StatId.Range, 50f);
            SetBase(StatId.ReloadSpeed, 1f);
            SetBase(StatId.MagazineSize, 12f);
            SetBase(StatId.ScripGain, 1f);
        }

        public int ModifierCount => _mods.Count;

        public float GetBase(StatId stat) => _bases.TryGetValue(stat, out var v) ? v : 0f;

        public void SetBase(StatId stat, float value)
        {
            _bases[stat] = value;
            _cache.Clear();
        }

        public ModifierHandle Add(StatModifier modifier)
        {
            int handle = _nextHandle++;
            _mods.Add(new Entry { Handle = handle, Mod = modifier });
            _cache.Clear();
            return new ModifierHandle(handle);
        }

        public bool Remove(ModifierHandle handle)
        {
            if (!handle.IsValid) return false;

            for (int i = 0; i < _mods.Count; i++)
            {
                if (_mods[i].Handle != handle.Id) continue;
                _mods.RemoveAt(i);
                _cache.Clear();
                return true;
            }
            return false;
        }

        public void ClearModifiers()
        {
            _mods.Clear();
            _cache.Clear();
        }

        /// <summary>
        /// Resolve a stat for an action carrying <paramref name="context"/> tags.
        /// Pass Tag.None for context-free lookups (max health, move speed).
        /// </summary>
        public float Resolve(StatId stat, Tag context = Tag.None)
        {
            var key = (stat, context);
            if (_cache.TryGetValue(key, out var cached)) return cached;

            float flat = 0f;
            float increased = 0f;
            float more = 1f;

            for (int i = 0; i < _mods.Count; i++)
            {
                StatModifier m = _mods[i].Mod;
                if (m.Stat != stat) continue;
                if (!m.Filter.Matches(context)) continue;

                switch (m.Op)
                {
                    case StatOp.Flat:      flat += m.Value; break;
                    case StatOp.Increased: increased += m.Value; break;
                    case StatOp.More:      more *= (1f + m.Value); break;
                }
            }

            float result = (GetBase(stat) + flat) * (1f + increased) * more;
            _cache[key] = result;
            return result;
        }

        // ---- Convenience accessors for hot, context-free stats ----------------
        // These route through Resolve so augments apply, and keep call sites readable.

        public float MaxHealth        => Resolve(StatId.MaxHealth);
        public float Armor            => Resolve(StatId.Armor);
        public float MoveSpeed        => Resolve(StatId.MoveSpeed, Tag.Mobility);
        public float SprintMultiplier => Resolve(StatId.SprintMultiplier, Tag.Mobility);
        public float JumpHeight       => Resolve(StatId.JumpHeight, Tag.Mobility);
    }
}
