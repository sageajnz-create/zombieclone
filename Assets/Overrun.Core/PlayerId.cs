using System;

namespace Overrun.Core
{
    /// <summary>
    /// Globally unique player handle.
    /// ClientId: one machine / one network connection.
    /// LocalSlot: index of a player on that machine (0..3).
    ///
    /// Two couch players share a ClientId, which is exactly why no gameplay system
    /// may take a ClientId on its own. See Docs/ARCHITECTURE.md Boundary B.
    ///
    /// NOTE: written as a plain readonly struct, not a `record struct`.
    /// Unity 6000.5 compiles at C# 9; record structs are C# 10, and `init` accessors
    /// require System.Runtime.CompilerServices.IsExternalInit which netstandard2.1
    /// does not provide.
    /// </summary>
    public readonly struct PlayerId : IEquatable<PlayerId>
    {
        public readonly ulong ClientId;
        public readonly byte LocalSlot;

        public PlayerId(ulong clientId, byte localSlot)
        {
            ClientId = clientId;
            LocalSlot = localSlot;
        }

        /// <summary>Sentinel for "no player". Not a valid roster entry.</summary>
        public static PlayerId None => new PlayerId(ulong.MaxValue, 0);

        public bool IsValid => ClientId != ulong.MaxValue;

        public bool Equals(PlayerId other) =>
            ClientId == other.ClientId && LocalSlot == other.LocalSlot;

        public override bool Equals(object obj) => obj is PlayerId other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (ClientId.GetHashCode() * 397) ^ LocalSlot.GetHashCode();
            }
        }

        public static bool operator ==(PlayerId left, PlayerId right) => left.Equals(right);
        public static bool operator !=(PlayerId left, PlayerId right) => !left.Equals(right);

        public override string ToString() => $"P({ClientId}:{LocalSlot})";
    }
}
