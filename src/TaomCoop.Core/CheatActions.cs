using BannerlordCoopLauncher.Core.Server;

namespace TaomCoop.Core;

// What the Cheats pages of the Coodsz Host and TAOM server tools say when a reply names an unknown command.
// The server's @DS@ commands list is not complete, so a cheat is never turned off from it; a reply that
// says the command is unknown gets a plain line naming the command.
public static class CheatActions
{
    // The line shown when the server answered that it does not know the command, or null.
    public static string? UnknownCommandNote(string command, IEnumerable<string> reply) =>
        AdminCommand.IsUnknownCommandReply(reply)
            ? "The server does not know the command " + command.Trim().Split(' ', 2)[0] + "."
            : null;

}
