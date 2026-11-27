namespace TaomCoop.Core;

public readonly record struct SaveCopy(string Path, DateTime? Created);

public static class LastGoodSave
{
    public const string Suffix = ".last-good";

    public static string? Pick(IReadOnlyList<SaveCopy> copies, DateTime? brokenAt)
    {
        var dated = copies.Where(copy => copy.Created != null).ToList();
        if (dated.Count == 0)
            return null;
        if (brokenAt == null)
            return dated.OrderByDescending(copy => copy.Created).First().Path;
        var good = dated
            .Where(copy => copy.Created < brokenAt.Value)
            .OrderByDescending(copy => copy.Created)
            .FirstOrDefault();
        return string.IsNullOrEmpty(good.Path) ? null : good.Path;
    }

    public static void CopyOver(string source, string destination)
    {
        if (source.Equals(destination, StringComparison.OrdinalIgnoreCase))
            return;
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.Read);
        input.CopyTo(output);
    }
}
