using System.Text.Json;

namespace BannerlordCoopLauncher.Core.Keep;

/// <summary>A part of a guide: a heading, short steps and an optional tip.</summary>
public sealed record GuideSection(string Heading, IReadOnlyList<string> Steps, string Tip);

/// <summary>One help page of the Guides page.</summary>
public sealed record Guide(string Id, string Title, string Summary, IReadOnlyList<GuideSection> Sections);

/// <summary>Which tool a set of guides is written for.</summary>
public enum GuideSet
{
    /// <summary>Coodsz Host (Keep\Guides\*.json).</summary>
    CoodszHost,
    /// <summary>The TAOM server tool (Keep\Guides\TaomServer\*.json).</summary>
    TaomServer,
    /// <summary>The TAOM client launcher (Keep\Guides\TaomClient\*.json).</summary>
    TaomClient,
    /// <summary>The Burning Empires server tool (Keep\Guides\BeServer\*.json).</summary>
    BeServer,
    /// <summary>The Burning Empires client launcher (Keep\Guides\BeClient\*.json).</summary>
    BeClient,
    /// <summary>The Realm of Thrones server tool (Keep\Guides\RotServer\*.json).</summary>
    RotServer,
    /// <summary>The Realm of Thrones client launcher (Keep\Guides\RotClient\*.json).</summary>
    RotClient,
}

/// <summary>
/// The built in help pages of the tools (Keep\Guides\*.json for Coodsz Host, and one folder for each
/// TAOM, Burning Empires and Realm of Thrones tool, embedded). The text is English like the rest of the tools; the window translates every
/// line (ExtractUiText collects them). The file name starts with the guide's place in the list.
/// </summary>
public static class GuideLibrary
{
    public const string ResourcePrefix = "Coodsz.Guides.";

    private static readonly Dictionary<GuideSet, Lazy<IReadOnlyList<Guide>>> Loaded = Enum.GetValues<GuideSet>()
        .ToDictionary(set => set, set => new Lazy<IReadOnlyList<Guide>>(() => Load(PrefixOf(set))));

    /// <summary>Coodsz Host's guides.</summary>
    public static IReadOnlyList<Guide> All => For(GuideSet.CoodszHost);

    /// <summary>The guides of one tool, in their order.</summary>
    public static IReadOnlyList<Guide> For(GuideSet set) => Loaded[set].Value;

    /// <summary>The start of the embedded file names of a set (see the Core project file).</summary>
    public static string PrefixOf(GuideSet set) => set switch
    {
        GuideSet.TaomServer => "Coodsz.TaomServerGuides.",
        GuideSet.TaomClient => "Coodsz.TaomClientGuides.",
        GuideSet.BeServer => "Coodsz.BeServerGuides.",
        GuideSet.BeClient => "Coodsz.BeClientGuides.",
        GuideSet.RotServer => "Coodsz.RotServerGuides.",
        GuideSet.RotClient => "Coodsz.RotClientGuides.",
        _ => ResourcePrefix,
    };

    private static IReadOnlyList<Guide> Load(string prefix)
    {
        var assembly = typeof(GuideLibrary).Assembly;
        var guides = new List<Guide>();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream == null)
                continue;
            using var reader = new StreamReader(stream);
            if (Parse(reader.ReadToEnd()) is { } guide)
                guides.Add(guide);
        }

        return guides;
    }

    /// <summary>One guide file, or null when it is not one.</summary>
    public static Guide? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var sections = new List<GuideSection>();
            if (root.TryGetProperty("sections", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var s in list.EnumerateArray())
                {
                    var steps = s.TryGetProperty("steps", out var array) && array.ValueKind == JsonValueKind.Array
                        ? array.EnumerateArray().Select(step => step.GetString() ?? "").Where(step => step.Length > 0).ToList()
                        : new List<string>();
                    sections.Add(new GuideSection(Text(s, "heading"), steps, Text(s, "tip")));
                }

            var id = Text(root, "id");
            var title = Text(root, "title");
            return id.Length == 0 || title.Length == 0 ? null : new Guide(id, title, Text(root, "summary"), sections);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Every line a guide shows: the title, the summary, each heading, step and tip.</summary>
    public static IEnumerable<string> Lines(Guide guide)
    {
        yield return guide.Title;
        if (guide.Summary.Length > 0)
            yield return guide.Summary;
        foreach (var section in guide.Sections)
        {
            if (section.Heading.Length > 0)
                yield return section.Heading;
            foreach (var step in section.Steps)
                yield return step;
            if (section.Tip.Length > 0)
                yield return section.Tip;
        }
    }

    /// <summary>
    /// The guides that have the search in any line, as the screen shows it (<paramref name="shown"/>
    /// turns an English line into the picked language). A title match comes first.
    /// </summary>
    public static IReadOnlyList<Guide> Search(IEnumerable<Guide> guides, string? query, Func<string, string>? shown = null)
    {
        var q = (query ?? "").Trim();
        var list = guides.ToList();
        if (q.Length == 0)
            return list;
        shown ??= s => s;
        bool Has(string line) => line.Contains(q, StringComparison.CurrentCultureIgnoreCase) || shown(line).Contains(q, StringComparison.CurrentCultureIgnoreCase);
        return list.Where(g => Lines(g).Any(Has)).OrderBy(g => Has(g.Title) ? 0 : 1).ToList();
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? "" : "";
}
