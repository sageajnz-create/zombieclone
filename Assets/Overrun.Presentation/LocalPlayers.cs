using System.Collections.Generic;
using UnityEngine;

namespace Overrun.Presentation
{
    /// <summary>
    /// The local players sitting at THIS machine — 1..4 of them. Remote players are not
    /// represented here; they have simulation state but no local presentation rig.
    ///
    /// Deliberately no static Instance. A singleton would be the "one global player"
    /// assumption this whole layer exists to avoid, and it is in the banned-patterns
    /// table in Docs/ARCHITECTURE.md §1. Consumers get this by serialized reference
    /// from the presentation root.
    /// </summary>
    public sealed class LocalPlayers : MonoBehaviour
    {
        public const int MaxLocalPlayers = 4;

        private readonly List<PlayerContext> _contexts = new List<PlayerContext>();

        public IReadOnlyList<PlayerContext> Contexts => _contexts;
        public int Count => _contexts.Count;
        public bool HasFreeSlot => _contexts.Count < MaxLocalPlayers;

        /// <summary>Returns the slot assigned, or -1 if the machine is full.</summary>
        public int Add(PlayerContext context)
        {
            if (context == null || _contexts.Count >= MaxLocalPlayers) return -1;
            if (_contexts.Contains(context)) return context.LocalSlot;

            byte slot = (byte)_contexts.Count;
            _contexts.Add(context);
            context.AssignSlot(slot);
            return slot;
        }

        public bool Remove(PlayerContext context) => _contexts.Remove(context);

        public PlayerContext GetBySlot(byte slot)
        {
            for (int i = 0; i < _contexts.Count; i++)
            {
                if (_contexts[i].LocalSlot == slot) return _contexts[i];
            }
            return null;
        }
    }
}
