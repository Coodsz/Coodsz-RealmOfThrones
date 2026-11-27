using BannerlordCoopLauncher.Core.Modules;

namespace TaomCoop.Core;

public static class TaomCompat157
{
    public const string Payload = "TaomCompat157";
    /// <summary>Every TAOM.CoopCompat version the tools carry under the TaomCompat157 payload (one folder each).</summary>
    public static readonly string[] BundledVersions = ["v2.0.41", "v2.0.42"];
    /// <summary>The newest bundled Compat. Prefer <see cref="BundledVersions"/> when checking which builds Install can put in.</summary>
    public const string BundledVersion = "v2.0.42";
    public const string Missing = "TAOM.CoopCompat is missing. Click Install or repair TAOM to install it.";
    public const string Installed = "TAOM and TAOM.CoopCompat are ready.";
    public const string MatchingBuildNeeded = "This tool does not carry TAOM.CoopCompat for your TAOM version. Update the tool or install the full TAOM download that includes it.";

    /// <summary>True when the tools carry a TAOM.CoopCompat folder that matches this TAOM version string.</summary>
    public static bool Carries(string taomVersion) =>
        BundledVersions.Contains(taomVersion, StringComparer.OrdinalIgnoreCase);

    /// <summary>The payload folder for this TAOM version, or null when the tools do not carry that build.</summary>
    public static string? PayloadFolder(string payloadRoot, string taomVersion) =>
        Carries(taomVersion) ? Path.Combine(payloadRoot, taomVersion) : null;

    /// <summary>
    /// The bundled TAOM.CoopCompat folder to copy for this PC's TAOM, or null to keep a matching or newer installed Compat.
    /// Throws when TAOM needs a Compat the tools do not carry, or when a needed payload folder is wrong.
    /// </summary>
    public static string? BundledSource(string gameRoot, string payloadRoot)
    {
        var taom = TaomModules157.TaomVersion(gameRoot);
        var local = Path.Combine(gameRoot, "Modules", TaomStack.CompatModuleId);
        var xml = Path.Combine(local, "SubModule.xml");
        if (File.Exists(xml))
        {
            if (ModuleIdentityReader.TryRead(xml) is not { Id: TaomStack.CompatModuleId } identity
                || !Version.TryParse(identity.Version.TrimStart('v', 'V'), out var installed))
                throw new IOException(MatchingBuildNeeded);
            // Keep a matching copy, or a newer installation the tools must not downgrade.
            if (identity.Version.Equals(taom, StringComparison.OrdinalIgnoreCase)
                || (Version.TryParse(taom.TrimStart('v', 'V'), out var required) && installed > required))
                return null;
        }

        if (!Carries(taom))
            throw new InvalidOperationException(MatchingBuildNeeded);

        var source = PayloadFolder(payloadRoot, taom)!;
        if (ModuleIdentityReader.TryRead(Path.Combine(source, "SubModule.xml"))
            is not { Id: TaomStack.CompatModuleId } payload
            || !payload.Version.Equals(taom, StringComparison.OrdinalIgnoreCase))
            throw new IOException(MatchingBuildNeeded);

        return source;
    }

    internal static bool IsNewer(string target, string source) =>
        ReadVersion(target) is { } installed && ReadVersion(source) is { } selected && installed > selected;

    private static System.Version? ReadVersion(string folder) =>
        ModuleIdentityReader.TryRead(Path.Combine(folder, "SubModule.xml")) is { Id: TaomStack.CompatModuleId } module
        && System.Version.TryParse(module.Version.TrimStart('v', 'V'), out var version) ? version : null;

    public static void Install(string gameRoot, string payloadRoot)
    {
        var source = BundledSource(gameRoot, payloadRoot);
        if (source == null)
            return;

        var modules = Path.Combine(gameRoot, "Modules");
        var target = Path.Combine(modules, TaomStack.CompatModuleId);
        if (!string.Equals(KitCoopServer.RealPath(modules), Path.GetFullPath(modules), StringComparison.OrdinalIgnoreCase)
            || ((Directory.Exists(target) || File.Exists(target)) && File.GetAttributes(target).HasFlag(FileAttributes.ReparsePoint)))
            throw new IOException(MatchingBuildNeeded);

        // Keep both the staging folder and the old copy outside Modules so the game never sees two copies.
        var staging = Path.Combine(gameRoot, ".coodsz-compat-" + Guid.NewGuid().ToString("N"));
        var copy = Path.Combine(staging, "module");
        var backup = Path.Combine(staging, "previous");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var path = Path.Combine(copy, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(file, path);
        }

        // Recheck after staging in case an update installed a matching version during the copy.
        var currentSource = BundledSource(gameRoot, payloadRoot);
        if (!string.Equals(source, currentSource, StringComparison.OrdinalIgnoreCase))
        {
            Directory.Delete(staging, recursive: true);
            if (currentSource == null)
                return;
            throw new IOException("TAOM changed during repair. Wait for the update to finish, then try again.");
        }

        var movedOld = Directory.Exists(target);
        if (movedOld)
            Directory.Move(target, backup);
        try
        {
            Directory.Move(copy, target);
        }
        catch
        {
            if (movedOld && !Directory.Exists(target))
                Directory.Move(backup, target);
            throw;
        }
    }
}
