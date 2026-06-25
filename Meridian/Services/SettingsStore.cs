using System.Text.Json;
using Meridian.Diagnostics;
using Meridian.Models;

namespace Meridian.Services;

// Backing store for user-tweakable settings, persisted to settings.json under
// the app's data root (alongside cache/, tokens/, logs/ — but at the root, not
// inside cache/, since settings are conceptually independent of cached data and
// should survive a cache wipe).
//
// Loaded once, lazily, on first access and held in memory. Writes go through
// an atomic temp-then-replace so a crash mid-write can never leave a truncated
// file that fails to parse on next launch — unlike the view/window state in
// DiskCache, which is cheap to lose; losing settings would silently reset the
// user's choices.
//
// Mutations raise Changed(propertyName) on the calling thread. Subscribers that
// touch UI must marshal to their own dispatcher; the store makes no thread
// guarantees of its own beyond "the in-memory value is updated before Changed
// fires".
internal static class SettingsStore
{
    private static readonly object Gate = new();
    // volatile: read on the lock-free fast path in Data's getter, so the
    // double-checked init needs the barrier to be correct on weak memory models.
    private static volatile SettingsData? _data;

    private static string FilePath => Path.Combine(AppPaths.Root, "settings.json");

    // Raised after a setting changes and the new value is persisted. The
    // argument is the property name (nameof) so subscribers can react
    // selectively without re-reading every value.
    public static event Action<string>? Changed;

    private static SettingsData Data
    {
        get
        {
            // Double-checked: the common path (already loaded) takes no lock.
            if (_data is not null) return _data;
            lock (Gate)
            {
                _data ??= Load();
                return _data;
            }
        }
    }

    public static bool FlashTaskbarOnReminder
    {
        get => Data.FlashTaskbarOnReminder;
        set => Set(value, nameof(FlashTaskbarOnReminder),
                   d => d.FlashTaskbarOnReminder, (d, v) => d.FlashTaskbarOnReminder = v);
    }

    public static bool ShowOngoingEventOverlay
    {
        get => Data.ShowOngoingEventOverlay;
        set => Set(value, nameof(ShowOngoingEventOverlay),
                   d => d.ShowOngoingEventOverlay, (d, v) => d.ShowOngoingEventOverlay = v);
    }

    // Reads/writes one field on the in-memory copy via strongly-typed
    // accessors — no reflection, so nothing here depends on metadata that
    // trimming/NativeAOT might drop. Persists only on an actual change, then
    // raises Changed outside the lock so a subscriber can't deadlock on it.
    private static void Set(bool value, string name,
                            Func<SettingsData, bool> get, Action<SettingsData, bool> set)
    {
        lock (Gate)
        {
            var d = _data ??= Load();
            if (get(d) == value) return;
            set(d, value);
            Save(d);
        }
        Changed?.Invoke(name);
    }

    private static SettingsData Load()
    {
        try
        {
            var path = FilePath;
            if (!File.Exists(path)) return new SettingsData();
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, DiskCacheJsonContext.Default.SettingsData)
                   ?? new SettingsData();
        }
        catch (Exception ex)
        {
            // Corrupt or unreadable file: fall back to defaults rather than
            // block the user out of their own app. Next successful Save rewrites it.
            Log.Error("Settings", ex, "load");
            return new SettingsData();
        }
    }

    private static void Save(SettingsData data)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            var path = FilePath;
            var tmp = path + ".tmp";
            using (var stream = File.Create(tmp))
                JsonSerializer.Serialize(stream, data, DiskCacheJsonContext.Default.SettingsData);
            // Atomic on NTFS: the reader sees either the old file or the new
            // one, never a half-written one. Move with overwrite (no separate
            // delete race).
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("Settings", ex, "save");
        }
    }
}
