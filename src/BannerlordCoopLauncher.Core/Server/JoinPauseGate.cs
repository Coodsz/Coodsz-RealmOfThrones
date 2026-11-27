namespace BannerlordCoopLauncher.Core.Server;

// One at a time for Coodsz Host's join pause steps and the host's own time commands. A step holds the gate for its
// whole send, so a resume never overtakes its pause. A host time command waits for a step already on its way, so the
// host's speed always goes out after it. A resume that waited and finds the host sent a time command meanwhile
// (JoinPause.HostCommandCount moved, noted before the host's command waits) is dropped. Each entry that returns true
// must be followed by Release.
public sealed class JoinPauseGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JoinPause _pause;

    public JoinPauseGate(JoinPause pause) => _pause = pause;

    // True while a join step or a host command holds the gate, so a host command would wait.
    public bool Busy => _gate.CurrentCount == 0;

    // A join step: false when it is a resume the host's own time command overtook while it waited.
    public async Task<bool> EnterStep(JoinPauseStep step)
    {
        var hostCommands = _pause.HostCommandCount;
        await _gate.WaitAsync().ConfigureAwait(false);
        if (step == JoinPauseStep.Resume && _pause.HostCommandCount != hostCommands)
        {
            _gate.Release();
            return false;
        }

        return true;
    }

    // A host command (after JoinPause.HostSent). Only a time command waits; false means it did not enter and needs no Release.
    public async Task<bool> EnterHost(string command)
    {
        if (JoinPause.HostMode(command) == null)
            return false;
        await _gate.WaitAsync().ConfigureAwait(false);
        return true;
    }

    public void Release() => _gate.Release();
}
