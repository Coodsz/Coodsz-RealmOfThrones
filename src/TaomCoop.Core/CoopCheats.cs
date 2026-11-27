namespace TaomCoop.Core;

public static class CoopCheats
{
    public static string Hero(string player)
    {
        var name = player.Trim().Replace("\"", "");
        if (name.Length == 0)
            throw new InvalidOperationException("Pick a player first.");
        return name.Contains(' ') ? "\"" + name + "\"" : name;
    }

    public static string Amount(string text, int fallback) =>
        int.TryParse(text, out var value) && value >= 0 ? value.ToString() : fallback.ToString();
}
