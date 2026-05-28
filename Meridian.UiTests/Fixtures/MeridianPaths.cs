namespace Meridian.UiTests.Fixtures;

// Canonical file-system paths for the SUT. Kept in one place so a TFM bump
// (e.g. net11) only needs editing here.
//
// The fixture launches Meridian with MERIDIAN_DATA_DIR set to a per-test temp
// directory, so the test process and the user's real %APPDATA%\Meridian never
// overlap. Path lookups inside that isolated dir go through `Under(root)`.
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

    // Paths inside an isolated data dir. Mirrors AppPaths in the SUT.
    public sealed record InDataDir(string Root)
    {
        public string Cache => Path.Combine(Root, "cache");
        public string ViewStateJson => Path.Combine(Cache, "viewstate.json");
        public string WindowStateJson => Path.Combine(Cache, "windowstate.json");
    }

    public static InDataDir Under(string root) => new(root);
}
