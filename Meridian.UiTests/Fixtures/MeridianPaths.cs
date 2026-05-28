namespace Meridian.UiTests.Fixtures;

// Canonical file-system paths for the SUT. Kept in one place so a TFM bump
// (e.g. net11) only needs editing here.
internal static class MeridianPaths
{
    public const string ProcessName = "Meridian";

    // Repo-relative debug exe. The test working directory is the test bin/,
    // so we walk up to the solution root then back down.
    public static string DebugExe { get; } = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        // bin/Debug/net10.0-windows/ → solution root is 4 levels up
        "..", "..", "..", "..",
        "Meridian", "bin", "Debug", "net10.0-windows10.0.19041.0", "win-x64",
        "Meridian.exe"));

    public static string AppDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Meridian", "cache");

    public static string ViewStateJson => Path.Combine(AppDataDir, "viewstate.json");
    public static string WindowStateJson => Path.Combine(AppDataDir, "windowstate.json");
}
