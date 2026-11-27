using System.Diagnostics;
using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Server;

namespace BannerlordCoopLauncher.Core.Modules;

/// <summary>
/// The exact Coop build: the ProductVersion of Coop's Common.dll, for example
/// 0.1.8+26a82a49d947ae45bd32977b1eefd30c54a419e5 (the version, then the commit it was built from). Coop turns a
/// join down unless both sides run the very same build, while SubModule.xml says v0.1.8 for every nightly, so a
/// tool that needs one Coop build compares this string.
/// </summary>
public static class CoopBuildId
{
    public const string AssemblyName = "Common.dll";

    private static readonly string[] BinFolders = ["Win64_Shipping_Client", "Win64_Shipping_Server"];

    /// <summary>The build of an installed Coop module folder (Modules\Coop on a PC, engine\Modules\Coop on a
    /// server): Common.dll's ProductVersion from bin\Win64_Shipping_Client, else bin\Win64_Shipping_Server. Null when
    /// there is none or it cannot be read.</summary>
    public static string? FromModule(string? moduleFolder)
    {
        if (string.IsNullOrWhiteSpace(moduleFolder))
            return null;
        foreach (var bin in BinFolders)
        {
            var dll = Path.Combine(moduleFolder, "bin", bin, AssemblyName);
            try
            {
                if (File.Exists(dll) && FileVersionInfo.GetVersionInfo(dll).ProductVersion is { } version && version.Trim().Length > 0)
                    return version.Trim();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
            {
            }
        }

        return null;
    }

    /// <summary>The build of a Coop server folder: its Coop module (engine\Modules\Coop), else the commit its
    /// release-info.txt names ("Paired Coop: BannerlordCoop commit ..."). Null when neither can be read.</summary>
    public static string? FromServer(string? serverRoot)
    {
        if (string.IsNullOrWhiteSpace(serverRoot))
            return null;
        if (FromModule(Path.Combine(ServerModuleInstall.ModulesRoot(serverRoot), "Coop")) is { } build)
            return build;
        var info = Path.Combine(serverRoot, "release-info.txt");
        try
        {
            return File.Exists(info) ? FromReleaseInfo(File.ReadAllText(info)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The commit of the "Paired Coop:" line of a Coop server's release-info.txt, or null.</summary>
    public static string? FromReleaseInfo(string? text)
    {
        var match = Regex.Match(text ?? "", @"(?im)^\s*Paired Coop:.*?\b([0-9a-f]{40})\b");
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }

    /// <summary>The commit of a build (after the +, or the whole string when it is a commit), lower case, or null.</summary>
    public static string? Commit(string? build)
    {
        var text = (build ?? "").Trim();
        var plus = text.IndexOf('+');
        var commit = plus >= 0 ? text[(plus + 1)..] : text;
        return Regex.IsMatch(commit, "^[0-9a-fA-F]{7,40}$") ? commit.ToLowerInvariant() : null;
    }

    /// <summary>The version of a build (before the +), or null when it has none.</summary>
    public static string? Version(string? build)
    {
        var text = (build ?? "").Trim();
        var plus = text.IndexOf('+');
        if (plus > 0)
            return text[..plus];
        return text.Length > 0 && Commit(text) == null ? text : null;
    }

    /// <summary>True when both are the same build. Two commits are compared (one may be the short form of the
    /// other), and two versions when both have one; without a commit on both sides the whole strings are.</summary>
    public static bool Same(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;
        var left = Commit(a);
        var right = Commit(b);
        if (left == null || right == null)
            return a.Trim().Equals(b.Trim(), StringComparison.OrdinalIgnoreCase);
        if (!(left.StartsWith(right, StringComparison.Ordinal) || right.StartsWith(left, StringComparison.Ordinal)))
            return false;
        return Version(a) is not { } one || Version(b) is not { } two || one.TrimStart('v', 'V').Equals(two.TrimStart('v', 'V'), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A build for a line people read: the version and the first 8 characters of the commit
    /// (0.1.8+26a82a49), "commit 26a82a49" when there is no version, or the string as it is.</summary>
    public static string Short(string? build)
    {
        var text = (build ?? "").Trim();
        if (Commit(text) is not { } commit)
            return text;
        var head = commit.Length > 8 ? commit[..8] : commit;
        return Version(text) is { } version ? version + "+" + head : "commit " + head;
    }
}
