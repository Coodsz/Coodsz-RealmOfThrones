using System.Security.Cryptography;
using System.Text;

namespace TaomCoop.Core;

/// <summary>
/// The 0.3.25 server (built on Coop 0.1.6) asks the person hosting to accept the
/// Coop server EULA and LICENSE before it hosts. This tool shows the packaged terms,
/// and only after the operator presses "I accept" passes DS_ACCEPT_EULA=true to that
/// start. The server then records it in the Data folder's eula.txt. Asked again only
/// when the terms change.
/// </summary>
public static class ServerTerms
{
    public const string EnvironmentVariable = "DS_ACCEPT_EULA";
    public const int ExitCode = 6;
    public const string Title = "Coop server terms";
    public const string AcceptButton = "I accept";

    public const string Body =
        "The TAOM-Coop server is built on Bannerlord Coop 0.1.6, which has a license agreement (EULA) for its dedicated server. The server does not host until the person hosting accepts it.\n\n"
        + "Read the terms below. Press I accept only if you agree. This tool then tells the server you accepted, and the server saves that in the Data folder's eula.txt. You are asked again only when the terms change.\n\n"
        + "The terms allow private, non-commercial community hosting. Paid hosting needs written permission from the Coop team.";

    public const string Refused =
        "The server was not started. It only starts after the person hosting accepts the Coop server terms. Press Start server again to read them.";

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
