namespace TaomCoop.Core;

/// <summary>
/// TAOM.CoopCompat's own pre serving check stopped a start (exit code 2 after a line such as
/// "FATAL during a: System.InvalidOperationException: TAOM.CoopCompat rejected AssertPreServingReady:
/// TAOM.CoopCompat pre-serving attestation failed: module=True, ... monsters=False"). The save never
/// loaded, so it is no load crash: no load mode retry and no crash bar on the first stop. Its preflight
/// also stops a start when one of its own DLLs is gone ("[BannerlordCoopServer] FATAL: TAOM.CoopCompat
/// preflight failed: required TAOM.CoopCompat feature DLL is missing", player report CR-HZYX5), when a DLL
/// is not the pinned one ("feature DLL hash ... does not match the wrapper pin") or its SubModule.xml is gone
/// ("required TAOM.CoopCompat module descriptor is missing", CR-XXP67), which Start server puts back from the
/// host kit (ServerProgramRepair.CompatFiles). The hash of the new TAOM download's TAOM.CoopCompat v2
/// (NewTaomCompat) gets that module's own line instead.
/// </summary>
public static class TaomStartupCheck
{
    public const string Text = "TAOM.CoopCompat stopped this start during its own startup check. Your save is fine. Press Start server again. If it stops the same way again, send the report.";

    public const string MissingDllText = "A TAOM.CoopCompat file is missing from the server folder. Antivirus most likely removed it. Press Start server and it puts the file back. If the file goes missing again, click Fix antivirus. Your save is fine.";

    public const string ChangedText = "A TAOM.CoopCompat file in the server folder changed or is gone. Antivirus may have quarantined it. Press Start server and it puts back the right file from the TAOM host kit. If it happens again, click Fix antivirus. Your save is fine.";

    public static bool IsStopLine(string? line) =>
        !string.IsNullOrEmpty(line)
        && (line.Contains("pre-serving attestation failed", StringComparison.OrdinalIgnoreCase)
            || line.Contains("rejected AssertPreServingReady", StringComparison.OrdinalIgnoreCase)
            || FileText(line) != null);

    public static bool IsMissingDllLine(string? line) =>
        !string.IsNullOrEmpty(line)
        && line.Contains("TAOM.CoopCompat preflight failed", StringComparison.OrdinalIgnoreCase)
        && line.Contains("DLL is missing", StringComparison.OrdinalIgnoreCase);

    /// <summary>The line for a preflight stop about one of TAOM.CoopCompat's own files, or null for any other line.</summary>
    public static string? FileText(string? line)
    {
        if (IsMissingDllLine(line))
            return MissingDllText;
        if (string.IsNullOrEmpty(line))
            return null;
        // The player report quoted the descriptor stop as "required TAOM.CoopCompat module descriptor is missing"
        // without saying whether the "preflight failed" prefix came with it (the wrapper's text is obfuscated), so
        // that one line counts either way.
        if (line.Contains("required TAOM.CoopCompat module descriptor is missing", StringComparison.OrdinalIgnoreCase))
            return ChangedText;
        if (!line.Contains("TAOM.CoopCompat preflight failed", StringComparison.OrdinalIgnoreCase))
            return null;
        if (line.Contains(NewTaomCompat.DllSha256[..8], StringComparison.OrdinalIgnoreCase))
            return NewTaomCompat.Text(null);
        return line.Contains("does not match the wrapper pin", StringComparison.OrdinalIgnoreCase)
            || line.Contains("module descriptor is missing", StringComparison.OrdinalIgnoreCase)
                ? ChangedText
                : null;
    }

    /// <summary>
    /// The window report for a log text, or null when no single line of it is a stop line. Each line is read on
    /// its own, like the server log watch, so two phrases on different lines never make one.
    /// </summary>
    public static TaomHostReport.Report? Report(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        var lines = text.Split('\n');
        if (!lines.Any(IsStopLine))
            return null;
        var shown = lines.Select(FileText).FirstOrDefault(found => found != null) ?? Text;
        return new([shown], shown, true);
    }
}
