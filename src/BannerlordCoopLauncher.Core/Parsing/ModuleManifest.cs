using System.Xml;
using System.Xml.Linq;

namespace BannerlordCoopLauncher.Core.Parsing;

/// <summary>A module this one names in its SubModule.xml. Before means it loads before this one.</summary>
public sealed record ModuleLink(string Id, string? Version, bool Optional);

/// <summary>
/// The load order facts of one SubModule.xml:
///   Needs: modules that load before this one (DependedModules, and DependedModuleMetadatas with
///          order="LoadBeforeThis"); Optional ones are not required.
///   LoadsBefore: modules that load after this one (DependedModuleMetadatas order="LoadAfterThis"
///          and ModulesToLoadAfterThis).
///   Incompatible: IncompatibleModules and DependedModuleMetadatas incompatible="true".
/// </summary>
public sealed record ModuleManifest(
    string Id,
    string Name,
    string Version,
    IReadOnlyList<ModuleLink> Needs,
    IReadOnlyList<string> LoadsBefore,
    IReadOnlyList<string> Incompatible,
    IReadOnlyList<string> Dlls)
{
    /// <summary>The game version the mod was made for, from its Native dependency, or null.</summary>
    public string? NativeVersion =>
        Needs.FirstOrDefault(link => link.Id.Equals("Native", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(link.Version))?.Version;

    public IEnumerable<string> RequiredIds => Needs.Where(link => !link.Optional).Select(link => link.Id);
}

public static class ModuleManifestReader
{
    public static ModuleManifest? Read(string moduleDirectory)
    {
        var path = Path.Combine(moduleDirectory, "SubModule.xml");
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static ModuleManifest? Parse(string xml)
    {
        XDocument document;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true };
            using var reader = XmlReader.Create(new StringReader(xml.TrimStart((char)0xFEFF)), settings);
            document = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }

        var root = document.Root;
        if (root == null)
            return null;
        var id = Value(root.Element("Id"));
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var needs = new List<ModuleLink>();
        var after = new List<string>();
        var incompatible = new List<string>();
        foreach (var module in root.Elements("DependedModules").Elements("DependedModule"))
        {
            var target = GetAttribute(module, "Id");
            if (target.Length > 0)
                needs.Add(new ModuleLink(target, NullIfEmpty(GetAttribute(module, "DependentVersion")), IsTrue(GetAttribute(module, "Optional"))));
        }

        foreach (var meta in root.Elements("DependedModuleMetadatas").Elements("DependedModuleMetadata"))
        {
            var target = GetAttribute(meta, "id");
            if (target.Length == 0)
                continue;
            if (IsTrue(GetAttribute(meta, "incompatible")))
            {
                incompatible.Add(target);
                continue;
            }

            var order = GetAttribute(meta, "order");
            if (order.Equals("LoadAfterThis", StringComparison.OrdinalIgnoreCase))
                after.Add(target);
            else if (order.Equals("LoadBeforeThis", StringComparison.OrdinalIgnoreCase))
                needs.Add(new ModuleLink(target, NullIfEmpty(GetAttribute(meta, "version")), IsTrue(GetAttribute(meta, "optional"))));
        }

        foreach (var module in root.Elements("ModulesToLoadAfterThis").Elements("Module"))
        {
            var target = GetAttribute(module, "Id");
            if (target.Length > 0)
                after.Add(target);
        }

        foreach (var module in root.Elements("IncompatibleModules").Elements("Module"))
        {
            var target = GetAttribute(module, "Id");
            if (target.Length > 0)
                incompatible.Add(target);
        }

        // One module named twice: a required link wins over an optional one.
        var merged = needs
            .GroupBy(link => link.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.FirstOrDefault(link => !link.Optional) ?? group.First())
            .ToList();
        var dlls = root.Elements("SubModules").Elements("SubModule")
            .Select(sub => Value(sub.Element("DLLName")))
            .Where(dll => dll.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var name = Value(root.Element("Name"));
        return new ModuleManifest(id.Trim(), name.Length == 0 ? id.Trim() : name, Value(root.Element("Version")),
            merged, after.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), incompatible.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), dlls);
    }

    private static string Value(XElement? element) => element?.Attribute("value")?.Value.Trim() ?? "";

    // Bannerlord files write both Id and id.
    private static string GetAttribute(XElement element, string name) =>
        (element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value ?? "").Trim();

    private static bool IsTrue(string value) => value.Equals("true", StringComparison.OrdinalIgnoreCase);

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
