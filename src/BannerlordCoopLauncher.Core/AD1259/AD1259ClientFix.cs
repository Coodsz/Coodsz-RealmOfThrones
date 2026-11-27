using BannerlordCoopLauncher.Core.Launch;

namespace BannerlordCoopLauncher.Core.AD1259;

/// <summary>Copies SkyApolo's bundled client module into the game's Modules folder.</summary>
public static class AD1259ClientFix
{
    public const string AddOnFolder = ClientAddOns.Folder;

    public static string Payload(string? baseDirectory = null) =>
        ClientAddOns.Payload(AD1259Stack.ClientFixModuleId, baseDirectory);

    /// <summary>
    /// Copies the shipped module over Modules\DedicatedServer.AD1259CoopClientFix, always the shipped copy.
    /// Throws when the launcher was unzipped without its ClientAddOns folder. Returns the antivirus line
    /// when a DLL the last install put there was gone, else null.
    /// </summary>
    public static string? Install(string gameRoot, string? payloadRoot = null) =>
        ClientAddOns.Install(gameRoot, AD1259Stack.ClientFixModuleId, AD1259Stack.ClientFixName, payloadRoot);
}
