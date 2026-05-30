using System.Runtime.InteropServices;

namespace Meridian.Services;

// Detects whether the process is running with MSIX package identity, and exposes
// the package-virtualized data folder + the package AUMID — all via kernel32, no
// Windows.ApplicationModel projection.
//
// Why not Windows.ApplicationModel.Package.Current / ApplicationData.Current?
// Package.Current THROWS when unpackaged (fragile for startup control flow), and
// both are CsWinRT-projected calls that need trim roots under PublishAot. The
// kernel32 *AppModel* APIs return clean integers instead: GetCurrentPackageFullName
// yields APPMODEL_ERROR_NO_PACKAGE (15700) when there is no identity, success (0)
// or ERROR_INSUFFICIENT_BUFFER (122) when there is. No exception, no projection,
// already the same style ToastSetup uses for its COM work.
internal static partial class PackageIdentity
{
    private const int ERROR_SUCCESS = 0;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;
    private const int APPMODEL_ERROR_NO_PACKAGE = 15700;

    // True when the process has MSIX package identity. Resolved once.
    public static bool IsPackaged { get; } = ResolveIsPackaged();

    private static bool ResolveIsPackaged()
    {
        uint length = 0;
        // Probe with a null buffer: NO_PACKAGE means unpackaged; any other return
        // (success or insufficient-buffer) means we have identity.
        int rc = GetCurrentPackageFullName(ref length, null);
        return rc != APPMODEL_ERROR_NO_PACKAGE;
    }

    // The package AUMID (<PackageFamilyName>!<AppId>) the OS routes notifications
    // to. Only meaningful when IsPackaged. Returns null on failure.
    public static string? GetApplicationUserModelId()
    {
        uint length = 0;
        int rc = GetCurrentApplicationUserModelId(ref length, null);
        if (rc != ERROR_INSUFFICIENT_BUFFER || length == 0) return null;

        var buf = new char[length];
        rc = GetCurrentApplicationUserModelId(ref length, buf);
        if (rc != ERROR_SUCCESS) return null;

        // length includes the terminating null.
        return new string(buf, 0, (int)length - 1);
    }

    // The package family name, used to build the LocalState path without invoking
    // the projected ApplicationData.Current.LocalFolder. Returns null on failure.
    public static string? GetPackageFamilyName()
    {
        uint length = 0;
        int rc = GetCurrentPackageFamilyName(ref length, null);
        if (rc != ERROR_INSUFFICIENT_BUFFER || length == 0) return null;

        var buf = new char[length];
        rc = GetCurrentPackageFamilyName(ref length, buf);
        if (rc != ERROR_SUCCESS) return null;

        return new string(buf, 0, (int)length - 1);
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetCurrentPackageFullName(ref uint packageFullNameLength, char[]? packageFullName);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetCurrentApplicationUserModelId(ref uint applicationUserModelIdLength, char[]? applicationUserModelId);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetCurrentPackageFamilyName(ref uint packageFamilyNameLength, char[]? packageFamilyName);
}
