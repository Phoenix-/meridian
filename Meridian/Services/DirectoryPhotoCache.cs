using System.Security.Cryptography;
using System.Text;
using Meridian.Auth;
using Meridian.Diagnostics;

namespace Meridian.Services;

// Disk-backed cache of directory profile-photo *bytes*, keyed by a hash of the
// photo URL. Stage 2 of the directory feature: DirectoryCache already resolves
// and persists each person's PhotoUrl; this turns that URL into on-disk image
// bytes the avatar can paint.
//
// The file name is derived from the URL, so when a person changes their photo
// the resolved URL changes too (DirectoryCache re-fetches it once its own name
// TTL lapses), producing a new cache file. The old file orphans. We don't sweep
// orphans the instant their owning entry changes — a re-auth or a transient
// resolve miss would needlessly discard a still-valid photo. Instead PruneOld
// (run opportunistically) deletes any photo file untouched for OrphanTtl, so a
// genuinely abandoned photo eventually goes while a churning one survives.
//
// All IO is non-throwing: a failed download or read just means the avatar keeps
// its colored-initial fallback. We deliberately never log the URL or the bytes
// (only sizes/hashes) — directory data is arbitrary remote content and has no
// business in a log file.
public static class DirectoryPhotoCache
{
    // A photo file untouched for this long is considered abandoned and swept by
    // PruneOld. Generous: photos rarely change, and re-downloading a wrongly
    // pruned one is cheap — but an account that's gone for a month shouldn't
    // leave its colleagues' faces on disk forever.
    private static readonly TimeSpan OrphanTtl = TimeSpan.FromDays(30);

    private static readonly HttpClient _http = new();
    private static readonly Lock _gate = new();

    // URL-hash -> in-flight download, so concurrent avatar rows for the same
    // person don't each fire a request for the same photo.
    private static readonly Dictionary<string, Task<byte[]?>> _inFlight = [];

    private static string PhotoDir =>
        Path.Combine(AppPaths.Cache, "directory", "photos");

    private static string PathForUrl(string url) =>
        Path.Combine(PhotoDir, HashUrl(url) + ".img");

    // Returns the photo bytes for a URL — from disk if present, otherwise
    // downloaded once and cached. Null on any failure (no photo / network),
    // which callers treat as "keep the initial bubble".
    public static async Task<byte[]?> GetAsync(string? photoUrl)
    {
        if (string.IsNullOrWhiteSpace(photoUrl)) return null;

        var path = PathForUrl(photoUrl);
        var onDisk = TryReadFile(path);
        if (onDisk is not null)
        {
            // Mark as freshly used so PruneOld keeps it: a photo still being
            // rendered must not age out just because its URL hasn't changed.
            try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); } catch { /* best-effort */ }
            return onDisk;
        }

        // Coalesce concurrent downloads of the same URL. Only the producer that
        // creates the Task removes the key (in DownloadAndStoreAsync's finally);
        // awaiters just await it. Removing in every awaiter's finally would let a
        // slow awaiter evict a newer producer's entry and re-fire the download.
        Task<byte[]?> download;
        lock (_gate)
        {
            if (!_inFlight.TryGetValue(photoUrl, out download!))
            {
                download = DownloadAndStoreAsync(photoUrl, path);
                _inFlight[photoUrl] = download;
            }
        }

        return await download;
    }

    private static async Task<byte[]?> DownloadAndStoreAsync(string url, string path)
    {
        try
        {
            var bytes = await _http.GetByteArrayAsync(url);
            if (bytes.Length == 0) return null;
            TryWriteFile(path, bytes);
            return bytes;
        }
        catch (Exception ex)
        {
            // Hash, not URL — see class remarks.
            Log.Error("Directory", ex, $"photo download failed for {HashUrl(url)}");
            return null;
        }
        finally
        {
            lock (_gate) _inFlight.Remove(url);
        }
    }

    // Deletes any cached photo file not accessed within OrphanTtl. A file's
    // last-write time is bumped on every cache hit (see GetAsync), so "accessed"
    // means "last served or downloaded". Fire-and-forget on a background thread:
    // callers (account invalidation) run on the UI thread and must not block on
    // a directory sweep. Self-guarding and non-throwing.
    public static void PruneOld() => _ = Task.Run(() =>
    {
        try
        {
            if (!Directory.Exists(PhotoDir)) return;
            var cutoff = DateTime.UtcNow - OrphanTtl;
            foreach (var file in Directory.EnumerateFiles(PhotoDir, "*.img"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                        File.Delete(file);
                }
                catch (Exception ex) { Log.Error("Directory", ex, "photo prune delete failed"); }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Directory", ex, "photo prune failed");
        }
    });

    // Removes the whole photo directory (memory has none — bytes live only on
    // disk). Pairs with DirectoryCache.InvalidateAll. Background + non-throwing
    // for the same UI-thread reason as PruneOld.
    public static void Clear() => _ = Task.Run(() =>
    {
        try
        {
            if (Directory.Exists(PhotoDir))
                Directory.Delete(PhotoDir, recursive: true);
        }
        catch (Exception ex)
        {
            Log.Error("Directory", ex, "photo clear failed");
        }
    });

    private static byte[]? TryReadFile(string path)
    {
        try { return File.Exists(path) ? File.ReadAllBytes(path) : null; }
        catch (Exception ex) { Log.Error("Directory", ex, "photo read failed"); return null; }
    }

    private static void TryWriteFile(string path, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(PhotoDir);
            File.WriteAllBytes(path, bytes);
        }
        catch (Exception ex) { Log.Error("Directory", ex, "photo write failed"); }
    }

    // Stable, filesystem-safe name for a URL. SHA-256 hex — collision-free for
    // our purposes and culture-invariant, unlike string.GetHashCode.
    private static string HashUrl(string url)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        return Convert.ToHexString(bytes);
    }
}
