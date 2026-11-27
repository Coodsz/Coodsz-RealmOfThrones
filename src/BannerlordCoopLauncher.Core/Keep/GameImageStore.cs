using BannerlordCoopLauncher.Core.IO;
namespace BannerlordCoopLauncher.Core.Keep;

/// <summary>What a picture shows. The folder name is the kind in lower case.</summary>
public enum GameImageKind { Item, Troop, Skill, Lord, Banner }

/// <summary>
/// The game pictures (items, troop portraits, skill icons, later lord portraits and banners)
/// cached on this PC under %AppData%\Coodsz\images\&lt;kind&gt;\&lt;game id&gt;.png.
/// </summary>
/// <remarks>
/// <para>
/// The pictures belong to TaleWorlds, so the Coodsz tools neither ship nor download them. They
/// are drawn by the player's own installed game: "Build pictures" starts Bannerlord once with the
/// Coodsz Image Exporter module, which renders every item and troop with the game's own thumbnail
/// renderer (the one the inventory screen uses) and saves them here. Nothing leaves the PC.
/// Until then, and for anything missing, the windows show neat text cards.
/// </para>
/// <para>File names are the game's own ids, so they are the same whatever language the game runs in.</para>
/// </remarks>
public sealed class GameImageStore
{
    public const string DoneFile = "export-done.txt";
    public const string LogFile = "export.log";

    public GameImageStore(string root) => Root = root;

    public static string DefaultRoot => Path.Combine(
        ToolData.RoamingRoot, "images");

    public string Root { get; }

    public static string Folder(GameImageKind kind) => kind.ToString().ToLowerInvariant();

    /// <summary>The cached picture for this game id, or null.</summary>
    public string? Find(GameImageKind kind, string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;
        var path = Path.Combine(Root, Folder(kind), id + ".png");
        return File.Exists(path) ? path : null;
    }

    public int Count(GameImageKind kind)
    {
        var folder = Path.Combine(Root, Folder(kind));
        try
        {
            return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.png").Count() : 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    public bool HasPictures => Count(GameImageKind.Item) + Count(GameImageKind.Troop) > 0;

    /// <summary>The exporter writes this when it is done; while it is there the exporter does nothing.</summary>
    public string DonePath => Path.Combine(Root, DoneFile);

    public bool Finished => File.Exists(DonePath);

    /// <summary>"1,230 item and 410 troop pictures" for the status line.</summary>
    public string Summary()
    {
        var items = Count(GameImageKind.Item);
        var troops = Count(GameImageKind.Troop);
        return items + troops == 0
            ? "No pictures yet. Build pictures draws them with your own game."
            : items + " item and " + troops + " troop pictures";
    }
}
