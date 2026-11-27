using BannerlordCoopLauncher.Core.Localization;

namespace BannerlordCoopLauncher.Core.Support;

// The Donate button every tool shows in its header. A click lists Coodsz's Linktree first (his Patreon and
// PayPal) and Buy Me a Coffee under it. The links live only here, so changing one changes every tool.
public static class Donate
{
    public const string Url = "https://linktr.ee/Coodsz";
    public const string BuyMeACoffeeUrl = "https://buymeacoffee.com/coodsz";

    // The menu lines, in order. Brand names, so they stay as written in every language.
    public static IReadOnlyList<(string Name, string Url)> Links { get; } =
    [
        ("Linktree (Patreon, PayPal)", Url),
        ("Buy Me a Coffee", BuyMeACoffeeUrl),
    ];

    public const string Title = "Donate";
    public const string ButtonTip = "Support Coodsz on Patreon, PayPal or Buy Me a Coffee. The tools stay free either way.";
    public const string OpenProblem = "Could not open your browser. Here is the link:";
    public const string LinkTip = "Opens this page in your browser.";

    // The box when the browser won't open, in the picked language, with the link on its own line. Shown as
    // it is, since translating it again lets a wider template take the link line.
    public static string OpenProblemText(string url = Url) => Translator.T(OpenProblem) + "\n" + url;
}
