using Meridian.Diagnostics;

namespace Meridian.Services;

// One-time copy of user state from the old UNPACKAGED location into the MSIX
// package-virtualized LocalState the first time the app runs packaged.
//
// Why this is needed: a user who has been running the portable/unpackaged build
// has tokens + caches under %APPDATA%\Meridian. When they install the MSIX, the
// packaged process reads from %LOCALAPPDATA%\Packages\<PFN>\LocalState (see
// AppPaths.ResolveRoot) — a fresh, empty store. Without migration they'd be
// silently logged out. For a daily-use calendar that's a painful regression.
//
// Strategy: COPY (not move), idempotent via a stamp file. Copy-not-move so the
// unpackaged build keeps working side-by-side during the transition; the worst
// case if anything goes wrong is a re-login, never data loss.
internal static class DataMigration
{
    private const string StampName = "migrated-from-unpackaged.stamp";

    // Run once on packaged startup, BEFORE any token/cache read. No-op when not
    // packaged, when already migrated, or when there's nothing to migrate.
    public static void MigrateFromUnpackagedIfNeeded()
    {
        if (!AppPaths.IsPackaged) return;

        try
        {
            var stampPath = Path.Combine(AppPaths.Root, StampName);
            if (File.Exists(stampPath)) return;

            // Resolve the real unpackaged Roaming\Meridian by absolute path.
            // We must NOT use SpecialFolder.ApplicationData / the APPDATA env
            // here: inside a packaged process those are redirected and would
            // point back at our own virtualized store.
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(userProfile)) { WriteStamp(stampPath); return; }

            var source = Path.Combine(userProfile, "AppData", "Roaming", "Meridian");
            if (!Directory.Exists(source))
            {
                // Nothing to migrate (fresh install). Stamp so we never re-check.
                Log.Write("Migrate", $"no unpackaged data at {source}; nothing to migrate");
                WriteStamp(stampPath);
                return;
            }

            // Only migrate when our destination tokens are empty — never clobber
            // data the packaged app has already written.
            if (Directory.Exists(AppPaths.Tokens) &&
                Directory.EnumerateFileSystemEntries(AppPaths.Tokens).Any())
            {
                Log.Write("Migrate", "packaged tokens already present; skipping migration");
                WriteStamp(stampPath);
                return;
            }

            Directory.CreateDirectory(AppPaths.Root);
            CopyDirIfExists(Path.Combine(source, "tokens"), AppPaths.Tokens);
            CopyDirIfExists(Path.Combine(source, "cache"), AppPaths.Cache);

            var diagSrc = Path.Combine(source, "diag.enabled");
            if (File.Exists(diagSrc))
                File.Copy(diagSrc, Path.Combine(AppPaths.Root, "diag.enabled"), overwrite: true);

            Log.Write("Migrate", $"copied unpackaged data from {source}");
            WriteStamp(stampPath);
        }
        catch (Exception ex)
        {
            // Migration is best-effort: a failure just means the user re-logs in.
            // Never break startup over it, and don't stamp so a transient failure
            // can retry on the next launch.
            Log.Error("Migrate", ex, "MigrateFromUnpackagedIfNeeded");
        }
    }

    private static void CopyDirIfExists(string source, string dest)
    {
        if (!Directory.Exists(source)) return;

        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            var target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void WriteStamp(string stampPath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(stampPath)!);
            File.WriteAllText(stampPath, DateTime.UtcNow.ToString("o"));
        }
        catch { /* best-effort */ }
    }
}
