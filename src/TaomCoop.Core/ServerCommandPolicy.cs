namespace TaomCoop.Core;

public static class ServerCommandPolicy
{
    public const int MaxLength = 500;

    public static string? Refusal(string command)
    {
        var text = command.Trim();
        if (text.Length == 0)
            return "Enter a command.";
        if (text.Length > MaxLength || text.IndexOfAny(['\r', '\n']) >= 0)
            return "That command was not sent.";
        return null;
    }
}
