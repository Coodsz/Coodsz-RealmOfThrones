using System.Globalization;
using System.Text;
using BannerlordCoopLauncher.Core.Modules;

namespace BannerlordCoopLauncher.Core.Support;

/// <summary>
/// The Your Discord name box of a crash report window. Reports are anonymous (the reporter is a
/// hash), so Send needs a Discord name Coodsz can reach the player at. It goes only in the summary
/// (contact), never in the zip, and Save a copy works without it. The name is kept per PC in
/// mod-reports.json next to the install id and filled in again next time, so after the first
/// report players just click Send.
/// </summary>
public static class CrashContact
{
    public const int MinChars = 2;
    public const int MaxChars = 40;

    public const string Label = "Your Discord name";
    public const string Note = "Coodsz uses it to reach out to you about this report. People who can read the report can see it.";
    public const string NeededText = "Enter your Discord name to send.";

    /// <summary>
    /// The name as it is sent: one line, control and invisible format characters taken out, spaces
    /// folded, trimmed and cut to MaxChars without splitting an emoji. Empty when nothing is left.
    /// </summary>
    public static string Clean(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return "";
        var text = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            var category = char.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.Control || char.IsWhiteSpace(ch))
                text.Append(' ');
            else if (category != UnicodeCategory.Format)
                text.Append(ch);
        }

        var cleaned = string.Join(' ', text.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (cleaned.Length <= MaxChars)
            return cleaned;
        var cut = MaxChars;
        if (char.IsHighSurrogate(cleaned[cut - 1]))
            cut--;
        return cleaned[..cut].TrimEnd();
    }

    /// <summary>True when the name is long enough for Send (MinChars to MaxChars after cleaning).</summary>
    public static bool CanSend(string? name) => Clean(name).Length >= MinChars;

    /// <summary>The same summary with the cleaned name, or without any when the name is too short.</summary>
    public static CrashReportSummary WithContact(CrashReportSummary summary, string? name)
    {
        var copy = CrashReports.CloneSummary(summary);
        copy.Contact = CanSend(name) ? Clean(name) : null;
        return copy;
    }

    /// <summary>The name kept on this PC, cleaned, to fill in the box. Empty the first time.</summary>
    public static string Remembered(ModReportSettings settings) => Clean(settings.CrashContact);

    /// <summary>Keeps the cleaned name for the next report. A name too short to send is not kept.</summary>
    public static void Remember(ModReportSettings settings, string? name)
    {
        if (CanSend(name))
            settings.CrashContact = Clean(name);
    }

    /// <summary>Loads the kept name from the settings file.</summary>
    public static string Load(string? path = null) => Remembered(ModReports.LoadSettings(path));

    /// <summary>Keeps the name in the settings file. A file problem only loses the change.</summary>
    public static void Save(string? name, string? path = null)
    {
        if (!CanSend(name))
            return;
        var settings = ModReports.LoadSettings(path);
        if (settings.CrashContact == Clean(name))
            return;
        Remember(settings, name);
        ModReports.SaveSettings(settings, path);
    }
}
