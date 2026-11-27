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
    private void SeedLogs()
    {
        var file = HostLogFile.Prefer(LogFiles()).FirstOrDefault();
        if (file == null)
            return;
        file.Refresh();
        var text = HostLogFile.ReadTail(file.FullName, 8_000).Trim();
        if (text.Length > 0)
            Coodsz.Ui.UiLanguage.SetLog(LogBox, text);
        _followedLogPath = file.FullName;
        _followedLogLength = file.Length;
    }

    private void FollowServerLog()
    {
        var file = HostLogFile.Prefer(LogFiles()).FirstOrDefault();
        if (file == null)
            return;
        file.Refresh();
        if (!file.FullName.Equals(_followedLogPath, StringComparison.OrdinalIgnoreCase) || file.Length < _followedLogLength)
        {
            _followedLogPath = file.FullName;
            _followedLogLength = Math.Max(0, file.Length - 8_000);
        }

        if (file.Length <= _followedLogLength)
            return;
        if (file.Length - _followedLogLength > 64_000)
            _followedLogLength = file.Length - 64_000;

        string chunk;
        try
        {
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var length = stream.Length;
            stream.Position = _followedLogLength;
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
            chunk = reader.ReadToEnd();
            _followedLogLength = length;
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        chunk = chunk.Trim('\r', '\n');
        if (chunk.Length == 0)
            return;
        var lines = 1;
        foreach (var ch in chunk)
        {
            if (ch == '\n')
                lines++;
        }

        if (lines <= RotSkippedLines.MostShown)
        {
            NoteLog(chunk);
            MirrorServerLines(chunk);
        }
        // A bigger chunk is skipped so the window does not freeze, and the line that says so comes once a minute.
        if (_skippedLines.Note(lines, DateTime.UtcNow) is { } skipped)
            NoteLog(skipped);
    }

    private readonly RotSkippedLines _skippedLines = new();

    // The one ordered path for every command this window writes to the server (ServerCommandQueue): the join pause, the
    // Pause and Play buttons, the console, the Cheats page, Play hold, kicks, the world save and stop. Each keeps its turn
    // until its reply is read, so no two writes or reply reads ever mix.
    private readonly ServerCommandQueue _commands = new();

    // Set by ReadCommandReply when its command never reached the server (no server, a broken pipe). Read in the same turn.
    private bool _commandWriteFailed;

    // Each command keeps the run it was queued for (ReadCommandReply drops it once that run ended).
    private async Task<IReadOnlyList<string>> RunCommandAndRead(string command)
    {
        var run = CurrentRun();
        return (await _commands.Enqueue(async () => (await ReadCommandReply(command, run))!))!;
    }

    // Null when stillValid says no at this command's turn, or again right before the write; then nothing was sent.
    private Task<IReadOnlyList<string>?> RunCommandAndRead(string command, Func<bool> stillValid)
    {
        var run = CurrentRun();
        return _commands.Enqueue(() => ReadCommandReply(command, run, stillValid), stillValid);
    }

    // The run a command is queued for: this window's own server process, or the start time of a server it did not start.
    private ServerRun CurrentRun()
    {
        var own = OwnsServerProcess() ? _process : null;
        if (own != null)
            return new ServerRun(own, null);
        var found = DedicatedServerWatch.TryStarted(_settings.ServerRoot, out var started, out var processId);
        return new ServerRun(null, ServerCommandQueue.ForeignStart(found, started), processId);
    }

    // A command whose answer is not shown (a kick), queued like every other one; its reply is read and dropped.
    private void SendQueued(string command, Func<bool> stillValid) => _ = RunCommandAndRead(command, stillValid);

    private static readonly TimeSpan QueuedWriteWait = TimeSpan.FromSeconds(15);

    /// <summary>
    /// A raw write (the world save, stop and the restart warnings) in its turn of the command queue. When the turn does
    /// not come within 15 seconds, the write goes out at once instead and the queued turn is dropped, so a stuck reply
    /// read never holds up a save or a stop and the line is never written twice.
    /// </summary>
    private Task<bool> WriteQueued(Func<bool> write) => _commands.WriteWithin(write, QueuedWriteWait);

    // A write still stuck on a full pipe when its turn ended (MaybeSent); the next reply read waits for it first.
    private Task<ConsoleWrite>? _stuckWrite;

    // How long a stuck write's own output gets to reach the log before the next reply read starts.
    private static readonly TimeSpan StuckReplySettle = TimeSpan.FromSeconds(1);

    // StandardInput is not thread safe, and WriteQueued's fallback can write while another turn writes. Held around the
    // write itself only, never around a reply read.
    private readonly object _stdinLock = new();

    // run: the server run the command was queued for. When that run ended before this turn, nothing is written, so a
    // command never reaches the next run. stillValid (a kick's "still on the server"): checked again right before the
    // write, after any wait here; null comes back when it says no.
    private async Task<IReadOnlyList<string>?> ReadCommandReply(string command, ServerRun run, Func<bool>? stillValid = null)
    {
        // No server at all (now, or when it was queued) says so the old way; only another run says the command's run ended.
        var check = ServerCommandQueue.Check(run, CurrentRun());
        if (check != RunCheck.Same)
        {
            var message = check == RunCheck.NoServer ? CheatBlockReason() : ServerCommandQueue.RunEnded;
            _commandWriteFailed = true;
            Remember(message);
            return new[] { message };
        }

        if (!OwnsServerProcess())
        {
            if (!ServerIsRunning())
            {
                var message = CheatBlockReason();
                _commandWriteFailed = true;
                Remember(message);
                return new[] { message };
            }

            var (reply, sent) = await SendThroughQueue(command);
            _commandWriteFailed = !sent;
            return reply.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }

        // An earlier write that was stuck on a full pipe when its turn ended may still land, with its output after it: wait
        // for it (at most 5 seconds) and let its output reach the log before this reply read takes its starting point.
        if (!await ServerCommandQueue.AfterStuckWrite(_stuckWrite, ServerCommandQueue.StdinLockWait, StuckReplySettle))
        {
            _commandWriteFailed = true;
            Remember(ServerCommandQueue.ConsoleTimeout);
            return new[] { ServerCommandQueue.ConsoleTimeout };
        }

        _stuckWrite = null;
        // The wait above can outlast the run (a stop, crash or restart meanwhile): checked again right before the write,
        // and the write goes to that run's own process, never to whatever _process is by then.
        if (run.Process is not { } queuedProcess || !ServerCommandQueue.SameRun(queuedProcess, _process))
        {
            _commandWriteFailed = true;
            Remember(ServerCommandQueue.RunEnded);
            return new[] { ServerCommandQueue.RunEnded };
        }

        if (stillValid != null && !stillValid())
            return null;

        var file = NewestCoopLog();
        file?.Refresh();
        var offset = file?.Length ?? 0L;
        _commandWriteFailed = false;
        ConsoleWrite written;
        Task<ConsoleWrite>? writing = null;
        try
        {
            var process = queuedProcess;
            // Off the window thread with one deadline for the lock and the write. Not begun by then: not sent, and never
            // sent later. Begun but stuck on a full pipe: it may still land, so it is neither a done line nor a failure.
            (written, writing) = await ServerCommandQueue.WriteBounded(_stdinLock, ServerCommandQueue.StdinLockWait, () =>
            {
                process.StandardInput.WriteLine(command);
                process.StandardInput.Flush();
            }, stillValid);
            // The last check, made inside the stdin lock right before the write, said no: nothing was written.
            if (written == ConsoleWrite.Dropped)
                return null;
            if (written == ConsoleWrite.NotSent)
                throw new TimeoutException();
        }
        catch (Exception ex)
        {
            // A reply that says "was not sent", so ServerTime.Result and every caller see a failure, never a done line.
            _commandWriteFailed = true;
            var notSent = ex is TimeoutException ? ServerCommandQueue.ConsoleTimeout : ServerCommandQueue.NotSent(ex.Message);
            Remember(notSent);
            return new[] { notSent };
        }

        if (written == ConsoleWrite.MaybeSent)
        {
            // No reply read: the turn ends here, and ServerTime.Result shows the line without a resend or a done line. The
            // next command waits for this write first, so its output is never read as the next command's reply.
            _stuckWrite = writing;
            Remember(ServerCommandQueue.ConsoleSlow);
            return new[] { ServerCommandQueue.ConsoleSlow };
        }

        Remember("> " + command);
        var lines = new List<string>();
        var carry = "";
        var sawEcho = false;
        DateTime? quietSince = null;
        var deadline = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(80);
            file = NewestCoopLog();
            if (file == null)
                continue;
            file.Refresh();
            if (file.Length < offset)
                offset = 0;
            var grew = false;
            if (file.Length > offset)
            {
                try
                {
                    if (!TryReadLogChunk(file, ref offset, MaxReplyReadBytes, out var piece))
                        continue;
                    carry += piece;
                    grew = true;
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
            }

            var newline = carry.LastIndexOf('\n');
            if (newline >= 0)
            {
                var ready = carry[..newline];
                carry = carry[(newline + 1)..];
                var stop = false;
                foreach (var raw in ready.Split('\n'))
                {
                    var line = raw.TrimEnd('\r').Trim();
                    if (line.Length == 0)
                        continue;
                    if (!sawEcho)
                    {
                        if (line.Contains("> " + command, StringComparison.Ordinal))
                            sawEcho = true;
                        continue;
                    }

                    if (line.Contains("[DedicatedServer]", StringComparison.Ordinal))
                    {
                        stop = true;
                        break;
                    }

                    lines.Add(StripLogTime(line));
                    if (lines.Count >= MaxShownReplyLines)
                    {
                        lines.Add(ReplyCutNotice);
                        offset = file.Length;
                        carry = "";
                        stop = true;
                        break;
                    }
                }

                if (stop)
                    break;
            }

            if (grew)
                quietSince = null;
            else if (sawEcho)
            {
                quietSince ??= DateTime.UtcNow;
                var quiet = lines.Count > 0 ? TimeSpan.FromMilliseconds(500) : TimeSpan.FromSeconds(2);
                if (DateTime.UtcNow - quietSince > quiet)
                    break;
            }
        }

        if (lines.Count > 0)
            Remember(string.Join(Environment.NewLine, lines));
        return lines;
    }

    private static bool TryReadLogChunk(FileInfo file, ref long offset, int maxBytes, out string text)
    {
        text = "";
        file.Refresh();
        if (file.Length <= offset)
            return false;
        var count = (int)Math.Min(file.Length - offset, maxBytes);
        var buffer = new byte[count];
        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Position = offset;
        var read = stream.Read(buffer, 0, count);
        if (read <= 0)
            return false;
        var end = read;
        if (offset + read < file.Length)
        {
            var last = Array.LastIndexOf(buffer, (byte)'\n', read - 1);
            if (last > 0)
                end = last + 1;
        }

        offset += end;
        text = Encoding.UTF8.GetString(buffer, 0, end);
        return text.Length > 0;
    }

    private static string StripLogTime(string line)
    {
        if (line.Length > 14 && line[2] == ':' && line[5] == ':' && line[8] == '.')
        {
            var cut = line.IndexOf("  ", StringComparison.Ordinal);
            if (cut is > 0 and < 16)
                return line[(cut + 2)..];
        }

        return line;
    }

    private FileInfo? NewestCoopLog()
    {
        var logs = Path.Combine(_settings.DataRoot, "logs");
        if (!Directory.Exists(logs))
            return null;
        return HostLogFile.ConsoleOf(Directory.EnumerateFiles(logs, "*.log", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path)), _runStartedUtc);
    }


    private bool ScanServing(string path)
    {
        const string marker = "[DedicatedServer] SERVING";
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (_servingCheckedPath != path)
            {
                _servingCheckedPath = path;
                _servingScanOffset = 0;
            }

            if (stream.Length <= _servingScanOffset)
                return false;

            // Step back far enough to read the whole line, so the start banner that only names
            // SERVING is told apart from the real line.
            var start = Math.Max(0, _servingScanOffset - (marker.Length + 400));
            stream.Position = start;
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var text = reader.ReadToEnd();
            _servingScanOffset = stream.Length;
            return ServerStateEvent.HasServingLine(text);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private string ReadPlayerLog()
    {
        var file = NewestCoopLog();
        if (file == null)
            return _playerSourceText;

        file.Refresh();
        if (file.FullName == _playerSourcePath && file.Length == _playerSourceLength)
            return _playerSourceText;

        const string playersEvent = "\"ev\":\"players\"";
        var tail = HostLogFile.ReadTail(file.FullName, 256_000);
        if (tail.Contains(playersEvent, StringComparison.Ordinal))
            _playerSourceText = tail;
        else if (_playerSourceText.Length == 0 || !file.FullName.Equals(_playerSourcePath, StringComparison.OrdinalIgnoreCase))
            _playerSourceText = HostLogFile.ReadTailContaining(file.FullName, playersEvent, 256_000);

        _playerSourcePath = file.FullName;
        _playerSourceLength = file.Length;
        return _playerSourceText;
    }

    private string ReadNewestLog()
    {
        var file = HostLogFile.Prefer(LogFiles()).FirstOrDefault();
        if (file == null)
        {
            _cachedLogPath = "";
            _cachedLogLength = -1;
            _cachedLogText = _logTail.ToString();
            return _cachedLogText;
        }

        file.Refresh();
        if (file.FullName == _cachedLogPath && file.Length == _cachedLogLength && file.LastWriteTimeUtc == _cachedLogWrite)
            return _cachedLogText;

        _cachedLogPath = file.FullName;
        _cachedLogLength = file.Length;
        _cachedLogWrite = file.LastWriteTimeUtc;
        _cachedLogText = _logTail.ToString() + Environment.NewLine + HostLogFile.ReadTail(file.FullName, 64_000);
        return _cachedLogText;
    }

    private IEnumerable<FileInfo> LogFiles()
    {
        var files = new List<string>();
        var logs = Path.Combine(_settings.DataRoot, "logs");
        if (Directory.Exists(logs))
            files.AddRange(Directory.EnumerateFiles(logs, "*.log", SearchOption.AllDirectories));
        var hostRoot = Path.GetDirectoryName(_settings.ServerRoot);
        if (!string.IsNullOrWhiteSpace(hostRoot) && Directory.Exists(hostRoot))
            files.AddRange(Directory.EnumerateFiles(hostRoot, "server-run*.log", SearchOption.TopDirectoryOnly));
        return HostLogFile.ThisRun(files.Select(path => new FileInfo(path)).Where(file => file.Exists && file.Length > 0), _runStartedUtc);
    }

    private void CaptureLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;
        // Play hold reads Coop's time lines on the window thread, in the order they came.
        if (PlayHold.IsTimeControlLine(line))
            Dispatcher.BeginInvoke(() => NotePlayHold(line));
        // The @DS@ serving state is the server's own ready signal; the real SERVING line (not
        // the start banner) is kept for servers that do not send it.
        if (ServerStateEvent.IsServingLine(line))
        {
            _seenServing = true;
            _servedThisRun = true;
        }
        if (WorldSavedLine.SavedFile(line) is { } savedFile)
        {
            _lastWorldSavedFile = savedFile;
            Interlocked.Increment(ref _worldSavedCount);
        }
        if (line.Contains("@DS@", StringComparison.Ordinal) && ServerStateEvent.Last(line) is { Fatal: true } state)
        {
            _fatalState = state;
            _seenServing = false;
        }
        _logTail.Add(line);
        NoteHostReport(line, exited: false);
        NoteSavedWorld(line);
        // The join checks need the last few lines, not the whole tail (same window as Coodsz Host).
        foreach (var advice in JoinResultExplain.Explain(_logTail.Recent(60), warSailsSetup: true))
        {
            if (_joinAdvice.Add(advice))
                NoteLog(advice);
        }
    }

    // A DLL Windows blocked is the one server problem this tool explains from the log itself; the exit code line
    // (NightlyServer.ExitHint) covers the rest. Shown once per run.
    private void NoteHostReport(string? text, bool exited)
    {
        if (exited)
            _seenServing = false;
        if (_blockedDllNoted || BlockedDllNotice.Explain(text) is not { } lines)
            return;
        _blockedDllNoted = true;
        foreach (var item in lines)
            NoteLog(item);
        SetControl("The server cannot load a DLL. Click Unblock DLLs on the Settings page, then click Start server again.", failed: true);
        if (BlockedDllNotice.Matches(text))
            ShowBlockedDllFix();
    }

    private void ShowBlockedDllFix()
    {
        // The blocked DLL line comes from the server output thread; WPF throws off the window thread and the output
        // reader rethrows on a pool thread, which closed the tool. Hop to the window thread first, like TAOM (c93f7577).
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(ShowBlockedDllFix));
            return;
        }

        ShowPage("Settings");
        if (!_unblockPulse.IsEnabled)
            _unblockPulse.Start();
        PulseUnblockButton();
        SetControl("The server cannot load a DLL. Click the red Unblock DLLs button, then start again.", failed: true);
    }

    private void StopUnblockAlert()
    {
        _unblockPulse.Stop();
        UnblockButton.ClearValue(Control.BackgroundProperty);
        UnblockButton.ClearValue(Control.ForegroundProperty);
        UnblockButton.ClearValue(Control.BorderBrushProperty);
        UnblockButton.ClearValue(Control.BorderThicknessProperty);
        UnblockButton.ClearValue(Control.FontSizeProperty);
        UnblockButton.FontWeight = FontWeights.Normal;
    }

    private void PulseUnblockButton()
    {
        _unblockBright = !_unblockBright;
        var color = _unblockBright ? Color.FromRgb(176, 42, 36) : Color.FromRgb(92, 22, 20);
        UnblockButton.Background = new SolidColorBrush(color);
        UnblockButton.Foreground = new SolidColorBrush(Color.FromRgb(255, 236, 232));
        UnblockButton.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 120, 108));
        UnblockButton.BorderThickness = new Thickness(3);
        UnblockButton.FontSize = 15;
        UnblockButton.FontWeight = FontWeights.Bold;
    }

    private void NoteLog(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;
        if (!Dispatcher.CheckAccess())
        {
            var copy = line;
            Dispatcher.BeginInvoke(() => NoteLog(copy));
            return;
        }

        Coodsz.Ui.UiLanguage.AppendLog(LogBox, LogBox.Text.Length > 0 ? Environment.NewLine : "", line);
        PushDashboardLines(line);
        if (LogBox.Text.Length > 80_000)
        {
            var text = LogBox.Text;
            var start = text.Length - 60_000;
            var newline = text.IndexOf('\n', start);
            LogBox.Text = newline >= 0 ? text[(newline + 1)..] : text[start..];
        }

        if (LogsPage.Visibility == Visibility.Visible)
            LogBox.ScrollToEnd();
    }

    private void Remember(string line)
    {
        if (!Dispatcher.CheckAccess())
        {
            var copy = line;
            Dispatcher.BeginInvoke(() => Remember(copy));
            return;
        }

        ColoredConsole.Append(HistoryBox, line, ConsolePage.Visibility == Visibility.Visible && AutoScroll.IsChecked == true);
    }
}
