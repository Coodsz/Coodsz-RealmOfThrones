namespace BannerlordCoopLauncher.Core.Localization;

/// <summary>
/// What the Settings page shows for a dropdown value. The boxes keep the raw value the code
/// writes to mod-config.json ("True", "VeryEasy", "NeverExpire"); the screen shows a plain
/// English label instead ("On", "Very easy", "Never expire"), which the window then translates.
/// </summary>
public static class ModSettingText
{
    /// <summary>The English label for a raw value; any other text comes back as it is.</summary>
    public static string ChoiceLabel(string? value) => value switch
    {
        null => "",
        "True" or "Enabled" => "On",
        "False" or "Disabled" => "Off",
        "VeryEasy" => "Very easy",
        "OneDayMax" => "One day max",
        "NeverExpire" => "Never expire",
        "AlwaysRetry" => "Always retry",
        "KeepClans" => "Keep clans",
        _ => value,
    };
}
