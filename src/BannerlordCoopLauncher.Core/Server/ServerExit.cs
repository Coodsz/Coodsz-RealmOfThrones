// Ported from CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep), Server/ServerExit.cs
// and the ServerExitInfo record from Server/ServerSupervisor.cs, changed for the Coodsz tools.

namespace BannerlordCoopLauncher.Core.Server;

/// <summary>What the exit code of the server process means.</summary>
/// <remarks>
/// The codes are the ones Coop documents in release-info.txt. Start-ModdedServer.ps1
/// exits with the engine's own code, so the same codes reach Coodsz Host.
/// </remarks>
public enum ServerExitKind
{
    /// <summary>0: a clean stop. The world was saved.</summary>
    Clean,

    /// <summary>2: the save could not be loaded, or loading timed out.</summary>
    LoadFailure,

    /// <summary>3: a fatal error while the server was serving.</summary>
    FatalWhileServing,

    /// <summary>4: the Coop module check failed. The module was changed or updated.</summary>
    ModuleVerificationFailed,

    /// <summary>6: the server terms (EULA) were not accepted. Added in Coop 0.1.6.</summary>
    EulaRequired,

    /// <summary>7: Coop's server launcher could not stage a dev module again.</summary>
    DevModuleRestageFailed,

    /// <summary>8: a module in the server's module list is missing ([BannerlordCoopServer] FATAL line).</summary>
    ModuleMissing,

    /// <summary>9: the save was made in the other War Sails mode (with NavalDLC or without it).</summary>
    WarSailsSaveMismatch,

    /// <summary>10: War Sails is set up in a way the server can't run ("warSails" and NavalDLC don't agree).</summary>
    WarSailsSetup,

    /// <summary>11: the save did not start loading, a damaged file or a Coop error. Like 2 for the host.</summary>
    SaveDidNotLoad,

    /// <summary>Any other code, crashes such as an access violation included.</summary>
    Unknown,
}

public static class ServerExit
{
    public const int CleanCode = 0;
    public const int LoadFailureCode = 2;
    public const int FatalWhileServingCode = 3;
    public const int ModuleVerificationFailedCode = 4;
    public const int EulaRequiredCode = 6;
    public const int DevModuleRestageFailedCode = 7;
    public const int ModuleMissingCode = 8;
    public const int WarSailsSaveMismatchCode = 9;
    public const int WarSailsSetupCode = 10;
    public const int SaveDidNotLoadCode = 11;

    public static ServerExitKind Classify(int exitCode) => exitCode switch
    {
        CleanCode => ServerExitKind.Clean,
        LoadFailureCode => ServerExitKind.LoadFailure,
        FatalWhileServingCode => ServerExitKind.FatalWhileServing,
        ModuleVerificationFailedCode => ServerExitKind.ModuleVerificationFailed,
        EulaRequiredCode => ServerExitKind.EulaRequired,
        DevModuleRestageFailedCode => ServerExitKind.DevModuleRestageFailed,
        ModuleMissingCode => ServerExitKind.ModuleMissing,
        WarSailsSaveMismatchCode => ServerExitKind.WarSailsSaveMismatch,
        WarSailsSetupCode => ServerExitKind.WarSailsSetup,
        SaveDidNotLoadCode => ServerExitKind.SaveDidNotLoad,
        _ => ServerExitKind.Unknown,
    };

    /// <summary>Is this a setup problem and not the save? A save rescue can't fix it.</summary>
    /// <remarks>Codes 4, 6, 7, 8, 9 and 10: a changed Coop module, the server terms, a module that
    /// could not be staged or is missing, and War Sails set up wrong or a save from the other War
    /// Sails mode. Loading the same save in load mode fails the same way.</remarks>
    public static bool IsSetupProblem(ServerExitKind kind) =>
        kind is ServerExitKind.ModuleVerificationFailed or ServerExitKind.EulaRequired
            or ServerExitKind.DevModuleRestageFailed or ServerExitKind.ModuleMissing
            or ServerExitKind.WarSailsSaveMismatch or ServerExitKind.WarSailsSetup;

    /// <summary>Can a restart fix this kind of exit?</summary>
    /// <remarks>
    /// Codes 2 and 11 mean the save may be broken, and loading it again and again does not help;
    /// restoring a backup does. Code 4 means the Coop module changed, so a restart loops
    /// forever. Code 6 needs the person hosting to accept the terms. Codes 7 to 10 are the
    /// setup (a module or War Sails) and fail the same way each time. None of them is
    /// restarted.
    /// </remarks>
    public static bool IsAutoRestartable(ServerExitKind kind) => kind switch
    {
        ServerExitKind.Clean => false,
        ServerExitKind.LoadFailure => false,
        ServerExitKind.SaveDidNotLoad => false,
        ServerExitKind.ModuleVerificationFailed => false,
        ServerExitKind.EulaRequired => false,
        ServerExitKind.DevModuleRestageFailed => false,
        ServerExitKind.ModuleMissing => false,
        ServerExitKind.WarSailsSaveMismatch => false,
        ServerExitKind.WarSailsSetup => false,
        ServerExitKind.FatalWhileServing => true,
        ServerExitKind.Unknown => true,
        _ => false,
    };
}

/// <param name="WasExpected">
/// True when this tool asked the server to stop. An asked for stop is never a crash,
/// whatever its code.
/// </param>
public readonly record struct ServerExitInfo(int ExitCode, ServerExitKind Kind, bool WasExpected)
{
    public static ServerExitInfo From(int exitCode, bool wasExpected) =>
        new(exitCode, ServerExit.Classify(exitCode), wasExpected);

    /// <summary>Is this exit a crash?</summary>
    public bool IsCrash => !WasExpected && Kind != ServerExitKind.Clean;

    /// <summary>The server run that exited (CoodszHost's ServerSession.Run), so a late exit is told apart from a newer run.</summary>
    public int Run { get; init; }
}
