using System.Text.Json;

namespace Snug.Windows.Core;

/// <summary>
/// An app can destroy and recreate a window after Snug has resized it.
/// Match returned windows only when the executable and last Snug-filled
/// dimensions agree. Old HWNDs must never be used to restore another window.
/// Based on upstream LeftBehind.swift, with Windows-specific HWND identity.
/// </summary>
public sealed record LeftBehindEntry(WindowRestoreRecord Record, DateTimeOffset Since);

public sealed class LeftBehindBook
{
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(14);
    public const int Limit = 20;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly object _gate = new();
    private readonly string _filePath;
    private List<LeftBehindEntry> _entries;

    public LeftBehindBook(string filePath)
    {
        _filePath = Path.GetFullPath(filePath);
        try
        {
            _entries = File.Exists(_filePath)
                ? JsonSerializer.Deserialize<List<LeftBehindEntry>>(File.ReadAllText(_filePath), JsonOptions) ?? []
                : [];
            _entries.RemoveAll(entry => entry.Record.FilledBounds is null ||
                string.IsNullOrWhiteSpace(entry.Record.ExecutablePath));
            PruneInMemory(DateTimeOffset.UtcNow);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupted history must not prevent Snug from starting; do not
            // modify the broken file until a subsequent successful save.
            _entries = [];
        }
    }

    public IReadOnlyList<LeftBehindEntry> Entries
    {
        get { lock (_gate) return _entries.ToArray(); }
    }

    public bool Add(WindowRestoreRecord record, DateTimeOffset now)
    {
        if (record.FilledBounds is not { Width: > 0, Height: > 0 } ||
            string.IsNullOrWhiteSpace(record.ExecutablePath))
            return false;

        lock (_gate)
        {
            _entries.RemoveAll(entry => entry.Record.Key == record.Key);
            _entries.Add(new LeftBehindEntry(record, now));
            PruneInMemory(now);
            Save();
            return true;
        }
    }

    public LeftBehindEntry? Match(WindowInfo window, WindowBounds actual, DateTimeOffset now,
        long? processStartedUtcTicks = null)
    {
        lock (_gate)
        {
            // The exact same HWND + PID is stronger than geometry: Windows may
            // resize a still-live window on monitor mode changes or fullscreen
            // transitions. A different process generation invalidates that ID.
            var candidates = _entries.Where(entry =>
                now - entry.Since <= KeepFor &&
                entry.Record.FilledBounds is not null &&
                StringComparer.OrdinalIgnoreCase.Equals(
                    entry.Record.ExecutablePath, window.ExecutablePath) &&
                !(entry.Record.Key == window.Key &&
                  entry.Record.ProcessStartedUtcTicks is { } originalStart &&
                  processStartedUtcTicks is { } currentStart &&
                  originalStart != currentStart)).ToArray();

            static LeftBehindEntry? Latest(IEnumerable<LeftBehindEntry> items) =>
                items.OrderByDescending(entry => entry.Since).FirstOrDefault();

            var exact = Latest(candidates.Where(entry => entry.Record.Key == window.Key));
            if (exact is not null) return exact;

            // Only a recreated window may use the older geometry/title
            // heuristic; never apply a former HWND's restore record blindly.
            var similar = candidates.Where(entry => entry.Record.FilledBounds is { } filled &&
                MatchesFilledSize(entry.Record, window, filled, actual)).ToArray();
            return Latest(similar.Where(entry =>
                    !string.IsNullOrWhiteSpace(entry.Record.Title) &&
                    StringComparer.Ordinal.Equals(entry.Record.Title, window.Title)))
                ?? Latest(similar);
        }
    }

    private static bool MatchesFilledSize(WindowRestoreRecord record, WindowInfo window,
        WindowBounds filled, WindowBounds actual)
    {
        // Actual Windows Electron restores can differ by three logical pixels
        // across DPI contexts. Wider tolerance only for an exact editor title.
        var tolerance = ZoomRecovery.ScopeOf(record.ExecutablePath) == ZoomScope.Workspace &&
            ZoomRecovery.IsWorkspaceTitle(window.Title) &&
            StringComparer.Ordinal.Equals(record.Title, window.Title) ? 4 : 1;
        return Math.Abs(filled.Width - actual.Width) <= tolerance &&
            Math.Abs(filled.Height - actual.Height) <= tolerance;
    }

    public LeftBehindEntry UpdateRecord(LeftBehindEntry entry, WindowRestoreRecord record)
    {
        lock (_gate)
        {
            var index = _entries.IndexOf(entry);
            if (index < 0) throw new InvalidOperationException("Left-behind entry no longer exists.");
            var updated = entry with { Record = record };
            _entries[index] = updated;
            Save();
            return updated;
        }
    }

    public void Remove(LeftBehindEntry entry)
    {
        lock (_gate)
        {
            if (_entries.Remove(entry)) Save();
        }
    }

    public void Prune(DateTimeOffset now)
    {
        lock (_gate)
        {
            var before = _entries.Count;
            PruneInMemory(now);
            if (before != _entries.Count) Save();
        }
    }

    private void PruneInMemory(DateTimeOffset now)
    {
        _entries.RemoveAll(entry => now - entry.Since > KeepFor);
        _entries = _entries.OrderBy(entry => entry.Since).TakeLast(Limit).ToList();
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("Left-behind history path has no parent.");
        Directory.CreateDirectory(directory);
        var temporary = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(_entries, JsonOptions));
            File.Move(temporary, _filePath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}