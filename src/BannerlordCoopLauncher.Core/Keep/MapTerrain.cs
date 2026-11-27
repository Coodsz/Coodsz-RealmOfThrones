using BannerlordCoopLauncher.Core.IO;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BannerlordCoopLauncher.Core.Keep;

/// <summary>What the paper map draws for a terrain cell.</summary>
public enum TerrainKind : byte
{
    /// <summary>No terrain read here (outside the grid, or a row not read yet).</summary>
    Unknown,
    Land,
    Forest,
    Mountain,
    Desert,
    Snow,
    Steppe,
    Swamp,
    Beach,
    River,
    Water,
    DeepWater,

    /// <summary>No navigation mesh: land nobody can walk on (high mountains, map edges).</summary>
    Impassable,
}

/// <summary>
/// The real shape of the campaign map read by <c>coodsz.terrain</c>: a grid over the map scene's
/// borders with the game's terrain type of each cell. Row 0 is the north edge (max y). It does not
/// change during a campaign, so it is read once per world and kept on this PC.
/// </summary>
public sealed class MapTerrain
{
    public const byte NoFace = 0xFF;
    public const byte NotRead = 0xFE;

    public MapTerrain(MapBounds bounds, int columns, int rows)
    {
        Bounds = bounds;
        Columns = columns;
        Rows = rows;
        Cells = new byte[columns * rows];
        Array.Fill(Cells, NotRead);
        _read = new bool[rows];
    }

    private readonly bool[] _read;

    public MapBounds Bounds { get; }
    public int Columns { get; }
    public int Rows { get; }

    /// <summary>The game's TerrainType number of each cell, <see cref="NoFace"/> where there is no navigation mesh.</summary>
    public byte[] Cells { get; }

    public int RowsRead => _read.Count(r => r);
    public bool IsComplete => RowsRead == Rows;

    /// <summary>The cell under a game point, <see cref="TerrainKind.Unknown"/> outside the grid.</summary>
    public TerrainKind KindAt(double x, double y)
    {
        var col = (int)Math.Floor((x - Bounds.MinX) / Bounds.Width * Columns);
        var row = (int)Math.Floor((Bounds.MaxY - y) / Bounds.Height * Rows);
        if (col < 0 || row < 0 || col >= Columns || row >= Rows)
            return TerrainKind.Unknown;
        return Kinds()[row * Columns + col];
    }

    private TerrainKind[]? _kinds;
    private readonly object _kindsLock = new();

    /// <summary>
    /// The kind of every cell. The game has no navigation mesh over the open sea, so a cell with no
    /// mesh that joins the sea (through other cells with no mesh) is open sea; the rest with no mesh
    /// is land nobody can walk on, like the high mountains.
    /// </summary>
    public TerrainKind[] Kinds()
    {
        if (_kinds != null)
            return _kinds;
        lock (_kindsLock)
        {
            if (_kinds != null)
                return _kinds;
            var kinds = new TerrainKind[Cells.Length];
            var queue = new Queue<int>();
            for (var i = 0; i < Cells.Length; i++)
            {
                kinds[i] = Kind(Cells[i]);
                if (Cells[i] is 10 or 18 or 19 or 24)
                    queue.Enqueue(i);
            }

            while (queue.Count > 0)
            {
                var i = queue.Dequeue();
                var col = i % Columns;
                void Visit(int j)
                {
                    if (Cells[j] != NoFace || kinds[j] == TerrainKind.DeepWater)
                        return;
                    kinds[j] = TerrainKind.DeepWater;
                    queue.Enqueue(j);
                }

                if (col > 0) Visit(i - 1);
                if (col < Columns - 1) Visit(i + 1);
                if (i >= Columns) Visit(i - Columns);
                if (i + Columns < Cells.Length) Visit(i + Columns);
            }

            if (IsComplete)
                _kinds = kinds;
            return kinds;
        }
    }

    /// <summary>How big one cell is in game units.</summary>
    public double CellSize => Bounds.Width / Math.Max(1, Columns);

    internal void MarkRead(int row) => _read[row] = true;

    public bool IsRowRead(int row) => _read[row];

    /// <summary>The paper map's kind of a game TerrainType number (TaleWorlds.Core.TerrainType).</summary>
    public static TerrainKind Kind(byte type) => type switch
    {
        NotRead => TerrainKind.Unknown,
        NoFace => TerrainKind.Impassable,
        1 or 14 or 17 => TerrainKind.Land, // plain, rural area, bridge
        2 or 16 => TerrainKind.Desert, // desert, dune
        3 => TerrainKind.Snow,
        4 => TerrainKind.Forest,
        5 => TerrainKind.Steppe,
        6 or 11 or 22 or 25 => TerrainKind.River, // fording, river, non navigable river, under a bridge
        7 or 13 or 21 => TerrainKind.Mountain, // mountain, canyon, cliff
        8 or 10 or 18 or 24 => TerrainKind.Water, // lake, water, coastal sea, sea restriction
        19 => TerrainKind.DeepWater, // open sea
        15 => TerrainKind.Swamp,
        20 => TerrainKind.Beach,
        23 => TerrainKind.Impassable, // land restriction
        _ => TerrainKind.Land,
    };

    public static bool IsWater(TerrainKind kind) => kind is TerrainKind.Water or TerrainKind.DeepWater;
}

/// <summary>Reads and writes the <c>out\terrain.txt</c> lines of <c>coodsz.terrain</c>.</summary>
public static class TerrainParser
{
    /// <summary>
    /// The rows of one answer put into <paramref name="into"/> (or a new grid from its header).
    /// Null when the header is missing or does not match the grid already read.
    /// </summary>
    public static MapTerrain? Parse(IEnumerable<string> lines, MapTerrain? into = null)
    {
        var terrain = into;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith("TERRAIN|", StringComparison.Ordinal))
            {
                var f = line.Split('|');
                if (f.Length < 7 || !Num(f[1], out var minX) || !Num(f[2], out var minY) || !Num(f[3], out var maxX) || !Num(f[4], out var maxY)
                    || !int.TryParse(f[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var columns)
                    || !int.TryParse(f[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rows)
                    || columns < 1 || rows < 1 || columns > 4000 || rows > 4000 || maxX <= minX || maxY <= minY)
                    return null;
                var bounds = new MapBounds(minX, minY, maxX, maxY);
                if (terrain == null)
                    terrain = new MapTerrain(bounds, columns, rows);
                else if (terrain.Columns != columns || terrain.Rows != rows || terrain.Bounds != bounds)
                    return null;
            }
            else if (terrain != null && line.StartsWith("TROW|", StringComparison.Ordinal))
            {
                var f = line.Split('|');
                if (f.Length >= 3 && int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var row) && row >= 0 && row < terrain.Rows
                    && Row(f[2], terrain.Cells.AsSpan(row * terrain.Columns, terrain.Columns)))
                    terrain.MarkRead(row);
            }
        }

        return terrain;
    }

    /// <summary>The first row the next answer should start at, or -1 when every row is read.</summary>
    public static int NextRow(MapTerrain terrain)
    {
        for (var r = 0; r < terrain.Rows; r++)
            if (!terrain.IsRowRead(r))
                return r;
        return -1;
    }

    /// <summary>A whole grid as lines in the same form, for the copy kept on this PC.</summary>
    public static string Write(MapTerrain terrain)
    {
        var b = terrain.Bounds;
        var text = new StringBuilder();
        text.Append("TERRAIN|").Append(F(b.MinX)).Append('|').Append(F(b.MinY)).Append('|').Append(F(b.MaxX)).Append('|').Append(F(b.MaxY))
            .Append('|').Append(terrain.Columns.ToString(CultureInfo.InvariantCulture)).Append('|').Append(terrain.Rows.ToString(CultureInfo.InvariantCulture))
            .Append("|0|").Append(terrain.Rows.ToString(CultureInfo.InvariantCulture)).Append("|0\n");
        for (var r = 0; r < terrain.Rows; r++)
        {
            text.Append("TROW|").Append(r.ToString(CultureInfo.InvariantCulture)).Append('|');
            var o = r * terrain.Columns;
            var c = 0;
            while (c < terrain.Columns)
            {
                var v = terrain.Cells[o + c];
                var run = 1;
                while (c + run < terrain.Columns && terrain.Cells[o + c + run] == v)
                    run++;
                text.Append(Char(v));
                if (run > 1)
                    text.Append(run.ToString(CultureInfo.InvariantCulture));
                c += run;
            }

            text.Append('\n');
        }

        return text.ToString();
    }

    private static bool Row(string runs, Span<byte> cells)
    {
        var at = 0;
        var i = 0;
        while (i < runs.Length)
        {
            var value = Value(runs[i++]);
            var count = 0;
            while (i < runs.Length && char.IsAsciiDigit(runs[i]))
                count = count * 10 + (runs[i++] - '0');
            if (count == 0)
                count = 1;
            if (at + count > cells.Length)
                return false;
            cells.Slice(at, count).Fill(value);
            at += count;
        }

        return at == cells.Length;
    }

    private static byte Value(char c) => c >= 'a' && c <= 'z' ? (byte)(c - 'a') : MapTerrain.NoFace;

    private static char Char(byte v) => v <= 25 ? (char)('a' + v) : '.';

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static bool Num(string s, out double value) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
}

/// <summary>
/// The terrain kept on this PC under %AppData%\Coodsz\map, one file per world. A world is told
/// apart by its settlements and where they are (a different map mod has different ones), so the
/// same campaign map is read from the server only once.
/// </summary>
public static class TerrainCache
{
    public static string DefaultFolder() =>
        Path.Combine(ToolData.RoamingRoot, "map");

    /// <summary>The name of a world's file, from its settlement ids and positions; empty without settlements.</summary>
    public static string Key(MapData map)
    {
        if (map.Settlements.Count == 0)
            return "";
        var text = new StringBuilder();
        foreach (var s in map.Settlements.OrderBy(s => s.Id, StringComparer.Ordinal))
            text.Append(s.Id).Append('|').Append(Math.Round(s.X).ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(Math.Round(s.Y).ToString(CultureInfo.InvariantCulture)).Append('\n');
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
        return "terrain-" + Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    public static MapTerrain? Load(string folder, string key)
    {
        if (key.Length == 0)
            return null;
        try
        {
            var path = Path.Combine(folder, key + ".txt");
            if (!File.Exists(path))
                return null;
            var terrain = TerrainParser.Parse(File.ReadAllLines(path));
            return terrain is { IsComplete: true } ? terrain : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Save(string folder, string key, MapTerrain terrain)
    {
        if (key.Length == 0 || !terrain.IsComplete)
            return;
        try
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, key + ".txt");
            var temp = path + ".tmp";
            File.WriteAllText(temp, TerrainParser.Write(terrain), new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Read again from the server next time.
        }
    }
}
