namespace TaomCoop.Core;

public enum CommandReplyTone
{
    Normal,
    Failed,
    InGame
}

public static class CommandReply
{
    public const string InGameNotice = "Type this in the game console. Press Alt+` in Bannerlord. This server tool cannot run it.";

    public static CommandReplyTone Tone(string line)
    {
        if (line.Contains("only available on the client", StringComparison.OrdinalIgnoreCase)
            || line.Contains("meant to be used in the game console", StringComparison.OrdinalIgnoreCase)
            || line.Contains("meant for the game console", StringComparison.OrdinalIgnoreCase))
            return CommandReplyTone.InGame;
        if (IsFailure(line))
            return CommandReplyTone.Failed;
        return CommandReplyTone.Normal;
    }

    private static bool IsFailure(string line)
    {
        return line.Contains("unable", StringComparison.OrdinalIgnoreCase)
            || line.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || line.Contains("could not", StringComparison.OrdinalIgnoreCase)
            || line.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || line.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
            || line.Contains("no hero", StringComparison.OrdinalIgnoreCase)
            || line.Contains("no active", StringComparison.OrdinalIgnoreCase)
            || line.Contains("unknown command", StringComparison.OrdinalIgnoreCase)
            || line.Contains("exception", StringComparison.OrdinalIgnoreCase)
            || line.Contains("object reference", StringComparison.OrdinalIgnoreCase)
            || line.Contains("usage:", StringComparison.OrdinalIgnoreCase)
            || line.Contains("printed nothing", StringComparison.OrdinalIgnoreCase)
            || line.Contains("was not sent", StringComparison.OrdinalIgnoreCase)
            // A console queue reply the server turned down (SendThroughQueue: "The server refused that command: ...").
            || line.Contains("refused", StringComparison.OrdinalIgnoreCase)
            || line.Contains("server is not running", StringComparison.OrdinalIgnoreCase)
            || line.Contains("did not start", StringComparison.OrdinalIgnoreCase)
            || line.Contains("is not a boolean", StringComparison.OrdinalIgnoreCase)
            || line.Contains("must be a positive", StringComparison.OrdinalIgnoreCase)
            || line.Contains("invalid", StringComparison.OrdinalIgnoreCase);
    }
}
