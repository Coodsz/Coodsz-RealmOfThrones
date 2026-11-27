namespace BannerlordCoopLauncher.Core.Steam;

public static class SteamRegistry
{
    public const string SteamKey = @"Software\Valve\Steam";
    public const string ActiveProcessKey = @"Software\Valve\Steam\ActiveProcess";

    // Debug builds only: COODSZ_TEST_NO_GAME=1 makes every tool behave as if Steam and Bannerlord were
    // missing, so the Find Bannerlord flow can be live tested without touching Steam's files.
#if DEBUG
    public static bool TestNoGame => Environment.GetEnvironmentVariable("COODSZ_TEST_NO_GAME") == "1";
#else
    public static bool TestNoGame => false;
#endif

    public static string? TryGetSteamPath()
    {
        if (!OperatingSystem.IsWindows() || TestNoGame)
            return null;

        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(SteamKey);
        var path = key?.GetValue("SteamPath") as string;
        if (string.IsNullOrWhiteSpace(path))
            return null;
        return path.Replace('/', '\\').TrimEnd('\\');
    }

    // SteamID64 of the account signed in to Steam right now, else the account
    // Steam marks as most recent in loginusers.vdf.
    public static ulong? TryGetSteamId64()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ActiveProcessKey))
        {
            if (key?.GetValue("ActiveUser") is int user && user != 0)
                return 76561197960265728UL + (uint)user;
        }

        var steam = TryGetSteamPath();
        if (steam == null)
            return null;
        var file = Path.Combine(steam, "config", "loginusers.vdf");
        try
        {
            return File.Exists(file) ? MostRecentUser(File.ReadAllText(file)) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static ulong? MostRecentUser(string loginUsersVdf)
    {
        ulong? first = null;
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
                     loginUsersVdf, "\"(?<id>7656\\d{13})\"\\s*\\{(?<body>[^}]*)\\}", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        {
            if (!ulong.TryParse(match.Groups["id"].Value, out var id))
                continue;
            first ??= id;
            if (System.Text.RegularExpressions.Regex.IsMatch(match.Groups["body"].Value, "\"(?i:MostRecent)\"\\s*\"1\"", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                return id;
        }

        return first;
    }

    // The Steam profile name of that account, the name a Steam lobby shows. Read from
    // loginusers.vdf, so it is the name Steam saw at the last sign in.
    public static string? TryGetPersonaName()
    {
        var id = TryGetSteamId64();
        var steam = TryGetSteamPath();
        if (id == null || steam == null)
            return null;
        try
        {
            var file = Path.Combine(steam, "config", "loginusers.vdf");
            return File.Exists(file) ? PersonaName(File.ReadAllText(file), id.Value) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static string? PersonaName(string loginUsersVdf, ulong steamId64)
    {
        var block = System.Text.RegularExpressions.Regex.Match(loginUsersVdf,
            "\"" + steamId64 + "\"\\s*\\{(?<body>[^}]*)\\}", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (!block.Success)
            return null;
        var name = System.Text.RegularExpressions.Regex.Match(block.Groups["body"].Value,
            "\"(?i:PersonaName)\"\\s*\"(?<name>(?:[^\"\\\\]|\\\\.)*)\"", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        var value = name.Success ? name.Groups["name"].Value.Replace("\\\"", "\"").Replace("\\\\", "\\").Trim() : "";
        return value.Length > 0 ? value : null;
    }

    // The id of the running Steam client, or 0 when Steam is not running. ActiveProcess\pid alone
    // is not enough: it can read 0 while Steam runs (seen after a dedicated server run), and it
    // keeps the old id after Steam closed. So the id counts only while that steam.exe lives, and
    // a steam.exe process counts when the registry has no live id.
    public static int GetActiveProcessId()
    {
        if (!OperatingSystem.IsWindows())
            return 0;

        int registered;
        using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ActiveProcessKey))
            registered = key?.GetValue("pid") is int pid ? pid : 0;
        return LiveSteamProcess(registered, SteamProcessIds());
    }

    public static bool IsRunning() => GetActiveProcessId() > 0;

    /// <summary>The registry id when a steam.exe with that id runs, else the first steam.exe, else 0.</summary>
    public static int LiveSteamProcess(int registeredPid, IReadOnlyCollection<int> steamProcessIds)
    {
        if (registeredPid > 0 && steamProcessIds.Contains(registeredPid))
            return registeredPid;
        return steamProcessIds.Count > 0 ? steamProcessIds.First() : 0;
    }

    private static IReadOnlyCollection<int> SteamProcessIds()
    {
        var ids = new List<int>();
        foreach (var process in System.Diagnostics.Process.GetProcessesByName("steam"))
        {
            try
            {
                if (!process.HasExited)
                    ids.Add(process.Id);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // A Steam started as another user still counts as running.
                ids.Add(process.Id);
            }
            finally
            {
                process.Dispose();
            }
        }

        return ids;
    }
}
