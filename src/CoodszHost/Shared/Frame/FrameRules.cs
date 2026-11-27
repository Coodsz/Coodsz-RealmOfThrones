namespace Coodsz.Ui;

/// <summary>The kinds of a chip on a mod row or a status card: a word and a dot in one colour.</summary>
public enum ChipKind { Ok, Warn, Bad, Info, Mute, Gold }

/// <summary>The state words of the header pill. The first five are the Server Tool's, the rest the client launchers'.</summary>
public enum StatusKind { Stopped, Starting, Online, Stopping, Crashed, NotReady, Ready, Joining, InGame, Refused }

/// <summary>The kinds of a banner under the header.</summary>
public enum BannerKind { Info, Warn, Error, Success }

/// <summary>One tool's colour, taken from the banner fill of its icon (assets/icons/*.svg).</summary>
public sealed record ToolAccentColors(string Tool, string Icon, string Deep, string Ink);

/// <summary>A block's place in a two column page grid.</summary>
public readonly record struct PlaySlot(int Row, int Column, int RowSpan = 1);

/// <summary>
/// The frame's colour tables and the banner stack rule, without WPF, so the tests can check them
/// (build-plan.md 1.2 and 1.3). The WPF parts (ToolAccent, Chip, StatusPill, BannerStack) read them.
/// </summary>
public static class FrameRules
{
    public const string CoopServer = "coop-server";
    public const string CoopClient = "coop";
    public const string TaomServer = "taom-server";
    public const string TaomClient = "taom";
    public const string AD1259Server = "ad1259-server";
    public const string AD1259Client = "ad1259";
    public const string BeServer = "be-server";
    public const string BeClient = "be";
    public const string RotServer = "rot-server";
    public const string RotClient = "rot";

    /// <summary>Deep is the icon's banner fill; Ink is a lighter shade that reads on the dark window (4.5:1 or more).</summary>
    public static readonly IReadOnlyList<ToolAccentColors> Accents =
    [
        new(CoopServer, "coodsz-coop-server.svg", "#791F1F", "#BC8F8F"),
        new(CoopClient, "coodsz-coop.svg", "#0C447C", "#86A2BE"),
        new(TaomServer, "coodsz-taom.svg", "#085041", "#84A8A0"),
        new(TaomClient, "coodsz-taom-client.svg", "#0F6E56", "#87B7AB"),
        new(AD1259Server, "coodsz-ad1259.svg", "#3C3489", "#9E9AC4"),
        new(AD1259Client, "coodsz-ad1259-client.svg", "#534AB7", "#A9A5DB"),
        new(BeServer, "coodsz-be.svg", "#D35400", "#E0874D"),
        new(BeClient, "coodsz-be-client.svg", "#E67E22", "#EB984E"),
        new(RotServer, "coodsz-rot.svg", "#141416", "#E0564E"),
        new(RotClient, "coodsz-rot-client.svg", "#2E1D1F", "#E8736B"),
    ];

    /// <summary>The colours of a tool, or the Coop Server Tool's for a name not in the table.</summary>
    public static ToolAccentColors Accent(string tool) =>
        Accents.FirstOrDefault(accent => accent.Tool == tool) ?? Accents[0];

    /// <summary>The Keep keys of a chip's word and dot, and of its fill, with the colour used when the key is missing.</summary>
    public static (string InkKey, string InkFallback, string BackKey, string BackFallback) Chip(ChipKind kind) => kind switch
    {
        ChipKind.Ok => ("Keep.Success", "#8FCB9B", "Keep.SuccessSoft", "#1E3326"),
        ChipKind.Warn => ("Keep.Amber", "#E0A23A", "Keep.AmberSoft", "#3A2E1A"),
        ChipKind.Bad => ("Keep.Red", "#E2786C", "Keep.BadSoft", "#3A201C"),
        ChipKind.Info => ("Keep.Accent", "#8EB4D4", "Keep.InfoSoft", "#17263A"),
        ChipKind.Gold => ("Keep.Warning", "#E2C27A", "Keep.WarningSoft", "#3A3420"),
        _ => ("Keep.TextDim", "#8EA0AE", "Keep.BgRaised", "#1B2833"),
    };

    /// <summary>The narrowest the main column of a two column page may get before the side column goes under it.</summary>
    public const double MainColumnMin = 620;

    /// <summary>True when a page this wide can't hold a side column of <paramref name="side"/> px beside a main column of
    /// at least <see cref="MainColumnMin"/> px, so the side column goes under the main one (the Server Tool's Dashboard).</summary>
    public static bool SideColumnWraps(double pageWidth, double side) => pageWidth > 0 && pageWidth < side + MainColumnMin;

    /// <summary>Where a client's Play page puts its blocks (row, column, row span). Wide: the main cards on the left over two
    /// rows, the side column (Next step) and the Join my server card under it on the right. Stacked: the side column comes
    /// first, so the Next step card with Connect stays in view at 960 px, then the main cards, then the Realm card.</summary>
    public static (PlaySlot Main, PlaySlot Side, PlaySlot Realm) PlaySlots(bool stack) => stack
        ? (new PlaySlot(1, 0), new PlaySlot(0, 0), new PlaySlot(2, 0))
        : (new PlaySlot(0, 0, 2), new PlaySlot(0, 1), new PlaySlot(1, 1));

    /// <summary>The chip colour of a header state: Stopped amber, Starting blue, Online green, Crashed red.</summary>
    public static ChipKind Status(StatusKind kind) => kind switch
    {
        StatusKind.Online or StatusKind.Ready => ChipKind.Ok,
        StatusKind.Stopped or StatusKind.Stopping => ChipKind.Warn,
        StatusKind.Crashed or StatusKind.Refused => ChipKind.Bad,
        StatusKind.Starting or StatusKind.Joining or StatusKind.InGame => ChipKind.Info,
        _ => ChipKind.Mute,
    };

    /// <summary>The chip colour of a banner kind.</summary>
    public static ChipKind Banner(BannerKind kind) => kind switch
    {
        BannerKind.Warn => ChipKind.Gold,
        BannerKind.Error => ChipKind.Bad,
        BannerKind.Success => ChipKind.Ok,
        _ => ChipKind.Info,
    };

    /// <summary>
    /// Which banners show: the visible ones by priority (lower first, then file order), at most <paramref name="max"/>
    /// unless expanded. Returns the indexes to show, most urgent first, and how many visible ones are left over.
    /// </summary>
    public static (IReadOnlyList<int> Shown, int Hidden) Banners(IReadOnlyList<(bool Visible, int Priority)> banners, int max = 2, bool expanded = false)
    {
        var visible = banners.Select((banner, index) => (banner, index)).Where(item => item.banner.Visible)
            .OrderBy(item => item.banner.Priority).ThenBy(item => item.index).Select(item => item.index).ToList();
        var shown = expanded ? visible : visible.Take(max).ToList();
        return (shown, visible.Count - shown.Count);
    }
}
