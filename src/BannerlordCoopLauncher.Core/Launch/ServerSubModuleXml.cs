using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace BannerlordCoopLauncher.Core.Launch;

/// <summary>
/// The extra SubModule entries the tools put in the server's DedicatedServer.Windows\SubModule.xml (Launcher
/// Cheats, Launcher Battle Size, Coop Save Rescue, Coodsz War Sync). Every write is a text edit that keeps the
/// file's BOM, line ends and indent. An XDocument save once put Launcher Cheats and Launcher Battle Size on the
/// line of &lt;/SubModules&gt;, the old text insert then took that whole line as its indent and copied both
/// into every line of the Coop Save Rescue entry, and its own check stopped finding that entry. Each start
/// added all three again (peaches, CR-A9XVH: seven copies after a few days). Collapse puts such a file right.
/// </summary>
public static class ServerSubModuleXml
{
    private static readonly byte[] Bom = { 0xEF, 0xBB, 0xBF };

    /// <summary>The file's text and whether it starts with a UTF-8 BOM.</summary>
    public static (string Text, bool Bom) Read(string xmlPath)
    {
        var bytes = File.ReadAllBytes(xmlPath);
        var bom = bytes.AsSpan().StartsWith(Bom);
        return (new UTF8Encoding(false).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0)), bom);
    }

    public static void Write(string xmlPath, string text, bool bom)
    {
        var body = new UTF8Encoding(false).GetBytes(text);
        File.WriteAllBytes(xmlPath, bom ? Bom.Concat(body).ToArray() : body);
    }

    /// <summary>True when a top level entry of SubModules has this DLLName or this Name. A file that is no
    /// valid XML falls back to looking for the entry's Name in the text.</summary>
    public static bool Has(string text, string dllName, string subModuleName)
    {
        try
        {
            var list = XDocument.Parse(text).Root?.Element("SubModules");
            return list != null && list.Elements("SubModule").Any(item =>
                string.Equals(Value(item, "DLLName"), dllName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Value(item, "Name"), subModuleName, StringComparison.OrdinalIgnoreCase));
        }
        catch (XmlException)
        {
            return Regex.IsMatch(text, @"<Name\s+value\s*=\s*""" + Regex.Escape(subModuleName) + @"""", RegexOptions.None, TimeSpan.FromSeconds(1));
        }
    }

    /// <summary>
    /// Adds a SubModule entry before the last &lt;/SubModules&gt; in the file's own indent and line ends. Only
    /// the leading whitespace of that line is the indent; when the line holds more than whitespace, the entry
    /// and &lt;/SubModules&gt; go on lines of their own. Null when the file has no &lt;/SubModules&gt;.
    /// </summary>
    public static string? Insert(string text, string subModuleName, string dllName, string classType)
    {
        var at = text.LastIndexOf("</SubModules>", StringComparison.Ordinal);
        if (at < 0)
            return null;
        var nl = NewLine(text);
        var lineStart = at == 0 ? 0 : text.LastIndexOf('\n', at - 1) + 1;
        var lineIndent = LeadingWhitespace(text, lineStart, at);
        // The indent of the <SubModules> line; the end tag's line may start with an entry instead.
        var open = OpenTags(text[..at]).LastOrDefault()?.Index ?? -1;
        var openLine = open <= 0 ? 0 : text.LastIndexOf('\n', open - 1) + 1;
        var indent = open < 0 ? lineIndent : LeadingWhitespace(text, openLine, open);
        var unit = Unit(text, indent);
        var entry = indent + unit + "<SubModule>" + nl
            + indent + unit + unit + "<Name value=\"" + Escape(subModuleName) + "\"/>" + nl
            + indent + unit + unit + "<DLLName value=\"" + Escape(dllName) + "\"/>" + nl
            + indent + unit + unit + "<SubModuleClassType value=\"" + Escape(classType) + "\"/>" + nl
            + indent + unit + "</SubModule>" + nl;
        // Only whitespace before </SubModules> on its line: the entry goes in front of that line.
        if (lineStart + lineIndent.Length == at)
            return text.Insert(lineStart, entry);
        return text.Insert(at, nl + entry + indent);
    }

    public const string BackupSuffix = ".coodsz-original";

    /// <summary>Copies SubModule.xml to SubModule.xml.coodsz-original when there is no copy yet. Every writer
    /// calls this right before it changes the file, so the copy holds the file as it was before any tool.</summary>
    public static void Backup(string xmlPath)
    {
        var backup = xmlPath + BackupSuffix;
        if (!File.Exists(backup))
            File.Copy(xmlPath, backup);
    }

    /// <summary>
    /// Drops a top level SubModule entry that is the same as an earlier one (Name, DLLName, SubModuleClassType,
    /// Tags and the rest, whitespace aside) and SubModule elements nested inside an entry. One DLL may have
    /// several entries with other classes or tags, and those all stay. The SubModules block is written again in
    /// the file's indent, line ends and BOM, after a backup (Backup). Returns how many top level entries went;
    /// 0 when nothing changed or the file can't be read.
    /// </summary>
    public static int Collapse(string xmlPath)
    {
        try
        {
            if (!File.Exists(xmlPath))
                return 0;
            var (text, bom) = Read(xmlPath);
            var collapsed = Collapse(text, out var removed);
            if (collapsed == null)
                return 0;
            Backup(xmlPath);
            Write(xmlPath, collapsed, bom);
            return removed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or RegexMatchTimeoutException)
        {
            // In use or locked, or too odd to read in time; the next start tries again.
            return 0;
        }
    }

    /// <summary>The collapsed text, or null when the file needs no change or looks different than expected.</summary>
    public static string? Collapse(string text, out int removed)
    {
        removed = 0;
        XElement? list;
        try
        {
            list = XDocument.Parse(text, LoadOptions.PreserveWhitespace).Root?.Element("SubModules");
        }
        catch (XmlException)
        {
            return null;
        }

        if (list == null)
            return null;
        var entries = list.Elements("SubModule").ToList();
        var nested = entries.Any(item => item.Descendants("SubModule").Any());
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var keep = new List<XNode>();
        foreach (var node in list.Nodes())
        {
            if (node is XText { Value: var value } && string.IsNullOrWhiteSpace(value))
                continue;
            if (node is XText or XCData)
                return null; // Text in the list itself; nothing a tool wrote, so leave it alone.
            if (node is XElement { Name.LocalName: "SubModule" } item)
            {
                // The whole entry without nested entries and whitespace: only a true repeat goes.
                var clean = Clean(item);
                var key = new StringBuilder();
                WriteElement(key, clean, "", "", "\n");
                if (!seen.Add(key.ToString()))
                {
                    removed++;
                    continue;
                }

                keep.Add(clean);
                continue;
            }

            keep.Add(node);
        }

        if (removed == 0 && !nested)
            return null;

        // The one <SubModules ...> start tag and the one end tag; anything else (an end tag in a comment, say)
        // is not a file the tools wrote.
        var opens = OpenTags(text);
        var close = text.LastIndexOf("</SubModules>", StringComparison.Ordinal);
        if (opens.Count != 1 || close < opens[0].Index || text.IndexOf("</SubModules>", StringComparison.Ordinal) != close)
            return null;
        var open = opens[0].Index;
        var nl = NewLine(text);
        var openLine = open == 0 ? 0 : text.LastIndexOf('\n', open - 1) + 1;
        var indent = LeadingWhitespace(text, openLine, open);
        var unit = Unit(text, indent);
        var inner = new StringBuilder();
        foreach (var node in keep)
        {
            if (node is XElement element)
            {
                WriteElement(inner, Clean(element), indent + unit, unit, nl);
            }
            else
            {
                inner.Append(nl).Append(indent + unit).Append(node.ToString(SaveOptions.DisableFormatting));
            }
        }

        var start = open + opens[0].Length;
        var result = text[..start] + inner + nl + indent + text[close..];
        return OnlyListChanged(text, keep, result) ? result : null;
    }

    // The rewrite is a text splice, so it is read back before it is written: it must parse, and apart from
    // whitespace it must be the original with only the SubModules list's content swapped for the kept entries.
    // A tag the regex misread (a quoted > in an attribute) or anything else outside the list changing means the
    // file is left alone.
    private static bool OnlyListChanged(string original, List<XNode> keep, string result)
    {
        try
        {
            var expected = XDocument.Parse(original);
            var list = expected.Root?.Element("SubModules");
            if (list == null)
                return false;
            list.ReplaceNodes(keep.Select(node => node is XElement element ? Clean(element) : node).ToList());
            var actual = XDocument.Parse(result);
            DropWhitespace(expected);
            DropWhitespace(actual);
            return XNode.DeepEquals(expected, actual);
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static void DropWhitespace(XDocument document) =>
        document.DescendantNodes().OfType<XText>().Where(text => text is not XCData && string.IsNullOrWhiteSpace(text.Value)).ToList()
            .ForEach(text => text.Remove());

    // An element on lines of its own: <Tag a="b"/> with no content, <Tag>text</Tag> with text only, else its
    // children one level deeper. Whitespace between elements is dropped and written again.
    private static void WriteElement(StringBuilder into, XElement element, string indent, string unit, string nl)
    {
        var name = element.Name.LocalName;
        var attributes = string.Concat(element.Attributes().Select(attribute => " " + attribute.ToString()));
        var children = element.Nodes().Where(node => node is not XText text || !string.IsNullOrWhiteSpace(text.Value)).ToList();
        into.Append(nl).Append(indent).Append('<').Append(name).Append(attributes);
        if (children.Count == 0)
        {
            into.Append("/>");
            return;
        }

        if (children.All(node => node is XText))
        {
            into.Append('>').Append(string.Concat(children.Select(node => node.ToString()))).Append("</").Append(name).Append('>');
            return;
        }

        into.Append('>');
        foreach (var child in children)
        {
            if (child is XElement inner)
                WriteElement(into, inner, indent + unit, unit, nl);
            else
                into.Append(nl).Append(indent + unit).Append(child.ToString(SaveOptions.DisableFormatting).Trim());
        }

        into.Append(nl).Append(indent).Append("</").Append(name).Append('>');
    }

    // A copy without the SubModule elements nested in it (the old insert left copies of other entries there).
    private static XElement Clean(XElement element)
    {
        var clean = new XElement(element);
        clean.Descendants("SubModule").Remove();
        return clean;
    }

    // Every <SubModules> start tag, with or without attributes; a self closing one is no list to rewrite.
    private static List<Match> OpenTags(string text) =>
        Regex.Matches(text, @"<SubModules(\s[^>]*)?>", RegexOptions.None, TimeSpan.FromSeconds(1))
            .Where(match => !match.Value.EndsWith("/>", StringComparison.Ordinal)).ToList();

    private static string? Value(XElement item, string child) => (string?)item.Element(child)?.Attribute("value");

    private static string NewLine(string text) => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static string LeadingWhitespace(string text, int from, int to)
    {
        var end = from;
        while (end < to && text[end] is ' ' or '\t')
            end++;
        return text[from..end];
    }

    // One indent step: a tab in a tab indented file, otherwise what the first nested line adds, else two spaces.
    private static string Unit(string text, string indent)
    {
        if (indent.Contains('\t'))
            return "\t";
        var match = Regex.Match(text, @"\n([ \t]*)<SubModule>", RegexOptions.None, TimeSpan.FromSeconds(1));
        if (match.Success && match.Groups[1].Value.StartsWith(indent, StringComparison.Ordinal) && match.Groups[1].Value.Length > indent.Length)
        {
            var step = match.Groups[1].Value[indent.Length..];
            if (step.Trim().Length == 0)
                return step.Contains('\t') ? "\t" : step;
        }

        return text.Contains("\n\t", StringComparison.Ordinal) ? "\t" : "  ";
    }

    private static string Escape(string value) => System.Security.SecurityElement.Escape(value) ?? value;
}
