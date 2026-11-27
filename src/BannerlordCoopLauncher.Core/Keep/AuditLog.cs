using BannerlordCoopLauncher.Core.IO;
// Ported from CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep),
// src/CoopKeep.App/Settings/AuditLogStore.cs and AdminAuditEntry in src/CoopKeep.Core/Server/AdminCommand.cs,
// translated from Turkish and changed for Coodsz Host (one store with its path, undo kept in memory).

using System.Text.Json;
using System.Text.Json.Serialization;

namespace BannerlordCoopLauncher.Core.Keep;

/// <summary>One action the host did to a player, as the server confirmed it.</summary>
public sealed record AuditEntry(DateTimeOffset At, string Player, string Text, string Note = "")
{
    [JsonIgnore]
    public string TimeText => At.ToLocalTime().ToString("HH:mm:ss");

    [JsonIgnore]
    public string DayText => At.ToLocalTime().ToString("d");

    [JsonIgnore]
    public string Line => Note.Length > 0 ? Player + ": " + Text + " (" + Note + ")" : Player + ": " + Text;
}

/// <summary>
/// The admin log of the Cheats page. These actions change the game's balance (gold, troops,
/// skills); a log that is gone when the tool closes cannot answer "who did what, when" between
/// people who play together, so every action the server confirmed is kept on disk. The newest
/// actions can be undone while the tool is open.
/// </summary>
public sealed class AuditLog
{
    /// <summary>Most entries kept on disk.</summary>
    public const int MaxEntries = 200;

    /// <summary>Most recent actions that keep an undo.</summary>
    public const int MaxUndo = 15;

    private readonly string _path;
    private readonly List<AuditEntry> _entries;
    private readonly Dictionary<AuditEntry, Func<Task>> _undo = new(ReferenceEqualityComparer.Instance);

    public AuditLog(string path)
    {
        _path = path;
        _entries = Load(path);
    }

    public static string DefaultPath => Path.Combine(
        ToolData.RoamingRoot, "admin-log.json");

    /// <summary>Newest first.</summary>
    public IReadOnlyList<AuditEntry> Entries => _entries;

    public event Action? Changed;

    public AuditEntry Add(string player, string text, string note = "", Func<Task>? undo = null)
    {
        var entry = new AuditEntry(DateTimeOffset.Now, player, text, (note ?? "").Trim());
        _entries.Insert(0, entry);
        while (_entries.Count > MaxEntries)
            _entries.RemoveAt(_entries.Count - 1);
        if (undo != null)
            _undo[entry] = undo;
        foreach (var old in _undo.Keys.Except(_entries.Take(MaxUndo)).ToList())
            _undo.Remove(old);
        Save();
        Changed?.Invoke();
        return entry;
    }

    public bool CanUndo(AuditEntry entry) => _undo.ContainsKey(entry);

    /// <summary>Runs the undo once; the undo itself is logged by whoever runs it.</summary>
    public async Task<bool> UndoAsync(AuditEntry entry)
    {
        if (!_undo.Remove(entry, out var undo))
            return false;
        Changed?.Invoke();
        await undo();
        return true;
    }

    public static List<AuditEntry> Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<List<AuditEntry>>(File.ReadAllText(path)) ?? new List<AuditEntry>();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            // A broken log file must not keep the tool from opening.
        }

        return new List<AuditEntry>();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not saved this time; the entry stays in memory.
        }
    }
}
