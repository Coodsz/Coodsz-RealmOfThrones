using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Support;

namespace TaomCoop.Core;

public static class TaomHostReport
{
    public readonly record struct Report(IReadOnlyList<string> Lines, string Status, bool Failed);

    /// <summary>A report for the host window. While the server still runs (exited false) only the
    /// reports that are sure while it runs count: a blocked DLL and the server's own fatal event. The
    /// others say the server stopped, so they wait until the process has really exited.</summary>
    public static Report? Explain(string? text, bool wasServing, bool exited = true)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var blocked = BlockedDllNotice.Explain(text);
        if (blocked != null)
            return new Report(blocked, "The server cannot load a DLL. Click Unblock DLLs in this server tool, then start again.", true);

        // TAOM.CoopCompat's own startup check, sure while the server runs: it is no load crash. Read line by line.
        var startupCheck = TaomStartupCheck.Report(text);
        if (startupCheck != null)
            return startupCheck;

        var fatal = Fatal(text);
        if (fatal != null)
            return fatal;
        if (!exited)
            return null;

        var save = SaveMismatch(text);
        if (save != null)
            return save;

        var failedLoad = FailedLoad(text);
        if (failedLoad != null)
            return failedLoad;

        var dependency = Dependency(text);
        if (dependency != null)
            return dependency;

        var duplicate = Duplicate(text);
        if (duplicate != null)
            return duplicate;

        var crash = Exception(text);
        if (crash != null)
            return crash;

        var code = ExitCode(text);
        if (code is 0 && wasServing)
        {
            return new Report(
            [
                "What happened: The server was already up and then it stopped on its own. The log shows a normal exit.",
                "What to do: Press Start server when you want players to join again.",
                "What not to do: Do not delete the campaign save. Do not prepare the server folder a second time."
            ],
            "The server stopped on its own.",
            false);
        }

        if (code != null)
        {
            return new Report(
            [
                "What happened: The server stopped before it was ready for players. The log does not name one broken module.",
                "What to do: Press Start server again. If it stops the same way, open the server logs and read the last lines.",
                "What not to do: Do not delete the campaign save. Do not start a vanilla new game. Do not prepare the server folder a second time."
            ],
            "The server stopped before it was ready for players.",
            true);
        }

        return null;
    }

    // The server's own fatal state event (@DS@{"ev":"state","phase":"fatal",...}).
    private static Report? Fatal(string text)
    {
        if (ServerStateEvent.Last(text) is not { Fatal: true } state)
            return null;
        if (ServerStateEvent.OlderTaomSave(state.Detail))
        {
            return new Report(
            [
                "What happened: The server stopped while it loaded the campaign. The save was made with an older TAOM version, and TAOM " + TaomStack.TaomVersionText + " cannot load it.",
                "What to do: Start a new campaign in Bannerlord with TAOM " + TaomStack.TaomVersionText + " and save it. Then pick that save here, click Name server copy and start the server.",
                "What not to do: Do not prepare the server folder a second time. It does not fix an old save."
            ],
            ServerStateEvent.FatalStatus(state),
            true);
        }

        return new Report(
        [
            "What happened: The server stopped while it loaded the campaign. The server says " + ServerStateEvent.ShortDetail(state.Detail),
            "What to do: Press Start server again. If it stops the same way, click Send server crash report and post the code on Discord.",
            "What not to do: Do not delete the campaign save. Do not prepare the server folder a second time."
        ],
        ServerStateEvent.FatalStatus(state),
        true);
    }

    private static Report? SaveMismatch(string text)
    {
        var match = Regex.Match(text, @"Save '([^']+)' was not found or is incompatible", RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;
        var name = match.Groups[1].Value;
        return new Report(
        [
            "What happened: The server stopped before anyone could join. The campaign save named " + name + " does not match this playtest. A save remembers the modules it was made with.",
            "What to do: Use a TAOM 2.0.29.5 campaign save that is already in the server Game Saves folder. If the line says Single player copy, click Name server copy first.",
            "What not to do: Do not start a vanilla new game. Do not delete the single-player save. Do not add mods, and do not prepare the server folder a second time."
        ],
        "That save does not match this playtest. Pick a server copy made with TAOM 2.0.29.5. TAOM 2.0.27 campaigns do not load on 2.0.29.5.",
        true);
    }

    private static Report? FailedLoad(string text)
    {
        var match = Regex.Match(text, @"\b(?<id>[A-Za-z0-9_.]+) could not be loaded correctly", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
            return null;
        var name = match.Groups["id"].Value;
        return new Report(
        [
            "What happened: The server stopped because " + name + " did not load. The TAOM host only loads its sealed module list.",
            "What to do: Close Bannerlord if it is open, then press Start server again. If the same module fails, that copy in the server folder is damaged. Do not add a second copy.",
            "What not to do: Do not turn Harmony or ButterLib on by themselves. Do not turn on War Sails. Do not move Coop, and do not make another Coop folder."
        ],
        name + " did not load. The sealed list was left as it is.",
        true);
    }

    private static Report? Dependency(string text)
    {
        var match = Regex.Match(text, @"\b(?<mod>[A-Za-z0-9_.]+)\s+depends on(?: module)?\s+(?<dep>[A-Za-z0-9_.]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
            return null;
        var owner = match.Groups["mod"].Value;
        var needed = match.Groups["dep"].Value;
        return new Report(
        [
            "What happened: The server stopped because " + owner + " needs " + needed + ", and " + needed + " is not loaded.",
            "What to do: The sealed list has to stay whole. Put " + needed + " back in the server Modules folder, then press Start server again.",
            "What not to do: Do not turn " + owner + " off. Do not install Harmony or ButterLib as separate mods. Do not add modules that are not in the playtest list."
        ],
        owner + " needs " + needed + ". The sealed list has to stay whole.",
        true);
    }

    private static Report? Duplicate(string text)
    {
        var match = Regex.Match(text, @"module(?: id)?\s+(?<id>[A-Za-z0-9_.]+)\s+is already", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
            return null;
        var name = match.Groups["id"].Value;
        return new Report(
        [
            "What happened: The server stopped because " + name + " is in more than one folder. The server can only use one copy.",
            "What to do: Move the extra folder out of the server Modules folder. Keep the playtest copy, then press Start server again.",
            "What not to do: Do not delete the playtest copy. Do not make a second Coop folder inside Modules."
        ],
        name + " is in more than one folder.",
        true);
    }

    private static Report? Exception(string text)
    {
        var lines = text.Replace("\r", "").Split('\n');
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            // A whole word that ends in Exception. A module name such as BetterExceptionWindow is not one.
            if (!Regex.IsMatch(lines[i], @"Exception\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) || IsNoise(lines[i]))
                continue;
            var window = string.Join(" ", lines.Skip(i).Take(6));
            var module = TaomStack.ServerModules.FirstOrDefault(id => Regex.IsMatch(window, @"\b" + Regex.Escape(id) + @"\b", RegexOptions.IgnoreCase));
            if (module == null)
                continue;
            var quote = Clean(lines[i]);
            return new Report(
            [
                "What happened: The server stopped while it was starting. The log says: " + quote + " That line names " + module + ".",
                "What to do: Press Start server again. If it stops on the same module, that copy in the server folder is damaged. Players still need the same " + TaomStack.KitVersion + " set.",
                "What not to do: Do not add mods to the sealed list. Do not delete the campaign save. Do not prepare the server folder a second time."
            ],
            "The server stopped while it was starting.",
            true);
        }

        return null;
    }

    private static int? ExitCode(string text)
    {
        var match = Regex.Match(text, @"engine exited with code\s+(-?\d+)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var code) ? code : null;
    }

    // The start script's own PowerShell error record never names a module (StartScriptOutput).
    private static bool IsNoise(string line) =>
        BannerlordCoopLauncher.Core.Support.StartScriptOutput.IsPowerShellRecord(line)
        || line.Contains("FMOD", StringComparison.OrdinalIgnoreCase)
        || line.Contains("Serilog", StringComparison.OrdinalIgnoreCase)
        || line.Contains("AccessTools", StringComparison.OrdinalIgnoreCase)
        || line.Contains("ReflectionTypeLoadException", StringComparison.OrdinalIgnoreCase)
        || line.Contains("_MODULES_", StringComparison.Ordinal);

    private static string Clean(string line)
    {
        var value = Regex.Replace(line.Trim(), @"^\[[^\]]+\]\s*", "");
        value = Regex.Replace(value, @"^\d{2}:\d{2}:\d{2}(?:\.\d+)?\s*", "");
        return value.Length <= 180 ? value : value[..177] + "...";
    }
}
