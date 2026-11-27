using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace TaomCoop.Core;

/// <summary>
/// TAOM's campaign map on the public Coop dedicated server for Bannerlord 1.5.7 (Coop v0.1.8). The server reads the map
/// from its own engine\Modules\DedicatedServer.Windows\SceneObj\Main_map, which is the vanilla map, so on a TAOM save the
/// engine logs millions of "Null nav mesh face" lines a minute. With TAOM_Map's map in that folder, stripped like the
/// package strips its own, it logs none (live test of 22 November 2026):
///   1. Every file of TAOM_Map's Main_map is copied as it is (atmosphere.xml, flora.bin, navmesh.bin, terrain.bin),
///      except flowmap.dds and references.txt. Folders like ShaderCache stay out too. The package's own copy has none.
///   2. scene.xscene keeps everything but the entities and the terrain element (16 MB became 307 KB for TAOM 2.0.41).
///   3. extents.json in the shape of the package's file, read from the scene before step 2: borderMin and borderMax from
///      the border_min and border_max entities, borderMaxHeight from border_max's height, and terrainSize from the
///      terrain's node_dimension_x and node_dimension_y times its node_size.
/// The package's own map is kept once as Main_map.before-taom and never touched after that. A package update that brings
/// its own Main_map back while Main_map.before-taom is there gets that map replaced, and Main_map.before-taom keeps the
/// first one. Undo by hand: delete Main_map and rename Main_map.before-taom back; the next 1.5.7 start stages TAOM's map
/// again as long as TAOM_Map is in the server. Apply is safe to run on every start: while the staged map still matches
/// TAOM_Map's, it changes nothing. It reads TAOM_Map's scene on every start, so a TAOM update that changes only the scene,
/// like new borders, gets the scene and extents.json written again too.
/// </summary>
public static class Taom157ServerMap
{
    public const string MapFolder = "Main_map";

    /// <summary>The package's own map, moved aside before TAOM's first goes in.</summary>
    public const string BeforeTaomFolder = MapFolder + ".before-taom";

    /// <summary>This class's own folder in SceneObj, the only one it ever deletes. The new map is built in it and moved out.</summary>
    public const string TempFolder = MapFolder + ".coodsz-new";

    public const string ExtentsFile = "extents.json";

    public const string StagedLine =
        "Put TAOM's map on the server: TAOM_Map's Main_map is now in the server's engine\\Modules\\DedicatedServer.Windows\\SceneObj folder, so the server uses the same map as the players. Its own map is kept there as Main_map.before-taom.";

    private const string SceneFile = "scene.xscene";
    // TAOM_Map's map files the server's map takes as they are.
    private static readonly string[] Compared = ["navmesh.bin", "terrain.bin", "flora.bin"];
    private static readonly string[] Needed = [SceneFile, "atmosphere.xml", .. Compared];
    // scene.xscene is written new, and the server's map does without the other two.
    private static readonly string[] LeftOut = [SceneFile, "flowmap.dds", "references.txt"];

    public static string SceneObj(string serverRoot) => Path.Combine(Modules(serverRoot), "DedicatedServer.Windows", "SceneObj");

    public static string TaomMap(string serverRoot) => Path.Combine(Modules(serverRoot), "TAOM_Map", "SceneObj", MapFolder);

    private static string Modules(string serverRoot) => Path.Combine(Path.GetFullPath(serverRoot), "engine", "Modules");

    /// <summary>One plain line for why TAOM's map can't go on this server folder, or null when it can. Throws the usual file
    /// exceptions when a file is locked.</summary>
    public static string? Problem(string serverRoot) => FolderProblem(serverRoot) ?? ReadScene(TaomMap(serverRoot), out _, out _);

    // What can be told without reading the scene. Apply checks it first, so a server folder that can't take the map is not touched.
    private static string? FolderProblem(string serverRoot)
    {
        // The swap moves folders, so a folder that is only a link to a Workshop item or another server is refused: the
        // move would change that other place.
        foreach (var folder in new[] { "DedicatedServer.Windows", "DedicatedServer.Windows\\SceneObj", "DedicatedServer.Windows\\SceneObj\\" + MapFolder,
                     "DedicatedServer.Windows\\SceneObj\\" + TempFolder, "TAOM_Map", "TAOM_Map\\SceneObj", "TAOM_Map\\SceneObj\\" + MapFolder })
        {
            if (IsLink(Path.Combine(Modules(serverRoot), folder)))
                return "The server's engine\\Modules\\" + folder + " folder is a link to another folder. Copy the real folder into the server instead, then press Start server again.";
        }

        var sceneObj = SceneObj(serverRoot);
        if (!Directory.Exists(Path.Combine(sceneObj, MapFolder)) && !Directory.Exists(Path.Combine(sceneObj, BeforeTaomFolder)))
            return "The server's engine\\Modules\\DedicatedServer.Windows\\SceneObj\\Main_map folder is missing, so the tool can't put TAOM's map in its place. Unpack the Coop server download into the server folder again, then press Start server.";
        foreach (var name in Needed)
        {
            if (!File.Exists(Path.Combine(TaomMap(serverRoot), name)))
                return "TAOM_Map's map is not all in the server: engine\\Modules\\TAOM_Map\\SceneObj\\Main_map\\" + name + " is missing. Copy TAOM_Map from the game's Modules folder into the server's engine\\Modules folder again, then press Start server.";
        }

        return null;
    }

    private static bool IsLink(string folder) =>
        Directory.Exists(folder) && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0;

    /// <summary>Puts TAOM's map in the server's place when it is not there or out of date. True when it did. Throws
    /// InvalidOperationException with <see cref="Problem"/>'s line when it can't, and the usual file exceptions when a file
    /// is locked. A run that stopped halfway is finished by the next one.</summary>
    public static bool Apply(string serverRoot)
    {
        if (FolderProblem(serverRoot) is { } problem)
            throw new InvalidOperationException(problem);

        var sceneObj = SceneObj(serverRoot);
        var temp = Path.Combine(sceneObj, TempFolder);
        // Left by a run that stopped: what is in it may be half built, so it is built again from the start.
        if (Directory.Exists(temp))
            Directory.Delete(temp, recursive: true);
        var source = TaomMap(serverRoot);
        var target = Path.Combine(sceneObj, MapFolder);
        // The scene is read first, also when the map is staged already: a TAOM update can change the scene alone.
        if (ReadScene(source, out var scene, out var extents) is { } sceneProblem)
            throw new InvalidOperationException(sceneProblem);
        var root = scene!.Root!;
        root.Element("entities")!.RemoveNodes();
        root.Element("terrain")!.Remove();
        var sceneBytes = SceneBytes(scene);
        var extentsBytes = NoMark.GetBytes(extents!);
        if (UpToDate(source, target, sceneBytes, extentsBytes))
            return false;

        var built = Path.Combine(temp, MapFolder);
        Directory.CreateDirectory(built);
        foreach (var file in Copied(source))
            File.Copy(file, Path.Combine(built, Path.GetFileName(file)));
        File.WriteAllBytes(Path.Combine(built, SceneFile), sceneBytes);
        File.WriteAllBytes(Path.Combine(built, ExtentsFile), extentsBytes);

        // Two moves: the map that is there goes aside, then the new one goes in. The package's own map goes to
        // before-taom the first time; an older staged map goes into this class's own folder, which is deleted after. A
        // map with TAOM_Map's nav mesh is a staged one also when before-taom is gone, so it never becomes the "original". A
        // stop between the two moves leaves no Main_map, and the next run builds the map again and moves it in.
        var before = Path.Combine(sceneObj, BeforeTaomFolder);
        if (Directory.Exists(target))
        {
            var staged = Directory.Exists(before) || SameFile(Path.Combine(source, "navmesh.bin"), Path.Combine(target, "navmesh.bin"));
            Directory.Move(target, staged ? Path.Combine(temp, "old") : before);
        }
        Directory.Move(built, target);
        Directory.Delete(temp, recursive: true);
        return true;
    }

    private static readonly UTF8Encoding NoMark = new(encoderShouldEmitUTF8Identifier: false);

    // The files of TAOM_Map's Main_map that go into the server's map as they are.
    private static IEnumerable<string> Copied(string source) =>
        Directory.EnumerateFiles(source).Where(file => !LeftOut.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase));

    // The staged map is TAOM_Map's when every file Apply copies is the same file (size first, then SHA-256), and its scene and
    // extents.json are what Apply writes from TAOM_Map's scene now. A scene change that is only whitespace doesn't count, and
    // other files in the staged folder don't either.
    private static bool UpToDate(string source, string target, byte[] scene, byte[] extents) =>
        Copied(source).All(file => SameFile(file, Path.Combine(target, Path.GetFileName(file))))
        && SameBytes(Path.Combine(target, SceneFile), scene) && SameBytes(Path.Combine(target, ExtentsFile), extents);

    private static bool SameBytes(string path, byte[] bytes) =>
        File.Exists(path) && new FileInfo(path).Length == bytes.Length && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes);

    private static bool SameFile(string source, string target) =>
        File.Exists(target) && new FileInfo(source).Length == new FileInfo(target).Length
        && Sha256Files.Hash(source).Equals(Sha256Files.Hash(target), StringComparison.OrdinalIgnoreCase);

    // TAOM_Map's scene and the extents.json text read from it, or the plain line for why it can't be used. Nothing is
    // guessed: a scene without the border entities or the terrain size gives a line.
    private static string? ReadScene(string source, out XDocument? scene, out string? extents)
    {
        scene = null;
        extents = null;
        XDocument document;
        try
        {
            // Like SubModuleXml: no DTD and no resolver, so the file can't pull in anything else.
            using var file = File.OpenRead(Path.Combine(source, SceneFile));
            using var reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreWhitespace = true });
            document = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return SceneUnreadable;
        }

        var root = document.Root;
        if (root?.Name.LocalName != "scene" || root.Element("entities") is not { } entities)
            return SceneUnreadable;
        var min = Position(entities, "border_min");
        var max = Position(entities, "border_max");
        if (min == null || max == null)
            return "TAOM_Map's scene.xscene in the server doesn't have one " + (min == null ? "border_min" : "border_max") + " entity with a position, so the tool can't tell where the map ends. Copy TAOM_Map from the game's Modules folder into the server's engine\\Modules folder again, then press Start server.";

        // The terrain's size is its nodes per side times the size of one node, not the number of node elements in it.
        var terrain = root.Element("terrain");
        if (!(Count(terrain?.Attribute("node_dimension_x")) is { } nodesX && Count(terrain?.Attribute("node_dimension_y")) is { } nodesY
              && Number(terrain?.Attribute("node_size")) is { } nodeSize && nodeSize > 0))
            return "TAOM_Map's scene.xscene in the server has no terrain with node_dimension_x, node_dimension_y and node_size, so the tool can't tell how big the map is. Copy TAOM_Map from the game's Modules folder into the server's engine\\Modules folder again, then press Start server.";

        // The package's own file: indent 2, LF line ends, whole numbers with one decimal.
        extents = string.Join("\n", "{", "  \"borderMin\": [", "    " + Json(min[0]) + ",", "    " + Json(min[1]), "  ],",
            "  \"borderMax\": [", "    " + Json(max[0]) + ",", "    " + Json(max[1]), "  ],",
            "  \"borderMaxHeight\": " + Json(max[2]) + ",",
            "  \"terrainSize\": [", "    " + Json(nodesX * nodeSize) + ",", "    " + Json(nodesY * nodeSize), "  ]", "}", "");
        scene = document;
        return null;
    }

    private const string SceneUnreadable =
        "TAOM_Map's scene.xscene in the server can't be read as a map scene. Copy TAOM_Map from the game's Modules folder into the server's engine\\Modules folder again, then press Start server.";

    // The x, y and z of the one top level entity of that name, as the scene writes it: position="62.000, 0.000, 26.000".
    private static double[]? Position(XElement entities, string name)
    {
        var found = entities.Elements("game_entity").Where(entity => (string?)entity.Attribute("name") == name).ToList();
        if (found.Count != 1 || found[0].Element("transform")?.Attribute("position")?.Value.Split(',') is not { Length: 3 } parts)
            return null;
        var values = new double[3];
        for (var i = 0; i < 3; i++)
        {
            if (Number(parts[i]) is not { } value)
                return null;
            values[i] = value;
        }

        return values;
    }

    private static double? Number(XAttribute? attribute) => attribute == null ? null : Number(attribute.Value);

    // Read the same way on every PC, also where the decimal mark is a comma.
    private static double? Number(string text) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : null;

    private static int? Count(XAttribute? attribute) =>
        attribute != null && int.TryParse(attribute.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count > 0 ? count : null;

    // -0.0 counts as 0, so it is written 0.0 and not -0.0.
    private static string Json(double value) =>
        value == 0 ? "0.0"
        : value == Math.Floor(value) && Math.Abs(value) < 1e15
            ? value.ToString("0.0", CultureInfo.InvariantCulture)
            : value.ToString("R", CultureInfo.InvariantCulture);

    // Without a byte order mark and with the declaration the package's own scene has, indented by two spaces.
    private static byte[] SceneBytes(XDocument scene)
    {
        using var bytes = new MemoryStream();
        using (var text = new StreamWriter(bytes, NoMark, leaveOpen: true) { NewLine = "\r\n" })
        {
            text.WriteLine("<?xml version=\"1.0\"?>");
            using (var xml = XmlWriter.Create(text, new XmlWriterSettings { Indent = true, IndentChars = "  ", NewLineChars = "\r\n", OmitXmlDeclaration = true, CloseOutput = false }))
                scene.Save(xml);
            text.WriteLine();
        }

        return bytes.ToArray();
    }
}
