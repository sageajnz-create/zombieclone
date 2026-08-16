using System;
using UnityEngine;

namespace Overrun.Core
{
    /// <summary>
    /// Gameplay buttons, packed into the InputFrame bitfields.
    /// </summary>
    [Flags]
    public enum InputButton : uint
    {
        None      = 0u,
        Fire      = 1u << 0,
        Aim       = 1u << 1,
        Reload    = 1u << 2,
        Interact  = 1u << 3,
        Jump      = 1u << 4,
        Sprint    = 1u << 5,
        Melee     = 1u << 6,
        Ability   = 1u << 7,
        Equipment = 1u << 8
    }

    /// <summary>
    /// One local player's intent for one tick. This is both the output of the local
    /// input router AND the exact client-to-server payload — deliberately the same
    /// type, so local play and networked play exercise the same path.
    ///
    /// Lives in Overrun.Core and carries NO networking dependency. Overrun.Simulation
    /// consumes it (PlayerPawn.ProcessInput) and Overrun.Net references Simulation, so
    /// declaring it in Overrun.Net would create a circular assembly reference that
    /// Unity rejects. Transport is handled by Overrun.Net via
    /// ForceNetworkSerializeByMemcpy. See Docs/ARCHITECTURE.md §6.
    ///
    /// Must remain unmanaged plain-old-data so it stays memcpy-serialisable.
    /// </summary>
    public struct InputFrame
    {
        public byte LocalSlot;
        public Vector2 Move;        // normalised, local space
        public Vector2 LookDelta;
        public uint Held;           // InputButton bitfield
        public uint Pressed;        // edge-triggered this tick
        public uint ClientTick;

        public bool IsHeld(InputButton button) => (Held & (uint)button) != 0u;
        public bool WasPressed(InputButton button) => (Pressed & (uint)button) != 0u;

        public void Set(InputButton button, bool held, bool pressed)
        {
            if (held) Held |= (uint)button; else Held &= ~(uint)button;
            if (pressed) Pressed |= (uint)button; else Pressed &= ~(uint)button;
        }
    }
}
