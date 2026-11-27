namespace TaomCoop.Core;

/// <summary>
/// The save fix notes of the TAOM Server Tool. The shared AutoRescue text names Coodsz Host's
/// Load backup button; this tool's button is Restore save backup on the Saves page.
/// </summary>
public static class TaomRescueText
{
    public const string FixedSaved = "The game was saved with the fixes. A backup from before them is kept. Restore save backup on the Saves page puts it back.";
    public const string FixedNotSaved = "The next save keeps the fixes. A backup from before them is kept. Restore save backup on the Saves page puts it back.";
    public const string RetryFailedBody = "It was not started again. Click Send my logs and post the code in Discord. Restore save backup on the Saves page puts back an earlier save.";

    public static string BackupKept(string saveName) => "Kept a backup of " + saveName + " from before the save fixes. Restore save backup on the Saves page puts it back.";
}
