using System.Security.Cryptography;
using System.Text;

namespace BannerlordCoopLauncher.Core.Modules;

/// <summary>
/// Changes a mod's own SubModule.xml in place while the game runs, and puts the original back after.
/// The original is kept next to it under the backup name, and the mark file holds the SHA256 of the
/// changed file: a SubModule.xml with another hash was replaced by a Workshop update, so it becomes the
/// new original and is changed again, and a restore never writes over an update. Each fix passes its
/// own backup and mark names, so two fixes never mix up their files. Used by TaomDependencyFix and
/// UIExtenderExPrefabsFix.
/// </summary>
public static class SubModuleXmlPatch
{
    public const string FileName = "SubModule.xml";

    /// <summary>True when the SubModule.xml in this folder is the one this fix wrote.</summary>
    public static bool IsApplied(string moduleDirectory, string markName)
    {
        var xmlPath = Path.Combine(moduleDirectory, FileName);
        var mark = Path.Combine(moduleDirectory, markName);
        return File.Exists(xmlPath) && File.Exists(mark) && File.ReadAllText(mark).Trim() == Hash(File.ReadAllBytes(xmlPath));
    }

    /// <summary>Changes the SubModule.xml with <paramref name="change"/>, which returns the new text or
    /// null when nothing has to change. True when the file was changed; false when there is no
    /// SubModule.xml, it is already changed, or nothing had to change. <paramref name="original"/> is
    /// the text before the change.</summary>
    public static bool Apply(string moduleDirectory, string backupName, string markName, Func<string, string?> change, out string? original)
    {
        original = null;
        var xmlPath = Path.Combine(moduleDirectory, FileName);
        if (!File.Exists(xmlPath))
            return false;
        var backup = Path.Combine(moduleDirectory, backupName);
        var mark = Path.Combine(moduleDirectory, markName);
        var current = File.ReadAllBytes(xmlPath);
        if (File.Exists(mark) && File.ReadAllText(mark).Trim() == Hash(current))
            return false;

        var text = ReadText(xmlPath, out var bom);
        original = text;
        var changed = change(text);
        if (changed == null)
        {
            // A Workshop update may have made the change itself; the old backup is not needed.
            Forget(backup, mark);
            return false;
        }

        // The file now is the original (first time, or a Workshop update replaced the changed one).
        File.WriteAllBytes(backup, current);
        var patched = new UTF8Encoding(bom).GetPreamble().Concat(new UTF8Encoding(false).GetBytes(changed)).ToArray();
        File.WriteAllBytes(xmlPath, patched);
        File.WriteAllText(mark, Hash(patched));
        return true;
    }

    /// <summary>Puts the original SubModule.xml back. True when it was put back; a file a Workshop
    /// update replaced is left as it is.</summary>
    public static bool Restore(string moduleDirectory, string backupName, string markName)
    {
        var xmlPath = Path.Combine(moduleDirectory, FileName);
        var backup = Path.Combine(moduleDirectory, backupName);
        var mark = Path.Combine(moduleDirectory, markName);
        if (!File.Exists(mark))
            return false;
        var restored = false;
        if (File.Exists(backup) && File.Exists(xmlPath) && File.ReadAllText(mark).Trim() == Hash(File.ReadAllBytes(xmlPath)))
        {
            File.Copy(backup, xmlPath, overwrite: true);
            restored = true;
        }

        Forget(backup, mark);
        return restored;
    }

    /// <summary>Puts back every SubModule.xml this fix changed under these roots (Workshop content
    /// folders, the game's Modules folder). Returns the mod folders that were put back.</summary>
    public static IReadOnlyList<string> RestoreAll(IEnumerable<string> roots, string backupName, string markName, List<string>? problems = null)
    {
        var restored = new List<string>();
        foreach (var root in roots.Where(root => !string.IsNullOrWhiteSpace(root)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var mark in Marks(root, markName, 0))
            {
                var folder = Path.GetDirectoryName(mark)!;
                try
                {
                    if (Restore(folder, backupName, markName))
                        restored.Add(folder);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    problems?.Add("Could not put back the original SubModule.xml in " + folder + ". " + ex.Message);
                }
            }
        }

        return restored;
    }

    private static IEnumerable<string> Marks(string directory, string markName, int depth)
    {
        if (depth > 4 || !Directory.Exists(directory))
            yield break;
        var mark = Path.Combine(directory, markName);
        if (File.Exists(mark))
            yield return mark;
        if (File.Exists(Path.Combine(directory, FileName)))
            yield break;
        string[] children;
        try
        {
            children = Directory.GetDirectories(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var child in children)
        {
            foreach (var found in Marks(child, markName, depth + 1))
                yield return found;
        }
    }

    private static void Forget(string backup, string mark)
    {
        if (File.Exists(backup))
            File.Delete(backup);
        if (File.Exists(mark))
            File.Delete(mark);
    }

    /// <summary>The file as text without its UTF-8 BOM; <paramref name="bom"/> says whether it had one.</summary>
    public static string ReadText(string path, out bool bom)
    {
        var raw = File.ReadAllBytes(path);
        bom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
        return new UTF8Encoding(false).GetString(raw, bom ? 3 : 0, raw.Length - (bom ? 3 : 0));
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
