namespace BannerlordCoopLauncher.Core.Support;

/// <summary>
/// CoopModPatch's first time setup: it copies the server DLLs it needs, writes "CoopModPatch
/// first-time setup complete" and "The server was stopped intentionally; start it again to load
/// them.", then ends the server with exit code 1. That stop is on purpose, so the tools say to
/// press Start again instead of showing a crash.
/// </summary>
public static class ModPatchSetup
{
    public const string SetupLine = "first-time setup complete";
    public const string StopLine = "The server was stopped intentionally; start it again to load them.";
    public const int ExitCode = 1;

    public const string Text = "Setup finished. Press Start server again.";

    public static bool IsSetupLine(string? line) =>
        line != null
        && line.Contains("CoopModPatch", StringComparison.OrdinalIgnoreCase)
        && line.Contains(SetupLine, StringComparison.OrdinalIgnoreCase);

    public static bool IsStopLine(string? line) =>
        line != null && line.Contains(StopLine, StringComparison.OrdinalIgnoreCase);

    /// <summary>Exit code 1 and both of CoopModPatch's lines in the log.</summary>
    public static bool Finished(int? exitCode, string? log)
    {
        if (exitCode != ExitCode || string.IsNullOrEmpty(log))
            return false;
        var lines = log.Replace("\r", "").Split('\n');
        return lines.Any(IsSetupLine) && lines.Any(IsStopLine);
    }

    /// <summary>Follows one server run line by line.</summary>
    public sealed class Watch
    {
        private bool _setup;
        private bool _stop;

        public bool Seen => _setup && _stop;

        public void Reset() => _setup = _stop = false;

        public void Observe(string? line)
        {
            _setup |= IsSetupLine(line);
            _stop |= IsStopLine(line);
        }

        /// <summary>This exit is CoopModPatch's own stop after its setup.</summary>
        public bool Finished(int exitCode) => Seen && exitCode == ExitCode;
    }
}
