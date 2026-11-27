using System.Buffers.Binary;
using System.IO.Compression;

namespace BannerlordCoopLauncher.Core.Support;

/// <summary>The picture types a screenshot may be. Anything else is refused.</summary>
public enum ReportImageKind
{
    None,
    Png,
    Jpeg
}

/// <summary>
/// One screenshot the player added to a report. Bytes are the picture as it is packed: the tool
/// re-encodes it first (no metadata, very large pictures made smaller), and the pack step strips
/// any metadata chunk again.
/// </summary>
public sealed record ReportScreenshot(string DisplayName, byte[] Bytes, ReportImageKind Kind)
{
    public long Size => Bytes.LongLength;
}

/// <summary>
/// What the player can add to Send my logs and the crash report windows: an optional explanation
/// (Tell us what happened) and up to five screenshots. Both are optional; with nothing added the
/// report is the same as before. The explanation is cleaned like the logs (LogZipBuilder.Clean) and
/// packed as player-notes.txt; the screenshots go under screenshots/. The name is not notes.txt,
/// because the log zips already have a NOTES.txt and Windows treats the two as one file.
/// </summary>
public static class ReportExtras
{
    public const int MaxNotesChars = 2000;
    public const int MaxScreenshots = 5;

    /// <summary>A picture file larger than this is not even read.</summary>
    public const long MaxScreenshotFileBytes = 25_000_000;

    /// <summary>One packed screenshot, after it was re-encoded.</summary>
    public const long MaxScreenshotBytes = 4_000_000;

    /// <summary>
    /// All packed screenshots together. The worker takes a zip of at most 8 MiB and the tools stay
    /// under CrashReports.MaxZipBytes (7.5 MB), so this leaves at least 3.4 MB for the logs.
    /// </summary>
    public const long MaxScreenshotsTotalBytes = 4_000_000;

    /// <summary>The longest side a packed screenshot keeps; larger pictures are made smaller.</summary>
    public const int MaxScreenshotEdge = 2048;

    /// <summary>A picture with more pixels than this is refused before it is decoded.</summary>
    public const long MaxScreenshotPixels = 80_000_000;

    public const string NotesName = "player-notes.txt";
    public const string ScreenshotFolder = "screenshots/";

    public const string NotesLabel = "Tell us what happened (optional)";
    public const string NotesTip = "Write what you were doing and what went wrong, in your own words. Names, addresses and passwords are taken out like in the logs.";
    public const string AddScreenshotsText = "Add screenshots";
    public const string AddScreenshotsTip = "Adds up to 5 PNG or JPG pictures to the report. You can also press Ctrl+V to paste a picture.";
    public const string PasteHint = "or press Ctrl+V to paste a picture";
    public const string ScreenshotNote = "Screenshots are sent as they are. Check them for anything private before sending.";
    public const string RemoveTip = "Takes this screenshot out of the report.";
    public const string OnlyPicturesText = "Only PNG or JPG pictures can be added.";
    public const string TooManyText = "You can add up to 5 screenshots.";
    public const string TooLargeText = "This picture is too large. Try a smaller screenshot.";
    public const string TotalTooLargeText = "The screenshots are too large together. Remove one, or add a smaller one.";
    public const string CouldNotReadText = "Could not read this picture. Try another file.";

    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    // The PNG chunks a picture needs to show the same. Every text, time, EXIF and color profile chunk is dropped.
    private static readonly HashSet<string> PngKeep = new(StringComparer.Ordinal) { "IHDR", "PLTE", "tRNS", "IDAT", "IEND", "gAMA", "sRGB" };

    /// <summary>The picture type from its first bytes (not the file name), or None.</summary>
    public static ReportImageKind DetectImage(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= PngSignature.Length && bytes[..PngSignature.Length].SequenceEqual(PngSignature))
            return ReportImageKind.Png;
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return ReportImageKind.Jpeg;
        return ReportImageKind.None;
    }

    /// <summary>
    /// The explanation as it is packed: at most MaxNotesChars, cleaned like the logs (the Windows and
    /// PC names, Steam ids, addresses and the known passwords taken out), Windows line ends. Empty
    /// when the player wrote nothing.
    /// </summary>
    public static string CleanNotes(string? notes, IEnumerable<string>? secrets = null)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return "";
        var text = notes.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (text.Length > MaxNotesChars)
            text = text[..MaxNotesChars];
        text = LogZipBuilder.Clean(text, secrets).Trim();
        if (text.Length > MaxNotesChars)
            text = text[..MaxNotesChars];
        return text.Replace("\n", "\r\n");
    }

    /// <summary>The counter under the box, like 120 / 2000.</summary>
    public static string CounterText(int length) => Math.Min(length, MaxNotesChars) + " / " + MaxNotesChars;

    /// <summary>Null when the screenshot may be added to the ones already there, or the line that says why not.</summary>
    public static string? CheckAdd(IReadOnlyCollection<ReportScreenshot> current, ReportScreenshot next)
    {
        if (current.Count >= MaxScreenshots)
            return TooManyText;
        if (next.Kind == ReportImageKind.None || DetectImage(next.Bytes) != next.Kind)
            return OnlyPicturesText;
        if (next.Size > MaxScreenshotBytes)
            return TooLargeText;
        if (current.Sum(shot => shot.Size) + next.Size > MaxScreenshotsTotalBytes)
            return TotalTooLargeText;
        return null;
    }

    /// <summary>The name of a screenshot in the zip: screenshots/screenshot-1.png, screenshot-2.jpg and so on.</summary>
    public static string ScreenshotEntryName(int index, ReportImageKind kind) =>
        ScreenshotFolder + "screenshot-" + (index + 1) + (kind == ReportImageKind.Png ? ".png" : ".jpg");

    /// <summary>
    /// The picture without its metadata: PNG keeps only the chunks it needs to show, JPEG loses
    /// every APP segment (EXIF, XMP, color profile, Photoshop) and comment except the Adobe color
    /// segment, and anything after the end of the picture. Null when it is not a well formed PNG or JPEG.
    /// </summary>
    public static byte[]? StripMetadata(byte[] bytes) => DetectImage(bytes) switch
    {
        ReportImageKind.Png => StripPng(bytes),
        ReportImageKind.Jpeg => StripJpeg(bytes),
        _ => null
    };

    private static byte[]? StripPng(byte[] bytes)
    {
        using var output = new MemoryStream(bytes.Length);
        output.Write(PngSignature);
        var at = PngSignature.Length;
        var sawHeader = false;
        while (at + 12 <= bytes.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at, 4));
            if (length > int.MaxValue || at + 12L + length > bytes.Length)
                return null;
            var type = System.Text.Encoding.ASCII.GetString(bytes, at + 4, 4);
            if (!sawHeader && type != "IHDR")
                return null;
            sawHeader = true;
            var total = 12 + (int)length;
            if (PngKeep.Contains(type))
                output.Write(bytes, at, total);
            at += total;
            if (type == "IEND")
                return output.ToArray();
        }

        return null;
    }

    private static byte[]? StripJpeg(byte[] bytes)
    {
        using var output = new MemoryStream(bytes.Length);
        output.WriteByte(0xFF);
        output.WriteByte(0xD8);
        var at = 2;
        while (at + 1 < bytes.Length)
        {
            if (bytes[at] != 0xFF)
                return null;
            var marker = bytes[at + 1];
            if (marker == 0xFF)
            {
                // Fill bytes before a marker.
                at++;
                continue;
            }

            if (marker == 0xD9)
            {
                output.WriteByte(0xFF);
                output.WriteByte(0xD9);
                return output.ToArray();
            }

            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                output.WriteByte(0xFF);
                output.WriteByte(marker);
                at += 2;
                continue;
            }

            if (at + 4 > bytes.Length)
                return null;
            var length = (bytes[at + 2] << 8) | bytes[at + 3];
            if (length < 2 || at + 2 + length > bytes.Length)
                return null;
            var keep = !(marker is >= 0xE0 and <= 0xEF && marker != 0xEE) && marker != 0xFE;
            if (keep)
                output.Write(bytes, at, 2 + length);
            at += 2 + length;

            if (marker == 0xDA)
            {
                // The picture data of a scan runs until the next marker that is not a stuffed 0xFF00 or a restart marker.
                var start = at;
                while (at + 1 < bytes.Length && !(bytes[at] == 0xFF && bytes[at + 1] != 0x00 && bytes[at + 1] is not (>= 0xD0 and <= 0xD7)))
                    at++;
                if (at + 1 >= bytes.Length)
                    return null;
                output.Write(bytes, start, at - start);
            }
        }

        return null;
    }

    /// <summary>
    /// A copy of the summary with the files the extras will add, so Show the summary that is sent
    /// lists them before Send. The sent summary is made from the final zip itself.
    /// </summary>
    public static CrashReportSummary PreviewSummary(CrashReportSummary summary, string cleanedNotes, IReadOnlyList<ReportScreenshot> screenshots)
    {
        var copy = CrashReports.CloneSummary(summary);
        if (cleanedNotes.Length > 0)
            copy.Files.Add(new CrashReportFile(NotesName, System.Text.Encoding.UTF8.GetByteCount(cleanedNotes)));
        for (var i = 0; i < screenshots.Count; i++)
            copy.Files.Add(new CrashReportFile(ScreenshotEntryName(i, screenshots[i].Kind), screenshots[i].Size));
        copy.Files = copy.Files.Take(CrashReports.MaxFiles).ToList();
        return copy;
    }

    /// <summary>Writes the explanation and the screenshots into an open zip. Each screenshot is stripped of metadata again.</summary>
    internal static void Write(ZipArchive zip, string cleanedNotes, IReadOnlyList<ReportScreenshot> screenshots)
    {
        if (cleanedNotes.Length > 0)
        {
            using var writer = new StreamWriter(zip.CreateEntry(NotesName, CompressionLevel.Optimal).Open(), new System.Text.UTF8Encoding(false));
            writer.Write(cleanedNotes);
        }

        for (var i = 0; i < screenshots.Count; i++)
        {
            var shot = screenshots[i];
            var bytes = StripMetadata(shot.Bytes) ?? throw new InvalidDataException("Screenshot " + (i + 1) + " is not a PNG or JPEG picture.");
            // Pictures are already compressed, so they are stored as they are.
            using var stream = zip.CreateEntry(ScreenshotEntryName(i, DetectImage(bytes)), CompressionLevel.NoCompression).Open();
            stream.Write(bytes);
        }
    }
}
