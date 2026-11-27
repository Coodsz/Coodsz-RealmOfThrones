using System.Xml;

namespace BannerlordCoopLauncher.Core.Parsing;

public sealed record SubModuleInfo(string Id, string? Version, string? Name);

public static class SubModuleXml
{
    public static bool TryRead(string xml, out SubModuleInfo? info, out string error)
    {
        info = null;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreWhitespace = true
            };

            using var reader = XmlReader.Create(new StringReader(xml), settings);
            string? id = null;
            string? version = null;
            string? name = null;

            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                    continue;

                if (reader.Name is "Id" or "Version" or "Name")
                {
                    var value = reader.GetAttribute("value");
                    if (reader.Name == "Id" && id == null)
                        id = value;
                    else if (reader.Name == "Version" && version == null)
                        version = value;
                    else if (reader.Name == "Name" && name == null)
                        name = value;
                }
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                error = "Invalid ID in SubModule.xml.";
                return false;
            }

            info = new SubModuleInfo(id.Trim(), version?.Trim(), name?.Trim());
            error = "";
            return true;
        }
        catch (XmlException)
        {
            error = "SubModule.xml invalid.";
            return false;
        }
    }

    public static IReadOnlyList<string> ReadDependencies(string xml)
    {
        var found = new List<string>();
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreWhitespace = true
            };
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.Name != "DependedModule")
                    continue;
                if (string.Equals(reader.GetAttribute("Optional"), "true", StringComparison.OrdinalIgnoreCase))
                    continue;
                var id = reader.GetAttribute("Id") ?? reader.GetAttribute("id");
                if (!string.IsNullOrWhiteSpace(id))
                    found.Add(id.Trim());
            }
        }
        catch (XmlException)
        {
            return Array.Empty<string>();
        }

        return found;
    }
}
