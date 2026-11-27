namespace RealmOfThrones.Core;

/// <summary>One link of the Encyclopedia page: the button word, the short line beside it and the page it opens.</summary>
public sealed record RotEncyclopediaLink(string Title, string Line, string Url);

/// <summary>
/// The Encyclopedia page of both Realm of Thrones tools: links to the pages the mod's community keeps, opened in the
/// player's browser. Only the links live here, with our own short lines. Nothing from those pages is copied in.
/// </summary>
public static class RotEncyclopedia
{
    /// <summary>The note at the top of the page. The tables were written for 5.5 and the wiki for 6.0.</summary>
    public const string Note = "These pages are made by the Realm of Thrones community, not by Coodsz. Some were written for older versions of the mod (the tables for 5.5, the wiki for 6.0), so details can differ in 8.1. Each button opens the page in your browser.";

    public const string CouldNotOpen = "Could not open the browser. Open this page by hand:";

    public static readonly IReadOnlyList<RotEncyclopediaLink> Links = new RotEncyclopediaLink[]
    {
        new("Realm of Thrones wiki", "Installation, FAQ, dragons, lore events, kingdoms, recruitable clans, troop vendors and mod settings.", "https://github.com/RealmofThrones/Wiki/wiki"),
        new("Troops", "A table of the mod's troops.", "https://rotmod.notion.site/1165d7c0cc8b803dac61e8e1bb8abb97?v=1165d7c0cc8b81d09e95000c4ff67659"),
        new("Armors", "A table of the mod's armor.", "https://rotmod.notion.site/1205d7c0cc8b80ef879cd588151945ab?v=1205d7c0cc8b813c8025000c9c37e712"),
        new("Shields", "A table of the mod's shields.", "https://rotmod.notion.site/1205d7c0cc8b8023af0fe52b94725c7b?v=1205d7c0cc8b81be82f9000c3e836713"),
        new("Weapons", "A table of the mod's weapons.", "https://rotmod.notion.site/1345d7c0cc8b80f2ba3bcd07567a895e?v=1345d7c0cc8b814db884000ce8bad0ea"),
        new("Cultures", "A table of the mod's cultures.", "https://rotmod.notion.site/1215d7c0cc8b80f9a6f8e2264ec0b4df?v=1215d7c0cc8b814aaf61000cc448d864"),
        new("Wanderers", "A table of the wanderers you can hire as companions.", "https://rotmod.notion.site/1265d7c0cc8b80e290ded846d05e597d?v=1265d7c0cc8b8195b5c2000cc18a2a76"),
        new("Kingdoms", "A table of the mod's kingdoms.", "https://rotmod.notion.site/1215d7c0cc8b80bda5b5ff85243fd962?v=1215d7c0cc8b81f98fe7000caa67a7e8"),
        new("Factions and clans", "A table of the mod's factions and clans.", "https://rotmod.notion.site/1355d7c0cc8b80428d92e1884ae724c6?v=1355d7c0cc8b81cd911c000c1122bd54"),
    };
}
