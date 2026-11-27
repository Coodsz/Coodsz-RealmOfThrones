namespace TaomCoop.Core;

public static class TaomLaunch
{
    public static string Arguments(bool connect, bool steamLobby, string? password, ulong? lobbyId = null, string? directEndpoint = null,
        IReadOnlyList<string>? modules = null)
    {
        var parts = new List<string> { "/singleplayer" };
        if (connect)
        {
            parts.Add("/client");
            if (steamLobby)
            {
                if (lobbyId is not > 0)
                    throw new InvalidOperationException("The Steam lobby was not found.");
                parts.Add("+connect_lobby");
                parts.Add(lobbyId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                if (string.IsNullOrWhiteSpace(directEndpoint))
                    throw new InvalidOperationException("Enter the host address and UDP port.");
                parts.Add("/autoconnect");
                parts.Add(directEndpoint.Trim());
                if (!string.IsNullOrEmpty(password))
                {
                    parts.Add("/cooppassword");
                    parts.Add(Quote(password));
                }
            }
        }

        // Module ids can have spaces ("swadian armoury"); the token must stay one argument.
        var token = "_MODULES_*" + string.Join("*", modules ?? TaomStack.ClientModules) + "*_MODULES_";
        parts.Add(token.Contains(' ') ? "\"" + token + "\"" : token);
        return string.Join(" ", parts);
    }

    public static string Quote(string value)
    {
        var text = value.Trim();
        if (text.IndexOfAny(['"', '\r', '\n']) >= 0)
            throw new InvalidOperationException("That value cannot contain quotes or line breaks.");
        return "\"" + text + "\"";
    }

    /// <summary>
    /// The command line as the launcher shows it in its Activity box: only the join password after /cooppassword
    /// becomes ****, so the address, the modules and the other switches stay readable, and a short password is hidden
    /// too (a general cleaner skips short secrets and also hides the address).
    /// </summary>
    public static string Shown(string arguments, string? password)
    {
        // Trimmed first, the way Quote puts it on the line: a saved password that ends in a line break is on the line
        // without it and must still be hidden. One with a quote or a line break inside never reaches the line (Arguments
        // refuses it on a Direct join, and a Steam lobby join carries no password), so there is nothing to hide and Quote
        // must not throw here.
        var text = password?.Trim();
        if (string.IsNullOrEmpty(text) || text.IndexOfAny(['"', '\r', '\n']) >= 0)
            return arguments;
        return arguments.Replace("/cooppassword " + Quote(text), "/cooppassword ****", StringComparison.Ordinal);
    }
}
