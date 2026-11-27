namespace TaomCoop.Core;

/// <summary>
/// TAOM-Coop 0.3.25 must be prepared into a NEW server folder, and the TAOM 2.0.29.5
/// map needs a NEW Data folder (2.0.27 campaigns fail on the renamed villages).
/// Hosts still pointing at the 0.3.17 default folders are moved to the 0.3.25 ones;
/// the old folders are left on disk untouched.
/// </summary>
public static class HostFolders
{
    public const string ServerFolderName = "Server-v0325-Coop-v016-r2";
    public const string DataFolderName = "Data-v0325";

    private static readonly string[] OldServerFolders = ["Server-v0317-Coop-v015-r1"];

    public const string MovedNote =
        "TAOM-Coop 0.3.25 needs a new server folder and a new Data folder, because TAOM 2.0.29.5 cannot load 2.0.27 campaigns. "
        + "This tool now uses " + ServerFolderName + " and " + DataFolderName + ". The old folders were not changed. "
        + "Make a new TAOM 2.0.29.5 campaign in single player, save it, pick that save, then click Prepare server.";

    public static bool MoveToCurrentKit(string? serverRoot, string? dataRoot, out string newServerRoot, out string newDataRoot)
    {
        newServerRoot = serverRoot ?? "";
        newDataRoot = dataRoot ?? "";
        if (string.IsNullOrWhiteSpace(serverRoot))
            return false;
        var trimmed = serverRoot.TrimEnd('\\', '/');
        var cut = trimmed.LastIndexOfAny(['\\', '/']);
        var name = cut < 0 ? trimmed : trimmed[(cut + 1)..];
        if (!OldServerFolders.Contains(name, StringComparer.OrdinalIgnoreCase))
            return false;
        // Keep the folder's own separator so a Windows path stays a Windows path.
        var parent = cut < 0 ? "" : trimmed[..(cut + 1)];
        newServerRoot = parent + ServerFolderName;
        newDataRoot = parent + DataFolderName;
        return true;
    }
}
