namespace Meridian.Services;

// Single source of truth for all on-disk paths the app uses. Honors a
// MERIDIAN_DATA_DIR env override so a UI test (or any other harness) can run
// in an isolated data dir without colliding with the user's real %APPDATA%
// state — cache, tokens, viewstate, logs, the lot.
//
// Resolved once at process start (static field initializer). The env must be
// set by whoever launches the exe BEFORE the process starts; modifying it
// from inside the process after the first read of `Root` has no effect.
internal static class AppPaths
{
    private const string EnvName = "MERIDIAN_DATA_DIR";

    // Where on-disk state lives. Three modes, resolved in priority order:
    //   1. IsIsolated  — MERIDIAN_DATA_DIR override (test harness).
    //   2. IsPackaged  — MSIX package-virtualized LocalState. We build the path
    //      from the package family name (%LOCALAPPDATA%\Packages\<PFN>\LocalState)
    //      rather than calling the CsWinRT-projected ApplicationData.Current
    //      .LocalFolder, which would need a trim root under PublishAot.
    //   3. default     — %APPDATA%\Meridian (unpackaged: F5, portable zip).
    public static string Root { get; } = ResolveRoot();

    private static string ResolveRoot()
    {
        var env = Environment.GetEnvironmentVariable(EnvName);
        if (env is not null) return env;

        if (PackageIdentity.IsPackaged)
        {
            var pfn = PackageIdentity.GetPackageFamilyName();
            if (!string.IsNullOrEmpty(pfn))
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Packages", pfn, "LocalState");
            // Identity present but PFN lookup failed: fall through to the default
            // rather than crash. Worst case the user re-logs in.
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Meridian");
    }

    public static string Cache => Path.Combine(Root, "cache");
    public static string Tokens => Path.Combine(Root, "tokens");
    public static string Logs => Path.Combine(Root, "logs");

    // True when running under an isolated data dir (test harness). Callers
    // use this to skip system-wide registrations (Start Menu shortcut, AUMID
    // registry, COM CLSID for toasts) that would otherwise stomp the user's
    // real install.
    public static bool IsIsolated { get; } =
        Environment.GetEnvironmentVariable(EnvName) is not null;

    // True when running with MSIX package identity. Callers skip the unpackaged
    // toast registration (the package owns AUMID/activator) and trigger the
    // one-time data migration from the old unpackaged %APPDATA% location.
    // Isolation wins: a test harness never counts as packaged.
    public static bool IsPackaged { get; } = !IsIsolated && PackageIdentity.IsPackaged;
}
