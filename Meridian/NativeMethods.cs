using System.Runtime.InteropServices;

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
}
