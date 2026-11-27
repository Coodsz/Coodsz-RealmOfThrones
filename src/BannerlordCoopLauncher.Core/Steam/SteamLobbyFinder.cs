using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace BannerlordCoopLauncher.Core.Steam;

public readonly record struct SteamLobby(ulong Id, string Owner, int Members = 0);

public static class SteamLobbyFinder
{
    public const string NoneFound = "No Steam lobby is advertising that name. The host server has to be running with Steam lobby on, and the name has to match the host's Steam name.";
    public const string MoreThanOne = "More than one Steam lobby matches that name. Type the host's full Steam name, or use Join Coop Sandbox in the game and pick the lobby.";
    public const string GaveUp = "No Steam lobby is advertising that name. The host server has to be running with Steam lobby on, and the name has to match the host's Steam name. Wait until the host server says Server online, then try again.";

    // The host's lobby shows up a few seconds after its server says SERVING, so a search
    // right after a start finds nothing yet. Keep looking this long before giving up.
    public static readonly TimeSpan RetryFor = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(4);

    /// <summary>Runs <see cref="Find"/> on a worker thread until the lobby is found, <see cref="RetryFor"/>
    /// has passed, or the token is cancelled. <paramref name="status"/> runs on the caller's thread.</summary>
    public static Task<ulong> FindWithRetryAsync(string gameRoot, string ownerName, Action<string> status, CancellationToken cancellationToken) =>
        RetryAsync(() => Task.Run(() => Find(gameRoot, ownerName), cancellationToken).WaitAsync(cancellationToken),ownerName.Trim(), status, RetryFor, RetryEvery, cancellationToken);

    /// <summary>Tries again only on "no lobby"; other errors (Steam closed, two matches) stop at once.</summary>
    public static async Task<ulong> RetryAsync(Func<Task<ulong>> attempt, string ownerName, Action<string> status, TimeSpan retryFor, TimeSpan retryEvery, CancellationToken cancellationToken)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        status("Looking for the Steam lobby for " + ownerName + ".");
        for (var tries = 1; ; tries++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await attempt();
            }
            catch (InvalidOperationException ex) when (ex.Message == NoneFound)
            {
                if (clock.Elapsed + retryEvery > retryFor)
                    throw new InvalidOperationException(GaveUp);
            }

            status(tries == 1
                ? "No Steam lobby for " + ownerName + " yet. The host server may still be starting. Trying again for up to " + (int)retryFor.TotalSeconds + " seconds."
                : "Still looking for the Steam lobby for " + ownerName + ".");
            await Task.Delay(retryEvery, cancellationToken);
        }
    }

    public static ulong? Select(IReadOnlyList<SteamLobby> lobbies, string search)
    {
        var text = search.Trim();
        if (text.Length == 0)
            return null;

        var exact = lobbies.Where(lobby => lobby.Owner.Equals(text, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count == 1)
            return exact[0].Id;
        if (exact.Count > 1)
            return exact.OrderByDescending(lobby => lobby.Members).ThenByDescending(lobby => lobby.Id).First().Id;

        var partial = lobbies.Where(lobby => lobby.Owner.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        if (partial.Count == 1)
            return partial[0].Id;
        if (partial.Count > 1)
            throw new InvalidOperationException(MoreThanOne);
        return null;
    }

    public static ulong Find(string gameRoot, string ownerName)
    {
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("Steam lobby search only runs on Windows.");
        var name = ownerName.Trim();
        var bin = Path.Combine(gameRoot, "bin", "Win64_Shipping_Client");
        var steamApi = Path.Combine(bin, "steam_api64.dll");
        var steamworks = Path.Combine(bin, "Steamworks.NET.dll");
        if (!File.Exists(steamApi) || !File.Exists(steamworks))
            throw new InvalidOperationException("Bannerlord's Steam files were not found next to the game.");

        lock (Gate)
        {
            var previous = Environment.CurrentDirectory;
            var appFolder = Path.Combine(Path.GetTempPath(), "coodsz-steam-appid");
            Directory.CreateDirectory(appFolder);
            File.WriteAllText(Path.Combine(appFolder, "steam_appid.txt"), "261550\n");
            var started = false;
            try
            {
                Environment.CurrentDirectory = appFolder;
                Environment.SetEnvironmentVariable("SteamAppId", "261550");
                if (LoadLibrary(steamApi) == IntPtr.Zero)
                    throw new InvalidOperationException("Bannerlord's Steam files were not found next to the game.");

                var assembly = Assembly.LoadFrom(steamworks);
                var api = Require(assembly, "Steamworks.SteamAPI");
                var matchmaking = Require(assembly, "Steamworks.SteamMatchmaking");
                started = Invoke(api, "Init") as bool? == true;
                if (!started)
                    throw new InvalidOperationException("Steam did not start. Open Steam, then try again.");
                for (var i = 0; i < 5; i++)
                {
                    Invoke(api, "RunCallbacks");
                    Thread.Sleep(20);
                }

                var exact = Query(api, matchmaking, name);
                var chosen = Select(exact, name);
                if (chosen == null && exact.Count == 1 && string.IsNullOrWhiteSpace(exact[0].Owner))
                    chosen = exact[0].Id;
                if (chosen == null)
                    chosen = Select(Query(api, matchmaking, ownerFilter: null), name);
                if (chosen == null)
                    throw new InvalidOperationException(NoneFound);
                return chosen.Value;
            }
            finally
            {
                if (started)
                {
                    try { Invoke(Require(Assembly.LoadFrom(steamworks), "Steamworks.SteamAPI"), "Shutdown"); }
                    catch (Exception) { }
                }
                Environment.CurrentDirectory = previous;
            }
        }
    }

    private static readonly object Gate = new();

    private static List<SteamLobby> Query(Type api, Type matchmaking, string? ownerFilter)
    {
        var distance = Enum.Parse(matchmaking.Assembly.GetType("Steamworks.ELobbyDistanceFilter")!, "k_ELobbyDistanceFilterWorldwide");
        var equal = Enum.ToObject(matchmaking.Assembly.GetType("Steamworks.ELobbyComparison")!, 0);
        Invoke(matchmaking, "AddRequestLobbyListDistanceFilter", distance);
        Invoke(matchmaking, "AddRequestLobbyListStringFilter", "coop_lobby_type", "standalone", equal);
        if (!string.IsNullOrWhiteSpace(ownerFilter))
            Invoke(matchmaking, "AddRequestLobbyListStringFilter", "coop_owner_name", ownerFilter.Trim(), equal);
        Invoke(matchmaking, "AddRequestLobbyListResultCountFilter", 50);
        var call = Invoke(matchmaking, "RequestLobbyList")
            ?? throw new InvalidOperationException("Steam did not answer the lobby search. Open Steam, then try again.");

        LobbyListSlot.Done = false;
        LobbyListSlot.Failed = false;
        LobbyListSlot.Result = null;
        var lobbyType = Require(matchmaking.Assembly, "Steamworks.LobbyMatchList_t");
        var callResultOpen = Require(matchmaking.Assembly, "Steamworks.CallResult`1");
        var callResult = callResultOpen.MakeGenericType(lobbyType);
        var callback = callResultOpen.GetNestedType("APIDispatchDelegate")!.MakeGenericType(lobbyType);
        var method = new DynamicMethod("OnLobbyList", typeof(void), [lobbyType, typeof(bool)], typeof(LobbyListSlot).Module, skipVisibility: true);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Box, lobbyType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(LobbyListSlot).GetMethod(nameof(LobbyListSlot.On))!);
        il.Emit(OpCodes.Ret);
        var handler = method.CreateDelegate(callback);
        var result = callResult.GetMethod("Create", [callback])!.Invoke(null, [handler]);
        var callType = Require(matchmaking.Assembly, "Steamworks.SteamAPICall_t");
        callResult.GetMethod("Set", [callType, callback])!.Invoke(result, [call, handler]);

        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!LobbyListSlot.Done && DateTime.UtcNow < deadline)
        {
            Invoke(api, "RunCallbacks");
            Thread.Sleep(50);
        }

        if (!LobbyListSlot.Done || LobbyListSlot.Failed || LobbyListSlot.Result == null)
            throw new InvalidOperationException("Steam did not answer the lobby search. Open Steam, then try again.");

        var count = (uint)LobbyListSlot.Result.GetType().GetField("m_nLobbiesMatching")!.GetValue(LobbyListSlot.Result)!;
        var lobbies = new List<SteamLobby>();
        var byIndex = matchmaking.GetMethod("GetLobbyByIndex")!;
        var data = matchmaking.GetMethod("GetLobbyData")!;
        for (var i = 0; i < count; i++)
        {
            var id = byIndex.Invoke(null, [i]);
            if (id == null)
                continue;
            var steamId = (ulong)id.GetType().GetField("m_SteamID")!.GetValue(id)!;
            var owner = data.Invoke(null, [id, "coop_owner_name"]) as string ?? "";
            var members = 0;
            var memberCount = matchmaking.GetMethod("GetNumLobbyMembers");
            if (memberCount != null)
                members = memberCount.Invoke(null, [id]) as int? ?? 0;
            lobbies.Add(new SteamLobby(steamId, owner, members));
        }

        return lobbies;
    }

    private static class LobbyListSlot
    {
        public static object? Result;
        public static bool Done;
        public static bool Failed;

        public static void On(object value, bool failed)
        {
            Result = value;
            Failed = failed;
            Done = true;
        }
    }

    private static Type Require(Assembly assembly, string name) =>
        assembly.GetType(name) ?? throw new InvalidOperationException("Bannerlord's Steam files were not found next to the game.");

    private static object? Invoke(Type type, string name, params object?[] args)
    {
        var method = type.GetMethod(name) ?? throw new InvalidOperationException("Bannerlord's Steam files were not found next to the game.");
        try
        {
            return method.Invoke(null, args);
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException ?? ex;
        }
    }

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string path);
}
