using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Saves;
using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;
using Microsoft.Win32;
using RealmOfThrones.Core;
using TaomCoop.Core;

namespace RealmOfThronesHost;

public partial class MainWindow
{
    private void RefreshPlayers_Click(object sender, RoutedEventArgs e) => RefreshState();

    private void KickPlayer_Click(object sender, RoutedEventArgs e)
    {
        if (PlayerBox.SelectedItem is not string label || label.Length == 0)
        {
            SetControl("Select a player first.", failed: true);
            return;
        }

        var player = SelectedOnlinePlayer();
        if (player == null)
        {
            SetControl("Select a player first.", failed: true);
            return;
        }

        SendQueued("kick " + player.Id, StillConnected(player));
    }

    private void BanPlayer_Click(object sender, RoutedEventArgs e)
    {
        var player = SelectedOnlinePlayer();
        if (player == null)
        {
            SetControl("Select an online player first.", failed: true);
            return;
        }

        var banned = ReadBannedPlayers();
        if (!PlayerBanList.IsBanned(banned, player.Name))
            banned.Add(player.Name);
        WriteBannedPlayers(banned);
        ShowBannedPlayers();
        NoteLog("Banned " + player.Name + ".");
        SendQueued("kick " + player.Id, StillConnected(player));
    }

    private void UnbanPlayer_Click(object sender, RoutedEventArgs e)
    {
        if (BanBox.SelectedItem is not string name || name.Length == 0)
        {
            SetControl("Select a banned player first.", failed: true);
            return;
        }

        var banned = ReadBannedPlayers();
        banned.RemoveAll(item => item.Equals(name, StringComparison.OrdinalIgnoreCase));
        WriteBannedPlayers(banned);
        ShowBannedPlayers();
        NoteLog("Unbanned " + name + ".");
    }

    // A kick goes out at its turn only while this player is still on the server with that id, so a queued kick never
    // hits whoever got the id after them.
    private Func<bool> StillConnected(HostPlayer player) =>
        () => HostPlayerLog.StillOn(_onlinePlayers, player);

    private HostPlayer? SelectedOnlinePlayer()
    {
        if (PlayerBox.SelectedItem is not string label)
            return null;
        return _onlinePlayers.FirstOrDefault(player => OnlineLabel(player) == label);
    }

    private static string OnlineLabel(HostPlayer player) => player.Id + "  " + player.Name + "  " + player.State;

    private string BanPath() => Path.Combine(_settings.DataRoot, "rot-bans.json");

    private List<string> ReadBannedPlayers()
    {
        var path = BanPath();
        if (!File.Exists(path))
            return [];
        return PlayerBanList.Read(File.ReadAllText(path));
    }

    private void WriteBannedPlayers(List<string> names)
    {
        var path = BanPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, PlayerBanList.Write(names));
    }

    private void ShowBannedPlayers()
    {
        var banned = ReadBannedPlayers();
        var selected = BanBox.SelectedItem as string;
        BanBox.Items.Clear();
        foreach (var name in banned)
            BanBox.Items.Add(name);
        BanStatus.Text = banned.Count == 0
            ? "No banned players."
            : banned.Count == 1 ? "1 banned player." : banned.Count + " banned players.";
        if (selected != null && BanBox.Items.Contains(selected))
            BanBox.SelectedItem = selected;
    }

    private void KickBannedPlayers()
    {
        if (!CommandPipeReady())
            return;

        var banned = ReadBannedPlayers();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var player in _onlinePlayers)
        {
            if (!PlayerBanList.IsBanned(banned, player.Name))
                continue;
            var key = player.Id + "\n" + player.Name;
            seen.Add(key);
            // Kick again every 30 seconds while the banned player is still listed,
            // in case the server did not pick up the last kick.
            if (_banKicks.TryGetValue(key, out var last) && DateTime.UtcNow - last < TimeSpan.FromSeconds(30))
                continue;
            _banKicks[key] = DateTime.UtcNow;
            SendQueued("kick " + player.Id, StillConnected(player));
        }

        foreach (var gone in _banKicks.Keys.Where(key => !seen.Contains(key)).ToList())
            _banKicks.Remove(gone);
    }

    // A run started or ended: its players, its cached players event and its ban kicks go, so nothing goes by id to another run.
    private void ForgetPlayers()
    {
        _onlinePlayers = [];
        _banKicks.Clear();
        _playerSourceText = "";
        _playerSourcePath = "";
        _playerSourceLength = -1;
    }

    private void LoadPlayerList()
    {
        // A players event that does not read keeps the list this window already has.
        var players = HostPlayerLog.Read(ReadPlayerLog(), _onlinePlayers);
        _onlinePlayers = players.ToList();
        var labels = _onlinePlayers.Select(OnlineLabel).ToList();
        var signature = string.Join("\n", labels);
        if (!signature.Equals(_playerListSignature, StringComparison.Ordinal))
        {
            _playerListSignature = signature;
            var selected = PlayerBox.SelectedItem as string;
            PlayerBox.Items.Clear();
            foreach (var label in labels)
                PlayerBox.Items.Add(label);
            if (selected != null && PlayerBox.Items.Contains(selected))
                PlayerBox.SelectedItem = selected;
        }

        KickBannedPlayers();
        NoteSharedSteamIds(players);
        OnlinePlayersButton.Text = "Online players: " + players.Count;
        if (CoopCheats.Visibility == Visibility.Visible)
        {
            var names = players.Select(player => player.Name).ToList();
            CoopCheats.PlayersChanged(names);
        }
    }

    // Two connections with one hero can be two non Steam copies with one fake Steam ID.
    // live-players.json says whether one Steam ID is behind it; only then is a line written.
    private void NoteSharedSteamIds(IReadOnlyList<HostPlayer> players)
    {
        var connections = players.Select(player => new ConnectedPlayer(player.Id, player.Name, player.State, player.Address)).ToList();
        if (!SharedSteamIdWatch.HasSharedHero(connections) || !ServerIsRunning())
            return;
        var roster = SharedSteamIdWatch.ReadRosterFile(CommandQueueRoots(), _runStartedUtc);
        foreach (var line in _sharedSteamIds.Check(connections, roster))
            NoteLog(line);
    }

    private IReadOnlyList<string> CurrentPlayerNames()
    {
        return HostPlayerLog.Read(ReadPlayerLog()).Select(player => player.Name).ToList();
    }
}
