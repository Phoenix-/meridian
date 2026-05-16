using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: DisableRuntimeMarshalling]

namespace Meridian;

internal static partial class NativeMethods
{
    internal const int SW_MAXIMIZE = 3;
    internal const int SW_SHOWNORMAL = 1;
    internal const int SW_RESTORE = 9;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool BringWindowToTop(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(nint hWnd);

    // Grants any thread of `dwProcessId` the right to call SetForegroundWindow
    // for one shot. ASFW_ANY (-1) means "any process". Must be called from a
    // thread that currently has the foreground-set right — INotification-
    // ActivationCallback.Activate is one such moment, granted by the shell.
    internal const uint ASFW_ANY = unchecked((uint)-1);
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AllowSetForegroundWindow(uint dwProcessId);

    // Synthesizes input from this process; that synthetic input event
    // re-grants this thread the right to SetForegroundWindow. Used to defeat
    // the foreground lock on cold-start activation where the new process has
    // no foreground rights at all (ASFW_ANY would have nothing to transfer).
    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public uint Type;
        public INPUTUNION U;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public ushort Vk;
        public ushort Scan;
        public uint   Flags;
        public uint   Time;
        public nint   ExtraInfo;
    }

    internal const uint INPUT_KEYBOARD     = 1;
    internal const ushort VK_MENU          = 0x12; // ALT
    internal const uint KEYEVENTF_KEYUP    = 0x2;

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint SendInput(uint cInputs,
        [In] INPUT[] pInputs, int cbSize);

    // FlashWindowEx — paints the taskbar button amber (and optionally flashes
    // the caption) until the window is brought to the foreground. On modern
    // Windows the default user setting is "highlight, don't blink", so the
    // button stays statically lit; older builds and accessibility configs
    // honour dwFlashCount/dwTimeout literally.
    [StructLayout(LayoutKind.Sequential)]
    internal struct FLASHWINFO
    {
        public uint cbSize;
        public nint hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    internal const uint FLASHW_STOP      = 0;
    internal const uint FLASHW_CAPTION   = 0x1;
    internal const uint FLASHW_TRAY      = 0x2;
    internal const uint FLASHW_ALL       = FLASHW_CAPTION | FLASHW_TRAY;
    internal const uint FLASHW_TIMERNOFG = 0xC; // flash until window comes to foreground

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool FlashWindowEx(ref FLASHWINFO pwfi);

    // ── Taskbar overlay icon (ITaskbarList3 + GDI for HICON construction) ────

    internal const uint CLSCTX_INPROC_SERVER = 0x1;

    [LibraryImport("ole32.dll")]
    internal static partial int CoCreateInstance(
        in Guid rclsid, nint pUnkOuter, uint dwClsContext, in Guid riid, out nint ppv);

    [StructLayout(LayoutKind.Sequential)]
    internal struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
        public uint xHotspot;
        public uint yHotspot;
        public nint hbmMask;
        public nint hbmColor;
    }

    // BITMAPINFOHEADER inlined — CreateDIBSection only reads up to biClrUsed
    // for 32bpp BI_RGB, so we omit the trailing colour table.
    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFO
    {
        public uint biSize;
        public int  biWidth;
        public int  biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int  biXPelsPerMeter;
        public int  biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [LibraryImport("user32.dll")]
    internal static partial nint GetDC(nint hWnd);

    [LibraryImport("user32.dll")]
    internal static partial int ReleaseDC(nint hWnd, nint hDC);

    [LibraryImport("gdi32.dll")]
    internal static partial nint CreateDIBSection(
        nint hdc, ref BITMAPINFO pbmi, uint iUsage, out nint ppvBits, nint hSection, uint dwOffset);

    [LibraryImport("gdi32.dll")]
    internal static partial nint CreateBitmap(int nWidth, int nHeight, uint cPlanes, uint cBitsPerPel, nint lpvBits);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteObject(nint hObject);

    [LibraryImport("user32.dll")]
    internal static partial nint CreateIconIndirect(ref ICONINFO piconinfo);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyIcon(nint hIcon);
}
