namespace Overrun.Simulation
{
    /// <summary>
    /// Server-validated interaction. The client may hint at a target; the server decides
    /// whether the player is in range, can afford it, and what happens
    /// (Docs/NETWORKING.md §4, Docs/GAMEPLAY_SYSTEMS.md §8).
    /// </summary>
    public interface IInteractable
    {
        string Prompt { get; }
        bool IsAvailable { get; }
        float InteractRange { get; }

        bool TryInteract(PlayerState player);
    }
}
