namespace TaomCoop.Core;

/// <summary>
/// The note under Start server once the server is serving. A starting note left up after
/// Serving made players think the server was still loading, so they pressed Start again.
/// </summary>
public static class ServerReadyNote
{
    public const string Ready = "Server is ready. Players can join now.";

    /// <summary>True when the shown note (its English text) only made sense before the server served.</summary>
    public static bool Replaces(string? shownEnglish)
    {
        if (string.IsNullOrWhiteSpace(shownEnglish))
            return false;
        return shownEnglish.StartsWith("Server is starting", StringComparison.Ordinal)
            || shownEnglish.StartsWith("The server cannot load a DLL", StringComparison.Ordinal)
            || shownEnglish.EndsWith("Starting the server.", StringComparison.Ordinal)
            || shownEnglish.EndsWith("Starting the server again.", StringComparison.Ordinal);
    }
}
