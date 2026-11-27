using System.Text;

namespace BannerlordCoopLauncher.Core.Legal;

/// <summary>
/// THIRD-PARTY-NOTICES.md at the repository root, embedded so every exe carries the licenses of the
/// code ported from other projects. The two exe zips hold only the exes, so this copy
/// is the one players get. The license window shows it under the Coodsz license.
/// </summary>
public static class ThirdPartyNotices
{
    public const string ResourceName = "Coodsz.THIRD-PARTY-NOTICES.md";

    private static readonly Lazy<string> Embedded = new(ReadEmbedded);

    /// <summary>The notices, with Windows line endings for a text box. Empty in a build without them.</summary>
    public static string Text => Embedded.Value;

    private static string ReadEmbedded()
    {
        using var stream = typeof(ThirdPartyNotices).Assembly.GetManifestResourceStream(ResourceName);
        if (stream == null)
            return "";
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().Replace("\r\n", "\n").Trim().Replace("\n", "\r\n");
    }
}
