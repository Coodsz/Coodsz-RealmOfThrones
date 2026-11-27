// Ported from CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep), Server/ServerCommand.cs,
// and changed for the Coodsz tools.

namespace BannerlordCoopLauncher.Core.Server;

/// <summary>
/// The lines this tool may write to the server's own console (its stdin), and how they are cleaned.
/// </summary>
/// <remarks>
/// This is a safety boundary. The server's console takes its 7 builtin commands and also
/// every game console command, cheats such as campaign.add_gold_to_hero included. A line
/// written to stdin must therefore be one of the builtin commands below, on one line.
/// The Console page sends typed commands through server-data\console-queue instead, which
/// is a separate path with its own rules.
/// </remarks>
public static class ServerCommand
{
    /// <summary>The builtin server commands this tool may write to stdin.</summary>
    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
    {
        "status", "players", "save", "say", "kick", "stop", "help",
    };

    /// <summary>
    /// Longest message sent with say. The protocol names no limit; this keeps it to one
    /// readable line.
    /// </summary>
    public const int MaxBroadcastLength = 200;

    public static string Status() => "status";
    public static string Players() => "players";
    public static string Save() => "save";
    public static string Stop() => "stop";
    public static string Help() => "help";

    /// <summary>A message to every player, for example the restart countdown.</summary>
    public static string Say(string? message) => "say " + SanitizeArgument(message, MaxBroadcastLength);

    /// <summary>Disconnects a player by peer id or name, or null when nothing is left after cleaning.</summary>
    public static string? Kick(string? idOrName)
    {
        var arg = SanitizeArgument(idOrName, 64);
        return arg.Length == 0 ? null : "kick " + arg;
    }

    /// <summary>Is this line fit for the server's stdin?</summary>
    /// <remarks>
    /// stdin is line based, so a line break inside a line would turn one command into two.
    /// Lines with a line break are refused here and removed in <see cref="SanitizeArgument"/>.
    /// </remarks>
    public static bool IsAllowed(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            return false;
        if (commandLine.Any(c => c is '\r' or '\n'))
            return false;
        var verb = commandLine.Split(' ', 2)[0];
        return Allowed.Contains(verb);
    }

    /// <summary>
    /// A line typed on the Console page that is one of the builtin commands, as the line to write to stdin,
    /// or null for anything else (those go through the command queue). The command queue only runs game
    /// console commands, so it refuses save, status and the others (Coop #54: typed save was refused).
    /// </summary>
    public static string? TypedBuiltin(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed) || typed.Any(c => c is '\r' or '\n'))
            return null;
        var parts = typed.Trim().Split(' ', 2);
        var verb = parts[0].ToLowerInvariant();
        var rest = parts.Length > 1 ? parts[1] : "";
        return verb switch
        {
            "say" => SanitizeArgument(rest, MaxBroadcastLength).Length > 0 ? Say(rest) : "say",
            "kick" => Kick(rest) ?? "kick",
            _ when Allowed.Contains(verb) => verb,
            _ => null,
        };
    }

    /// <summary>Makes an argument one line of plain text without control characters.</summary>
    /// <remarks>
    /// Control characters become spaces rather than being removed: removing them could
    /// turn "ki\nck" into a real word.
    /// </remarks>
    public static string SanitizeArgument(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var buffer = new char[value.Length];
        var n = 0;
        foreach (var c in value)
            buffer[n++] = char.IsControl(c) ? ' ' : c;

        var cleaned = string.Join(' ', new string(buffer, 0, n).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return cleaned.Length > maxLength ? cleaned[..maxLength].TrimEnd() : cleaned;
    }
}
