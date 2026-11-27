// Ported from CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep), Saves/SaveSet.cs
// (the save name check and the file extensions), changed for the Coodsz tools.
//
// Copyright (c) 2026 aderimo
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.Text;

namespace BannerlordCoopLauncher.Core.Saves;

/// <summary>
/// A dedicated server campaign is <c>&lt;name&gt;.sav</c> with its Coop
/// <c>&lt;name&gt;.json</c> (the players of the save) and, when Start-ModdedServer.ps1
/// wrote one, <c>&lt;name&gt;.launcher.json</c> (the mods it ran with). The files move
/// together: a .sav put back without its .json loses the player mapping.
/// </summary>
public static class SaveSet
{
    public const string SaveExtension = ".sav";
    public const string CompanionExtension = ".json";
    public const string LauncherExtension = ".launcher.json";

    /// <summary>
    /// Shown on its own line after a single player save is copied to the server. Not checked from the save: its
    /// header lists StoryMode for Sandbox saves too, and the real game type sits in the compressed body.
    /// </summary>
    public const string SandboxOnlyNote = "Only Sandbox campaigns run on a Coop server. A save from the Story Mode campaign can't load there.";

    /// <summary>Every file of one save, the .sav first.</summary>
    public static readonly IReadOnlyList<string> Extensions = new[] { SaveExtension, CompanionExtension, LauncherExtension };

    /// <summary>
    /// The marker the server keeps next to a save while it writes it. The dedicated server makes
    /// it a folder (it holds primary.json), so a File.Exists check alone never sees it.
    /// </summary>
    public const string TransactionExtension = ".autosave-transaction";

    public static string TransactionMarker(string savesDirectory, string saveName) =>
        Path.Combine(savesDirectory, saveName + TransactionExtension);

    /// <summary>True while the marker is there, as a folder or as a file.</summary>
    public static bool MarkerExists(string marker) => Directory.Exists(marker) || File.Exists(marker);

    /// <summary>True while the server is still writing the save.</summary>
    public static bool BeingSaved(string savesDirectory, string saveName) =>
        MarkerExists(TransactionMarker(savesDirectory, saveName));

    /// <summary>
    /// Why the server start script refuses this name for a NEW campaign (-NewSaveName), or null: the same rule as
    /// Start-ModdedServer.ps1, any letter or digit, then letters, digits, spaces, hyphens and underscores. The script
    /// once took only A to Z there and refused "YÜZÜK TEST" only after Start was pressed.
    /// </summary>
    public static string? NewSaveNameProblem(string? name) =>
        NameMatches(name, NewSaveNamePattern) ? null : NewSaveNameRule;

    public const string NewSaveNameRule = "A new campaign name can only use letters, numbers, spaces, hyphens and underscores, up to 64 characters, starting with a letter or number.";

    /// <summary>The -NewSaveName rule of the start script this repo ships (Start-ModdedServer.ps1).</summary>
    public const string NewSaveNamePattern = @"^[\p{L}\p{Nd}][\p{L}\p{Nd} _-]{0,63}$";

    /// <summary>The rule of start scripts before letters of any language were allowed: A to Z only.</summary>
    public const string OldNewSaveNamePattern = "^[A-Za-z0-9][A-Za-z0-9 _-]{0,63}$";

    public const string OldScriptNewSaveNameRule = "This server's start script is an older copy that only takes the English letters A to Z, numbers, spaces, hyphens and underscores in a new campaign name. Pick a name like that, or close anything that has the server folder open and press Start server again so the script is updated.";

    /// <summary>
    /// The -NewSaveName rule a server folder's own start script really uses: read from its text, or the old A to Z
    /// rule when the line cannot be found. A tool that did not write that script (or whose copy could not be written)
    /// checks with this, so a name the script would refuse never gets past Start.
    /// </summary>
    public static string NewSaveNamePatternIn(string? scriptText)
    {
        var match = System.Text.RegularExpressions.Regex.Match(scriptText ?? "",
            @"\$selectedSaveName\s+-cnotmatch\s+'(?<pattern>[^']+)'", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (!match.Success)
            return OldNewSaveNamePattern;
        var pattern = match.Groups["pattern"].Value;
        try
        {
            _ = new System.Text.RegularExpressions.Regex(pattern);
            return pattern;
        }
        catch (ArgumentException)
        {
            return OldNewSaveNamePattern;
        }
    }

    /// <summary>Why this server folder's start script would refuse the name for a new campaign, or null.</summary>
    public static string? NewSaveNameProblemFor(string? name, string? scriptText)
    {
        var pattern = NewSaveNamePatternIn(scriptText);
        if (NameMatches(name, pattern))
            return null;
        return NameMatches(name, NewSaveNamePattern) ? OldScriptNewSaveNameRule : NewSaveNameRule;
    }

    private static bool NameMatches(string? name, string pattern) =>
        System.Text.RegularExpressions.Regex.IsMatch(name ?? "", pattern, System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>Is this name safe to use as a file name? Guards against paths such as ..\x.</summary>
    public static bool IsValidSaveName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        if (name.Length > 120)
            return false;

        // No path separator or drive.
        if (name.Contains('/') || name.Contains('\\') || name.Contains(':'))
            return false;
        if (name.Contains("..", StringComparison.Ordinal))
            return false;
        if (name.StartsWith('.') || name.EndsWith('.'))
            return false;
        if (name.Trim() != name)
            return false;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;

        // Device names Windows keeps for itself.
        var stem = name.Split('.')[0];
        string[] reserved =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };
        return !reserved.Contains(stem, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>How long before or after the "world saved" line the .sav found for a bent name may have been written.</summary>
    public static readonly TimeSpan LoggedSaveWindow = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The save on disk that a name read from the server's console means, or null when no backup
    /// may be made (then <paramref name="refusal"/> says why). A heuristic fallback: the server writes
    /// its console in the OEM code page, so "YÜZÜKLERİN" can arrive as "YšZšKLER˜N", as "YÜZÜKLERIN"
    /// (best fit), with ? or with U+FFFD. Backing up the wrong world under the logged name is worse
    /// than no backup, so the rules are strict:
    /// <list type="bullet">
    /// <item>A .sav with the name as it came wins, and a name that came as plain ASCII is never remapped.</item>
    /// <item>Only a .sav with a non ASCII name counts. Each run of non ASCII characters or ? in the logged
    /// name stands for 1 to 3 times as many non ASCII characters on disk, never for an ASCII one.</item>
    /// <item>An ASCII letter matches itself, or an accented letter on disk that loses its accent to it.</item>
    /// <item>Exactly one .sav may fit. With two or more nothing is backed up.</item>
    /// <item>That .sav must have been written within <see cref="LoggedSaveWindow"/> of the line.</item>
    /// </list>
    /// When nothing fits the name comes back as it came, so the backup reports the missing .sav.
    /// It only lists the saves folder; the caller still checks the result with IsValidSaveName.
    /// </summary>
    public static string? ResolveLoggedName(string savesDirectory, string loggedName, DateTime loggedUtc, out string? refusal)
    {
        refusal = null;
        try
        {
            if (!loggedName.Any(Bent) || File.Exists(Path.Combine(savesDirectory, loggedName + SaveExtension)) || !Directory.Exists(savesDirectory))
                return loggedName;
            var fits = new DirectoryInfo(savesDirectory).EnumerateFiles("*" + SaveExtension)
                .Select(file => (File: file, Name: Path.GetFileNameWithoutExtension(file.Name)))
                .Where(item => item.Name.Any(c => c >= 0x80) && Fits(loggedName, item.Name))
                .Take(2)
                .ToList();
            if (fits.Count == 0)
                return loggedName;
            if (fits.Count > 1)
            {
                refusal = "Did not back up " + loggedName + ". More than one save fits the name the server wrote (" + fits[0].Name + ", " + fits[1].Name + "), so none was backed up.";
                return null;
            }

            var (file, name) = fits[0];
            if ((file.LastWriteTimeUtc - loggedUtc).Duration() > LoggedSaveWindow)
            {
                refusal = "Did not back up " + loggedName + ". The save that fits the name the server wrote, " + name + ", was not written just now, so it may be another world.";
                return null;
            }

            return name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return loggedName;
        }
    }

    private static bool Bent(char c) => c >= 0x80 || c == '?';

    // Walks both names once per (logged position, disk position) pair, so a long name cannot make
    // it slow the way a backtracking pattern could.
    private static bool Fits(string logged, string disk)
    {
        var seen = new HashSet<(int, int)>();
        bool From(int i, int j)
        {
            if (i == logged.Length)
                return j == disk.Length;
            if (j == disk.Length || !seen.Add((i, j)))
                return false;
            if (!Bent(logged[i]))
                return SameLetter(logged[i], disk[j]) && From(i + 1, j + 1);
            var run = 1;
            while (i + run < logged.Length && Bent(logged[i + run]))
                run++;
            for (var take = 1; take <= 3 * run && j + take <= disk.Length && disk[j + take - 1] >= 0x80; take++)
            {
                if (From(i + run, j + take))
                    return true;
            }

            return false;
        }

        return From(0, 0);
    }

    private static bool SameLetter(char logged, char disk)
    {
        if (char.ToUpperInvariant(logged) == char.ToUpperInvariant(disk))
            return true;
        if (disk < 0x80 || !char.IsAsciiLetter(logged))
            return false;
        // A best fit code page writes İ as I and ş as s.
        var plain = disk == 'ı' ? 'i' : disk.ToString().Normalize(NormalizationForm.FormD)[0];
        return char.ToUpperInvariant(plain) == char.ToUpperInvariant(logged);
    }
}
