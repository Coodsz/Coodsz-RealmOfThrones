namespace TaomCoop.Core;

/// <summary>
/// One Connect at a time in the TAOM Client Launcher. Five Connect clicks in 8 seconds started two games at the same
/// moment, and the second crashed because the first already had TAOM's log file open (TAOM CR-W4H9X). A Connect holds
/// this from the click until its game has joined, been turned down or closed. A later click while it looks for the
/// Steam lobby starts that search over; any other later click does nothing. Used from the window's thread only.
/// </summary>
public sealed class OneConnect
{
    private CancellationTokenSource? _search;
    private bool _searchAgain;

    /// <summary>True from the click until that Connect is done.</summary>
    public bool Running { get; private set; }

    /// <summary>False when a Connect is already running; else this click holds it until <see cref="End"/>.</summary>
    public bool TryBegin()
    {
        if (Running)
            return false;
        Running = true;
        return true;
    }

    public void End()
    {
        EndSearch();
        _searchAgain = false;
        Running = false;
    }

    /// <summary>The token for one Steam lobby search of the running Connect.</summary>
    public CancellationToken BeginSearch()
    {
        EndSearch();
        _search = new CancellationTokenSource();
        return _search.Token;
    }

    // Not disposed: a source with no timer holds nothing, and a cancel may still be running its callbacks.
    public void EndSearch() => _search = null;

    /// <summary>
    /// A later click: stops the running Steam lobby search so it starts over, and says true. False when the running
    /// Connect is not looking for a lobby, so the click does nothing.
    /// </summary>
    public bool SearchAgain()
    {
        if (_search == null)
            return false;
        _searchAgain = true;
        // The callbacks run off this thread, so the running Connect resumes on its own turn, not inside this click.
        _ = _search.CancelAsync();
        return true;
    }

    /// <summary>True once after <see cref="SearchAgain"/> stopped the search; the Connect then looks again.</summary>
    public bool TakeSearchAgain()
    {
        var again = _searchAgain;
        _searchAgain = false;
        return again;
    }
}
