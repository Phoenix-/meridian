using System.Runtime.InteropServices;

namespace Meridian;

internal static partial class NativeMethods
{
    internal const int SW_MAXIMIZE = 3;
    internal const int SW_RESTORE = 9;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint hWnd, int nCmdShow);
}
