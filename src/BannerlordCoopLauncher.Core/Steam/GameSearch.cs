namespace BannerlordCoopLauncher.Core.Steam;

public sealed record GameSearch(string? Root, string? Reason)
{
    public const string SteamMissingReason = "Steam was not found on this PC.";
    public const string LibraryListUnreadableReason = "Steam's library list could not be read. Pick the Bannerlord folder yourself.";
    public const string GameMissingReason = "Bannerlord is not in any Steam library on this PC.";
}
