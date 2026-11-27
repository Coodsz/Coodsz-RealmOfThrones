namespace RealmOfThrones.Ui;

/// <summary>
/// Player text the Realm of Thrones server tool and client both show. The client links this file
/// in. It has no WPF in it, so the tests can call it.
/// </summary>
public static class RotUiText
{
    public const string StillSaving = "Still saving. Large worlds take a while.";

    /// <summary>War Sails is always on for Realm of Thrones: there is no switch, only this line.</summary>
    public const string WarSailsLine = "War Sails is always on, because Realm of Thrones needs it.";

    /// <summary>The Client Launcher's notes on Play: War Sails, then what is off in the first version, then the long first join.</summary>
    public const string WarSailsNeeded = "Realm of Thrones needs the War Sails DLC on every player's PC.";

    public const string FirstJoinWait =
        "The first join after you install or change mods can take several minutes while the game prepares the map and compiles shaders. Do not close the game.";

    public static IReadOnlyList<string> ClientNotes => OffInFirstVersion.Prepend(WarSailsNeeded).Append(FirstJoinWait).ToList();

    /// <summary>What is off in the first version, for the Made for card.</summary>
    public static readonly IReadOnlyList<string> OffInFirstVersion =
    [
        "Enlistment, duels, the White Walker start and ravens are off.",
        "Dragons are not tested in Coop yet.",
        "Story choices are decided by the first player to answer, within 60 seconds."
    ];

    /// <summary>The status line after Prepare server or Start server finds problems: only the first,
    /// the rest are in the log.</summary>
    public static string FirstProblem(IReadOnlyList<string> problems)
    {
        if (problems.Count == 0)
            return "";
        var first = problems[0];
        var more = problems.Count - 1;
        if (more <= 0)
            return first;
        return first + " " + (more == 1 ? "1 more problem is" : more + " more problems are") + " in the log. Click Open server logs to see them.";
    }

    /// <summary>The orange line near Connect when the host's mod list has other versions. Null when none differ.</summary>
    public static string? DriftNote(int differences) => differences switch
    {
        <= 0 => null,
        1 => "1 mod version differs from the host. See the log.",
        _ => differences + " mod versions differ from the host. See the log."
    };
}
