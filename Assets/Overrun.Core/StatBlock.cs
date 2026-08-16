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
        /// <summary>The three stacking layers, resolved but not yet applied to a base.</summary>
        private struct Layers
        {
            public float Flat;
            public float Increased;
            public float More;
        }

        // Cache the LAYERS, not the final value. Caching the result would only serve
        // Resolve(); weapons need the same modifiers applied to their own base value via
        // ResolveFor(), and a per-weapon base must not thrash a shared cache.
        //
        // Keyed by the full (stat, tag-context) pair. A packed scalar key would have to
        // squeeze a 64-bit Tag plus a StatId into one word, and a collision there would
        // silently return another stat's modifiers — the worst possible failure mode.
        private readonly Dictionary<(StatId Stat, Tag Context), Layers> _cache =
            new Dictionary<(StatId, Tag), Layers>();

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
        public float Resolve(StatId stat, Tag context = Tag.None) =>
            Apply(GetBase(stat), GetLayers(stat, context));

        /// <summary>
        /// Apply this block's modifiers to an externally-owned base value — a weapon's own
        /// damage, an enemy definition's health. Lets one player's augments modify several
        /// weapons correctly without any of them overwriting a shared base.
        /// </summary>
        public float ResolveFor(float baseValue, StatId stat, Tag context = Tag.None) =>
            Apply(baseValue, GetLayers(stat, context));

        private static float Apply(float baseValue, Layers l) =>
            (baseValue + l.Flat) * (1f + l.Increased) * l.More;

        private Layers GetLayers(StatId stat, Tag context)
        {
            var key = (stat, context);
            if (_cache.TryGetValue(key, out Layers cached)) return cached;

            var layers = new Layers { Flat = 0f, Increased = 0f, More = 1f };

            for (int i = 0; i < _mods.Count; i++)
            {
                StatModifier m = _mods[i].Mod;
                if (m.Stat != stat) continue;
                if (!m.Filter.Matches(context)) continue;

                switch (m.Op)
                {
                    case StatOp.Flat:      layers.Flat += m.Value; break;
                    case StatOp.Increased: layers.Increased += m.Value; break;
                    case StatOp.More:      layers.More *= (1f + m.Value); break;
                }
            }

            _cache[key] = layers;
            return layers;
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
