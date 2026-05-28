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

    public static string Root { get; } =
        Environment.GetEnvironmentVariable(EnvName)
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "Meridian");

    public static string Cache => Path.Combine(Root, "cache");
    public static string Tokens => Path.Combine(Root, "tokens");
    public static string Logs => Path.Combine(Root, "logs");

    // True when running under an isolated data dir (test harness). Callers
    // use this to skip system-wide registrations (Start Menu shortcut, AUMID
    // registry, COM CLSID for toasts) that would otherwise stomp the user's
    // real install.
    public static bool IsIsolated { get; } =
        Environment.GetEnvironmentVariable(EnvName) is not null;
}
