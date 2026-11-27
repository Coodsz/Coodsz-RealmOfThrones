using BannerlordCoopLauncher.Core.Configuration;
using BannerlordCoopLauncher.Core.Modules;

namespace TaomCoop.Core;

public static class CoopModConfig
{
    public static string UserPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mount and Blade II Bannerlord", "CoopData", "mod-config.json");

    /// <summary>A true or false setting, read where the server reads it. A Coop setting the
    /// file does not have gives the server's default (ModSettingsCatalog).</summary>
    public static bool ReadBool(string json, string key)
    {
        // A key that is not a Coop setting is read wherever it is first found.
        var raw = SectionOf(key) != null ? ReadRaw(json, key) : JsoncEditor.Parse(json ?? "").GetRawValue(key);
        if (raw != null && (raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw.Equals("false", StringComparison.OrdinalIgnoreCase)))
            return raw.Equals("true", StringComparison.OrdinalIgnoreCase);
        if (raw == null && ModSettingsCatalog.FindByKey(key) is { Default: bool fallback })
            return fallback;
        throw new InvalidOperationException("mod-config.json does not have one " + key + " setting.");
    }

    public static string Repair(string json) => JsoncValues.Repair(json);

    public static string SetBool(string json, string key, bool value) =>
        SetValue(json, key, value ? "true" : "false");

    // Where the Coop server reads each setting (GameInterface/Configuration/ModConfigData.cs) is kept
    // in one list, ModSettingsCatalog. A key outside its block is an unknown key to the server.

    /// <summary>The block the server reads this key from (modOptions, difficulty or voice),
    /// or null for a key that is not a Coop mod-config setting (server-config.json keys,
    /// the old network block).</summary>
    public static string? SectionOf(string key) => ModSettingsCatalog.SectionOf(key);

    /// <summary>Writes one setting where the server reads it. A Coop setting is changed
    /// inside its block (modOptions, difficulty, voice) or added there when missing, and a
    /// stray copy at the top level is taken out. Any other key is changed where it is, or
    /// added at the top level. Comments and the other settings stay as they are.</summary>
    public static string SetValue(string json, string key, string raw)
    {
        json = Repair(json);
        var section = SectionOf(key);
        var properties = JsoncValues.Properties(json);
        if (section == null)
        {
            var existing = properties.Where(p => p.Key == key).OrderBy(p => p.Depth).FirstOrDefault();
            return existing.Key == null
                ? JsoncValues.Insert(json, key, raw)
                : json[..existing.ValueStart] + raw + json[existing.ValueEnd..];
        }

        json = RemoveTopLevel(json, key);
        var editor = JsoncEditor.Parse(json);
        editor.SetRawValue(section, key, raw);
        return editor.Text;
    }

    /// <summary>The raw value of a setting as the server reads it (from inside its block), or null.</summary>
    public static string? ReadRaw(string json, string key)
    {
        if (string.IsNullOrEmpty(json))
            return null;
        var section = SectionOf(key);
        if (section != null)
            return JsoncEditor.Parse(json).GetRawValue(section, key);
        var found = JsoncValues.Properties(json).FirstOrDefault(p =>
            p.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && p.Depth == 1);
        return found.Key == null ? null : json[found.ValueStart..found.ValueEnd];
    }

    private static bool InBlock(JsoncValues.Property property, string section) =>
        property.Depth == 2 && string.Equals(property.Parent, section, StringComparison.OrdinalIgnoreCase);

    private static string RemoveTopLevel(string json, string key)
    {
        while (JsoncValues.Properties(json).FirstOrDefault(p => p.Depth == 1 && p.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) is { Key: not null } stray)
            json = JsoncValues.RemoveProperty(json, stray);
        return json;
    }

    /// <summary>A Coop setting found at the top level of the file, where the server never
    /// reads it (older tools added missing settings there). Moved is false when its block
    /// already had the setting, so the stray copy was only taken out.</summary>
    public readonly record struct StrayFix(string Key, string Section, bool Moved);

    /// <summary>Moves top level copies of Coop settings into their block, or takes them out
    /// when the block already has that setting (the block's value is the one the server uses).</summary>
    public static string MoveStraySettings(string json, List<StrayFix>? fixes)
    {
        while (true)
        {
            var properties = JsoncValues.Properties(json);
            var stray = properties.FirstOrDefault(p => p.Depth == 1 && SectionOf(p.Key) != null);
            if (stray.Key == null)
                return json;
            var section = SectionOf(stray.Key)!;
            var raw = json[stray.ValueStart..stray.ValueEnd];
            var inBlock = properties.Any(p => InBlock(p, section) && p.Key.Equals(stray.Key, StringComparison.OrdinalIgnoreCase));
            json = JsoncValues.RemoveProperty(json, stray);
            if (!inBlock)
                json = JsoncValues.InsertInto(json, section, stray.Key, raw);
            fixes?.Add(new StrayFix(stray.Key, section, !inBlock));
        }
    }

    public static string StrayFixNote(StrayFix fix) => fix.Moved
        ? "Moved " + fix.Key + " into " + fix.Section + " in mod-config.json. The server only reads it there, so the copy at the top of the file did nothing."
        : "Removed an extra " + fix.Key + " from the top of mod-config.json. The server only reads the one in " + fix.Section + ", and that one was kept.";

    // The Coop server's BattleSizeProvider reads modOptions.battleSize (a whole number),
    // uses 1000 when it is missing, and clamps it to 200..1000 (kept in ModSettingsCatalog).
    // With Limitless and Limitless.CoopCompat on the server the top is BigBattles.Max.
    public const int BattleSizeMin = ModSettingsCatalog.BattleSizeMin;
    public const int BattleSizeMax = ModSettingsCatalog.BattleSizeMax;
    public const int BattleSizeDefault = ModSettingsCatalog.BattleSizeDefault;

    public static int ClampBattleSize(double size, BigBattleMode mode = BigBattleMode.Normal) =>
        BigBattles.Clamp(size, mode);

    /// <summary>The battle size the server will use, read from modOptions like the server
    /// does. A missing or unreadable value gives the server default of 1000.</summary>
    public static int ReadBattleSize(string json, BigBattleMode mode = BigBattleMode.Normal) =>
        ReadSavedBattleSize(json) is { } size ? ClampBattleSize(size, mode) : BattleSizeDefault;

    /// <summary>modOptions.battleSize as the file has it, not clamped; null when missing or unreadable.</summary>
    public static double? ReadSavedBattleSize(string json) =>
        double.TryParse(ReadRaw(json, "battleSize"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var size)
            ? size
            : null;

    /// <summary>Writes modOptions.battleSize (clamped to 200..1000, or wider with Limitless), adding the key inside
    /// modOptions when it is missing. Comments and the other settings stay as they are.</summary>
    public static string SetBattleSize(string json, int size, BigBattleMode mode = BigBattleMode.Normal) =>
        SetValue(json, "battleSize", ClampBattleSize(size, mode).ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>What the health check found in mod-config.json. Text is the repaired file.
    /// BadLine is set when the server's parser still cannot read it.</summary>
    public sealed record Health(
        string Text,
        bool Changed,
        IReadOnlyList<JsoncValues.BoolFix> BoolFixes,
        bool OtherRepairs,
        int? BadLine,
        string BadText,
        IReadOnlyList<StrayFix> Strays);

    private static readonly System.Text.Json.JsonDocumentOptions ServerLike = new()
    {
        CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static bool Parses(string json)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json, ServerLike);
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    /// <summary>Repairs the known safe mistakes (True or FALSE written with capitals, and the
    /// broken numbers and mod options block <see cref="JsoncValues.Repair"/> handles), then
    /// checks that the file reads as JSON with comments and trailing commas, the way the
    /// Coop server reads it. One mistake makes the server ignore the whole file.</summary>
    public static Health Check(string json)
    {
        json ??= "";
        var fixes = new List<JsoncValues.BoolFix>();
        var cased = JsoncValues.FixBoolCase(json, fixes);
        var repaired = JsoncValues.Repair(cased);
        var other = !repaired.Equals(cased, StringComparison.Ordinal);
        int? badLine = null;
        var badText = "";
        var strays = new List<StrayFix>();
        try
        {
            using (System.Text.Json.JsonDocument.Parse(repaired, ServerLike)) { }
            // Only a file that reads cleanly gets its stray top level settings moved.
            var moved = MoveStraySettings(repaired, strays);
            if (Parses(moved))
                repaired = moved;
            else
                strays.Clear();
        }
        catch (System.Text.Json.JsonException ex)
        {
            var line = (int)(ex.LineNumber ?? 0) + 1;
            badLine = line;
            var lines = repaired.Replace("\r\n", "\n").Split('\n');
            if (line <= lines.Length)
                badText = lines[line - 1].Trim();
            if (badText.Length > 90)
                badText = badText[..87] + "...";
        }

        return new Health(repaired, !repaired.Equals(json, StringComparison.Ordinal), fixes, other, badLine, badText, strays);
    }

    public static string BoolFixNote(JsoncValues.BoolFix fix) =>
        "Fixed mod-config.json line " + fix.Line + ". " + fix.Key + " said " + fix.Written + ", but the server only reads " + fix.Fixed
        + " in lowercase. With the capital letter the server ignores the whole file and uses defaults like battle size 1000.";

    public static string BadLineWarning(Health health, string path)
    {
        if (health.BadLine is not int line)
            return "";
        if (health.BadText.Length == 0)
            return "mod-config.json has a mistake on line " + line + " that the server cannot read. Until it is fixed, the server ignores the whole file and uses its defaults, like battle size 1000. Fix that line in " + path + ", or delete that file so Coop makes a new one with default settings.";
        return "mod-config.json has a mistake on line " + line + " that the server cannot read. That line reads " + health.BadText + ". Until it is fixed, the server ignores the whole file and uses its defaults, like battle size 1000. Fix that line in " + path + ", or delete that file so Coop makes a new one with default settings.";
    }
}
