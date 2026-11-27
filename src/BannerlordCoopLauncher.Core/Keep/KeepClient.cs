using BannerlordCoopLauncher.Core.Server;

namespace BannerlordCoopLauncher.Core.Keep;

/// <summary>The outcome of one change: the server's confirmed old and new value, or why it did not happen.</summary>
public sealed record KeepResult(bool Ok, ToolsChange? Change, string Message, IReadOnlyList<string> Lines)
{
    public static KeepResult Failed(string message, IReadOnlyList<string>? lines = null) =>
        new(false, null, message, lines ?? Array.Empty<string>());
}

/// <summary>
/// Talks to the Coodsz Server Tools module for the Cheats page, the player profile and the Items
/// window. Every read comes from the running server; every change is checked against the value
/// the module read back after it (<c>COODSZ_SET ... old= new=</c>).
/// </summary>
public sealed class KeepClient
{
    public const string NoTools =
        "This server does not have Coodsz Server Tools yet. Stop the server and Start it again; Coodsz Coop Server Tool adds the module on Start.";

    public const string NoAnswer = "The server did not answer. Wait until the status says Server online, then try again.";

    private readonly Func<string, Task<IReadOnlyList<string>>> _run;
    private readonly Func<string, string?> _readFile;
    private IReadOnlyList<GameItem>? _items;
    private IReadOnlyList<TroopType>? _troops;
    private WorldData? _world;

    /// <param name="run">Sends one console command to the running server and returns its answer lines.</param>
    /// <param name="readFile">Reads a list file the module wrote (the host checks it is inside the server folder).</param>
    public KeepClient(Func<string, Task<IReadOnlyList<string>>> run, Func<string, string?> readFile)
    {
        _run = run;
        _readFile = readFile;
    }

    /// <summary>Null until checked; false when the server does not know coodsz.ping.</summary>
    public bool? ToolsReady { get; private set; }

    public async Task<bool> CheckToolsAsync()
    {
        var lines = await _run(ToolsCommand.Ping());
        ToolsReady = ToolsParser.IsPong(lines);
        return ToolsReady.Value;
    }

    /// <summary>Forget the cached lists and the tools check (a new server start).</summary>
    public void Reset()
    {
        ToolsReady = null;
        _items = null;
        _troops = null;
        _world = null;
    }

    public async Task<IReadOnlyList<HeroSummary>> HeroesAsync(string? name = null) =>
        ToolsParser.ParseHeroes(await _run(ToolsCommand.Heroes(name)));

    public async Task<HeroProfile?> ProfileAsync(string heroId) => ToolsParser.ParseProfile(await _run(ToolsCommand.Hero(heroId)));

    public async Task<HeroDevelopment?> DevelopmentAsync(string heroId) => ToolsParser.ParseDevelopment(await _run(ToolsCommand.Skills(heroId)));

    public async Task<IReadOnlyList<EquipmentPiece>> EquipmentAsync(string heroId) => ToolsParser.ParseEquipment(await _run(ToolsCommand.Equipment(heroId)));

    public async Task<IReadOnlyList<InventoryItem>> InventoryAsync(string heroId) => ToolsParser.ParseInventory(await _run(ToolsCommand.Inventory(heroId)));

    public async Task<PartyRoster?> RosterAsync(string heroId) => ToolsParser.ParseRoster(await _run(ToolsCommand.Roster(heroId)));

    /// <summary>
    /// Sends one change and reads the module's answer. Ok only when the module confirmed it with
    /// the value it read back; the message says what happened in plain words.
    /// </summary>
    public async Task<KeepResult> ChangeAsync(string command)
    {
        IReadOnlyList<string> lines;
        try
        {
            lines = await _run(command);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            return KeepResult.Failed(ex.Message);
        }

        return Interpret(lines);
    }

    public static KeepResult Interpret(IReadOnlyList<string> lines)
    {
        if (ToolsParser.ParseChange(lines) is { } change)
            return new KeepResult(true, change, "", lines);
        if (AdminCommand.IsUnknownCommandReply(lines))
            return KeepResult.Failed(NoTools, lines);
        if (ToolsParser.Error(lines) is { } error)
            return KeepResult.Failed(Explain(error), lines);
        var text = string.Join(" ", lines.Where(l => !string.IsNullOrWhiteSpace(l))).Trim();
        return KeepResult.Failed(text.Length > 0 ? text : NoAnswer, lines);
    }

    /// <summary>The module's English reasons, said for people.</summary>
    public static string Explain(string error)
    {
        if (error.Contains("no campaign", StringComparison.OrdinalIgnoreCase))
            return "The campaign is not loaded yet. Wait until the status says Server online.";
        if (error.Contains("has no party", StringComparison.OrdinalIgnoreCase))
            return "This hero has no party right now (in a settlement without a party, captured or dead), so troops and items cannot be changed.";
        if (error.Contains("has no clan", StringComparison.OrdinalIgnoreCase))
            return "This hero has no clan, so influence and renown cannot be changed.";
        if (error.Contains("not found", StringComparison.OrdinalIgnoreCase))
            return "The server could not find that: " + error;
        return error;
    }

    /// <summary>Every item in the game, from the running server (read once per start).</summary>
    public async Task<IReadOnlyList<GameItem>> ItemsAsync()
    {
        if (_items is { Count: > 0 })
            return _items;
        var text = await ListFileAsync(ToolsCommand.Items());
        _items = text == null ? Array.Empty<GameItem>() : ItemCatalog.Giveable(ToolsParser.ParseItems(text.Split('\n')));
        return _items;
    }

    /// <summary>Every soldier, mercenary and bandit troop kind, from the running server (read once per start).</summary>
    public async Task<IReadOnlyList<TroopType>> TroopTypesAsync()
    {
        if (_troops is { Count: > 0 })
            return _troops;
        var text = await ListFileAsync(ToolsCommand.TroopTypes());
        _troops = text == null ? Array.Empty<TroopType>() : ToolsParser.ParseTroopTypes(text.Split('\n'));
        return _troops;
    }

    /// <summary>
    /// The running campaign's kingdoms, clans, lords, settlements, troops and items for the
    /// Encyclopedia page. Read once per start; <paramref name="refresh"/> reads it again (lords move,
    /// towns change hands). Empty with a reason in plain words when the server could not send it.
    /// </summary>
    public async Task<(WorldData World, string Message)> WorldAsync(bool refresh = false)
    {
        if (_world is { IsEmpty: false } cached && !refresh)
            return (cached, "");

        var lines = await _run(ToolsCommand.World());
        if (AdminCommand.IsUnknownCommandReply(lines))
            return (WorldData.Empty, NoTools);
        if (ToolsParser.Error(lines) is { } error)
            return (WorldData.Empty, Explain(error));
        var file = ToolsParser.ParseFile(lines);
        var text = file == null ? null : _readFile(file.Path);
        if (text == null)
            return (WorldData.Empty, NoAnswer);

        var world = WorldParser.Parse(text.Split('\n'));
        world = world.WithItems(await ItemsAsync());
        _world = world;
        return (world, "");
    }

    /// <summary>
    /// Where everything is on the campaign map, for the live map page. Not cached: the page asks
    /// every few seconds. With <paramref name="known"/> holding the settlements already read and
    /// <paramref name="settlements"/> false, only the parties are read and put on them. Empty with a
    /// reason in plain words when the server could not send it.
    /// </summary>
    public async Task<(MapData Map, string Message)> MapAsync(MapData known, bool settlements)
    {
        var partiesOnly = !settlements && !known.IsEmpty;
        var lines = await _run(ToolsCommand.Map(partiesOnly));
        if (AdminCommand.IsUnknownCommandReply(lines))
            return (MapData.Empty, NoTools);
        if (ToolsParser.Error(lines) is { } error)
            return (MapData.Empty, Explain(error));
        var file = ToolsParser.ParseFile(lines);
        var text = file == null ? null : _readFile(file.Path);
        if (text == null)
            return (MapData.Empty, NoAnswer);

        var map = MapParser.Parse(text.Split('\n'));
        return (partiesOnly ? known.WithParties(map) : map, "");
    }

    /// <summary>How many cells across the terrain grid is, and how many rows one answer carries, so a read never holds the game up for long (live on Calradia: the whole 1200 x 1005 grid took 86 ms on the server, a 600 one 22 ms).</summary>
    public const int TerrainColumns = 1200;
    public const int TerrainRowsPerRead = 250;

    /// <summary>
    /// The real shape of the campaign map (land, sea, rivers, forests, mountains) from the map
    /// scene on the server, read a few rows at a time. It does not change during a campaign, so a
    /// copy is kept in <paramref name="cacheFolder"/> (%AppData%\Coodsz\map) and read from there the
    /// next time the same world runs. Null with a reason when it could not be read; null with no
    /// reason when the server's tools are too old to have it (the map is then drawn as plain paper).
    /// </summary>
    public async Task<(MapTerrain? Terrain, string Message)> TerrainAsync(MapData map, string? cacheFolder = null)
    {
        cacheFolder ??= TerrainCache.DefaultFolder();
        var key = TerrainCache.Key(map);
        if (TerrainCache.Load(cacheFolder, key) is { } cached)
            return (cached, "");

        MapTerrain? terrain = null;
        var from = 0;
        while (true)
        {
            var lines = await _run(ToolsCommand.Terrain(TerrainColumns, from, TerrainRowsPerRead));
            if (AdminCommand.IsUnknownCommandReply(lines))
                return (null, "");
            if (ToolsParser.Error(lines) is { } error)
                return (null, Explain(error));
            var file = ToolsParser.ParseFile(lines);
            var text = file == null ? null : _readFile(file.Path);
            if (text == null)
                return (null, NoAnswer);
            terrain = TerrainParser.Parse(text.Split('\n'), terrain);
            if (terrain == null)
                return (null, NoAnswer);
            var next = TerrainParser.NextRow(terrain);
            if (next < 0)
                break;
            // An answer that did not carry the rows asked for would loop for ever.
            if (!terrain.IsRowRead(from))
                return (null, NoAnswer);
            from = next;
        }

        TerrainCache.Save(cacheFolder, key, terrain);
        return (terrain, "");
    }

    private async Task<string?> ListFileAsync(string command)
    {
        var file = ToolsParser.ParseFile(await _run(command));
        return file == null ? null : _readFile(file.Path);
    }
}
