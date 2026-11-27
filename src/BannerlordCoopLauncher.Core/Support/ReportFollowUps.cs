using System.IO.Compression;
using System.Net.Http;

namespace BannerlordCoopLauncher.Core.Support;

/// <summary>What the No, still broken form sends: the earlier report, its key, the steps answer, the player's words and what to add.</summary>
public sealed record FollowUpForm(string Code, string Key, string Steps, string Text, bool IncludeLogs, ReportScreenshot? Screenshot);

public enum FollowUpStatus
{
    Sent,
    Empty,
    TooOld,
    Busy,

    /// <summary>The worker answered but could not add it to the report (GitHub down, a refusal): try again later.</summary>
    Refused,

    /// <summary>No answer from the worker at all (offline).</summary>
    Failed,

    /// <summary>Something on this PC: the logs or the screenshot could not be packed.</summary>
    LocalFailed
}

/// <summary>What sending the form got back: the new logs' code and the words as they were sent (cleaned).</summary>
public sealed record FollowUpResult(FollowUpStatus Status, string? NewCode = null, string CleanedText = "");

/// <summary>
/// No, still broken (the reply card in My reports): the player's answer to Coodsz's reply goes through the
/// worker like any report, never straight to Coodsz. It is packed the same way as Send my logs: the
/// tool's own cleaned logs from the last run when Add my new logs is ticked (no crash dump), the words
/// cleaned like the logs as player-notes.txt, and at most one screenshot, re-encoded and stripped of
/// its metadata. The summary carries followUp with the earlier report's code and reply key, so the
/// worker adds it to that report's issue (server/mod-reports-worker/src/followup.js) instead of filing
/// a new one, and only the PC that sent the report can.
/// </summary>
public static class ReportFollowUps
{
    /// <summary>The longest answer. The worker quotes at most 1500 characters too.</summary>
    public const int MaxText = 1500;

    /// <summary>The largest screenshot after it was re-encoded (ReportScreenshotImages.Prepare).</summary>
    public const long MaxScreenshotBytes = 2_000_000;

    /// <summary>
    /// The text cut to at most max characters at a whole character: never inside a surrogate pair, an emoji
    /// joined with ZWJ or a letter with its combining marks (StringInfo text elements).
    /// </summary>
    public static string Cut(string text, int max)
    {
        if (text.Length <= max)
            return text;
        var length = 0;
        var elements = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var end = elements.ElementIndex + ((string)elements.Current).Length;
            if (end > max)
                break;
            length = end;
        }

        return text[..length];
    }

    /// <summary>
    /// The request the form packs: Send my logs, without a crash dump, and with no logs at all when the
    /// box is not ticked (an empty zip that only gets the words and the screenshot).
    /// </summary>
    public static CrashReportRequest ForFollowUp(CrashReportRequest request, bool includeLogs) => new()
    {
        Tool = request.Tool,
        Side = request.Side,
        Reason = CrashReason.Logs,
        ToolVersion = request.ToolVersion,
        Coop = request.Coop,
        Game = request.Game,
        Secrets = request.Secrets,
        FileStem = request.FileStem,
        Modules = includeLogs ? request.Modules : Array.Empty<(string Id, string? Version)>(),
        Collect = includeLogs ? request.Collect : EmptyZip,
        ServerFolders = request.ServerFolders,
        ModuleRoots = includeLogs ? request.ModuleRoots : Array.Empty<(string Label, string Folder)>(),
        FindDump = null,
        IncludeDump = false
    };

    private static IReadOnlyList<string> EmptyZip(string zipPath, LogZipLimits limits)
    {
        using (ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
        }

        return Array.Empty<string>();
    }

    /// <summary>The summary that is sent: the packed one, no problem picked, and followUp with the code, key and steps.</summary>
    public static CrashReportSummary WithFollowUp(CrashReportSummary summary, string code, string key, string steps)
    {
        var copy = CrashReports.CloneSummary(summary);
        copy.Problem = null;
        copy.ReplyKey = null;
        copy.FollowUp = new CrashFollowUp { Code = code, Key = key, Steps = FollowUpSteps.IsKnown(steps) ? steps : FollowUpSteps.All };
        return copy;
    }

    /// <summary>
    /// Packs and sends the form. Runs the packing off the calling thread and never throws. The words must
    /// not be empty after cleaning; the screenshot must be a PNG or JPEG of at most MaxScreenshotBytes.
    /// </summary>
    public static async Task<FollowUpResult> SendAsync(HttpClient http, CrashReportRequest request, string install, FollowUpForm form,
        string? endpoint = CrashReports.Endpoint, string? folder = null)
    {
        var text = (form.Text ?? "").Trim();
        text = Cut(text, MaxText);
        var cleaned = ReportExtras.CleanNotes(text, request.Secrets);
        if (cleaned.Length == 0)
            return new FollowUpResult(FollowUpStatus.Empty);
        if (!CrashReports.IsCode(form.Code) || !ReportReplies.IsKey(form.Key) || !FollowUpSteps.IsKnown(form.Steps))
            return new FollowUpResult(FollowUpStatus.TooOld);
        var shots = new List<ReportScreenshot>();
        if (form.Screenshot is { } shot)
        {
            if (shot.Kind == ReportImageKind.None || ReportExtras.DetectImage(shot.Bytes) != shot.Kind || shot.Size > MaxScreenshotBytes)
                return new FollowUpResult(FollowUpStatus.LocalFailed);
            shots.Add(shot);
        }

        var packRequest = ForFollowUp(request, form.IncludeLogs);
        CrashReportPack? pack = null;
        CrashReportPack? final = null;
        try
        {
            pack = await Task.Run(() => CrashReports.Prepare(packRequest, install, folder)).ConfigureAwait(false);
            final = await Task.Run(() => CrashReports.WithExtras(packRequest, pack, cleaned, shots)).ConfigureAwait(false);
            var result = await CrashReports.SendAsync(http, final, WithFollowUp(final.Summary, form.Code, form.Key, form.Steps), endpoint).ConfigureAwait(false);
            return result.Status switch
            {
                // Sent only when the worker says the answer is on the report ("filed": true). A code without
                // it means the comment did not land, so the player is asked to try again.
                CrashReportSendStatus.Sent when result.Filed == true => new FollowUpResult(FollowUpStatus.Sent, result.Code, cleaned.Replace("\r\n", "\n")),
                CrashReportSendStatus.Sent => new FollowUpResult(FollowUpStatus.Refused),
                CrashReportSendStatus.RateLimited => new FollowUpResult(FollowUpStatus.Busy),
                // The worker answers 404 when the key no longer opens the report (older than 30 days).
                _ when result.Detail?.StartsWith("HTTP 404", StringComparison.Ordinal) == true => new FollowUpResult(FollowUpStatus.TooOld),
                // Any other answer from the worker (the 502 when GitHub took no comment): it was reached, so not the connection.
                // Too large: the same pack is too large again later, so it is the pack on this PC (untick the logs or the screenshot).
                CrashReportSendStatus.TooLarge => new FollowUpResult(FollowUpStatus.LocalFailed),
                CrashReportSendStatus.Refused => new FollowUpResult(FollowUpStatus.Refused),
                _ => new FollowUpResult(FollowUpStatus.Failed)
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            // The pack, not the network: CrashReports.SendAsync never throws for a network problem.
            return new FollowUpResult(FollowUpStatus.LocalFailed);
        }
        finally
        {
            if (final != null && !ReferenceEquals(final, pack))
                CrashReports.DeleteQuietly(final.ZipPath);
            if (pack != null)
                CrashReports.DeleteQuietly(pack.ZipPath);
        }
    }
}
