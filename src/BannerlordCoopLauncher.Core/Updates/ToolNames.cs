namespace BannerlordCoopLauncher.Core.Updates;

public enum ToolRole
{
    Server,
    Client
}

/// <summary>
/// One Coodsz tool: the exe name it ships under from the two exe releases on, the names it shipped
/// under before, and the release zips that carry it.
/// </summary>
public sealed record ToolName(
    string Product,
    ToolRole Role,
    string NewExeName,
    IReadOnlyList<string> OldExeNames,
    string NewZipPrefix,
    IReadOnlyList<string> OldZipPatterns)
{
    public IEnumerable<string> AllExeNames => OldExeNames.Prepend(NewExeName);

    public bool IsNewZip(string assetName) =>
        assetName.StartsWith(NewZipPrefix, StringComparison.OrdinalIgnoreCase)
        && assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The one list of tool names the updater and the rename cleanup read. Exe names match exactly, never
/// by a part of the name, so "Coodsz Coop Server Tool" can never pass for a TAOM tool.
/// </summary>
public static class ToolNames
{
    public static readonly IReadOnlyList<ToolName> All =
    [
        new("Coop", ToolRole.Server, "Coodsz Coop Server Tool.exe",
            ["CoodszHost.exe"],
            "Coodsz-Coop-Tools-v",
            ["Coodsz-Host", "CoodszHost", "Coop-and-Friends"]),
        // Coodsz and Friends (the More Nations build) folded into the client launcher's More Nations
        // switch, so both old client names have one successor.
        new("Coop", ToolRole.Client, "Coodsz Coop Client Launcher.exe",
            ["Coodsz Coop and Friends.exe", "Coodsz and Friends.exe"],
            "Coodsz-Coop-Tools-v",
            ["Coop-and-Friends"]),
        new("TAOM", ToolRole.Server, "Coodsz TAOM Server Tool.exe",
            ["Coodsz Dedicated TAOM Server Tool.exe"],
            "Coodsz-TAOM-Tools-v",
            ["Server-Tool"]),
        // The client launchers keep their names, so they have no old name of their own.
        new("TAOM", ToolRole.Client, "Coodsz TAOM Client Launcher.exe",
            [],
            "Coodsz-TAOM-Tools-v",
            ["Client-Launcher"]),
        new("AD1259", ToolRole.Server, "Coodsz AD1259 Server Tool.exe",
            ["Coodsz AD1259 Server.exe"],
            "Coodsz-AD1259-Tools-v",
            ["AD1259-Server"]),
        new("AD1259", ToolRole.Client, "Coodsz AD1259 Client Launcher.exe",
            [],
            "Coodsz-AD1259-Tools-v",
            ["AD1259-Client-Launcher"]),
        // Burning Empires was never released, so no old zip exists. The old exe names are the test builds
        // testers ran; listing them keeps their loose files safe from another tool's cleanup.
        new("BurningEmpires", ToolRole.Server, "Coodsz Burning Empires Server Tool.exe",
            ["Coodsz Burning Empires Server.exe"],
            "Coodsz-Burning-Empires-Tools-v",
            []),
        new("BurningEmpires", ToolRole.Client, "Coodsz Burning Empires Client Launcher.exe",
            ["Coodsz Burning Empires Client.exe"],
            "Coodsz-Burning-Empires-Tools-v",
            []),
        // Realm of Thrones is not released yet, so it has no old exe names or zips.
        new("RealmOfThrones", ToolRole.Server, "Coodsz Realm of Thrones Server Tool.exe",
            [],
            "Coodsz-Realm-of-Thrones-Tools-v",
            []),
        new("RealmOfThrones", ToolRole.Client, "Coodsz Realm of Thrones Client Launcher.exe",
            [],
            "Coodsz-Realm-of-Thrones-Tools-v",
            [])
    ];

    /// <summary>Every Coodsz exe name, old and new. An update never removes one that is not its own.</summary>
    public static IEnumerable<string> KnownExeNames => All.SelectMany(tool => tool.AllExeNames);

    public static bool IsKnownExe(string fileName) => KnownExeNames.Contains(Path.GetFileName(fileName), StringComparer.OrdinalIgnoreCase);

    // The loose libraries the earlier releases shipped beside the exe. Every tool was loose before
    // v1.2.23, and the TAOM tools stayed loose (framework dependent) until the two exe release.
    private static readonly IReadOnlyList<string> CoreLooseFiles = ["BannerlordCoopLauncher.Core.dll"];

    private static readonly IReadOnlyList<string> TaomLooseFiles =
    [
        "BannerlordCoopLauncher.Core.dll",
        "TaomCoop.Core.dll",
        "LiteNetLib.dll",
        "Mono.Cecil.dll",
        "Mono.Cecil.Mdb.dll",
        "Mono.Cecil.Pdb.dll",
        "Mono.Cecil.Rocks.dll"
    ];

    // The Burning Empires test builds were loose (framework dependent) too, with their own core beside the TAOM one.
    private static readonly IReadOnlyList<string> BeLooseFiles = [.. TaomLooseFiles, "BurningEmpires.Core.dll"];

    /// <summary>
    /// The library files this tool's own earlier payloads put beside its exe, so its own update may
    /// remove them. A name not in ToolNames (an exe the player renamed) only owns the shared core.
    /// </summary>
    public static IReadOnlyList<string> PreviousLooseFiles(string exeFileName) => Find(exeFileName)?.Product switch
    {
        "TAOM" => TaomLooseFiles,
        "BurningEmpires" => BeLooseFiles,
        _ => CoreLooseFiles
    };

    /// <summary>The tool an exe file name belongs to, old name or new, or null when it is not a Coodsz tool name.</summary>
    public static ToolName? Find(string exeFileName)
    {
        var name = Path.GetFileName(exeFileName);
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name += ".exe";
        return All.FirstOrDefault(tool => tool.AllExeNames.Any(known => known.Equals(name, StringComparison.OrdinalIgnoreCase)));
    }

    public static bool IsNewName(string exeFileName) =>
        Find(exeFileName) is { } tool && tool.NewExeName.Equals(WithExe(exeFileName), StringComparison.OrdinalIgnoreCase);

    public static bool IsOldName(string exeFileName) =>
        Find(exeFileName) is { } tool && tool.OldExeNames.Contains(WithExe(exeFileName), StringComparer.OrdinalIgnoreCase);

    private static string WithExe(string exeFileName)
    {
        var name = Path.GetFileName(exeFileName);
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
    }
}
