using BannerlordCoopLauncher.Core.AD1259;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Nexus;

namespace BannerlordCoopLauncher.Core.Joining;

/// <summary>What is wrong with a mod the server needs.</summary>
public enum ServerModProblem
{
    /// <summary>Not on this PC.</summary>
    Missing,
    /// <summary>On this PC but turned off.</summary>
    Off,
    /// <summary>On this PC, but not the version the server uses.</summary>
    WrongVersion
}

/// <summary>How the launcher gets a mod the server needs.</summary>
public enum ServerModFetch
{
    /// <summary>Steam subscribes and downloads it, or downloads its update.</summary>
    Workshop,
    /// <summary>It is on this PC; the launcher only turns it on.</summary>
    TurnOn,
    /// <summary>It comes with the launcher folder and is copied into Modules.</summary>
    Launcher,
    /// <summary>The launcher can not get it. The player gets it from its page.</summary>
    ByHand
}

/// <summary>One mod the server needs that this PC does not have turned on, with how it is got.</summary>
public sealed record ServerModNeed(string Id, string Name, ServerModProblem Problem, ServerModFetch Fetch, string? Link = null)
{
    public bool Automatic => Fetch != ServerModFetch.ByHand;

    /// <summary>The hover text of its chip: the module id when the name differs, and what is wrong.</summary>
    public string Tip
    {
        get
        {
            var id = Name.Equals(Id, StringComparison.OrdinalIgnoreCase) ? "" : Id + ". ";
            var what = Problem switch
            {
                ServerModProblem.Off => "It is on this PC but turned off.",
                ServerModProblem.WrongVersion => "This PC has another version than the server.",
                _ => "It is not on this PC yet."
            };
            return id + what;
        }
    }
}

/// <summary>What Connect does about the mods the server needs.</summary>
public enum ServerModAction
{
    /// <summary>Nothing is missing. Connect works as it always did.</summary>
    Connect,
    /// <summary>The launcher can get every mod: get them, turn them on, then connect.</summary>
    GetAndConnect,
    /// <summary>Some mods need the player: get the others, then stop.</summary>
    GetSomeThenStop,
    /// <summary>Only the player can get them: list them and stop.</summary>
    GetByHand
}

/// <summary>
/// The one prompt Connect shows when the server needs mods this PC does not have turned on: a short
/// title with the count, the mods as chips with their friendly names and +N more, the mods the launcher
/// can not get listed by name with their Get it link, and one main button. Used by Coodsz Coop and
/// Friends and the TAOM client launcher, so they read the same.
/// </summary>
public sealed record ServerModPrompt(IReadOnlyList<ServerModNeed> Mods)
{
    /// <summary>How many chips show before the +N more toggle, like the save mods card.</summary>
    public const int ChipLimit = SaveModCard.ChipLimit;

    public const string CancelButton = "Cancel";
    public const string CancelTip = "Closes this. Nothing is changed and the game does not start.";
    public const string GetItButton = "Get it";
    public const string LessButton = "Show less";

    /// <summary>The prompt for these needs, or null when nothing is missing and Connect goes on as before.
    /// A mod is listed once. The ones the launcher can not get come first, so they are never under +N more.</summary>
    public static ServerModPrompt? For(IEnumerable<ServerModNeed> needs)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = needs.Where(need => !string.IsNullOrWhiteSpace(need.Id) && seen.Add(need.Id.Trim()))
            .OrderBy(need => need.Automatic)
            .ToList();
        return list.Count == 0 ? null : new ServerModPrompt(list);
    }

    /// <summary>What Connect does for these needs. No needs at all is a plain Connect.</summary>
    public static ServerModAction ActionFor(IReadOnlyCollection<ServerModNeed> needs) =>
        needs.Count == 0 ? ServerModAction.Connect
        : needs.All(need => need.Automatic) ? ServerModAction.GetAndConnect
        : needs.Any(need => need.Automatic) ? ServerModAction.GetSomeThenStop
        : ServerModAction.GetByHand;

    public ServerModAction Action => ActionFor(Mods);

    public int Count => Mods.Count;

    /// <summary>The mods the launcher gets by itself.</summary>
    public IReadOnlyList<ServerModNeed> Automatic => Mods.Where(need => need.Automatic).ToList();

    /// <summary>The mods the player has to get, listed by name with their Get it link.</summary>
    public IReadOnlyList<ServerModNeed> ByHand => Mods.Where(need => !need.Automatic).ToList();

    public string Title => Count == 1
        ? "This server needs a mod you don't have turned on"
        : "This server needs " + Count + " mods you don't have turned on";

    public string Why => Action switch
    {
        ServerModAction.GetAndConnect => Count == 1
            ? "One click gets it, turns it on and connects."
            : "One click gets them, turns them on in the right order and connects.",
        ServerModAction.GetSomeThenStop => "One click gets the others. Get the rest yourself, then click Connect again.",
        _ => Count == 1
            ? "Get it yourself and put it in the game's Modules folder. Then click Connect again."
            : "Get them yourself and put them in the game's Modules folder. Then click Connect again."
    };

    /// <summary>The line above the mods the player gets; empty when the launcher gets them all.</summary>
    public string ByHandNote => ByHand.Count switch
    {
        0 => "",
        1 => "The launcher can't get this one by itself.",
        _ => "The launcher can't get these by itself."
    };

    /// <summary>The main button's text; empty when there is none.</summary>
    public string MainButton => Action switch
    {
        ServerModAction.GetAndConnect => Count == 1 ? "Get it and connect" : "Get them and connect",
        ServerModAction.GetSomeThenStop => "Get the others",
        _ => ""
    };

    public string MainTip => Action switch
    {
        ServerModAction.GetAndConnect when Automatic.All(need => need.Fetch == ServerModFetch.Launcher) =>
            "Copies the mods that come with the launcher into Modules, turns the mods on, then starts the game and joins.",
        ServerModAction.GetAndConnect when Automatic.Any(need => need.Fetch == ServerModFetch.Launcher) =>
            "Copies the mods that come with the launcher, subscribes in Steam for the rest and waits for the downloads, turns the mods on, then starts the game and joins.",
        ServerModAction.GetAndConnect => "Subscribes in Steam and waits for the downloads, turns the mods on, then starts the game and joins.",
        ServerModAction.GetSomeThenStop => "Gets the mods the launcher can get. The game does not start until the rest are there too.",
        _ => ""
    };

    /// <summary>The chips that show: the first <see cref="ChipLimit"/>, or all of them when expanded.</summary>
    public IReadOnlyList<ServerModNeed> Shown(bool expanded) =>
        expanded || Mods.Count <= ChipLimit ? Mods : Mods.Take(ChipLimit).ToList();

    public bool HasMore => Mods.Count > ChipLimit;

    public int MoreCount(bool expanded) => expanded ? 0 : Math.Max(0, Mods.Count - ChipLimit);

    public string MoreButton(bool expanded) => expanded ? LessButton : "+" + MoreCount(false) + " more";

    public string MoreTip(bool expanded) => expanded ? "Shows only the first mods again." : "Shows every mod this server needs.";
}

/// <summary>
/// The mods a server needs that this PC does not have turned on. The list Connect sends to Coop's join
/// check is the setup's list, which is the host's list when the player picked the host's setup or
/// imported the host's mod list. Coop compares it with the server's mods by id and version, and its
/// reply names only the first difference, so the setup is the full list the launcher can know before
/// the game starts.
/// </summary>
public static class ServerModNeeds
{
    public static string WorkshopPage(string workshopId) => "https://steamcommunity.com/sharedfiles/filedetails/?id=" + workshopId.Trim();

    /// <summary>The Nexus Mods page of a mod players get by hand (MCM since its Workshop item was removed), or null.</summary>
    public static string? NexusPage(string moduleId) =>
        NexusModCatalog.Mods.FirstOrDefault(mod => mod.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase)) is { } nexus
            ? "https://www.nexusmods.com/" + NexusModCatalog.GameDomain + "/mods/" + nexus.ModId
            : null;

    /// <summary>Coop's GitHub release for a Coop with another version than the server's: Steam has only one Coop
    /// version, and Coop 0.1.7 came out on GitHub first. Null for every other mod.</summary>
    public static string? CoopPage(string moduleId) =>
        BannerlordCoopModules.IsEither(moduleId) ? CoopInstall.ReleaseUrl : null;

    /// <summary>
    /// Coodsz Coop and Friends: the setup's enabled mods that are missing or out of date.
    /// <paramref name="byHand"/> says the launcher can not get a missing mod (hidden or removed on the
    /// Workshop, or not on it at all); <paramref name="bundled"/> says it comes with the launcher folder.
    /// A version a locked setup asks for is fixed by hand, since Steam only has the newest one.
    /// </summary>
    public static IReadOnlyList<ServerModNeed> FromResolution(ModuleResolution resolution, Func<ResolvedModule, bool> byHand, Func<string, bool> bundled)
    {
        var needs = new List<ServerModNeed>();
        foreach (var module in resolution.Modules)
        {
            if (!module.Enabled || module.IsBaseModule)
                continue;
            var name = string.IsNullOrWhiteSpace(module.Name) ? module.ModuleId : module.Name.Trim();
            var workshop = SharedModList.IsWorkshopId(module.WorkshopId);
            switch (module.State)
            {
                case ModuleState.Missing:
                    if (byHand(module))
                        needs.Add(new ServerModNeed(module.ModuleId, name, ServerModProblem.Missing, ServerModFetch.ByHand,
                            CoopPage(module.ModuleId) ?? NexusPage(module.ModuleId)));
                    else if (bundled(module.ModuleId))
                        needs.Add(new ServerModNeed(module.ModuleId, name, ServerModProblem.Missing, ServerModFetch.Launcher));
                    else if (workshop)
                        needs.Add(new ServerModNeed(module.ModuleId, name, ServerModProblem.Missing, ServerModFetch.Workshop, WorkshopPage(module.WorkshopId!)));
                    else
                        needs.Add(new ServerModNeed(module.ModuleId, name, ServerModProblem.Missing, ServerModFetch.ByHand, NexusPage(module.ModuleId)));
                    break;
                case ModuleState.UpdatePending when byHand(module):
                    needs.Add(new ServerModNeed(module.ModuleId, name, ServerModProblem.WrongVersion, ServerModFetch.ByHand,
                        CoopPage(module.ModuleId) ?? NexusPage(module.ModuleId)));
                    break;
                case ModuleState.UpdatePending when workshop:
                    needs.Add(new ServerModNeed(module.ModuleId, name, ServerModProblem.WrongVersion, ServerModFetch.Workshop, WorkshopPage(module.WorkshopId!)));
                    break;
                case ModuleState.VersionMismatch:
                    needs.Add(new ServerModNeed(module.ModuleId, name, ServerModProblem.WrongVersion, ServerModFetch.ByHand,
                        CoopPage(module.ModuleId) ?? (workshop ? WorkshopPage(module.WorkshopId!) : null)));
                    break;
            }
        }

        return needs;
    }

    /// <summary>
    /// True when every problem that keeps Connect locked is one the prompt deals with, so Connect can
    /// stay on and show it. Anything else (no Coop, a base game module, a wrong module id, extra copies)
    /// keeps Connect locked as before.
    /// </summary>
    public static bool PromptCovers(ModuleResolution resolution) =>
        resolution.IncludesCoop
        && resolution.All.Any(module => module.State != ModuleState.Ready)
        && resolution.All.All(module => module.State == ModuleState.Ready
            || (!module.IsBaseModule && (module.State is ModuleState.Missing or ModuleState.VersionMismatch
                || (module.State == ModuleState.UpdatePending && SharedModList.IsWorkshopId(module.WorkshopId)))));

    /// <summary>
    /// The AD1259 pack: every mod that is missing or the wrong version. A Workshop mod is subscribed
    /// through Steam when it is missing; the others open their page. A Workshop mod with the wrong
    /// version is got by hand too, like VersionMismatch in FromResolution: Steam has only its newest version, so
    /// subscribing again would not change it and Get all missing would go round in a loop.
    /// </summary>
    public static IReadOnlyList<ServerModNeed> FromAD1259(IEnumerable<AD1259ModStatus> statuses)
    {
        var needs = new List<ServerModNeed>();
        foreach (var status in statuses)
        {
            if (status.State is not (AD1259ModState.Missing or AD1259ModState.WrongVersion))
                continue;
            // The Coop nightly is never fetched: its row already says to move it out or get the Workshop Coop again. A lone
            // CoopNightly with Coop missing is Missing, so the Workshop subscribe stays.
            if (status.Nightly && status.State == AD1259ModState.WrongVersion)
                continue;
            var mod = status.Mod;
            var problem = status.State == AD1259ModState.Missing ? ServerModProblem.Missing : ServerModProblem.WrongVersion;
            var fetch = problem == ServerModProblem.Missing && !mod.BaseGame && SharedModList.IsWorkshopId(mod.WorkshopId)
                ? ServerModFetch.Workshop
                : ServerModFetch.ByHand;
            needs.Add(new ServerModNeed(mod.Id, mod.Name, problem, fetch, problem == ServerModProblem.WrongVersion ? CoopPage(mod.Id) ?? mod.Link : mod.Link));
        }

        return needs;
    }
}
