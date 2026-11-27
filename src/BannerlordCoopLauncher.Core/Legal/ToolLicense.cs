using BannerlordCoopLauncher.Core.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BannerlordCoopLauncher.Core.Legal;

/// <summary>
/// The Coodsz Coop Tools License (LICENSE.txt at the repository root, embedded here so
/// every tool shows the same copy). Each tool asks once at the first start; the answer
/// is kept for this Windows user in %AppData%\Coodsz\license-accepted.json, so agreeing
/// in one tool covers all of them. When the license text changes (its hash differs),
/// every tool asks again. Every accepted hash is kept, so a tool with the old text and a
/// tool with the new text never ask again once both were accepted.
///
/// The tools released before the hash list (Coop and Friends v1.2.77, TAOM v1.1.53,
/// AD1259 v1.0.32 and older) read only "hash" and rewrite the file with just hash,
/// accepted and tool. So "hash" keeps the first answer and is never replaced, and the
/// list is also kept in a side file those tools never touch.
/// </summary>
public static class ToolLicense
{
    public const string FileName = "license-accepted.json";
    public const string ListFileName = "license-accepted-hashes.json";
    public const string ResourceName = "Coodsz.LICENSE.txt";

    // The window around the license. The license itself stays English (legal text).
    public const string Title = "License";
    public const string Intro = "Please read and accept the license to use the tools.";
    public const string EnglishOnly = "The license is in English.";
    public const string Agree = "I agree";
    public const string AgreeTip = "Accepts the license. You are asked again only when the license changes.";
    public const string Exit = "Exit";
    public const string ExitTip = "Closes the tool without accepting the license.";

    /// <summary>Folder for the answer; the same one as the language pick.</summary>
    public static string SettingsFolder { get; set; } =
        ToolData.RoamingRoot;

    public static string SettingsPath => Path.Combine(SettingsFolder, FileName);

    /// <summary>Every accepted hash, safe from older tools that rewrite the answer file without the list.</summary>
    public static string ListPath => Path.Combine(SettingsFolder, ListFileName);

    private static readonly Lazy<string> Embedded = new(ReadEmbedded);

    /// <summary>The full license text, with Windows line endings for the text box.</summary>
    public static string Text => Embedded.Value;

    private static string ReadEmbedded()
    {
        using var stream = typeof(ToolLicense).Assembly.GetManifestResourceStream(ResourceName);
        if (stream == null)
            return "";
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Normalize(reader.ReadToEnd()).Replace("\n", "\r\n");
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Trim();

    /// <summary>SHA-256 of the text, line endings ignored, so a checkout with CRLF gives the same hash.</summary>
    public static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(text)))).ToLowerInvariant();

    public static bool IsAccepted() => IsAccepted(Text);

    /// <summary>True when this exact license text was accepted. A missing, unreadable or corrupt file asks again.</summary>
    public static bool IsAccepted(string licenseText)
    {
        // A build without the license has nothing to show, and must never lock a tool.
        if (string.IsNullOrWhiteSpace(licenseText))
            return true;
        try
        {
            return AcceptedHashes().Contains(Hash(licenseText), StringComparer.OrdinalIgnoreCase);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (JsonException) { return false; }
    }

    /// <summary>
    /// Every accepted hash: the single hash older tools wrote, the list, and the side file's list.
    /// The answer file decides if there is an answer at all, so without it nothing counts.
    /// </summary>
    private static List<string> AcceptedHashes()
    {
        if (!File.Exists(SettingsPath))
            return [];
        var saved = JsonSerializer.Deserialize<Acceptance>(File.ReadAllText(SettingsPath));
        return Merge([saved?.Hash, .. saved?.Hashes ?? [], .. SideList()]);
    }

    /// <summary>The side file's list. A missing, unreadable or corrupt side file only loses the list.</summary>
    private static List<string> SideList()
    {
        try
        {
            if (!File.Exists(ListPath))
                return [];
            return JsonSerializer.Deserialize<Acceptance>(File.ReadAllText(ListPath))?.Hashes ?? [];
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
        catch (JsonException) { return []; }
    }

    private static List<string> Merge(IEnumerable<string?> all)
    {
        var hashes = new List<string>();
        foreach (var hash in all)
        {
            if (!string.IsNullOrWhiteSpace(hash) && !hashes.Contains(hash, StringComparer.OrdinalIgnoreCase))
                hashes.Add(hash);
        }

        return hashes;
    }

    public static void Accept(string tool) => Accept(Text, tool, DateTimeOffset.UtcNow);

    /// <summary>Saves the answer. A failed write only means the next start asks again.</summary>
    public static void Accept(string licenseText, string tool, DateTimeOffset when)
    {
        try
        {
            Directory.CreateDirectory(SettingsFolder);
            var hash = Hash(licenseText);
            Acceptance? saved;
            try { saved = File.Exists(SettingsPath) ? JsonSerializer.Deserialize<Acceptance>(File.ReadAllText(SettingsPath)) : null; }
            catch (JsonException) { saved = null; }
            var hashes = Merge([saved?.Hash, .. saved?.Hashes ?? [], .. SideList(), hash]);
            var answer = new Acceptance
            {
                // Older tools read only hash and rewrite the file when it is not their own text,
                // so the hash already there stays. Replacing it made the two tools ask in turn forever.
                Hash = string.IsNullOrWhiteSpace(saved?.Hash) ? hash : saved.Hash,
                Hashes = hashes,
                Accepted = when.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                Tool = tool,
            };
            var indented = new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
            File.WriteAllText(ListPath, JsonSerializer.Serialize(new Acceptance { Hashes = hashes }, indented));
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(answer, indented));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public sealed class Acceptance
    {
        [System.Text.Json.Serialization.JsonPropertyName("hash")]
        public string? Hash { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("hashes")]
        public List<string>? Hashes { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("accepted")]
        public string? Accepted { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("tool")]
        public string? Tool { get; set; }
    }
}
