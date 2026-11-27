namespace BannerlordCoopLauncher.Core.Steam;

// Files a non Steam (cracked) copy of the game leaves in its folder. Such copies share one
// fake Steam ID, so friends who join with them end up in the same character on the server.
// This only notes what it finds in the launcher log and in the help zip; it never blocks.
public static class NonSteamCopy
{
    // Folders and files the Steam emulators and crack groups put next to Bannerlord.exe or in
    // the game folder. steam_appid.txt is not one of them: Steam's own copy has it too.
    public static readonly string[] Folders = { "steam_settings", "SmartSteamEmu" };

    public static readonly string[] Files =
    {
        "steam_emu.ini", "SmartSteamEmu.ini", "ColdClientLoader.ini", "steam_interfaces.txt",
        "CODEX.ini", "CPY.ini", "RUNE.ini", "EMPRESS.ini", "OnlineFix.ini"
    };

    public const string UnsignedSteamApi = "unsigned steam_api64.dll";

    // The signs found in this game folder, by name. Empty for a normal Steam copy or no folder.
    public static IReadOnlyList<string> Find(string? gameRoot)
    {
        var found = new List<string>();
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
            return found;
        var bin = GameRoot.WorkingDirectory(gameRoot);
        foreach (var folder in new[] { bin, gameRoot })
        {
            foreach (var name in Folders)
                if (Directory.Exists(Path.Combine(folder, name)) && !found.Contains(name, StringComparer.OrdinalIgnoreCase))
                    found.Add(name);
            foreach (var name in Files)
                if (File.Exists(Path.Combine(folder, name)) && !found.Contains(name, StringComparer.OrdinalIgnoreCase))
                    found.Add(name);
        }

        // The steam_api64.dll Steam installs is signed (by TaleWorlds). The emulators that
        // replace it are not.
        var steamApi = Path.Combine(bin, "steam_api64.dll");
        if (File.Exists(steamApi) && HasSignature(steamApi) == false)
            found.Add(UnsignedSteamApi);
        return found;
    }

    // One plain line for the log, or null when nothing was found.
    public static string? Note(string? gameRoot) => Note(Find(gameRoot));

    public static string? Note(IReadOnlyList<string> signs) =>
        signs.Count == 0
            ? null
            : "This game folder has files from a non Steam copy (" + string.Join(", ", signs) + "). Coop needs each player to own the game on Steam.";

    // True when the PE file carries an Authenticode signature (its security directory is not
    // empty), false when it has none, null when the file can not be read as a PE file.
    public static bool? HasSignature(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return HasSignature(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static bool? HasSignature(Stream stream)
    {
        try
        {
            using var reader = new BinaryReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);
            if (stream.Length < 0x40 || reader.ReadUInt16() != 0x5A4D)
                return null;
            stream.Position = 0x3C;
            var peAt = reader.ReadInt32();
            if (peAt <= 0 || peAt + 24 > stream.Length)
                return null;
            stream.Position = peAt;
            if (reader.ReadUInt32() != 0x00004550)
                return null;
            var optionalAt = peAt + 24;
            stream.Position = optionalAt;
            var magic = reader.ReadUInt16();
            // The data directories start at 96 (PE32) or 112 (PE32+); the security one is the fifth.
            var directories = magic switch { 0x10B => 96, 0x20B => 112, _ => -1 };
            if (directories < 0)
                return null;
            var securityAt = optionalAt + directories + 4 * 8;
            if (securityAt + 8 > stream.Length)
                return null;
            stream.Position = securityAt;
            var address = reader.ReadUInt32();
            var size = reader.ReadUInt32();
            return address != 0 && size != 0;
        }
        catch (EndOfStreamException)
        {
            return null;
        }
    }
}
