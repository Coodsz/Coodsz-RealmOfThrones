using System.Text.RegularExpressions;

namespace TaomCoop.Core;

public static class SaveSlot
{
    public static string ResolveCopyName(string typed, string selectedName)
    {
        var name = typed.Trim();
        return name.Length == 0 ? selectedName.Trim() : name;
    }

    public static string? NameError(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
            return "Type a name for the server copy.";
        if (trimmed.Equals("default_new_game", StringComparison.OrdinalIgnoreCase)
            || trimmed != Path.GetFileName(trimmed)
            || Path.GetExtension(trimmed).Length != 0
            || trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return "Use one save name, without a folder or a file extension. A vanilla new game is refused.";
        return null;
    }

    public static bool CanHost(string savePath, string serverSavesDirectory) =>
        !string.IsNullOrWhiteSpace(savePath)
        && File.Exists(savePath)
        && IsInsideDirectory(savePath, serverSavesDirectory);

    public static bool IsInsideDirectory(string path, string directory)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    public static string RewriteSlotId(string json, string oldName, string newName)
    {
        var pattern = "(\"UniqueGameId\"\\s*:\\s*\")" + Regex.Escape(oldName) + "(\")";
        var regex = new Regex(pattern);
        if (regex.Matches(json).Count != 1)
            return json;
        return regex.Replace(json, match => match.Groups[1].Value + newName + match.Groups[2].Value, 1);
    }

    public static void CopySidecar(string sourceJson, string destinationDirectory, string oldName, string newName)
    {
        if (!File.Exists(sourceJson))
            return;
        Directory.CreateDirectory(destinationDirectory);
        var json = File.ReadAllText(sourceJson);
        File.WriteAllText(Path.Combine(destinationDirectory, newName + ".json"), RewriteSlotId(json, oldName, newName));
    }
}
