namespace TaomCoop.Core;

/// <summary>
/// Safe mode's "Plain TAOM this one time", held by the Connect that one Safe mode click starts. Each click carries
/// its own flag through all of its awaits, so a second click that ends first (Connect busy, a failed join check)
/// no longer switches off the first click's Plain TAOM while that one is still in the join check. Two Safe mode
/// clicks about 2 seconds apart used to start the game with the saved setup (TAOM plus).
/// </summary>
public sealed class SafeModeJoin
{
    private readonly AsyncLocal<bool> _on = new();

    /// <summary>True inside a join started by <see cref="RunAsync"/>, through all its awaits; false everywhere else.</summary>
    public bool On => _on.Value;

    /// <summary>Runs one join with Plain TAOM. The flag goes back by itself when this join's code is done.</summary>
    public async Task RunAsync(Func<Task> join)
    {
        // Set inside this async method, so only this call and what it awaits see it; the caller never does.
        _on.Value = true;
        await join();
    }
}
