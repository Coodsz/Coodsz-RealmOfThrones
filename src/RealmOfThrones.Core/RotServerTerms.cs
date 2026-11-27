using System.Security.Cryptography;
using System.Text;
using BannerlordCoopLauncher.Core.Server;

namespace RealmOfThrones.Core;

/// <summary>
/// The Coop dedicated server asks the person hosting to accept its EULA and LICENSE before it hosts, the same
/// flow as the Burning Empires server tool (BeServerTerms). This tool shows the terms the server folder came
/// with, and only after the operator presses "I accept" passes DS_ACCEPT_EULA=true to that start
/// (NightlyServer.StartPlan); otherwise the start removes any DS_ACCEPT_EULA the tool inherited. The server then
/// records the answer in its data folder's eula.txt. Asked again only when the terms change.
/// </summary>
public static class RotServerTerms
{
    public const string EnvironmentVariable = NightlyServer.AcceptTermsVariable;
    public const int ExitCode = 6;
    public const string Title = "Coop server terms";
    public const string AcceptButton = "I accept";

    public const string Body =
        "The Realm of Thrones server runs on the Bannerlord Coop dedicated server, which has a license agreement (EULA). The server does not host until the person hosting accepts it.\n\n"
        + "Read the terms below. Click I accept only if you agree. This tool then tells the server you accepted, and the server saves that in its eula.txt. You are asked again only when the terms change.\n\n"
        + "The terms allow private, non-commercial community hosting. Paid hosting needs written permission from the Coop team.";

    public const string Refused =
        "The server was not started. It only starts after the person hosting accepts the Coop server terms. Click Start server again to read them.";

    private static readonly string[] EulaNames = ["EULA.txt", "EULA.md"];

    public static string? TermsText(string serverFolder)
    {
        var eula = EulaNames.Select(name => Path.Combine(serverFolder, name)).FirstOrDefault(File.Exists);
        if (eula == null)
            return null;
        var text = new StringBuilder(File.ReadAllText(eula).TrimEnd());
        var license = Path.Combine(serverFolder, "LICENSE.txt");
        if (File.Exists(license))
            text.Append("\n\n--- LICENSE.txt ---\n\n").Append(File.ReadAllText(license).TrimEnd());
        return text.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
    }

    public static string? TermsHash(string serverFolder)
    {
        var text = TermsText(serverFolder);
        return text == null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    public static bool NeedsAnswer(string? termsHash, string? acceptedHash) =>
        termsHash != null && !string.Equals(termsHash, acceptedHash, StringComparison.OrdinalIgnoreCase);
}
