using System;
using UnityEngine;
using Overrun.Core;

namespace Overrun.Data
{
    /// <summary>
    /// One authored stat change on an augment. Serialized so a designer can add a
    /// modifier without writing C#. Converted to <see cref="StatModifier"/> at apply time.
    /// </summary>
    [Serializable]
    public struct AuthoredModifier
    {
        public StatId Stat;
        public StatOp Op;
        public float Value;
        public Tag RequiredTags;

        public StatModifier ToRuntime() =>
            new StatModifier(Stat, Op, Value, new TagMask(RequiredTags));
    }

    /// <summary>
    /// An augment is rarity + tags + a list of modifiers. Applying one never means
    /// editing the code that reads a stat — it means adding rows to a StatBlock
    /// (Docs/GAMEPLAY_SYSTEMS.md §3).
    ///
    /// Never mutated at runtime: in the Editor those writes persist to disk.
    /// </summary>
    [CreateAssetMenu(fileName = "AugmentDef_", menuName = "Overrun/Augment Definition")]
    public sealed class AugmentDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable id used on the wire. Never send the asset itself.")]
        public int DefinitionId;
        public string DisplayName = "Augment";
        [TextArea] public string Description;
        [Tooltip("1 = Common. VS001 treats every augment equally.")]
        public int Rarity = 1;
        public Tag Tags;

        [Header("Effect")]
        public AuthoredModifier[] Modifiers;
        public int MaxStacks = 1;

        public void ApplyTo(StatBlock stats)
        {
            if (stats == null || Modifiers == null) return;
            for (int i = 0; i < Modifiers.Length; i++)
            {
                stats.Add(Modifiers[i].ToRuntime());
            }
        }
    }
}
