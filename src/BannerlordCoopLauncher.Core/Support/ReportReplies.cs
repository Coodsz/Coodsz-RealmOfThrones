using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Modules;

namespace BannerlordCoopLauncher.Core.Support;

/// <summary>One answer this tool already showed: the comment id and its date. An edited comment has a new date, so it shows once more.</summary>
public sealed class ReplySeen
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("at")] public string At { get; set; } = "";
}

/// <summary>One sent report this tool can get replies for: the code, its private reply key and what was already shown.</summary>
public sealed class ReplyKeyEntry
{
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("sentAt")] public DateTime SentAt { get; set; }
    [JsonPropertyName("seen")] public List<ReplySeen> Seen { get; set; } = new();

    /// <summary>The report's short title for My reports: the known cause or the crash line, cleaned. Empty for older entries.</summary>
    [JsonPropertyName("title")] public string? Title { get; set; }

    /// <summary>crash, server-crash or logs (CrashReason), for the title when there is none.</summary>
    [JsonPropertyName("reason")] public string? Reason { get; set; }
}

/// <summary>What the worker last said about a report and what the player did: Yes, it's fixed (SolvedAt).</summary>
public sealed class ReportState
{
    [JsonPropertyName("code")] public string Code { get; set; } = "";

    /// <summary>looking, answered, solved or fixed, as the worker said (ReportStatus).</summary>
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("fixedIn")] public string? FixedIn { get; set; }
    [JsonPropertyName("statusAt")] public DateTime? StatusAt { get; set; }
    [JsonPropertyName("solvedAt")] public DateTime? SolvedAt { get; set; }
}

/// <summary>The player's own No, still broken answer, kept to show under Coodsz's reply. Only the cleaned words.</summary>
public sealed class ReportFollowUp
{
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonPropertyName("at")] public DateTime At { get; set; }

    /// <summary>all, some or none (FollowUpSteps).</summary>
    [JsonPropertyName("steps")] public string Steps { get; set; } = FollowUpSteps.All;
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("logs")] public bool Logs { get; set; }
    [JsonPropertyName("screenshot")] public bool Screenshot { get; set; }

    /// <summary>The code the new logs got, for the player's own record.</summary>
    [JsonPropertyName("newCode")] public string? NewCode { get; set; }
}

/// <summary>The answer to Did you try the steps?</summary>
public static class FollowUpSteps
{
    public const string All = "all";
    public const string Some = "some";
    public const string None = "none";

    public static bool IsKnown(string? steps) => steps is All or Some or None;
}

/// <summary>The status badge of a report: Looking into it, Answered, Solved or Fixed in a version.</summary>
public enum ReportStatusKind
{
    Looking,
    Answered,
    Solved,
    Fixed
}

/// <summary>The badge to show. FixedIn is the version for Fixed, like 1.3.3.</summary>
public sealed record ReportStatus(ReportStatusKind Kind, string? FixedIn = null)
{
    public const string Looking = "looking";
    public const string Answered = "answered";
    public const string Solved = "solved";
    public const string Fixed = "fixed";

    private static readonly Regex Version = new(@"^\d{1,4}(?:\.\d{1,4}){1,3}$", RegexOptions.CultureInvariant);

    public static bool IsKnown(string? status) => status is Looking or Answered or Solved or Fixed;

    public static bool IsVersion(string? version) => Version.IsMatch(version ?? "");

    /// <summary>
    /// The badge from what this PC knows. The newest of the three things the player saw counts: the newest
    /// answer (answerAt), the player's newest No, still broken (followUpAt) and Yes, it's fixed (state.SolvedAt).
    /// A follow up last means Looking into it until Coodsz answers again. Yes last means Solved, or Fixed in
    /// a version when Coodsz labeled it. An answer last means what the worker said, or Answered without it.
    /// </summary>
    public static ReportStatus Of(ReportState? state, DateTime? answerAt, DateTime? followUpAt)
    {
        var solvedAt = state?.SolvedAt;
        var fixedIn = state?.Status == Fixed && IsVersion(state!.FixedIn) ? state.FixedIn : null;
        // A release label the worker first said after the player's No wins, like on the worker: Coodsz adds
        // it when the release fixes it, even without a new /reply.
        if (fixedIn != null && followUpAt is { } after && state!.StatusAt > after)
            return new ReportStatus(ReportStatusKind.Fixed, fixedIn);
        if (followUpAt is { } f && (answerAt == null || f > answerAt) && (solvedAt == null || f > solvedAt))
            return new ReportStatus(ReportStatusKind.Looking);
        if (solvedAt is { } s && (answerAt == null || s >= answerAt))
            return fixedIn != null ? new ReportStatus(ReportStatusKind.Fixed, fixedIn) : new ReportStatus(ReportStatusKind.Solved);
        return state?.Status switch
        {
            Fixed when fixedIn != null => new ReportStatus(ReportStatusKind.Fixed, fixedIn),
            Fixed or Solved => new ReportStatus(ReportStatusKind.Solved),
            Answered => new ReportStatus(ReportStatusKind.Answered),
            // The worker said looking, but this PC has an answer it got later: the answer counts.
            Looking when answerAt != null && (state!.StatusAt == null || answerAt > state.StatusAt) => new ReportStatus(ReportStatusKind.Answered),
            Looking => new ReportStatus(ReportStatusKind.Looking),
            _ => new ReportStatus(answerAt != null ? ReportStatusKind.Answered : ReportStatusKind.Looking)
        };
    }
}

/// <summary>report-replies-&lt;tool key&gt;.json.</summary>
public sealed class ReplyKeyFile
{
    [JsonPropertyName("schema")] public int Schema { get; set; } = 1;
    [JsonPropertyName("reports")] public List<ReplyKeyEntry> Reports { get; set; } = new();

    /// <summary>Where the next check starts when there are more than 10 reports (ReportReplyStore.NextBatch).</summary>
    [JsonPropertyName("cursor")] public int Cursor { get; set; }
}

/// <summary>Where an answer in My reports came from.</summary>
public static class AnswerSource
{
    /// <summary>A known cause fix from crash-signatures.json, shown on the crash screen.</summary>
    public const string Known = "known";

    /// <summary>Coodsz's own /reply on the report.</summary>
    public const string Reply = "reply";

    /// <summary>An automatic answer (phase 2, the worker marks it auto).</summary>
    public const string Auto = "auto";
}

/// <summary>One answer kept for My reports. Only the cleaned text is kept, never the reply key.</summary>
public sealed class ReportAnswer
{
    /// <summary>The report code, or empty for a known fix shown before anything was sent.</summary>
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("at")] public string At { get; set; } = "";
    [JsonPropertyName("savedAt")] public DateTime SavedAt { get; set; }
    [JsonPropertyName("source")] public string Source { get; set; } = AnswerSource.Reply;
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("read")] public bool Read { get; set; }
    [JsonPropertyName("logs")] public bool LogsButton { get; set; }
    [JsonPropertyName("replaced")] public bool Replaced { get; set; }

    /// <summary>
    /// DropMissing set this answer aside while it was unread (it marked it read). When the worker serves it again it
    /// shows again, unread once more. Left out of the file while false, so the saved shape only grows when it is needed.
    /// </summary>
    [JsonPropertyName("setAsideUnread"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool SetAsideUnread { get; set; }

    internal string Identity => Code + "|" + Id + "|" + At;
}

/// <summary>report-answers-&lt;tool key&gt;.json.</summary>
public sealed class ReportAnswerFile
{
    [JsonPropertyName("schema")] public int Schema { get; set; } = 1;
    [JsonPropertyName("answers")] public List<ReportAnswer> Answers { get; set; } = new();

    /// <summary>The status per report and the player's Yes, it's fixed.</summary>
    [JsonPropertyName("states")] public List<ReportState> States { get; set; } = new();

    /// <summary>The player's own No, still broken answers.</summary>
    [JsonPropertyName("followUps")] public List<ReportFollowUp> FollowUps { get; set; } = new();
}

/// <summary>One answer the worker served, cleaned. Auto is an automatic answer (phase 2).</summary>
public sealed record ReplyItem(string Code, string Id, string At, string Text, bool LogsButton, bool Auto);

/// <summary>What the card shows under the reply (ReportReplyText.BottomOf).</summary>
public enum CardBottom
{
    None,
    Form,
    Solved,
    Question
}

/// <summary>One part of a reply on the card: a numbered step (Step is its number) or a line of text.</summary>
public sealed record ReplyBlock(int? Step, string Text);

/// <summary>What Yes, it's fixed got back: done, an unknown or expired report, too many tries, or no answer.</summary>
public enum SolvedResult
{
    Done,
    Unknown,
    Busy,
    Failed
}

/// <summary>
/// What one check found: the answers not shown yet, and how long to wait before the next check. Called is false when
/// nothing was asked. Answered is true when the worker answered the check, and Asked is the wait it asked for then
/// (its "next", at most 6 hours), which ReplySchedule honours when it is longer than the schedule's own.
/// </summary>
public sealed record ReplyCheck(IReadOnlyList<ReplyItem> NewItems, TimeSpan Next, bool Called, bool Answered = false, TimeSpan? Asked = null);

/// <summary>
/// How much a report needs the reply check: Looking while it waits for Coodsz (Looking into it), Open while it has an
/// answer the player has not read yet, Read when it is answered and every answer is read, None when it is Solved or Fixed.
/// </summary>
public enum ReplyNeed
{
    None,
    Read,
    Open,
    Looking
}

/// <summary>One report the reply check asks about: when it was sent, the player's newest No, still broken, and how much it needs the check.</summary>
public sealed record ReplyScheduleReport(DateTime SentAt, DateTime? FollowUpAt, ReplyNeed Need);

/// <summary>The wait before the next check and the most random time added on top, so tools do not all ask at once.</summary>
public sealed record ReplyWait(TimeSpan Wait, TimeSpan Jitter);

/// <summary>
/// When the reply check asks next (v1.3.5, with the worker's reply push), sized to the worker's free plan: every 30 s
/// for the first 10 minutes after a report is sent or answered with No, still broken; then every 2 to 3 minutes while
/// a report younger than 30 days is open (Looking into it for less than a day since its send or No, or an answer not
/// read yet); every 15 to 30 minutes while the only reports left are answered and read, or quiet for a day; and no check at all while the tool's window is minimized or no report
/// needs one. A restored window or a sent report brings a check within seconds. A worker that asks for longer (an
/// older worker says 30 minutes, a busy worker brakes) is always honoured.
/// </summary>
public static class ReplySchedule
{
    public static readonly TimeSpan FastWait = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan FastJitter = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan FastFor = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan OpenWait = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan OpenJitter = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan ReadWait = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan ReadJitter = TimeSpan.FromMinutes(15);

    /// <summary>A report Looking into it with no send or No, still broken for this long gets the slow check too.</summary>
    public static readonly TimeSpan QuietAfter = TimeSpan.FromHours(24);

    /// <summary>The first check after the tool starts, and the check soon after a restore or a send.</summary>
    public static readonly TimeSpan StartWait = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan ResumeWait = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan ResumeJitter = TimeSpan.FromSeconds(10);

    /// <summary>The jitter on a wait the worker asked for, and after a failed check, as before v1.3.5.</summary>
    public static readonly TimeSpan SlowJitter = TimeSpan.FromMinutes(1);

    /// <summary>The retry while the keys file can't be read for a moment: 30 to 60 s, so the checks never stop on it.</summary>
    public static readonly ReplyWait BusyRetry = new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

    /// <summary>
    /// The wait from the reports, or null for no check: the window is minimized, or no report younger than 30 days
    /// needs one. The worker's wait wins when it is longer.
    /// </summary>
    public static ReplyWait? Next(DateTime utcNow, IEnumerable<ReplyScheduleReport> reports, TimeSpan? workerWait = null, bool minimized = false)
    {
        if (minimized)
            return null;
        ReplyWait? plan = null;
        foreach (var report in reports)
        {
            // A report or send time ahead of the 30 s checks means the PC clock went back, as ClockWentBack does for a
            // planned check: it counts as sent 10 minutes ago, so the checks go on without the 30 s ones, which would last
            // until the clock caught up. Not only past 6 hours: the time ahead shrinks as the clock runs, and the checks
            // would stop once it went under.
            var sentAt = Ahead(utcNow, report.SentAt) ? utcNow - FastFor : report.SentAt;
            var followUpAt = Ahead(utcNow, report.FollowUpAt) ? utcNow - FastFor : report.FollowUpAt;
            // A report older than 30 days is not asked about.
            var age = utcNow - sentAt;
            if (report.Need == ReplyNeed.None || age >= ReportReplies.KeepFor)
                continue;
            var started = followUpAt is { } followUp && followUp > sentAt ? followUp : sentAt;
            var since = utcNow - started;
            if (since < FastFor && since > -FastFor)
                return Honour(new ReplyWait(FastWait, FastJitter), workerWait);
            // Looking into it with no send or No, still broken for a day: Coodsz is on it, so the slow check is enough.
            if (report.Need == ReplyNeed.Open || (report.Need == ReplyNeed.Looking && since < QuietAfter))
                plan = new ReplyWait(OpenWait, OpenJitter);
            else
                plan ??= new ReplyWait(ReadWait, ReadJitter);
        }

        return plan == null ? null : Honour(plan, workerWait);
    }

    /// <summary>
    /// The wait after a check: null when the schedule says no check, the check's own wait when it failed or was refused
    /// (10 minutes, or the worker's wait on a 429), else the schedule with the wait the worker asked for.
    /// </summary>
    public static ReplyWait? After(ReplyCheck check, DateTime utcNow, IEnumerable<ReplyScheduleReport> reports, bool minimized = false)
    {
        var plan = Next(utcNow, reports, check.Answered ? check.Asked : null, minimized);
        if (plan == null)
            return null;
        return check.Called && !check.Answered ? new ReplyWait(check.Next, SlowJitter) : plan;
    }

    /// <summary>
    /// After with the reports read from the store. A keys file that can't be read for a moment (ReportFileBusyException)
    /// never stops the checks, since its unlocking raises no event that would start them again: the reports the store
    /// could read plan as usual (ReportKeysBusyException carries them), and where they give no check, BusyWait does.
    /// Minimized stays no check. Another unreadable file keeps the check's own wait.
    /// </summary>
    public static ReplyWait? AfterCheck(ReplyCheck check, DateTime utcNow, Func<IReadOnlyList<ReplyScheduleReport>> reports, bool minimized = false)
    {
        try
        {
            return After(check, utcNow, reports(), minimized);
        }
        catch (ReportKeysBusyException ex)
        {
            return After(check, utcNow, ex.Reports, minimized) ?? (minimized ? null : BusyWait(check));
        }
        catch (ReportFileBusyException)
        {
            return minimized ? null : BusyWait(check);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ReplyWait(check.Next, SlowJitter);
        }
    }

    /// <summary>
    /// Whether a check that is due goes out (ReplyChecker's tick): no when minimized or no report needs one. A keys file
    /// that is busy for a moment (ReportKeysBusyException, ReportFileBusyException) never stops it while the window is
    /// open, since that file may hold the open report; minimized it stops like any other. Another unreadable file does
    /// not stop it either.
    /// </summary>
    public static bool ShouldCheck(DateTime utcNow, Func<IReadOnlyList<ReplyScheduleReport>> reports, bool minimized)
    {
        try
        {
            return Next(utcNow, reports(), null, minimized) != null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return !minimized;
        }
    }

    /// <summary>
    /// Replan with the reports read from the store, for ReplyChecker: the next check and the newest send or No, still
    /// broken (a newer one than lastStart is a send, which brings a check soon). With a keys file busy for a moment the
    /// reports that could be read plan as usual (ReportKeysBusyException carries them), and where they give no check
    /// while the window is open, the planned check or BusyRetry stays. With nothing readable the planned check stays,
    /// or BusyRetry when none is planned. Never before notBeforeUtc. random is 0 to 1.
    /// </summary>
    public static (DateTime? Next, DateTime? NewestStart) ReplanFrom(DateTime utcNow, DateTime? plannedUtc, DateTime notBeforeUtc, DateTime? lastStart,
        Func<IReadOnlyList<ReplyScheduleReport>> reports, bool minimized, bool resume, double random)
    {
        if (ClockWentBack(utcNow, notBeforeUtc))
            notBeforeUtc = DateTime.MinValue;
        if (ClockWentBack(utcNow, plannedUtc))
            plannedUtc = null;
        DateTime Retry()
        {
            var retry = utcNow + BusyRetry.Wait + BusyRetry.Jitter * Math.Clamp(random, 0, 1);
            return retry > notBeforeUtc ? retry : notBeforeUtc;
        }

        IReadOnlyList<ReplyScheduleReport> list;
        var busy = false;
        try
        {
            list = reports();
        }
        catch (ReportKeysBusyException ex)
        {
            list = ex.Reports;
            busy = true;
        }
        catch (ReportFileBusyException)
        {
            return (plannedUtc ?? (minimized ? null : Retry()), lastStart);
        }

        var newest = NewestStart(list);
        resume |= newest > (lastStart ?? DateTime.MinValue);
        var next = Replan(utcNow, plannedUtc, notBeforeUtc, list, minimized, resume, random);
        if (next == null && busy && !minimized)
            next = plannedUtc ?? Retry();
        return (next, newest ?? lastStart);
    }

    /// <summary>
    /// The retry while the keys file is busy: BusyRetry, or the wait the worker asked for when it is longer (its next
    /// on an answer, or the check's own wait after a refused or failed call), so a busy file never asks sooner.
    /// </summary>
    public static ReplyWait BusyWait(ReplyCheck check)
    {
        var asked = !check.Called ? TimeSpan.Zero : check.Answered ? check.Asked ?? TimeSpan.Zero : check.Next;
        return asked > BusyRetry.Wait ? new ReplyWait(asked > ReportReplies.LongestWait ? ReportReplies.LongestWait : asked, SlowJitter) : BusyRetry;
    }

    /// <summary>
    /// How much a report needs the check from its badge: an unread answer is Open, Looking into it (or no badge yet) is
    /// Looking, Answered and read is Read, Solved and Fixed are None.
    /// </summary>
    public static ReplyNeed NeedOf(ReportStatusKind? status, bool unread) => status switch
    {
        ReportStatusKind.Solved or ReportStatusKind.Fixed => ReplyNeed.None,
        _ when unread => ReplyNeed.Open,
        ReportStatusKind.Answered => ReplyNeed.Read,
        _ => ReplyNeed.Looking
    };

    /// <summary>The newest send or No, still broken among the reports: a newer one than last time means a report was just sent.</summary>
    public static DateTime? NewestStart(IEnumerable<ReplyScheduleReport> reports) =>
        reports.Select(report => report.FollowUpAt is { } followUp && followUp > report.SentAt ? followUp : report.SentAt)
            .Select(at => (DateTime?)at).Max();

    /// <summary>
    /// The next check after something changed (the window was minimized or restored, a report was sent, an answer read):
    /// null to stop checking. resume (a restore or a send), or no check planned, brings one within ResumeWait and its
    /// jitter, never before notBeforeUtc (the worker's last wait). Otherwise, and when the planned check is sooner
    /// anyway, the planned one stays, so its jitter does too. random is 0 to 1.
    /// </summary>
    public static DateTime? Replan(DateTime utcNow, DateTime? plannedUtc, DateTime notBeforeUtc, IEnumerable<ReplyScheduleReport> reports,
        bool minimized, bool resume, double random)
    {
        if (ClockWentBack(utcNow, notBeforeUtc))
            notBeforeUtc = DateTime.MinValue;
        if (ClockWentBack(utcNow, plannedUtc))
            plannedUtc = null;
        if (Next(utcNow, reports, null, minimized) == null)
            return null;
        if (!resume && plannedUtc != null)
            return plannedUtc;
        var at = utcNow + ResumeWait + ResumeJitter * Math.Clamp(random, 0, 1);
        if (at < notBeforeUtc)
            at = notBeforeUtc;
        return plannedUtc is { } planned && planned <= at ? planned : at;
    }

    /// <summary>
    /// True for a planned time more than the longest wait (6 hours) ahead: nothing plans that far, so the PC clock
    /// went back, and the time no longer counts (a check would wait until the old clock time came round again).
    /// </summary>
    public static bool ClockWentBack(DateTime utcNow, DateTime? at) => at is { } planned && planned > utcNow + ReportReplies.LongestWait;

    // A report or send time further ahead than the 30 s checks reach: the PC clock went back since.
    private static bool Ahead(DateTime utcNow, DateTime? at) => at is { } time && time > utcNow + FastFor;

    private static ReplyWait Honour(ReplyWait plan, TimeSpan? workerWait) =>
        workerWait is { } asked && asked > plan.Wait
            ? new ReplyWait(asked > ReportReplies.LongestWait ? ReportReplies.LongestWait : asked, SlowJitter)
            : plan;
}

/// <summary>One row of My reports: a sent report with its answer, a sent report with no answer yet, or a known fix.</summary>
public sealed record MyReportRow(string Code, DateTime? SentAt, ReportAnswer? Answer);

/// <summary>
/// The top banner while an answer is unread (ReportReplies.Banner): the newest unread answer's report code (empty
/// for a known fix shown before it was sent), whether that answer was automatic, and how many other reports have one.
/// </summary>
public sealed record ReplyBanner(string Code, bool Auto, int More);

/// <summary>How the My reports button looks (ReportReplies.Look): shown, glowing, and the red count or null.</summary>
public sealed record MyReportsLook(bool Visible, bool Glow, string? Badge);

/// <summary>The words the reply window and My reports show. They go through Translator.T like every other text.</summary>
public static class ReportReplyText
{
    public const string Title = "Coodsz replied to your report";
    public const string AutoTitle = "Automatic answer";
    public const string SendLogsAgain = "Send my logs again";
    public const string SendLogsAgainTip = "Opens Send my logs, so you can send fresh logs for this problem.";
    public const string StillNotWorking = "Still not working? Ask Coodsz on Discord";
    public const string StillNotWorkingTip = "Opens the Bannerlord Coop Discord and copies a short message with your report code, ready to paste.";
    public const string Copied = "A message with your code is copied. Paste it in Discord.";
    public const string CopyFailed = "Could not copy the message. Post your report code in Discord instead.";
    public const string MyReports = "My reports";
    public const string MyReportsTip = "Your sent reports and every answer to them.";
    public const string MarkAllRead = "Mark all read";
    public const string MarkAllReadTip = "Shows every answer in the list as read, so none stays bold.";
    public const string Empty = "Nothing here yet. Reports you send and the answers to them show up here.";
    public const string NoMatch = "Nothing matches your search.";
    public const string NoAnswerYet = "No answer yet";
    public const string KnownFix = "Known fix";
    public const string CoodszReplied = "Coodsz replied";
    public const string Replaced = "Replaced by a newer answer";
    public const string NotSent = "Not sent";
    public const string PickOne = "Click a report to read the answer.";
    public const string AnswersHere = "If Coodsz answers, you see the answer right here in this tool.";
    public const string LogLine = "Coodsz replied to report {0}.";
    public const string OkTip = "Closes this window. The answer stays in My reports.";

    // The report card (v1.3.3): the header with its status badge, the reply and Did this fix it?
    public const string YourReport = "Your report {0}";
    public const string StatusLooking = "Looking into it";
    public const string StatusAnswered = "Answered";
    public const string StatusSolved = "Solved";
    public const string StatusFixedIn = "Fixed in v{0}";
    public const string SentOn = "Sent {0}";
    public const string RepliedAt = "replied {0}";
    public const string CrashTitle = "Crash report";
    public const string ServerCrashTitle = "Server crash report";
    public const string LogsTitle = "Logs sent";
    public const string DidThisFixIt = "Did this fix it?";
    public const string YesFixed = "Yes, it's fixed";
    public const string YesFixedTip = "Tells Coodsz the answer fixed it.";
    public const string NoBroken = "No, still broken";
    public const string NoBrokenTip = "Opens a short form to tell Coodsz what happens now and send your new logs.";
    public const string FormTitle = "Sorry it's still broken. Tell Coodsz what happened";
    public const string TriedSteps = "Did you try the steps?";
    public const string StepsAll = "Yes, all of them";
    public const string StepsSome = "Some of them";
    public const string StepsNone = "No, I couldn't";
    public const string WhatNow = "What happens now?";
    public const string WhatNowHint = "Write what you did and what you see, like an error or where it stops.";
    public const string AddNewLogs = "Add my new logs from the last run (recommended)";
    public const string AddNewLogsTip = "Packs this tool's logs the same way Send my logs does, cleaned first, and adds them to this report.";
    public const string AddScreenshot = "Add a screenshot";
    public const string RemoveScreenshot = "Remove the screenshot";
    public const string SendToCoodsz = "Send to Coodsz";
    public const string Cancel = "Cancel";
    public const string PickSteps = "Pick if you tried the steps first.";
    public const string WriteFirst = "Write what happens now first.";
    public const string Sending = "Sending to Coodsz.";
    public const string FollowUpSent = "Your answer and new logs were added to this report. You'll see Coodsz's reply here.";
    public const string FollowUpSentNoLogs = "Your answer was added to this report. You'll see Coodsz's reply here.";
    public const string FollowUpTooOld = "This report is older than 30 days, so it can't take an answer now. Use Send my logs instead.";
    public const string FollowUpFailed = "Could not send your answer. Check the internet connection and try again.";
    public const string FollowUpRefused = "Your answer could not be added to the report right now. Try again in a few minutes.";
    public const string FollowUpLocalFailed = "Could not get your answer ready on this PC. Try again, or untick the logs or remove the screenshot.";
    public const string TooManyTries = "Too many tries right now. Try again in a few minutes.";
    public const string You = "You, {0}";
    public const string TriedAll = "tried all steps";
    public const string TriedSome = "tried some steps";
    public const string TriedNone = "couldn't try the steps";
    public const string NewLogsAdded = "new logs added";
    public const string ScreenshotAdded = "screenshot added";
    public const string SolvedTitle = "Great, glad it works now";
    public const string SolvedText = "This report is marked solved. If it comes back, press \"It broke again\" and your old answers stay with it.";
    public const string BrokeAgain = "It broke again";
    public const string BrokeAgainTip = "Opens the same form, so you can tell Coodsz it came back.";
    public const string SolvedFailed = "Could not reach the report server. Try again later.";
    public const string NewAnswer = "1 report with a new answer";
    public const string NewAnswers = "{0} reports with a new answer";
    public const string FollowUpErrorLog = "Could not send the answer to report {0}. {1}: {2}";
    public const string PictureFiles = "PNG or JPG pictures";
    public const string AllFiles = "All files";
    public const string FollowUpLog = "Sent your answer to report {0}.";
    public const string SolvedLog = "Marked report {0} as solved.";

    // The banner at the top while an answer is unread (ReportReplies.BannerText) and its Open button.
    public const string BannerReply = "Coodsz answered your report {0}";
    public const string BannerReplyMore = "Coodsz answered your report {0} and {1} more";
    public const string BannerAuto = "Automatic answer to your report {0}";
    public const string BannerAutoMore = "Automatic answer to your report {0} and {1} more";
    public const string BannerAutoNoCode = "Automatic answer to your crash";
    public const string BannerAutoNoCodeMore = "Automatic answer to your crash and {0} more";
    public const string BannerOpen = "Open";
    public const string BannerOpenTip = "Opens this report in My reports. This banner goes once every answer is read.";

    /// <summary>Every text above, for the translation test.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Title, AutoTitle, SendLogsAgain, SendLogsAgainTip, StillNotWorking, StillNotWorkingTip, Copied, CopyFailed,
        MyReports, MyReportsTip, MarkAllRead, MarkAllReadTip, Empty, NoMatch, NoAnswerYet, KnownFix, CoodszReplied, Replaced, NotSent, PickOne, AnswersHere, LogLine, OkTip,
        YourReport, StatusLooking, StatusAnswered, StatusSolved, StatusFixedIn, SentOn, RepliedAt, CrashTitle, ServerCrashTitle, LogsTitle,
        DidThisFixIt, YesFixed, YesFixedTip, NoBroken, NoBrokenTip, FormTitle, TriedSteps, StepsAll, StepsSome, StepsNone, WhatNow, WhatNowHint,
        AddNewLogs, AddNewLogsTip, AddScreenshot, RemoveScreenshot, SendToCoodsz, Cancel, PickSteps, WriteFirst, Sending, FollowUpSent, FollowUpSentNoLogs,
        FollowUpTooOld, FollowUpFailed, FollowUpRefused, FollowUpLocalFailed, TooManyTries, You, TriedAll, TriedSome, TriedNone, NewLogsAdded, ScreenshotAdded, SolvedTitle, SolvedText,
        BrokeAgain, BrokeAgainTip, SolvedFailed, NewAnswer, NewAnswers, FollowUpLog, SolvedLog, FollowUpErrorLog, PictureFiles, AllFiles,
        BannerReply, BannerReplyMore, BannerAuto, BannerAutoMore, BannerAutoNoCode, BannerAutoNoCodeMore, BannerOpen, BannerOpenTip
    ];

    /// <summary>The badge text of a status (Fixed in v{0} still needs its version put in).</summary>
    public static string StatusLabel(ReportStatusKind kind) => kind switch
    {
        ReportStatusKind.Answered => StatusAnswered,
        ReportStatusKind.Solved => StatusSolved,
        ReportStatusKind.Fixed => StatusFixedIn,
        _ => StatusLooking
    };

    /// <summary>The title when the report kept none: Crash report, Server crash report or Logs sent.</summary>
    public static string ReasonTitle(string? reason) => reason switch
    {
        CrashReason.Crash => CrashTitle,
        CrashReason.ServerCrash => ServerCrashTitle,
        _ => LogsTitle
    };

    /// <summary>The badge's tip in English: 1 report with a new answer, or {0} reports with a new answer.</summary>
    public static string NewAnswersLabel(int count) => count == 1 ? NewAnswer : NewAnswers;

    /// <summary>
    /// What goes under the reply on the card, the first that fits: the No form while it is open (also while
    /// it sends and after a failed send, so the words stay and Send works again), the solved card, Did this
    /// fix it? when there is an answer and no newer No, or nothing.
    /// </summary>
    public static CardBottom BottomOf(bool formOpen, bool canAct, bool solved, bool waiting, bool hasAnswer) =>
        formOpen && canAct ? CardBottom.Form
        : solved ? CardBottom.Solved
        : !waiting && hasAnswer && canAct ? CardBottom.Question
        : CardBottom.None;

    public static string StepsLabel(string? steps) => steps switch
    {
        FollowUpSteps.Some => TriedSome,
        FollowUpSteps.None => TriedNone,
        _ => TriedAll
    };

    /// <summary>The label of an answer's source.</summary>
    public static string SourceLabel(string? source) => source switch
    {
        AnswerSource.Known => KnownFix,
        AnswerSource.Auto => AutoTitle,
        _ => CoodszReplied
    };
}

/// <summary>
/// In tool replies (docs/design/in-tool-replies.md). A sent report gets a random reply key that goes
/// once with the summary (summary.replyKey) and stays on this PC next to the code. Coodsz answers on
/// GitHub with /reply, and the tool asks POST /replies for the answers to its own codes, only while it
/// has keys younger than 30 days. The key never goes into the zip, report.json, a log or a URL.
/// </summary>
public static class ReportReplies
{
    public const string Endpoint = "https://coodsz-mod-reports.coodsz.workers.dev/replies";
    public const string RequestKind = "CoodszReplyCheck";
    public const int MaxReports = 10;
    public const int MaxText = 1500;

    /// <summary>The most of a /replies answer the tool reads. Ten reports with one answer each stay far below it.</summary>
    public const int MaxResponseBytes = 256 * 1024;
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(30);
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>The wait between checks when the worker does not say, and the shortest wait it may ask for.</summary>
    public static readonly TimeSpan DefaultWait = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan LongestWait = TimeSpan.FromHours(6);

    /// <summary>The official Bannerlord Coop Discord, a vanity invite that does not expire.</summary>
    public const string CoopDiscordInvite = "https://discord.gg/bannerlordcoop";

    /// <summary>The same server by id, for when the invite does not open. Works only for players already in it.</summary>
    public const string CoopDiscordChannel = "https://discord.com/channels/709516043332354119";

    private static readonly Regex KeyPattern = new("^[A-Za-z0-9_-]{43}$", RegexOptions.CultureInvariant);
    private static readonly Regex IdPattern = new("^[A-Za-z0-9_-]{1,40}$", RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // ----- The key -----

    /// <summary>32 random bytes, base64url without padding: 43 characters.</summary>
    public static string NewKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool IsKey(string? key) => KeyPattern.IsMatch(key ?? "");

    // ----- The request and the answer -----

    /// <summary>
    /// The body of POST /replies: {"kind":"CoodszReplyCheck","schema":1,"fast":true,"reports":[{"code","key"}]}, at most
    /// 10 reports. "fast" tells a worker with reply push that this tool follows ReplySchedule, so a short next is fine;
    /// an older worker ignores it.
    /// </summary>
    public static string RequestJson(IEnumerable<(string Code, string Key)> reports) => JsonSerializer.Serialize(new
    {
        kind = RequestKind,
        schema = 1,
        fast = true,
        reports = reports.Where(report => CrashReports.IsCode(report.Code) && IsKey(report.Key)).Take(MaxReports)
            .Select(report => new { code = report.Code, key = report.Key }).ToList()
    });

    /// <summary>
    /// The answers in the worker's reply, cleaned, only for the codes that were asked, and the wait it asks
    /// for. Junk gives no answers. Never throws.
    /// </summary>
    public static (IReadOnlyList<ReplyItem> Items, int? Next) ParseResponse(string? json, IReadOnlyCollection<string> askedCodes)
    {
        var items = new List<ReplyItem>();
        int? next = null;
        try
        {
            using var doc = JsonDocument.Parse(json ?? "");
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return (items, null);
            if (root.TryGetProperty("next", out var nextValue) && nextValue.ValueKind == JsonValueKind.Number && nextValue.TryGetInt32(out var seconds))
                next = seconds;
            if (!root.TryGetProperty("replies", out var replies) || replies.ValueKind != JsonValueKind.Array)
                return (items, next);
            foreach (var reply in replies.EnumerateArray().Take(MaxReports))
            {
                if (reply.ValueKind != JsonValueKind.Object || Text(reply, "code") is not { } code || !askedCodes.Contains(code)
                    || !reply.TryGetProperty("items", out var list) || list.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var item in list.EnumerateArray().Take(20))
                {
                    if (item.ValueKind != JsonValueKind.Object)
                        continue;
                    var id = item.TryGetProperty("id", out var idValue) && idValue.ValueKind == JsonValueKind.Number ? idValue.GetRawText() : Text(item, "id");
                    var at = Text(item, "at");
                    if (id == null || !IdPattern.IsMatch(id) || at == null || at.Length > 40
                        || !DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _))
                        continue;
                    var text = Scrub(Text(item, "text"), code);
                    if (text.Length == 0)
                        continue;
                    var logs = item.TryGetProperty("buttons", out var buttons) && buttons.ValueKind == JsonValueKind.Array
                        && buttons.EnumerateArray().Any(button => button.ValueKind == JsonValueKind.String && button.GetString() == "send-logs");
                    // The automatic answers of phase 2: "auto": true, or "source": "auto".
                    var auto = (item.TryGetProperty("auto", out var autoValue) && autoValue.ValueKind == JsonValueKind.True)
                        || Text(item, "source") == AnswerSource.Auto;
                    items.Add(new ReplyItem(code, id, at, text, logs, auto));
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            items.Clear();
        }

        return (items, next);
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>
    /// The status the worker gave each asked code: "status" looking, answered, solved or fixed, and "fixedIn"
    /// a version for fixed. An older worker sends none, and junk gives none. Never throws.
    /// </summary>
    public static IReadOnlyDictionary<string, (string Status, string? FixedIn)> ParseStatuses(string? json, IReadOnlyCollection<string> askedCodes)
    {
        var statuses = new Dictionary<string, (string, string?)>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(json ?? "");
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("replies", out var replies) || replies.ValueKind != JsonValueKind.Array)
                return statuses;
            foreach (var reply in replies.EnumerateArray().Take(MaxReports))
            {
                if (reply.ValueKind != JsonValueKind.Object || Text(reply, "code") is not { } code || !askedCodes.Contains(code)
                    || Text(reply, "status") is not { } status || !ReportStatus.IsKnown(status))
                    continue;
                var fixedIn = Text(reply, "fixedIn");
                statuses[code] = (status, status == ReportStatus.Fixed && ReportStatus.IsVersion(fixedIn) ? fixedIn : null);
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            statuses.Clear();
        }

        return statuses;
    }

    /// <summary>
    /// The asked codes whose entry says "full": true: the worker sent every answer it serves for that report, so an answer
    /// this PC holds that is not in it was deleted on GitHub. Only the v1.3.5 worker sends it, and only when it really
    /// knows (not after a failed GitHub read). An older worker sends none, and junk gives none. Never throws.
    /// </summary>
    public static IReadOnlySet<string> ParseFull(string? json, IReadOnlyCollection<string> askedCodes)
    {
        var full = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(json ?? "");
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("replies", out var replies) || replies.ValueKind != JsonValueKind.Array)
                return full;
            foreach (var reply in replies.EnumerateArray().Take(MaxReports))
            {
                if (reply.ValueKind == JsonValueKind.Object && Text(reply, "code") is { } code && askedCodes.Contains(code)
                    && reply.TryGetProperty("full", out var value) && value.ValueKind == JsonValueKind.True
                    && reply.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                    full.Add(code);
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            full.Clear();
        }

        return full;
    }

    /// <summary>
    /// The short title kept with a report for My reports: one line, cleaned like a reply, at most 80
    /// characters. Empty when there is nothing to show (the reason's own title is used then).
    /// </summary>
    public static string ShortTitle(string? text)
    {
        var line = (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (line.Length == 0)
            return "";
        line = Scrub(line.Length > 400 ? line[..400] : line);
        return line.Length > 80 ? line[..77].TrimEnd() + "..." : line;
    }

    /// <summary>
    /// When the answer was written: its own GitHub date (the newest edit), else when this PC kept it (a
    /// known fix). Not the saved time: the other exe can save an old reply after the player's newer No.
    /// </summary>
    public static DateTime AnswerTime(ReportAnswer answer) =>
        DateTimeOffset.TryParse(answer.At, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at.UtcDateTime : answer.SavedAt;

    /// <summary>
    /// True when answer a is newer than b the way the worker picks the current answer: the higher comment id, and for
    /// the same comment the newer edit (At). An id that is no number (a known fix) falls back to the answer's time.
    /// </summary>
    public static bool Newer(ReportAnswer a, ReportAnswer b)
    {
        if (long.TryParse(a.Id, NumberStyles.None, CultureInfo.InvariantCulture, out var x) && long.TryParse(b.Id, NumberStyles.None, CultureInfo.InvariantCulture, out var y))
            return x != y ? x > y : AnswerTime(a) > AnswerTime(b);
        return AnswerTime(a) > AnswerTime(b);
    }

    /// <summary>The time a row of My reports shows: when the answer was written (AnswerTime), or when the report was sent.</summary>
    public static DateTime? RowTime(MyReportRow row) => row.Answer is { } answer ? AnswerTime(answer) : row.SentAt;

    /// <summary>The reports with an unread answer: two answers to one report count once, each known fix with no code once.</summary>
    public static int UnreadReports(IEnumerable<ReportAnswer> answers) =>
        answers.Where(answer => !answer.Read).Select(answer => answer.Code.Length > 0 ? answer.Code : "known|" + answer.Id).Distinct(StringComparer.Ordinal).Count();

    /// <summary>
    /// The banner at the top of the tool while an answer is unread: the newest unread answer's report, and how
    /// many other reports have one. Null when every answer is read. Two answers to one report count once.
    /// </summary>
    public static ReplyBanner? Banner(IEnumerable<ReportAnswer> answers)
    {
        var unread = answers.Where(answer => !answer.Read).OrderByDescending(AnswerTime).ToList();
        if (unread.Count == 0)
            return null;
        var newest = unread[0];
        return new ReplyBanner(newest.Code, newest.Source != AnswerSource.Reply, UnreadReports(unread) - 1);
    }

    /// <summary>
    /// The banner's line in English, like Coodsz answered your report CR-XXXXX and 2 more. The window's text hook
    /// translates it (the longer and {1} more template is matched first), so a language switch changes it too.
    /// </summary>
    public static string BannerText(ReplyBanner banner)
    {
        var template = (banner.Auto, banner.Code.Length > 0, banner.More > 0) switch
        {
            (false, _, false) => ReportReplyText.BannerReply,
            (false, _, true) => ReportReplyText.BannerReplyMore,
            (true, true, false) => ReportReplyText.BannerAuto,
            (true, true, true) => ReportReplyText.BannerAutoMore,
            (true, false, false) => ReportReplyText.BannerAutoNoCode,
            (true, false, true) => ReportReplyText.BannerAutoNoCodeMore
        };
        // A known fix shown before the report was sent has no code, so the count is {0} there.
        return banner.Auto && banner.Code.Length == 0
            ? string.Format(CultureInfo.InvariantCulture, template, banner.More)
            : string.Format(CultureInfo.InvariantCulture, template, banner.Code, banner.More);
    }

    /// <summary>
    /// How the My reports button looks: shown once there is a sent report or an answer, glowing and with the
    /// red count while a report has an unread answer. The count stops at 99+.
    /// </summary>
    public static MyReportsLook Look(int rows, int unread) =>
        new(rows > 0, unread > 0, unread <= 0 ? null : unread > 99 ? "99+" : unread.ToString(CultureInfo.InvariantCulture));

    private static readonly Regex StepLine = new(@"^(\d{1,2})[.)]\s+(.+)$", RegexOptions.CultureInvariant);

    /// <summary>
    /// The reply as the card shows it: paragraphs and numbered steps as written (a line like "2. Pick
    /// Vanilla" or "2) Pick Vanilla" is step 2), with a line that names Discord moved to the end, so the
    /// steps stay together and the Ask on Discord line comes last.
    /// </summary>
    public static IReadOnlyList<ReplyBlock> ReplyBlocks(string? text)
    {
        var blocks = new List<ReplyBlock>();
        var last = new List<ReplyBlock>();
        foreach (var raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;
            var target = line.Contains("discord", StringComparison.OrdinalIgnoreCase) ? last : blocks;
            var step = StepLine.Match(line);
            target.Add(step.Success
                ? new ReplyBlock(int.Parse(step.Groups[1].Value, CultureInfo.InvariantCulture), step.Groups[2].Value.Trim())
                : new ReplyBlock(null, line));
        }

        return blocks.Concat(last).ToList();
    }

    // ----- Yes, it's fixed -----

    public const string SolvedEndpoint = Endpoint + "/solved";
    public const string SolvedKind = "CoodszReplySolved";

    /// <summary>The body of POST /replies/solved: the code and its reply key, nothing else.</summary>
    public static string SolvedJson(string code, string key) => JsonSerializer.Serialize(new { kind = SolvedKind, schema = 1, code, key });

    /// <summary>
    /// Tells the worker the answer fixed the report (Yes, it's fixed). The worker checks the key and closes or
    /// labels the issue (its SOLVED_ACTION). A 15 s timeout, and it never throws.
    /// </summary>
    public static async Task<SolvedResult> MarkSolvedAsync(HttpClient http, string code, string key, string? endpoint = SolvedEndpoint,
        CancellationToken cancellationToken = default)
    {
        if (!CrashReports.IsCode(code) || !IsKey(key) || !ModReports.HasEndpoint(endpoint))
            return SolvedResult.Unknown;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            using var content = new StringContent(SolvedJson(code, key), Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(TestWorkerUrl.Apply(endpoint!.Trim()), content, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return SolvedResult.Busy;
            if (!response.IsSuccessStatusCode)
                return SolvedResult.Failed;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("solved", out var solved) && solved.ValueKind == JsonValueKind.True
                ? SolvedResult.Done
                : SolvedResult.Unknown;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException or JsonException or InvalidOperationException)
        {
            return SolvedResult.Failed;
        }
    }

    /// <summary>The wait before the next check: the worker's next, at least 10 minutes and at most 6 hours.</summary>
    public static TimeSpan NextWait(int? seconds)
    {
        if (seconds is not { } value || value <= 0)
            return DefaultWait;
        var wait = TimeSpan.FromSeconds(value);
        return wait < DefaultWait ? DefaultWait : wait > LongestWait ? LongestWait : wait;
    }

    /// <summary>
    /// Asks the worker for the answers to this tool's reports and keeps the new ones (seen, and in My
    /// reports). No keys, no call. A 15 s timeout, and it never throws.
    /// </summary>
    public static async Task<ReplyCheck> CheckAsync(HttpClient http, ReportReplyStore store, string? endpoint = Endpoint, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ReplyKeyEntry> keys;
        try
        {
            // A key that waited in memory gets its next chance to be written.
            store.Prune();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        try
        {
            keys = store.NextBatch();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ReplyCheck([], DefaultWait, false);
        }

        if (keys.Count == 0 || !ModReports.HasEndpoint(endpoint))
            return new ReplyCheck([], DefaultWait, false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            using var content = new StringContent(RequestJson(keys.Select(entry => (entry.Code, entry.Key))), Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(TestWorkerUrl.Apply(endpoint!.Trim()), content, timeout.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            var (items, next) = ParseResponse(body, keys.Select(entry => entry.Code).ToHashSet(StringComparer.Ordinal));
            if (!response.IsSuccessStatusCode)
                return new ReplyCheck([], response.StatusCode == HttpStatusCode.TooManyRequests ? NextWait(next) : DefaultWait, true);
            var full = ParseFull(body, keys.Select(entry => entry.Code).ToHashSet(StringComparer.Ordinal));
            var fresh = store.TakeNew(items, full);
            try
            {
                store.KeepStatuses(ParseStatuses(body, keys.Select(entry => entry.Code).ToHashSet(StringComparer.Ordinal)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The badge waits for the next check.
            }

            try
            {
                // Coodsz deleted an answer on GitHub: with the worker's full list it stops showing as current.
                store.DropMissing(full, items);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // It is done at the next check.
            }

            try
            {
                store.Prune();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The new answers are kept; the old keys go on a later check.
            }

            var asked = next is > 0 ? TimeSpan.FromSeconds(Math.Min(next.Value, LongestWait.TotalSeconds)) : (TimeSpan?)null;
            return new ReplyCheck(fresh, NextWait(next), true, true, asked);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new ReplyCheck([], DefaultWait, true);
        }
    }

    public const string KeyWaitsText = "Report {0} waits to be saved for My reports, because another Coodsz tool is saving the same file. It is saved at the next check.";
    public const string KeyLostText = "Report {0} could not be saved for My reports before this tool closed, so Coodsz's answer to it can't show in this tool.";
    public const string FollowUpWaitsText = "Your answer to report {0} reached Coodsz, but another program is using the My reports file, so it is saved at the next check.";
    public const string FollowUpLostText = "Your answer to report {0} reached Coodsz but could not be saved for My reports before this tool closed.";

    /// <summary>
    /// After a report is sent: keeps its reply key (key null when the worker can't answer it) and gives a
    /// known fix the screen showed its code. Runs off the window thread and never throws. True only
    /// when the key is in the file now; a key that waits in memory is logged and saved at the next check.
    /// </summary>
    public static async Task<bool> KeepSentAsync(ReportReplyStore store, string code, string? key, string? knownFixId, Action<string> log,
        TimeSpan? retryDelay = null, string? title = null, string? reason = null)
    {
        var kept = false;
        if (key != null)
        {
            kept = await store.RememberAsync(code, key, delay: retryDelay, title: title, reason: reason).ConfigureAwait(false);
            if (!kept && store.PendingCodes().Contains(code))
                log(string.Format(CultureInfo.InvariantCulture, KeyWaitsText, code));
        }

        if (knownFixId != null)
        {
            try
            {
                await Task.Run(() => store.AttachCode(knownFixId, code)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log("Could not keep the report for My reports. " + ex.Message);
            }
        }

        return kept;
    }

    // ----- The scrub -----

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);
    private static Regex Rx(string pattern) => new(pattern, RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex CodeFence = Rx(@"(?s)```.*?(?:```|$)");
    private static readonly Regex HtmlComment = Rx(@"(?s)<!--.*?(?:-->|$)");
    private static readonly Regex Html = Rx(@"<[^>\r\n]{0,500}>");
    private static readonly Regex MarkdownImage = Rx(@"!\[[^\]\r\n]{0,300}\]\([^)\r\n]{0,500}\)");
    private static readonly Regex MarkdownLink = Rx(@"\[([^\]\r\n]{0,300})\]\(([^)\r\n]{0,500})\)");
    private static readonly Regex Link = Rx(@"(?i)\b(?:https?://|www\.)[^\s<>()""']+");

    // The links a reply may keep, the same list as the worker's (reply-text.js ALLOWED_LINKS): the Coop
    // Discord, the Coodsz tool release pages and Steam Workshop pages. Shown as plain text, never clickable.
    private static readonly Regex[] AllowedLinks =
    [
        Rx(@"(?i)^https?://discord\.gg/[\w-]+/?$"),
        Rx(@"(?i)^https?://(?:www\.)?discord\.com/invite/[\w-]+/?$"),
        Rx(@"(?i)^https?://(?:www\.)?discord\.com/channels/709516043332354119(?:/\d+)*/?$"),
        Rx(@"(?i)^https?://github\.com/Coodsz/Coodsz-(?:Coop-and-Friends|TAOM|AD1259|BurningEmpires|RealmOfThrones)/releases(?:/[\w./-]*)?$"),
        Rx(@"(?i)^https?://steamcommunity\.com/sharedfiles/filedetails/\?id=\d+$")
    ];
    private static readonly Regex Mention = Rx(@"(?<![\w@.])@[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})(?:/[A-Za-z0-9_.-]+)?");
    private static readonly Regex IssueRef = Rx(@"(?<![\w&])(?:[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)?#\d+\b");
    private static readonly Regex AnyCode = Rx(@"\bCR-[A-Z0-9]{5}\b");
    private static readonly Regex Control = Rx(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F\u200B-\u200F\u202A-\u202E\u2066-\u2069]");
    private static readonly Regex Spaces = Rx(@"[ \t]{2,}");
    private static readonly Regex Blank = Rx(@"\n{3,}");

    /// <summary>
    /// The reply as the tool shows it: plain text, the same patterns the worker's cleanReply takes out
    /// taken out again (HTML, images, code, links not on the allow list, Steam ids, addresses, Windows user
    /// paths, passwords, tokens, emails, @mentions, issue numbers, other CR codes), at most 1500 characters.
    /// </summary>
    public static string Scrub(string? text, string? ownCode = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        try
        {
            var cleaned = text.Replace("\r\n", "\n").Replace('\r', '\n');
            cleaned = Control.Replace(cleaned, "");
            cleaned = CodeFence.Replace(cleaned, "");
            cleaned = HtmlComment.Replace(cleaned, "");
            cleaned = MarkdownImage.Replace(cleaned, "");
            cleaned = MarkdownLink.Replace(cleaned, match => IsAllowedLink(match.Groups[2].Value.Trim())
                ? match.Groups[1].Value + " (" + match.Groups[2].Value.Trim() + ")"
                : match.Groups[1].Value);
            cleaned = Html.Replace(cleaned, "");
            cleaned = WebUtility.HtmlDecode(cleaned);
            cleaned = Html.Replace(cleaned, "");
            cleaned = Link.Replace(cleaned, match =>
            {
                // A full stop or comma after a link is the sentence's, not the link's.
                var bare = match.Value.TrimEnd('.', ',', ';', ':', '!', '?');
                return (IsAllowedLink(bare) ? bare : "") + match.Value[bare.Length..];
            });
            cleaned = LogZipBuilder.Clean(cleaned);
            cleaned = Mention.Replace(cleaned, "");
            cleaned = IssueRef.Replace(cleaned, "");
            cleaned = AnyCode.Replace(cleaned, match => match.Value == ownCode ? match.Value : "");
            cleaned = Spaces.Replace(cleaned, " ");
            cleaned = string.Join('\n', cleaned.Split('\n').Select(line => line.Trim()));
            cleaned = Blank.Replace(cleaned, "\n\n").Trim();
            return cleaned.Length > MaxText ? cleaned[..(MaxText - 3)].TrimEnd() + "..." : cleaned;
        }
        catch (RegexMatchTimeoutException)
        {
            return "";
        }
    }

    public static bool IsAllowedLink(string link) => AllowedLinks.Any(pattern => pattern.IsMatch(link));

    // ----- Still not working? -----

    private static readonly Regex ToolNamePattern = new("^[A-Za-z0-9 ]{1,60}$", RegexOptions.CultureInvariant);

    /// <summary>
    /// The ready message Still not working? copies: only the code, the tool name and its version, and
    /// the promise to send logs with Send my logs. It stays English, since it goes to Coodsz, and never
    /// asks for files.
    /// </summary>
    public static string DiscordMessage(string? code, string? toolName, string? toolVersion)
    {
        var name = ToolNamePattern.IsMatch(toolName ?? "") ? toolName! : DiscordUnknownTool;
        var version = ModReports.CleanVersion(toolVersion);
        var tool = version.Length > 0 ? name + " v" + version : name;
        return CrashReports.IsCode(code)
            ? string.Format(CultureInfo.InvariantCulture, DiscordWithCode, code, tool)
            : string.Format(CultureInfo.InvariantCulture, DiscordNoCode, tool);
    }

    // English on purpose: the message is for Coodsz, not the player's screen (ExtractUiText leaves it out).
    public const string DiscordWithCode = "Hi Coodsz, the fix for my report {0} didn't work. Tool: {1}. If you need my logs, I'll send them with Send my logs in the tool.";
    public const string DiscordNoCode = "Hi Coodsz, the fix the tool showed me didn't work. Tool: {0}. If you need my logs, I'll send them with Send my logs in the tool.";
    public const string DiscordUnknownTool = "Coodsz tool";

    // ----- My reports -----

    /// <summary>The rows that match the search: the code or the answer text, any case. An empty search keeps all.</summary>
    public static IReadOnlyList<MyReportRow> Search(IEnumerable<MyReportRow> rows, string? query)
    {
        var words = (query ?? "").Trim();
        if (words.Length == 0)
            return rows.ToList();
        return rows.Where(row => row.Code.Contains(words, StringComparison.OrdinalIgnoreCase)
            || (row.Answer?.Text.Contains(words, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
    }

    internal static T? Load<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>True when the file is there but can't be opened now (held by another process, or no access). A missing file is false.</summary>
    internal static bool CannotRead(string path)
    {
        try
        {
            if (!File.Exists(path))
                return false;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    // A file that is there but cannot be read is kept as .bad once before it is written over.
    internal static bool IsJunk(string path)
    {
        try
        {
            if (!File.Exists(path))
                return false;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.ValueKind != JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return true;
        }
    }

    internal static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // A temp name of its own, so two writers never share one.
        var temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}

/// <summary>Another Coodsz tool held the file longer than the short wait. The change is tried again later.</summary>
public class ReportFileBusyException(string fileName) : IOException(BusyText + " " + fileName)
{
    public const string BusyText = "Another Coodsz tool is saving the same file. It is tried again later.";
}

/// <summary>A keys file was busy while the reply schedule was read: Reports holds the reports the other files gave.</summary>
public sealed class ReportKeysBusyException(string fileName, IReadOnlyList<ReplyScheduleReport> reports) : ReportFileBusyException(fileName)
{
    public IReadOnlyList<ReplyScheduleReport> Reports { get; } = reports;
}

/// <summary>
/// The two files of one tool in its ToolData folder: report-replies-&lt;key&gt;.json (codes and keys,
/// dropped after 30 days) and report-answers-&lt;key&gt;.json (every answer shown, the newest 200).
/// Each exe writes only its own files. The other exe of the same product is read too, so an answer
/// to a report sent from the Server Tool also shows in the Client Launcher. Read is kept per exe.
/// Every write holds a named mutex for its file, so a second copy of the same exe never loses a change.
/// </summary>
public sealed class ReportReplyStore
{
    public const int MaxAnswers = 200;
    public const string KeysPrefix = "report-replies-";
    public const string AnswersPrefix = "report-answers-";

    /// <summary>How long a write waits for another process holding the same file. Short, so a click never hangs.</summary>
    public static TimeSpan FileWait { get; set; } = TimeSpan.FromSeconds(1);

    private static readonly object Gate = new();
    private readonly Func<DateTime> _utcNow;
    private readonly IReadOnlyList<string> _otherFolders;

    public ReportReplyStore(string folder, string toolKey, IEnumerable<string>? productFolders = null, Func<DateTime>? utcNow = null)
    {
        Folder = folder;
        ToolKey = toolKey;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _otherFolders = (productFolders ?? []).Append(folder).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public string Folder { get; }
    public string ToolKey { get; }
    public string KeysPath => Path.Combine(Folder, KeysPrefix + ToolKey + ".json");
    public string AnswersPath => Path.Combine(Folder, AnswersPrefix + ToolKey + ".json");

    /// <summary>The running tool's store, reading the product folder and its Server and Client folders. Null before ToolData.Start.</summary>
    public static ReportReplyStore? ForThisTool()
    {
        if (ToolData.Current is not { } current || ToolData.CurrentPlan is not { } plan)
            return null;
        var product = current.ProductFolder;
        return new ReportReplyStore(current.Folder, plan.Key, [product, Path.Combine(product, "Server"), Path.Combine(product, "Client")]);
    }

    /// <summary>The name of the mutex that guards one file, the same in every process for the same path.</summary>
    public static string MutexName(string path)
    {
        var full = Path.GetFullPath(path).ToUpperInvariant();
        return "Local\\Coodsz-report-file-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..32];
    }

    // One writer at a time per file, across processes: this process's lock, then the file's named mutex,
    // waited for at most FileWait. A wait that runs out throws ReportFileBusyException (an IOException).
    private static T Locked<T>(string path, Func<T> work)
    {
        lock (Gate)
        {
            using var mutex = new Mutex(false, MutexName(path));
            bool owned;
            try
            {
                owned = mutex.WaitOne(FileWait);
            }
            catch (AbandonedMutexException)
            {
                // The other process died while it held the file. The file is whole (written by a move), so go on.
                owned = true;
            }

            if (!owned)
                throw new ReportFileBusyException(Path.GetFileName(path));
            try
            {
                return work();
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }
    }

    private static void Locked(string path, Action work) => Locked(path, () =>
    {
        work();
        return true;
    });

    // ----- Keys -----

    /// <summary>Keeps the key of a report the worker said it can answer. The same code again keeps the newest key.</summary>
    public void Remember(string code, string key, string? title = null, string? reason = null)
    {
        if (!CrashReports.IsCode(code) || !ReportReplies.IsKey(key))
            return;
        Remember(new ReplyKeyEntry { Code = code, Key = key, SentAt = _utcNow(), Title = ReportReplies.ShortTitle(title), Reason = CleanReason(reason) });
    }

    private void Remember(ReplyKeyEntry entry) => Locked(KeysPath, () =>
    {
        var file = LoadOwnKeys();
        file.Reports.RemoveAll(other => other.Code == entry.Code);
        file.Reports.Add(entry);
        SaveKeys(file);
    });

    /// <summary>
    /// Remember off the window thread, tried a few times while another exe holds the file. When every try
    /// fails the key waits in memory: the check still asks with it, and the next write of the keys file
    /// (a check, a prune, another report) saves it. True only when the key is in the file now.
    /// </summary>
    public async Task<bool> RememberAsync(string code, string key, int tries = 3, TimeSpan? delay = null, string? title = null, string? reason = null)
    {
        if (!CrashReports.IsCode(code) || !ReportReplies.IsKey(key))
            return false;
        var entry = new ReplyKeyEntry { Code = code, Key = key, SentAt = _utcNow(), Title = ReportReplies.ShortTitle(title), Reason = CleanReason(reason) };
        for (var attempt = 1; attempt <= Math.Max(1, tries); attempt++)
        {
            try
            {
                await Task.Run(() => Remember(entry)).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt < tries)
                    await Task.Delay(delay ?? TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
            }
        }

        lock (Gate)
        {
            if (!Pending.TryGetValue(KeysPath, out var list))
                Pending[KeysPath] = list = new List<ReplyKeyEntry>();
            list.RemoveAll(other => other.Code == entry.Code);
            list.Add(entry);
        }

        return false;
    }

    private static string? CleanReason(string? reason) => reason is CrashReason.Crash or CrashReason.ServerCrash or CrashReason.Logs ? reason : null;

    // Keys that could not be written yet, per keys file, for this process only.
    private static readonly Dictionary<string, List<ReplyKeyEntry>> Pending = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The codes whose keys still wait to be written. Lost if the tool closes before a write works.</summary>
    public IReadOnlyList<string> PendingCodes()
    {
        lock (Gate)
            return Pending.TryGetValue(KeysPath, out var list) ? list.Select(entry => entry.Code).ToList() : [];
    }

    private IReadOnlyList<ReplyKeyEntry> PendingEntries()
    {
        lock (Gate)
            return Pending.TryGetValue(KeysPath, out var list) ? list.ToList() : [];
    }

    /// <summary>Every report this PC can ask about: this exe's and the other exe's, younger than 30 days, newest first.</summary>
    public IReadOnlyList<ReplyKeyEntry> KeysToCheck()
    {
        lock (Gate)
            return AllKeys().ToList();
    }

    public bool HasKeys => KeysToCheck().Count > 0;

    /// <summary>
    /// The reports for the next check, at most 10. With more than 10 the batches take turns (a cursor kept
    /// in this exe's keys file), so the 11th report is asked about on the next check, not never.
    /// </summary>
    public IReadOnlyList<ReplyKeyEntry> NextBatch()
    {
        var all = KeysToCheck();
        if (all.Count <= ReportReplies.MaxReports)
            return all;
        return Locked(KeysPath, () =>
        {
            var file = LoadOwnKeys();
            var start = file.Cursor >= 0 && file.Cursor < all.Count ? file.Cursor : 0;
            var batch = Enumerable.Range(0, ReportReplies.MaxReports).Select(i => all[(start + i) % all.Count]).ToList();
            file.Cursor = (start + ReportReplies.MaxReports) % all.Count;
            SaveKeys(file);
            return (IReadOnlyList<ReplyKeyEntry>)batch;
        });
    }

    private IEnumerable<ReplyKeyEntry> AllKeys()
    {
        // A waiting key counts as this exe's, so the check asks with it before it is written.
        var own = PendingEntries().Concat(LoadOwnKeys().Reports);
        var others = OtherFiles(KeysPrefix, KeysPath)
            .SelectMany(path => ReportReplies.Load<ReplyKeyFile>(path)?.Reports ?? []);
        return own.Concat(others)
            .Where(IsFresh)
            .GroupBy(entry => entry.Code, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderByDescending(entry => entry.SentAt)
            .ThenBy(entry => entry.Code, StringComparer.Ordinal);
    }

    private bool IsFresh(ReplyKeyEntry? entry) =>
        entry != null && CrashReports.IsCode(entry.Code) && ReportReplies.IsKey(entry.Key) && _utcNow() - entry.SentAt < ReportReplies.KeepFor
        && entry.SentAt <= _utcNow().AddDays(1);

    private ReplyKeyFile LoadOwnKeys()
    {
        var file = ReportReplies.Load<ReplyKeyFile>(KeysPath) ?? new ReplyKeyFile();
        file.Reports = (file.Reports ?? []).Where(IsFresh).ToList();
        foreach (var entry in file.Reports)
            entry.Seen = (entry.Seen ?? []).Where(seen => seen != null).ToList();
        return file;
    }

    // Always called while the file's mutex is held. Keys that waited in memory are written with it.
    private void SaveKeys(ReplyKeyFile file)
    {
        KeepJunk(KeysPath);
        var waiting = PendingEntries();
        foreach (var entry in waiting)
        {
            file.Reports.RemoveAll(other => other.Code == entry.Code);
            file.Reports.Add(entry);
        }

        file.Reports = file.Reports.Where(IsFresh).ToList();
        ReportReplies.Save(KeysPath, file);
        if (waiting.Count > 0)
            lock (Gate)
                if (Pending.TryGetValue(KeysPath, out var list))
                    list.RemoveAll(entry => waiting.Contains(entry));
    }

    /// <summary>
    /// Drops the reports older than 30 days from this exe's file and writes any key that waited in memory.
    /// The tool runs it on start, before and after each check, and when it closes.
    /// </summary>
    public void Prune()
    {
        // A No, still broken that waited in memory gets its next chance to be written.
        if (PendingFollowUpEntries().Count > 0)
        {
            try
            {
                Locked(AnswersPath, () => SaveAnswers(LoadOwnAnswers()));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // It waits for the next check; the keys below still get pruned.
            }
        }

        var waiting = PendingEntries().Count > 0;
        if (!waiting && !File.Exists(KeysPath))
            return;
        Locked(KeysPath, () =>
        {
            var file = LoadOwnKeys();
            var before = ReportReplies.Load<ReplyKeyFile>(KeysPath)?.Reports?.Count ?? -1;
            if (waiting || before != file.Reports.Count)
                SaveKeys(file);
        });
    }

    // ----- New answers -----

    /// <summary>
    /// The answers not shown yet: a new comment id, or a known one with a new date (edited). Each one is
    /// kept in My reports as unread first and only then marked seen, so an answer is never marked seen
    /// without being kept. An older answer to the same report is marked replaced.
    /// </summary>
    public IReadOnlyList<ReplyItem> TakeNew(IEnumerable<ReplyItem> items, IReadOnlySet<string>? fullCodes = null)
    {
        // Always the answers file first, then the keys file, so two writers never wait on each other.
        return Locked(AnswersPath, () => Locked(KeysPath, () =>
        {
            var fresh = new List<ReplyItem>();
            var keys = LoadOwnKeys();
            var answers = LoadOwnAnswers();
            // The other exe's answers count too when Restore asks whether a newer answer is shown.
            var others = Answers().Where(other => answers.Answers.All(own => own.Identity != other.Identity)).ToList();
            var restored = false;
            foreach (var item in items)
            {
                var entry = keys.Reports.FirstOrDefault(report => report.Code == item.Code);
                var seenHere = entry?.Seen.Any(seen => seen.Id == item.Id && seen.At == item.At) ?? false;
                var kept = answers.Answers.Any(answer => answer.Code == item.Code && answer.Id == item.Id && answer.At == item.At);
                // The worker serves only a report's current answer. One kept here as replaced (a newer one was
                // deleted, or a wrong full list set it aside) is current again: it shows again, and the others go back.
                // Only from a full list, or when nothing newer is kept: a response from a lagging KV copy, or the stored
                // record after a fresh read it could not store, can serve an older answer than one already shown.
                if (Restore(answers, others, item, fullCodes?.Contains(item.Code) == true))
                    restored = true;
                if (seenHere || kept || fresh.Any(other => other.Code == item.Code && other.Id == item.Id && other.At == item.At))
                    continue;
                fresh.Add(item);
                entry?.Seen.RemoveAll(seen => seen.Id == item.Id);
                entry?.Seen.Add(new ReplySeen { Id = item.Id, At = item.At });
                answers.Answers.RemoveAll(answer => answer.Code == item.Code && answer.Id == item.Id);
                foreach (var older in answers.Answers.Where(answer => answer.Code == item.Code && answer.Source != AnswerSource.Known))
                    older.Replaced = true;
                answers.Answers.Add(new ReportAnswer
                {
                    Code = item.Code,
                    Id = item.Id,
                    At = item.At,
                    SavedAt = _utcNow(),
                    Source = item.Auto ? AnswerSource.Auto : AnswerSource.Reply,
                    Text = ReportReplies.Scrub(item.Text, item.Code),
                    LogsButton = item.LogsButton
                });
            }

            if (fresh.Count > 0)
            {
                // The answer is kept before it is marked seen: if keeping it fails, the next check brings it again.
                SaveAnswers(answers);
                SaveKeys(keys);
            }
            else if (restored)
                SaveAnswers(answers);

            return (IReadOnlyList<ReplyItem>)fresh;
        }));
    }

    // A kept answer the worker serves as current while it is marked replaced: it shows again (unread again when
    // DropMissing marked it read), and every other answer to that report is the replaced one. Only when the response
    // is full for the report, or no answer kept for it in either exe that is not replaced is newer (Newer: by comment
    // id, as the worker picks the current answer). True when it changed.
    private static bool Restore(ReportAnswerFile answers, IReadOnlyList<ReportAnswer> others, ReplyItem item, bool full)
    {
        var current = answers.Answers.FirstOrDefault(answer => answer.Code == item.Code && answer.Id == item.Id && answer.At == item.At);
        if (current is not { Replaced: true })
            return false;
        if (!full && answers.Answers.Concat(others).Any(answer => answer != current && answer.Code == item.Code && answer.Source != AnswerSource.Known
                && !answer.Replaced && ReportReplies.Newer(answer, current)))
            return false;
        current.Replaced = false;
        if (current.SetAsideUnread)
            current.Read = false;
        current.SetAsideUnread = false;
        // The others are set aside like DropMissing does: replaced, and read (unread again if one is served again).
        foreach (var other in answers.Answers.Where(answer => answer != current && answer.Code == item.Code && answer.Source != AnswerSource.Known && !answer.Replaced))
        {
            other.Replaced = true;
            other.SetAsideUnread = !other.Read;
            other.Read = true;
        }

        return true;
    }

    /// <summary>
    /// The worker sent its full answer list for these codes (ParseFull): an answer this exe holds for one of them that
    /// the list no longer has (Coodsz deleted it on GitHub) is marked replaced and read, so it no longer shows as the
    /// current answer or glows. Known fixes are never touched, and nothing is removed. Writes only when something changed.
    /// </summary>
    public void DropMissing(IReadOnlySet<string> fullCodes, IEnumerable<ReplyItem> items)
    {
        if (fullCodes.Count == 0)
            return;
        var current = items.Select(item => item.Code + "|" + item.Id).ToHashSet(StringComparer.Ordinal);
        Locked(AnswersPath, () =>
        {
            var file = LoadOwnAnswers();
            var changed = false;
            foreach (var answer in file.Answers.Where(answer => fullCodes.Contains(answer.Code) && answer.Source != AnswerSource.Known
                         && !answer.Replaced && !current.Contains(answer.Code + "|" + answer.Id)))
            {
                answer.Replaced = true;
                // Kept so a wrong full list heals: when the worker serves it again it is unread again (Restore).
                answer.SetAsideUnread = !answer.Read;
                answer.Read = true;
                changed = true;
            }

            if (changed)
                SaveAnswers(file);
        });
    }

    // ----- My reports -----

    // A known fix is the same fix in both exes, whatever minute each one saved it, so it counts once.
    private static string SameAnswer(ReportAnswer answer) =>
        answer.Source == AnswerSource.Known ? "known|" + answer.Code + "|" + answer.Id : answer.Identity;

    private static bool IsKnownFixId(string? id) => id != null && id.StartsWith("known-", StringComparison.Ordinal) && id.Length > 6;

    /// <summary>Every answer: this exe's, then the other exe's it does not have. Only this exe's read flags count.</summary>
    public IReadOnlyList<ReportAnswer> Answers()
    {
        lock (Gate)
        {
            var own = LoadOwnAnswers().Answers;
            var known = own.Select(SameAnswer).ToHashSet(StringComparer.Ordinal);
            var others = OtherFiles(AnswersPrefix, AnswersPath)
                .SelectMany(path => ReportReplies.Load<ReportAnswerFile>(path)?.Answers ?? [])
                .Where(answer => answer != null && answer.Text is { Length: > 0 }
                    // A reply needs its code. A known fix may have none when it was shown and not sent.
                    && (CrashReports.IsCode(answer.Code)
                        || (answer.Source == AnswerSource.Known && string.IsNullOrEmpty(answer.Code) && IsKnownFixId(answer.Id)))
                    && known.Add(SameAnswer(answer)))
                .Select(answer =>
                {
                    answer.Code ??= "";
                    answer.At ??= "";
                    answer.Read = false;
                    answer.Text = ReportReplies.Scrub(answer.Text, answer.Code);
                    return answer;
                });
            return own.Concat(others).OrderByDescending(answer => answer.SavedAt).ToList();
        }
    }

    /// <summary>The rows of My reports, newest first: every answer, and every sent report with no answer yet.</summary>
    public IReadOnlyList<MyReportRow> Rows()
    {
        var answers = Answers();
        List<ReplyKeyEntry> keys;
        lock (Gate)
            keys = AllKeys().ToList();
        var sent = keys.ToDictionary(entry => entry.Code, entry => entry.SentAt, StringComparer.Ordinal);
        var rows = answers.Select(answer => new MyReportRow(answer.Code, sent.TryGetValue(answer.Code, out var at) ? at : null, answer)).ToList();
        rows.AddRange(keys.Where(entry => answers.All(answer => answer.Code != entry.Code)).Select(entry => new MyReportRow(entry.Code, entry.SentAt, null)));
        // Newest first by the time each row shows (when Coodsz replied, or when the report was sent).
        return rows.OrderByDescending(row => ReportReplies.RowTime(row) ?? DateTime.MinValue).ToList();
    }

    public int UnreadCount() => Answers().Count(answer => !answer.Read);

    /// <summary>The reports with an unread answer, for the badge on My reports. Two answers to one report count once.</summary>
    public int UnreadReports() => ReportReplies.UnreadReports(Answers());

    // ----- The status, Yes, it's fixed and No, still broken -----

    public const int MaxFollowUps = 200;
    public const int MaxFollowUpsPerReport = 20;

    /// <summary>The key entry of a report this PC sent (this exe's or the other's), or null when it is older than 30 days.</summary>
    /// <remarks>
    /// Throws ReportFileBusyException when the report is not found and a keys file is there but can't be read now (the
    /// other exe or an antivirus holds it), so a busy file is never taken for a report older than 30 days.
    /// </remarks>
    public ReplyKeyEntry? Entry(string code)
    {
        lock (Gate)
        {
            var entry = AllKeys().FirstOrDefault(found => found.Code == code);
            if (entry == null && OtherFiles(KeysPrefix, KeysPath).Prepend(KeysPath).FirstOrDefault(ReportReplies.CannotRead) is { } busy)
                throw new ReportFileBusyException(Path.GetFileName(busy));
            return entry;
        }
    }

    /// <summary>Keeps the status the worker gave each report in this exe's answers file. Writes only when something changed.</summary>
    public void KeepStatuses(IReadOnlyDictionary<string, (string Status, string? FixedIn)> statuses)
    {
        if (statuses.Count == 0)
            return;
        Locked(AnswersPath, () =>
        {
            var file = LoadOwnAnswers();
            var changed = false;
            foreach (var (code, value) in statuses)
            {
                if (!CrashReports.IsCode(code) || !ReportStatus.IsKnown(value.Status))
                    continue;
                var state = StateIn(file, code);
                var fixedIn = ReportStatus.IsVersion(value.FixedIn) ? value.FixedIn : null;
                if (state.Status == value.Status && state.FixedIn == fixedIn)
                    continue;
                state.Status = value.Status;
                state.FixedIn = fixedIn;
                state.StatusAt = _utcNow();
                changed = true;
            }

            if (changed)
                SaveAnswers(file);
        });
    }

    /// <summary>Yes, it's fixed went through: the report shows Solved from now on, until a newer answer or It broke again.</summary>
    public void MarkSolved(string code)
    {
        if (!CrashReports.IsCode(code))
            return;
        Locked(AnswersPath, () =>
        {
            var file = LoadOwnAnswers();
            StateIn(file, code).SolvedAt = _utcNow();
            SaveAnswers(file);
        });
    }

    /// <summary>Keeps the player's own No, still broken answer to show under the reply: the words cleaned again, at most 1500 characters.</summary>
    public void AddFollowUp(ReportFollowUp followUp)
    {
        if (!CrashReports.IsCode(followUp.Code))
            return;
        var kept = CleanFollowUp(followUp);
        Locked(AnswersPath, () =>
        {
            var file = LoadOwnAnswers();
            file.FollowUps.Add(kept);
            SaveAnswers(file);
        });
    }

    private ReportFollowUp CleanFollowUp(ReportFollowUp followUp) => new()
    {
        Code = followUp.Code,
        At = followUp.At == default ? _utcNow() : followUp.At,
        Steps = FollowUpSteps.IsKnown(followUp.Steps) ? followUp.Steps : FollowUpSteps.All,
        Text = ReportReplies.Scrub(followUp.Text, followUp.Code),
        Logs = followUp.Logs,
        Screenshot = followUp.Screenshot,
        NewCode = CrashReports.IsCode(followUp.NewCode) ? followUp.NewCode : null
    };

    // No, still broken answers the worker took that could not be saved yet, per answers file, for this process only.
    private static readonly Dictionary<string, List<ReportFollowUp>> PendingFollowUps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Keeps No, still broken after the worker took it (it is never sent again). When the answers file can't be written
    /// now (the other exe or an antivirus holds it), the answer waits in memory: it counts for the badge and the
    /// schedule at once, and the next write of the answers file saves it (each check tries). True when it is in the file.
    /// </summary>
    public bool KeepFollowUp(ReportFollowUp followUp)
    {
        if (!CrashReports.IsCode(followUp.Code))
            return false;
        var kept = CleanFollowUp(followUp);
        try
        {
            Locked(AnswersPath, () =>
            {
                var file = LoadOwnAnswers();
                file.FollowUps.Add(kept);
                SaveAnswers(file);
            });
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lock (Gate)
            {
                if (!PendingFollowUps.TryGetValue(AnswersPath, out var list))
                    PendingFollowUps[AnswersPath] = list = new List<ReportFollowUp>();
                list.Add(kept);
            }

            return false;
        }
    }

    /// <summary>The codes whose No, still broken still waits to be saved. Lost if the tool closes before a write works.</summary>
    public IReadOnlyList<string> PendingFollowUpCodes()
    {
        lock (Gate)
            return PendingFollowUps.TryGetValue(AnswersPath, out var list) ? list.Select(followUp => followUp.Code).ToList() : [];
    }

    private IReadOnlyList<ReportFollowUp> PendingFollowUpEntries()
    {
        lock (Gate)
            return PendingFollowUps.TryGetValue(AnswersPath, out var list) ? list.ToList() : [];
    }

    /// <summary>What this PC knows about a report: this exe's state, with the other exe's newer status and Yes added in.</summary>
    public ReportState? State(string code) => MergeState(code, AllStates());

    /// <summary>The player's own answers to a report, oldest first, from both exes.</summary>
    public IReadOnlyList<ReportFollowUp> FollowUps(string code) => FollowUpsOf(code, AllFollowUps());

    /// <summary>The badge of a report: its newest answer, the player's newest answer and Yes, it's fixed, with the worker's status.</summary>
    public ReportStatus StatusOf(string code)
    {
        var answerAt = Answers().Where(answer => answer.Code == code).Select(answer => (DateTime?)ReportReplies.AnswerTime(answer)).Max();
        var followUpAt = FollowUps(code).Select(followUp => (DateTime?)followUp.At).Max();
        return ReportStatus.Of(State(code), answerAt, followUpAt);
    }

    /// <summary>The badge of every report in My reports, each file read once.</summary>
    public IReadOnlyDictionary<string, ReportStatus> Statuses()
    {
        var answers = Answers();
        var states = AllStates();
        var followUps = AllFollowUps();
        List<string> codes;
        lock (Gate)
            codes = AllKeys().Select(entry => entry.Code).ToList();
        return codes.Concat(answers.Select(answer => answer.Code)).Where(CrashReports.IsCode).Distinct(StringComparer.Ordinal)
            .ToDictionary(code => code, code => ReportStatus.Of(MergeState(code, states),
                answers.Where(answer => answer.Code == code).Select(answer => (DateTime?)ReportReplies.AnswerTime(answer)).Max(),
                FollowUpsOf(code, followUps).Select(followUp => (DateTime?)followUp.At).Max()), StringComparer.Ordinal);
    }

    /// <summary>
    /// The reports the reply check asks about, for its schedule (ReplySchedule): when each was sent, the player's newest
    /// No, still broken and how much it needs the check (ReplyNeed). Empty when there are no keys, so no files are read
    /// for the badges then.
    /// </summary>
    public IReadOnlyList<ReplyScheduleReport> ScheduleReports()
    {
        List<ReplyKeyEntry> keys;
        lock (Gate)
            keys = AllKeys().ToList();
        // A keys file that can't be read now (the other exe or an antivirus holds it) leaves the list incomplete: it
        // may hold the only open report. The caller gets what could be read and keeps at least a retry instead of
        // stopping the checks (ReplySchedule.AfterCheck).
        var busy = OtherFiles(KeysPrefix, KeysPath).Prepend(KeysPath).FirstOrDefault(ReportReplies.CannotRead);
        IReadOnlyList<ReplyScheduleReport> reports = keys.Count == 0 ? [] : ScheduleReportsOf(keys);
        if (busy != null)
            throw new ReportKeysBusyException(Path.GetFileName(busy), reports);
        return reports;
    }

    private IReadOnlyList<ReplyScheduleReport> ScheduleReportsOf(List<ReplyKeyEntry> keys)
    {
        var statuses = Statuses();
        var followUps = AllFollowUps();
        var unread = Answers().Where(answer => !answer.Read).Select(answer => answer.Code).ToHashSet(StringComparer.Ordinal);
        return keys.Select(entry => new ReplyScheduleReport(
            entry.SentAt,
            followUps.Where(followUp => followUp.Code == entry.Code).Select(followUp => (DateTime?)followUp.At).Max(),
            ReplySchedule.NeedOf(statuses.TryGetValue(entry.Code, out var status) ? status.Kind : null, unread.Contains(entry.Code)))).ToList();
    }

    private List<ReportState> AllStates()
    {
        lock (Gate)
            return LoadOwnAnswers().States
                .Concat(OtherFiles(AnswersPrefix, AnswersPath).SelectMany(path => ReportReplies.Load<ReportAnswerFile>(path)?.States ?? []))
                .Where(state => state != null && CrashReports.IsCode(state.Code))
                .ToList();
    }

    private List<ReportFollowUp> AllFollowUps()
    {
        lock (Gate)
            return LoadOwnAnswers().FollowUps
                .Concat(PendingFollowUpEntries())
                .Concat(OtherFiles(AnswersPrefix, AnswersPath).SelectMany(path => ReportReplies.Load<ReportAnswerFile>(path)?.FollowUps ?? []))
                .Where(followUp => followUp != null && CrashReports.IsCode(followUp.Code))
                .ToList();
    }

    private static ReportState? MergeState(string code, IEnumerable<ReportState> all)
    {
        var states = all.Where(state => state.Code == code).ToList();
        if (states.Count == 0)
            return null;
        var newest = states.OrderByDescending(state => state.StatusAt ?? DateTime.MinValue).First();
        return new ReportState { Code = code, Status = newest.Status, FixedIn = newest.FixedIn, StatusAt = newest.StatusAt, SolvedAt = states.Max(state => state.SolvedAt) };
    }

    private static IReadOnlyList<ReportFollowUp> FollowUpsOf(string code, IEnumerable<ReportFollowUp> all) =>
        all.Where(followUp => followUp.Code == code)
            .GroupBy(followUp => followUp.At)
            .Select(group => group.First())
            .Select(followUp => new ReportFollowUp
            {
                Code = code, At = followUp.At, Steps = FollowUpSteps.IsKnown(followUp.Steps) ? followUp.Steps : FollowUpSteps.All,
                Text = ReportReplies.Scrub(followUp.Text, code), Logs = followUp.Logs, Screenshot = followUp.Screenshot,
                NewCode = CrashReports.IsCode(followUp.NewCode) ? followUp.NewCode : null
            })
            .OrderBy(followUp => followUp.At)
            .ToList();

    private static ReportState StateIn(ReportAnswerFile file, string code)
    {
        var state = file.States.FirstOrDefault(other => other.Code == code);
        if (state == null)
        {
            state = new ReportState { Code = code };
            file.States.Add(state);
        }

        return state;
    }

    /// <summary>When this report was sent, if this PC still has its key.</summary>
    public DateTime? SentAt(string code)
    {
        lock (Gate)
            return AllKeys().FirstOrDefault(entry => entry.Code == code)?.SentAt;
    }

    /// <summary>Marks one answer read in this exe's file (an answer from the other exe is copied in first).</summary>
    public void MarkRead(ReportAnswer answer) => MarkRead([answer]);

    public void MarkAllRead() => MarkRead(Answers());

    private void MarkRead(IEnumerable<ReportAnswer> list)
    {
        Locked(AnswersPath, () =>
        {
            var file = LoadOwnAnswers();
            var changed = false;
            foreach (var answer in list)
            {
                var own = file.Answers.FirstOrDefault(other => SameAnswer(other) == SameAnswer(answer));
                if (own == null)
                {
                    own = new ReportAnswer
                    {
                        Code = answer.Code, Id = answer.Id, At = answer.At, SavedAt = answer.SavedAt, Source = answer.Source,
                        Text = ReportReplies.Scrub(answer.Text, answer.Code), LogsButton = answer.LogsButton, Replaced = answer.Replaced,
                        SetAsideUnread = answer.SetAsideUnread
                    };
                    file.Answers.Add(own);
                }

                changed |= !own.Read;
                own.Read = true;
                answer.Read = true;
            }

            if (changed)
                SaveAnswers(file);
        });
    }

    /// <summary>
    /// Keeps a known cause fix the crash screen showed, already read since the player sees it there.
    /// The same fix shown again before any send replaces the earlier one.
    /// </summary>
    public void SaveKnownFix(string signatureId, string text)
    {
        var cleaned = ReportReplies.Scrub(text);
        if (string.IsNullOrWhiteSpace(signatureId) || cleaned.Length == 0)
            return;
        Locked(AnswersPath, () =>
        {
            var file = LoadOwnAnswers();
            var id = KnownId(signatureId);
            file.Answers.RemoveAll(answer => answer.Source == AnswerSource.Known && answer.Id == id && answer.Code == "");
            file.Answers.Add(new ReportAnswer
            {
                Id = id,
                At = _utcNow().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                SavedAt = _utcNow(),
                Source = AnswerSource.Known,
                Text = cleaned,
                Read = true
            });
            SaveAnswers(file);
        });
    }

    /// <summary>The report was sent after all: the known fix it showed gets its code.</summary>
    public void AttachCode(string signatureId, string code)
    {
        if (!CrashReports.IsCode(code))
            return;
        Locked(AnswersPath, () =>
        {
            var file = LoadOwnAnswers();
            var id = KnownId(signatureId);
            var answer = file.Answers.Where(other => other.Source == AnswerSource.Known && other.Id == id && other.Code == "")
                .OrderByDescending(other => other.SavedAt).FirstOrDefault();
            if (answer == null)
                return;
            answer.Code = code;
            SaveAnswers(file);
        });
    }

    private static string KnownId(string signatureId) => "known-" + Regex.Replace(signatureId ?? "", "[^A-Za-z0-9_-]", "");

    private ReportAnswerFile LoadOwnAnswers()
    {
        var file = ReportReplies.Load<ReportAnswerFile>(AnswersPath) ?? new ReportAnswerFile();
        file.Answers = (file.Answers ?? []).Where(answer => answer != null && answer.Text is { Length: > 0 }).ToList();
        foreach (var answer in file.Answers)
        {
            answer.Code ??= "";
            answer.Id ??= "";
            answer.At ??= "";
        }

        file.States = (file.States ?? []).Where(state => state != null && CrashReports.IsCode(state.Code)).ToList();
        file.FollowUps = (file.FollowUps ?? []).Where(followUp => followUp != null && CrashReports.IsCode(followUp.Code)).ToList();
        foreach (var followUp in file.FollowUps)
            followUp.Text ??= "";
        return file;
    }

    // Always called while the file's mutex is held. No, still broken answers that waited in memory are written with it.
    private void SaveAnswers(ReportAnswerFile file)
    {
        KeepJunk(AnswersPath);
        var waiting = PendingFollowUpEntries();
        foreach (var followUp in waiting)
            if (!file.FollowUps.Any(other => other.Code == followUp.Code && other.At == followUp.At))
                file.FollowUps.Add(followUp);
        file.Answers = file.Answers.OrderByDescending(answer => answer.SavedAt).Take(MaxAnswers).OrderBy(answer => answer.SavedAt).ToList();
        // The newest 200 states and answers of the player, and at most 20 of those per report.
        file.States = file.States.OrderByDescending(state => state.StatusAt ?? state.SolvedAt ?? DateTime.MinValue).Take(MaxAnswers).ToList();
        file.FollowUps = file.FollowUps.GroupBy(followUp => followUp.Code)
            .SelectMany(group => group.OrderByDescending(followUp => followUp.At).Take(MaxFollowUpsPerReport))
            .OrderByDescending(followUp => followUp.At).Take(MaxFollowUps).OrderBy(followUp => followUp.At).ToList();
        ReportReplies.Save(AnswersPath, file);
        if (waiting.Count > 0)
            lock (Gate)
                if (PendingFollowUps.TryGetValue(AnswersPath, out var list))
                    list.RemoveAll(followUp => waiting.Contains(followUp));
    }

    private static void KeepJunk(string path)
    {
        try
        {
            if (ReportReplies.IsJunk(path))
                File.Copy(path, path + ".bad", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private IEnumerable<string> OtherFiles(string prefix, string ownPath)
    {
        foreach (var folder in _otherFolders)
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.Exists(folder) ? Directory.GetFiles(folder, prefix + "*.json") : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
                if (!string.Equals(Path.GetFullPath(file), Path.GetFullPath(ownPath), StringComparison.OrdinalIgnoreCase))
                    yield return file;
        }
    }
}
