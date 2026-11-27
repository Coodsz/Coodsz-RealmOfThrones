using System.IO;
using System.Windows.Controls;
using BannerlordCoopLauncher.Core.Configuration;
using BannerlordCoopLauncher.Core.Server;
using RealmOfThrones.Core;
using TaomCoop.Core;

namespace RealmOfThronesHost;

// Server rules on the Settings page, in the collapsed Advanced: server rules fold, with the Coop Server Tool's
// controls and defaults (CoodszHost\MainWindow.ServerRules.cs): pause on join and the offline XP freeze on, unstuck
// allowed, and the kingdom protection following Coop's own option until the host picks one. Save settings writes
// them to coodsz-server-tools.json in the server's data folder (RotServerTools.RulesPath), where Coodsz Server Tools
// reads the unstuck rule, the XP freeze and the Always protection within a few seconds. Pause on join runs here, from
// the players events of the server log. Once the host picked a kingdom choice, Save settings and every Start write
// Coop's modOptions.blockAiWarDeclarationsOnOfflinePlayers in mod-config.json to match (on unless the choice is Off).
public partial class MainWindow
{
    private readonly JoinPause _joinPause = new();
    private bool _pauseOnJoin = HostServerRules.Defaults.PauseOnJoin;
    // Pause and resume go out one at a time and in order: a resume never overtakes its pause.
    private readonly SemaphoreSlim _joinPauseGate = new(1, 1);

    private string ServerRulesPath => RotServerTools.RulesPath(_settings.DataRoot);

    // Called once from the constructor, after the data folder is known.
    private void StartServerRules()
    {
        foreach (var label in ServerRulesFile.UnstuckLabels)
            UnstuckBox.Items.Add(new ComboBoxItem { Content = label });
        foreach (var label in ServerRulesFile.KingdomProtectionLabels)
            KingdomProtectionBox.Items.Add(new ComboBoxItem { Content = label });
        ShowOnOff(PauseOnJoinBox);
        ShowOnOff(FreezeOfflineXpBox);
        LoadServerRules();
    }

    private void LoadServerRules()
    {
        var rules = ServerRulesFile.Read(ServerRulesPath);
        UnstuckBox.SelectedIndex = (int)rules.Unstuck;
        PauseOnJoinBox.IsChecked = rules.PauseOnJoin;
        FreezeOfflineXpBox.IsChecked = rules.FreezeOfflineXp;
        _pauseOnJoin = rules.PauseOnJoin;
        ShowKingdomProtection();
    }

    /// <summary>
    /// The kingdom dropdown: the host's pick, or with none, Coop's option as mod-config.json has it now. Run on every
    /// Settings page fill, after Prepare and Start and after a server folder change, so it never shows a stale value.
    /// </summary>
    private void ShowKingdomProtection()
    {
        if (KingdomProtectionBox.Items.Count == 0)
            return;
        KingdomProtectionBox.SelectedIndex = (int)ServerRulesFile.ShownKingdomChoice(ServerRulesFile.Read(ServerRulesPath).KingdomProtection, CoopOfflineWarOption());
    }

    private bool CoopOfflineWarOption()
    {
        try
        {
            var store = new ModConfigStore(ModConfigPath);
            return store.Exists && ServerRulesFile.CoopOfflineWarIn(store.Read(RotStack.CoopVersion));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private KingdomProtectionChoice KingdomProtectionFromPage() =>
        (KingdomProtectionChoice)Math.Max(0, KingdomProtectionBox.SelectedIndex);

    private HostServerRules ServerRulesFromPage() =>
        new((UnstuckChoice)Math.Max(0, UnstuckBox.SelectedIndex), PauseOnJoinBox.IsChecked == true, FreezeOfflineXpBox.IsChecked == true,
            ServerRulesFile.KingdomChoiceToSave(KingdomProtectionFromPage(), ServerRulesFile.Read(ServerRulesPath).KingdomProtection, CoopOfflineWarOption()));

    /// <summary>
    /// From Save settings, on the mod-config.json text being saved: once the host picked a kingdom choice (the dropdown
    /// differs from what Coop's option in this very text gives, or a pick is kept), Coop's option follows it. With no pick
    /// the text is left as it is, so nothing is written from a value read earlier.
    /// </summary>
    private string SaveKingdomProtection(string json)
    {
        var current = ServerRulesFile.CoopOfflineWarIn(ModConfigStore.ReadText(json, RotStack.CoopVersion));
        if (ServerRulesFile.KingdomChoiceToSave(KingdomProtectionFromPage(), ServerRulesFile.Read(ServerRulesPath).KingdomProtection, current) is not { } picked)
            return json;
        return ModConfigStore.WriteText(json, new Dictionary<string, object> { [ServerRulesFile.CoopOfflineWarOption] = ServerRulesFile.CoopOfflineWarValue(picked) },
            out _, RotStack.CoopVersion);
    }

    private void SaveServerRules(HostServerRules rules)
    {
        var before = ServerRulesFile.Read(ServerRulesPath);
        _pauseOnJoin = rules.PauseOnJoin;
        if (rules == before && File.Exists(ServerRulesPath) && File.ReadAllText(ServerRulesPath).Contains(ServerRulesFile.UnstuckKey, StringComparison.Ordinal))
            return;
        ServerRulesFile.Write(ServerRulesPath, rules);
        if (rules.Unstuck != before.Unstuck)
            NoteLog(rules.Unstuck switch
            {
                UnstuckChoice.NotInFights => "Unstuck for players: not in fights, as a prisoner or near enemies, and once every 5 minutes. The server uses it within a few seconds.",
                UnstuckChoice.HostOnly => "Unstuck for players: host only. Players can't use coop.unstuck; you still can from the console. The server uses it within a few seconds.",
                _ => "Unstuck for players: allowed, like Coop on its own.",
            });
        if (rules.PauseOnJoin != before.PauseOnJoin)
            NoteLog(rules.PauseOnJoin ? "Pause on join is on." : "Pause on join is off.");
        if (rules.FreezeOfflineXp != before.FreezeOfflineXp)
            NoteLog(rules.FreezeOfflineXp ? "Offline XP freeze is on." : "Offline XP freeze is off.");
        if (rules.KingdomProtection != null && rules.KingdomProtection != before.KingdomProtection)
            NoteLog(rules.KingdomProtection switch
            {
                KingdomProtectionChoice.Always => "Player kingdoms are protected from AI wars always, online or offline. The server uses it within a few seconds.",
                KingdomProtectionChoice.Off => "Player kingdoms are not protected from AI wars. Restart the server so Coop uses it.",
                _ => "Player kingdoms are protected from AI wars only while their players are offline, with Coop's own protection. Restart the server so Coop uses it.",
            });
    }

    /// <summary>
    /// Right before Start, after mod-config.json is written: once the host picked a kingdom choice, Coop's offline war
    /// option follows it. With no pick it is left alone.
    /// </summary>
    private void ApplyKingdomProtection()
    {
        if (ServerRulesFile.Read(ServerRulesPath).KingdomProtection is not { } picked)
            return;
        try
        {
            var store = new ModConfigStore(ModConfigPath);
            if (!store.Exists)
                return;
            var on = ServerRulesFile.CoopOfflineWarValue(picked);
            if (store.Write(new Dictionary<string, object> { [ServerRulesFile.CoopOfflineWarOption] = on }, RotStack.CoopVersion) > 0)
                NoteStart(on
                    ? "Turned on Coop's protection for player kingdoms while their players are offline, as chosen in Advanced: server rules."
                    : "Turned off Coop's protection for player kingdoms while their players are offline, as chosen in Advanced: server rules.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            NoteStart("Could not set Coop's protection for player kingdoms in mod-config.json. " + ex.Message);
        }
    }

    // ---- pause on join -----------------------------------------------------------------------

    // Every refresh while the tool runs: the players of the server log and whether it serves. A server that stopped took
    // its pause with it; the next start is a new session.
    private void NoteJoinPause(IReadOnlyList<HostPlayer> players, bool serving)
    {
        var connections = players.Select(player => new ConnectedPlayer(player.Id, player.Name, player.State, player.Address)).ToList();
        DoJoinPause(_joinPause.UpdateServing(connections, DateTime.UtcNow, _pauseOnJoin, serving));
    }

    private async void DoJoinPause(JoinPauseStep step)
    {
        if (step == JoinPauseStep.None)
            return;
        var hostCommands = _joinPause.HostCommandCount;
        await _joinPauseGate.WaitAsync();
        try
        {
            // A time command the host sent while this step waited for the gate is his choice: a resume no longer applies.
            if (step == JoinPauseStep.Resume && _joinPause.HostCommandCount != hostCommands)
                return;
            if (step == JoinPauseStep.Pause)
            {
                var mode = await RunCommandAndRead(JoinPause.GetModeCommand);
                if (!_joinPause.Paused(string.Join("\n", mode), DateTime.UtcNow))
                    return;
                // Every step that changes the speed checks inside the command gate that the host sent no time command since.
                var asked = _joinPause.HostCommandCount;
                var paused = await RunCommandAndRead(JoinPause.PauseCommand, () => _joinPause.HostCommandCount == asked);
                if (paused == null)
                    return;
                if (ServerTime.Result(JoinPause.PauseMode, true, string.Join("\n", paused), false).Failed)
                {
                    _joinPause.PauseFailed();
                    return;
                }

                // The host set a speed while the pause was on its way: his speed goes back on at once.
                var hostSpeed = _joinPause.PauseLanded();
                if (hostSpeed != null)
                {
                    var landed = _joinPause.HostCommandCount;
                    var put = await RunCommandAndRead(hostSpeed, () => _joinPause.HostCommandCount == landed);
                    if (put == null)
                        return;
                    NoteLog(!ServerTime.Result(JoinPause.HostMode(hostSpeed) ?? JoinPause.NormalMode, true, string.Join("\n", put), false).Failed
                        ? "You set the speed while a join pause was going out, so the game runs at " + SpeedName(_joinPause.HostChoice ?? JoinPause.NormalMode) + " as you chose."
                        : "Pause on join could not put your speed back. Press Play on the Dashboard.");
                    return;
                }

                if (!_joinPause.Holding)
                    return;

                // A pause stuck on a full pipe may land a moment later, so the line does not say it already did.
                var joining = string.Join(", ", _joinPause.Waiting.Select(Shown));
                var speed = SpeedName(_joinPause.ResumeMode);
                NoteLog(ServerCommandQueue.Unconfirmed(paused)
                    ? "The server's console is slow, so the game may not be paused yet while " + joining + " joins. It goes back to " + speed + " when they are on the map (at most 3 minutes)."
                    : "Paused the game while " + joining + " joins. It goes back to " + speed + " when they are on the map (at most 3 minutes).");
            }
            else
            {
                var gaveUp = _joinPause.GaveUp;
                // Checked again once the command gate is ours, right before it goes out: a Pause or Play the host sent
                // from the console or the buttons while this waited wins.
                var resumed = await RunCommandAndRead(_joinPause.ResumeCommand, () => _joinPause.HostCommandCount == hostCommands);
                if (resumed == null)
                    return;
                NoteLog(ServerTime.Result(_joinPause.ResumeMode, true, string.Join("\n", resumed), false).Failed
                    ? "Pause on join could not put the speed back. Press Play on the Dashboard."
                    : ServerCommandQueue.Unconfirmed(resumed)
                        ? "The server's console is slow, so the game may not run again yet. If it stays paused, press Play on the Dashboard."
                    : gaveUp
                        ? "A player is still loading after 3 minutes, so the game runs again at " + SpeedName(_joinPause.ResumeMode) + "."
                        : "Everyone is on the map, so the game runs again at " + SpeedName(_joinPause.ResumeMode) + ".");
            }
        }
        catch (Exception ex)
        {
            _joinPause.PauseFailed();
            NoteLog("Pause on join: " + ex.Message);
        }
        finally
        {
            _joinPauseGate.Release();
        }
    }

    // A joining player has no name yet.
    private static string Shown(string key) => key.StartsWith('#') ? "a new player" : key;

    private static string SpeedName(string mode) => mode switch
    {
        JoinPause.FastMode => "2x speed",
        _ => "normal speed",
    };
}
