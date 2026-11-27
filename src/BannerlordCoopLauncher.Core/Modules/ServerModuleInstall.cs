using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Models;
using BannerlordCoopLauncher.Core.Parsing;

namespace BannerlordCoopLauncher.Core.Modules;

/// <summary>What a mod brings to a dedicated server.</summary>
public enum ServerCode
{
    /// <summary>No DLL submodules: items, troops, scenes. The server loads all of it.</summary>
    DataOnly,
    /// <summary>Every DLL submodule has a build in bin\Win64_Shipping_Server.</summary>
    ServerBuild,
    /// <summary>The mod itself marks its client code DedicatedServerType none, so the server skips it.</summary>
    ClientCodeSkipped,
    /// <summary>Client code with no server build and no skip mark. Loading it on the server can crash it.</summary>
    ClientCodeOnly
}

/// <summary>
/// Copies a mod into a dedicated server's engine\Modules folder. Shared by Coodsz Host and the
/// TAOM server tool. A server copy that has server DLLs is never replaced by a client only copy,
/// and Coop only comes with its server files.
/// </summary>
public static class ServerModuleInstall
{
    public static readonly string[] EditorCacheFolders =
    {
        "AssetSources", "RuntimeDataCache", "SceneEditData", "EmAssetPackages"
    };

    // Mods whose server folder DLL is really the client build and crashes the dedicated server.
    // Their code has to stay off the server; the module itself stays in the list so players match.
    public static readonly IReadOnlyCollection<string> ClientCodeOnServerCrashes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "XorberaxLegacy" };

    // Mods CoopModPatch has a server adapter for. They mark their code DedicatedServerType none,
    // yet ship a server build, and the adapter needs that code running on the server. Coodsz Host
    // loads them through server-module-policy.json forceHeadlessModules (the runtime bootstrap's
    // BANNERLORD_DS_FORCE_HEADLESS_MODULE_DIRECTORIES). A server without that bootstrap needs
    // the skip mark taken off its own copy instead, see TurnOnServerCode.
    public static readonly IReadOnlyCollection<string> CoopModPatchServerCode =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ImprovedGarrisons", "SettlementCultureDrift" };

    public const string CoopModPatchId = "CoopModPatch";

    public static string ModulesRoot(string serverRoot) => Path.Combine(serverRoot, "engine", "Modules");

    public static void Install(string sourceDirectory, string serverRoot, string moduleId)
    {
        if (!ProfileJson.IsSafeModuleId(moduleId))
            throw new InvalidOperationException("This mod id has a character that can't be in a folder name.");
        var modulesRoot = Path.GetFullPath(ModulesRoot(serverRoot));
        var destination = Path.GetFullPath(Path.Combine(modulesRoot, moduleId.Trim()));
        if (!destination.StartsWith(modulesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("This mod id has a character that can't be in a folder name.");
        var source = Path.GetFullPath(sourceDirectory);
        if (source.Equals(destination, StringComparison.OrdinalIgnoreCase)
            || source.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return;
        var refused = ClientOverwriteReason(source, destination, moduleId);
        if (refused != null)
            throw new InvalidOperationException(refused);
        Directory.CreateDirectory(modulesRoot);
        if (Directory.Exists(destination))
            Directory.Delete(destination, recursive: true);
        Copy(source, destination);
        StripEditorCaches(destination);
    }

    public static string? ClientOverwriteReason(string source, string destination, string moduleId)
    {
        if (IsBannerlordCoop(moduleId) || ModuleIdIsCoop(source) || ModuleIdIsCoop(destination))
        {
            if (!File.Exists(Path.Combine(source, "bin", "Win64_Shipping_Server", "Coop.Core.dll")))
                return "Bannerlord Coop stays with the dedicated server. The game copy only has client files, so it was not copied.";
            if (HasServerBin(destination))
                return "Bannerlord Coop is already on the dedicated server, so it was left in place.";
            return null;
        }

        // Their server build is made from the client one, so a client only update replaces it.
        if (Directory.Exists(destination) && HasServerBin(destination) && !HasServerBin(source)
            && !CoopModPatchServerCode.Contains(moduleId))
            return "This copy only has the game's client files. The dedicated server copy was left in place.";
        return null;
    }

    /// <summary>
    /// The mod folder has its campaign map scene (SceneObj\Main_map\scene.xscene). A Workshop re-upload or partial
    /// mirror of a map mod with the same id can lack it (CR-JTQ45: the server's AD1259 came back without its map while
    /// the game's own copy had it), so a copy with it is the source, and a copy that lands without it failed.
    /// </summary>
    public static bool HasMapScene(string moduleDirectory) =>
        !string.IsNullOrWhiteSpace(moduleDirectory) && File.Exists(Path.Combine(moduleDirectory, "SceneObj", "Main_map", "scene.xscene"));

    public static bool HasServerBin(string moduleDirectory)
    {
        var bin = Path.Combine(moduleDirectory, "bin", "Win64_Shipping_Server");
        return Directory.Exists(bin) && Directory.EnumerateFiles(bin, "*.dll").Any();
    }

    public static void StripEditorCaches(string moduleDirectory)
    {
        if (string.IsNullOrWhiteSpace(moduleDirectory) || !Directory.Exists(moduleDirectory))
            return;
        foreach (var name in EditorCacheFolders)
        {
            var path = Path.Combine(moduleDirectory, name);
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }

    // ----- Client code on the server -----

    private static readonly Regex SubModuleBlock = new(@"<SubModule>(?<body>[\s\S]*?)</SubModule>", RegexOptions.Compiled, TimeSpan.FromSeconds(2));
    private static readonly Regex DllName = new(@"<DLLName\s+value\s*=\s*""(?<dll>[^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
    private static readonly Regex ServerTypeTag = new(@"<Tag\s+key\s*=\s*""DedicatedServerType""\s+value\s*=\s*""(?<value>[^""]*)""\s*/>", RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    /// <summary>What this module brings to a dedicated server, from its SubModule.xml and its
    /// bin\Win64_Shipping_Server folder.</summary>
    public static ServerCode Classify(string moduleDirectory)
    {
        var xmlPath = Path.Combine(moduleDirectory, "SubModule.xml");
        var xml = File.Exists(xmlPath) ? File.ReadAllText(xmlPath) : "";
        return Classify(xml, ServerDlls(moduleDirectory), ModuleId(xml));
    }

    public static ServerCode Classify(string xml, IReadOnlyCollection<string> serverDlls, string? moduleId = null)
    {
        var any = false;
        var skipped = false;
        var clientOnly = false;
        foreach (Match block in SubModuleBlock.Matches(xml))
        {
            var dll = DllName.Match(block.Value);
            if (!dll.Success)
                continue;
            any = true;
            var tag = ServerTypeTag.Match(block.Value);
            if (tag.Success && tag.Groups["value"].Value.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                skipped = true;
                continue;
            }

            if (!serverDlls.Contains(Path.GetFileName(dll.Groups["dll"].Value), StringComparer.OrdinalIgnoreCase)
                || (moduleId != null && ClientCodeOnServerCrashes.Contains(moduleId)))
                clientOnly = true;
        }

        if (!any)
            return ServerCode.DataOnly;
        if (clientOnly)
            return ServerCode.ClientCodeOnly;
        return skipped ? ServerCode.ClientCodeSkipped : ServerCode.ServerBuild;
    }

    /// <summary>
    /// The SubModule.xml for the server copy of a mod: every DLL submodule without a server build
    /// (and every submodule of a mod known to crash the server) gets DedicatedServerType none, so
    /// the server keeps the mod in its list and loads its data but does not run that code.
    /// Everything else in the file stays as it is. Running it twice changes nothing more.
    /// </summary>
    public static string TurnOffClientCode(string xml, IReadOnlyCollection<string> serverDlls, string? moduleId = null)
    {
        var forceAll = moduleId != null && ClientCodeOnServerCrashes.Contains(moduleId);
        return SubModuleBlock.Replace(xml, block =>
        {
            var dll = DllName.Match(block.Value);
            if (!dll.Success)
                return block.Value;
            var hasServerBuild = serverDlls.Contains(Path.GetFileName(dll.Groups["dll"].Value), StringComparer.OrdinalIgnoreCase);
            if (hasServerBuild && !forceAll)
                return block.Value;
            var tag = ServerTypeTag.Match(block.Value);
            if (tag.Success)
            {
                if (tag.Groups["value"].Value.Equals("none", StringComparison.OrdinalIgnoreCase))
                    return block.Value;
                var group = tag.Groups["value"];
                var at = group.Index;
                return block.Value[..at] + "none" + block.Value[(at + group.Length)..];
            }

            var noneTag = "<Tag key=\"DedicatedServerType\" value=\"none\" />";
            var body = block.Value;
            var tagsOpen = Regex.Match(body, @"<Tags\s*>", RegexOptions.IgnoreCase);
            if (tagsOpen.Success)
                return body.Insert(tagsOpen.Index + tagsOpen.Length, noneTag);
            var tagsEmpty = Regex.Match(body, @"<Tags\s*/>", RegexOptions.IgnoreCase);
            if (tagsEmpty.Success)
                return body[..tagsEmpty.Index] + "<Tags>" + noneTag + "</Tags>" + body[(tagsEmpty.Index + tagsEmpty.Length)..];
            var close = body.LastIndexOf("</SubModule>", StringComparison.Ordinal);
            return body[..close] + "<Tags>" + noneTag + "</Tags>" + body[close..];
        });
    }

    private static readonly Regex SideTag = new(@"[ \t]*<Tag\s+key\s*=\s*""(?:DedicatedServerType|IsNoRenderModeElement)""[^>]*/>[ \t]*(?:\r?\n)?", RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    /// <summary>
    /// The SubModule.xml for the server copy of a mod whose code has to run on the server: every
    /// DLL submodule with a server build loses its DedicatedServerType and IsNoRenderModeElement
    /// tags. An untagged submodule passes the engine's loadable check on the dedicated server (the
    /// way CoopModPatch ships its own), which is what Coodsz Host's forced headless load does for
    /// that module. Submodules without a server build are left as they are.
    /// </summary>
    public static string TurnOnServerCode(string xml, IReadOnlyCollection<string> serverDlls) =>
        SubModuleBlock.Replace(xml, block =>
        {
            var dll = DllName.Match(block.Value);
            if (!dll.Success || !serverDlls.Contains(Path.GetFileName(dll.Groups["dll"].Value), StringComparer.OrdinalIgnoreCase))
                return block.Value;
            return SideTag.Replace(block.Value, "");
        });

    /// <summary>Gives a server copy without a binWin64_Shipping_Server the DLLs of its client build.
    /// True when DLLs were copied.</summary>
    public static bool AddServerBuildFromClient(string moduleDirectory)
    {
        var client = Path.Combine(moduleDirectory, "bin", "Win64_Shipping_Client");
        if (HasServerBin(moduleDirectory) || !Directory.Exists(client))
            return false;
        var dlls = Directory.GetFiles(client, "*.dll");
        if (dlls.Length == 0)
            return false;
        var server = Path.Combine(moduleDirectory, "bin", "Win64_Shipping_Server");
        Directory.CreateDirectory(server);
        foreach (var dll in dlls)
            File.Copy(dll, Path.Combine(server, Path.GetFileName(dll)), overwrite: true);
        return true;
    }

    public static IReadOnlyCollection<string> ServerDlls(string moduleDirectory)
    {
        var bin = Path.Combine(moduleDirectory, "bin", "Win64_Shipping_Server");
        if (!Directory.Exists(bin))
            return Array.Empty<string>();
        return Directory.EnumerateFiles(bin, "*.dll").Select(Path.GetFileName).OfType<string>().ToList();
    }

    private static string? ModuleId(string xml) =>
        SubModuleXml.TryRead(xml, out var info, out _) && info != null ? info.Id : null;

    private static bool IsBannerlordCoop(string moduleId) =>
        moduleId.Equals("Coop", StringComparison.OrdinalIgnoreCase)
        || moduleId.Equals("CoopNightly", StringComparison.OrdinalIgnoreCase);

    private static bool ModuleIdIsCoop(string directory)
    {
        var xml = Path.Combine(directory, "SubModule.xml");
        if (!File.Exists(xml))
            return false;
        try
        {
            var id = ModuleId(File.ReadAllText(xml));
            return id != null && IsBannerlordCoop(id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// The files of a good copy of a mod that the server copy lacks, as paths relative to the mod folder. Only names
    /// and lengths are compared, so it stays cheap on a mod of several GB. Only the folders a dedicated server loads
    /// count (ServerFolders); loose top level files and other folders do not, since the game writes into the player's
    /// copy while it plays (AD1259's log.txt and PlayerBanners) and the launchers park files there. Inside those
    /// folders, backup and generated names never count (IsBackupOrGenerated). A different size only counts for the
    /// DLLs under bin and the files under SceneObj; data and GUI files can be patched by a tool, so there only a
    /// missing file counts. The raw Assets folder counts as there when ServerRawAssets renamed it to Assets.off. Empty
    /// when either folder is missing. A copy cut short keeps its SubModule.xml with the right version and loses the
    /// rest (Tatache, AD1259 crash reports #13 to #15: no bin and no SceneObj\Main_map, so the server ran plain Calradia).
    /// </summary>
    public static IReadOnlyList<string> MissingFiles(string source, string serverCopy)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(serverCopy) || !Directory.Exists(source) || !Directory.Exists(serverCopy))
            return missing;
        try
        {
            foreach (var file in Directory.EnumerateFiles(source, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            {
                var relative = Path.GetRelativePath(source, file);
                var slash = relative.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
                if (slash < 0)
                    continue;
                var top = relative[..slash];
                if (!ServerFolders.Contains(top, StringComparer.OrdinalIgnoreCase) || IsBackupOrGenerated(Path.GetFileName(file)))
                    continue;
                long? length = top.Equals("SceneObj", StringComparison.OrdinalIgnoreCase)
                               || (top.Equals("bin", StringComparison.OrdinalIgnoreCase) && file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    ? new FileInfo(file).Length
                    : null;
                if (Present(Path.Combine(serverCopy, relative), length))
                    continue;
                if (top.Equals("Assets", StringComparison.OrdinalIgnoreCase)
                    && Present(Path.Combine(serverCopy, ServerRawAssets.HiddenName, relative[(slash + 1)..]), length))
                    continue;
                missing.Add(relative);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A copy that can't be read is no proof the server copy is short, so it is left as it is.
            return new List<string>();
        }

        return missing;
    }

    /// <summary>
    /// The top level folders of a mod a dedicated server loads: code, the campaign map scenes, data, GUI, the server's
    /// own asset packages and prefabs, and the raw Assets folder (renamed to Assets.off on the server).
    /// </summary>
    public static readonly string[] ServerFolders = ["bin", "SceneObj", "ModuleData", "GUI", "DsAssetPackages", "Prefabs", "Assets"];

    /// <summary>
    /// Backups and generated files no server needs: the Coodsz tools' own parked and backup files (.coodsz-old,
    /// .coodsz-off, .pre-password.bak and the like), .before-fix copies, logs, temp files, and the CompiledPrefabs.zip
    /// UIExtenderEx writes on the player's PC.
    /// </summary>
    public static bool IsBackupOrGenerated(string fileName) =>
        fileName.Contains(".coodsz-", StringComparison.OrdinalIgnoreCase)
        || fileName.Contains(".before-fix", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("CompiledPrefabs.zip", StringComparison.OrdinalIgnoreCase)
        || BackupEndings.Any(ending => fileName.EndsWith(ending, StringComparison.OrdinalIgnoreCase));

    private static readonly string[] BackupEndings = [".bak", ".old", ".backup", ".log", ".tmp"];

    // There, and at this length when a length is given.
    private static bool Present(string path, long? length)
    {
        var info = new FileInfo(path);
        return info.Exists && (length == null || info.Length == length);
    }

    // SubModule.xml goes last. A copy cut short (the tool closed while it copied a big Assets folder) then has
    // no SubModule.xml, so the next Start sees the mod as missing and copies it again, instead of reading the
    // right version from a folder without its bin or SceneObj.
    private static void Copy(string source, string destination, bool top = true)
    {
        Directory.CreateDirectory(destination);
        var last = new List<string>();
        foreach (var file in Directory.EnumerateFiles(source))
        {
            if (top && Path.GetFileName(file).Equals("SubModule.xml", StringComparison.OrdinalIgnoreCase))
            {
                last.Add(file);
                continue;
            }

            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
            Copy(directory, Path.Combine(destination, Path.GetFileName(directory)), top: false);
        foreach (var file in last)
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
    }
}

/// <summary>Refuses server copies of mods whose client code crashes a dedicated server.</summary>
public static class ServerModuleGuard
{
    public static void RejectClientXorberax(string serverRoot)
    {
        var module = Path.Combine(serverRoot, "engine", "Modules", "XorberaxLegacy");
        var dll = Path.Combine(module, "bin", "Win64_Shipping_Server", "XorberaxLegacy.dll");
        var xml = Path.Combine(module, "SubModule.xml");
        if (!File.Exists(dll) || !File.Exists(xml))
            return;

        var text = File.ReadAllText(xml);
        if (Regex.IsMatch(text, "DedicatedServerType\"\\s+value\\s*=\\s*\"none\"", RegexOptions.IgnoreCase))
            return;

        throw new InvalidOperationException(
            "Xorberax's client DLL is set to load on the dedicated server. That build crashes the server. Leave it off the server.");
    }
}
