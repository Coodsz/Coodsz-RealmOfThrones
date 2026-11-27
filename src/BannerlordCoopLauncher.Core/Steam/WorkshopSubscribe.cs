using System.Runtime.InteropServices;

namespace BannerlordCoopLauncher.Core.Steam;

// What the subscribe loop needs from Steam. The real one talks to Steam through
// the game's own steam_api64.dll; tests use a fake.
public interface IWorkshopApi
{
    // False when Steam refused to start the subscribe call.
    bool Subscribe(ulong itemId);
    void Download(ulong itemId);
    WorkshopItemState State(ulong itemId);
    (ulong Downloaded, ulong Total) Progress(ulong itemId);
    void Pump();
}

[Flags]
public enum WorkshopItemState : uint
{
    None = 0,
    Subscribed = 1,
    LegacyItem = 2,
    Installed = 4,
    NeedsUpdate = 8,
    Downloading = 16,
    DownloadPending = 32
}

// Ready: installed and current. StillDownloading: subscribed, Steam is still
// downloading. Failed: Steam never subscribed (hidden, removed or refused item).
// AlreadyCurrent: part of Ready. Steam called these installed and current from
// the start and downloaded nothing, even when asked to look for an update.
public sealed record WorkshopSubscribeResult(IReadOnlyList<ulong> Ready, IReadOnlyList<ulong> StillDownloading, IReadOnlyList<ulong> Failed, IReadOnlyList<ulong>? AlreadyCurrent = null)
{
    public IReadOnlyList<ulong> NotReady => StillDownloading.Concat(Failed).ToList();

    public IReadOnlyList<ulong> NothingDownloaded => AlreadyCurrent ?? Array.Empty<ulong>();
}

public static class WorkshopSubscribe
{
    public static bool IsReady(WorkshopItemState state) =>
        state.HasFlag(WorkshopItemState.Subscribed)
        && state.HasFlag(WorkshopItemState.Installed)
        && !state.HasFlag(WorkshopItemState.NeedsUpdate)
        && !state.HasFlag(WorkshopItemState.Downloading)
        && !state.HasFlag(WorkshopItemState.DownloadPending);

    private static bool HasActivity(WorkshopItemState state) =>
        state.HasFlag(WorkshopItemState.NeedsUpdate)
        || state.HasFlag(WorkshopItemState.Downloading)
        || state.HasFlag(WorkshopItemState.DownloadPending);

    // Subscribes to every item that is not subscribed, asks Steam to download the
    // ones that are missing or out of date, and waits until they are installed.
    public static WorkshopSubscribeResult Run(
        IWorkshopApi api,
        IReadOnlyList<ulong> itemIds,
        IReadOnlyDictionary<ulong, string> names,
        Action<string> log,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        TimeSpan? poll = null,
        TimeSpan? subscribeWait = null,
        TimeSpan? settle = null)
    {
        string Name(ulong id) => names.TryGetValue(id, out var name) ? name + " (" + id + ")" : id.ToString();
        var failed = new List<ulong>();
        var readyAtStart = new HashSet<ulong>();
        foreach (var id in itemIds)
        {
            var state = api.State(id);
            if (!state.HasFlag(WorkshopItemState.Subscribed))
            {
                if (!api.Subscribe(id))
                {
                    failed.Add(id);
                    log("Steam refused to subscribe to " + Name(id) + ".");
                    continue;
                }

                log("Asked Steam to subscribe to " + Name(id) + ".");
            }

            if (IsReady(state))
                readyAtStart.Add(id);
            // Always ask, even when Steam calls the item current. The launcher only
            // asks for items it found missing or older than the Workshop page, and
            // this is what makes Steam look for an update it has not noticed yet.
            api.Download(id);
        }

        var started = DateTime.UtcNow;
        var deadline = started + timeout;
        // A hidden, friends-only or removed item never becomes subscribed. Stop
        // waiting for those after a short while instead of the whole timeout.
        var subscribeDeadline = started + (subscribeWait ?? TimeSpan.FromSeconds(30));
        // An item Steam called current gets a few seconds to start an update
        // before it counts as ready.
        var settleUntil = started + (settle ?? TimeSpan.FromSeconds(10));
        var active = new HashSet<ulong>();
        var lastPercent = new Dictionary<ulong, int>();
        var interval = poll ?? TimeSpan.FromMilliseconds(500);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            api.Pump();
            var now = DateTime.UtcNow;
            foreach (var id in itemIds.Except(failed).ToList())
            {
                var state = api.State(id);
                if (HasActivity(state))
                    active.Add(id);
                if (now >= subscribeDeadline && !state.HasFlag(WorkshopItemState.Subscribed))
                {
                    failed.Add(id);
                    log("Steam did not subscribe to " + Name(id) + ".");
                }
            }

            var waiting = itemIds.Except(failed).Where(id => !IsReady(api.State(id))).ToList();
            var settling = now < settleUntil
                && itemIds.Except(failed).Any(id => readyAtStart.Contains(id) && !active.Contains(id));
            if ((waiting.Count == 0 && !settling) || now >= deadline)
            {
                var ready = itemIds.Except(failed).Except(waiting).ToList();
                var current = ready.Where(id => readyAtStart.Contains(id) && !active.Contains(id)).ToList();
                return new WorkshopSubscribeResult(ready, waiting, failed, current);
            }

            foreach (var id in waiting)
            {
                var (downloaded, total) = api.Progress(id);
                if (total == 0)
                    continue;
                var percent = (int)(downloaded * 100 / total);
                var step = percent / 25 * 25;
                if (lastPercent.TryGetValue(id, out var last) && last >= step)
                    continue;
                lastPercent[id] = step;
                log("Downloading " + Name(id) + ": " + percent + "%.");
            }

            Thread.Sleep(interval);
        }
    }
}

// The library is never unloaded: Steam keeps threads in it after shutdown.
// Steam's flat API from the game's steam_api64.dll, signed in as the Steam user
// for Bannerlord (app 261550). Steam shows Bannerlord as running while this is
// open, so it is closed again right after the downloads finish.
public sealed class SteamApiWorkshop : IWorkshopApi, IDisposable
{
    public const string BannerlordAppId = "261550";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool InitFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void VoidFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetUgcFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong SubscribeFn(IntPtr ugc, ulong itemId);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint StateFn(IntPtr ugc, ulong itemId);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool DownloadFn(IntPtr ugc, ulong itemId, [MarshalAs(UnmanagedType.I1)] bool highPriority);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool DownloadInfoFn(IntPtr ugc, ulong itemId, out ulong downloaded, out ulong total);

    private readonly IntPtr _ugc;
    private readonly VoidFn _shutdown;
    private readonly VoidFn _runCallbacks;
    private readonly SubscribeFn _subscribe;
    private readonly StateFn _state;
    private readonly DownloadFn _download;
    private readonly DownloadInfoFn _downloadInfo;
    private bool _disposed;

    private SteamApiWorkshop(IntPtr ugc, VoidFn shutdown, VoidFn runCallbacks, SubscribeFn subscribe, StateFn state, DownloadFn download, DownloadInfoFn downloadInfo)
    {
        _ugc = ugc;
        _shutdown = shutdown;
        _runCallbacks = runCallbacks;
        _subscribe = subscribe;
        _state = state;
        _download = download;
        _downloadInfo = downloadInfo;
    }

    public static string SteamApiPath(string gameRoot) =>
        Path.Combine(gameRoot, "bin", "Win64_Shipping_Client", "steam_api64.dll");

    // Returns null with a reason when Steam cannot be used, so the caller can
    // fall back to opening the Workshop pages.
    public static SteamApiWorkshop? TryOpen(string gameRoot, out string reason)
    {
        reason = "";
        if (!OperatingSystem.IsWindows())
        {
            reason = "Steam subscribing only works on Windows.";
            return null;
        }

        var dll = SteamApiPath(gameRoot);
        if (!File.Exists(dll))
        {
            reason = "steam_api64.dll was not found in the game folder.";
            return null;
        }

        IntPtr library;
        try
        {
            library = NativeLibrary.Load(dll);
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
        {
            reason = "Could not load the game's steam_api64.dll. " + ex.Message;
            return null;
        }

        T Fn<T>(string name) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

        var getUgcName = new[] { "SteamAPI_SteamUGC_v018", "SteamAPI_SteamUGC_v017", "SteamAPI_SteamUGC_v016" }
            .FirstOrDefault(name => NativeLibrary.TryGetExport(library, name, out _));
        if (getUgcName == null)
        {
            reason = "The game's steam_api64.dll is too old for Workshop subscribing.";
            return null;
        }

        // Every function is looked up before Steam starts, so a missing one can
        // never leave Steam showing Bannerlord as running.
        InitFn init;
        GetUgcFn getUgc;
        VoidFn shutdown, runCallbacks;
        SubscribeFn subscribe;
        StateFn state;
        DownloadFn download;
        DownloadInfoFn downloadInfo;
        try
        {
            init = Fn<InitFn>("SteamAPI_Init");
            getUgc = Fn<GetUgcFn>(getUgcName);
            shutdown = Fn<VoidFn>("SteamAPI_Shutdown");
            runCallbacks = Fn<VoidFn>("SteamAPI_RunCallbacks");
            subscribe = Fn<SubscribeFn>("SteamAPI_ISteamUGC_SubscribeItem");
            state = Fn<StateFn>("SteamAPI_ISteamUGC_GetItemState");
            download = Fn<DownloadFn>("SteamAPI_ISteamUGC_DownloadItem");
            downloadInfo = Fn<DownloadInfoFn>("SteamAPI_ISteamUGC_GetItemDownloadInfo");
        }
        catch (EntryPointNotFoundException ex)
        {
            reason = "The game's steam_api64.dll is missing " + ex.Message;
            return null;
        }

        Environment.SetEnvironmentVariable("SteamAppId", BannerlordAppId);
        Environment.SetEnvironmentVariable("SteamGameId", BannerlordAppId);
        if (!init())
        {
            ClearAppId();
            reason = "Steam did not answer. Open Steam and sign in with the account that owns Bannerlord.";
            return null;
        }

        var ugc = getUgc();
        if (ugc == IntPtr.Zero)
        {
            shutdown();
            ClearAppId();
            reason = "Steam did not open its Workshop service.";
            return null;
        }

        return new SteamApiWorkshop(ugc, shutdown, runCallbacks, subscribe, state, download, downloadInfo);
    }

    // SubscribeItem returns 0 (k_uAPICallInvalid) when Steam does not start the call.
    public bool Subscribe(ulong itemId) => _subscribe(_ugc, itemId) != 0;

    public void Download(ulong itemId) => _download(_ugc, itemId, true);

    public WorkshopItemState State(ulong itemId) => (WorkshopItemState)_state(_ugc, itemId);

    public (ulong Downloaded, ulong Total) Progress(ulong itemId) =>
        _downloadInfo(_ugc, itemId, out var downloaded, out var total) ? (downloaded, total) : (0, 0);

    public void Pump() => _runCallbacks();

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _shutdown();
        ClearAppId();
    }

    private static void ClearAppId()
    {
        Environment.SetEnvironmentVariable("SteamAppId", null);
        Environment.SetEnvironmentVariable("SteamGameId", null);
    }
}
