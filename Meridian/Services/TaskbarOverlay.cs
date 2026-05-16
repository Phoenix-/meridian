using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Meridian.Diagnostics;
using Windows.UI;

namespace Meridian.Services;

// Wraps ITaskbarList3::SetOverlayIcon — paints a small badge in the lower-right
// corner of the taskbar button. We use it to show a coloured dot while a timed
// event is in progress, complementing the FlashWindowEx amber highlight that
// fires at reminder time.
//
// AOT note: the project ships with PublishAot=true, so the COM call goes
// through [GeneratedComInterface] (no IDispatch / RCW reflection). The HICON
// is built with CreateIconIndirect from an in-memory 32bpp ARGB bitmap —
// avoids dragging GDI+ in just for a 16-pixel circle.
//
// All public methods marshal the call onto the UI thread's HWND; pass any
// Color (the alpha channel is honoured) or null to clear.
internal static class TaskbarOverlay
{
    private static ITaskbarList3? _taskbar;
    private static nint _currentIcon;

    public static void SetCircle(Color? color)
    {
        var hwnd = TryGetHwnd();
        if (hwnd == 0) return;

        try
        {
            var taskbar = EnsureTaskbar();
            if (taskbar is null) return;

            if (color is null)
            {
                taskbar.SetOverlayIcon(hwnd, 0, null);
                DestroyCurrentIcon();
                return;
            }

            var icon = CreateCircleIcon(color.Value);
            if (icon == 0)
            {
                taskbar.SetOverlayIcon(hwnd, 0, null);
                DestroyCurrentIcon();
                return;
            }

            taskbar.SetOverlayIcon(hwnd, icon, null);
            DestroyCurrentIcon();
            _currentIcon = icon;
        }
        catch (Exception ex)
        {
            Log.Error("Overlay", ex, "SetCircle");
        }
    }

    private static unsafe ITaskbarList3? EnsureTaskbar()
    {
        if (_taskbar is not null) return _taskbar;

        var clsid = new Guid("56FDF344-FD6D-11D0-958A-006097C9A090"); // CLSID_TaskbarList
        var iid   = typeof(ITaskbarList3).GUID;
        int hr = NativeMethods.CoCreateInstance(in clsid, 0, NativeMethods.CLSCTX_INPROC_SERVER, in iid, out var ptr);
        if (hr < 0 || ptr == 0)
        {
            Log.Write("Overlay", $"CoCreateInstance(TaskbarList) failed hr=0x{hr:x8}");
            return null;
        }

        // ComInterfaceMarshaller.ConvertToManaged takes ownership of the
        // ref CoCreateInstance gave us — no Release() here.
        var obj = ComInterfaceMarshaller<ITaskbarList3>.ConvertToManaged((void*)ptr);
        if (obj is null) return null;
        obj.HrInit();
        _taskbar = obj;
        return obj;
    }

    private static unsafe nint CreateCircleIcon(Color color)
    {
        const int Size = 16;
        var pixels = new uint[Size * Size];

        // Anti-aliased filled disc. Centre at (7.5, 7.5), radius 7 with a 1px
        // soft edge — keeps the dot crisp at 16×16 without obvious staircase.
        const double Cx = 7.5, Cy = 7.5, R = 7.0;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                double dx = x - Cx, dy = y - Cy;
                double d = Math.Sqrt(dx * dx + dy * dy);
                double cover = Math.Clamp(R - d + 0.5, 0.0, 1.0);
                if (cover <= 0) continue;

                byte a = (byte)Math.Round(color.A * cover);
                // BGRA premultiplied — CreateIconIndirect with a 32bpp DIB
                // section expects premultiplied alpha for proper compositing
                // on the taskbar.
                byte r = (byte)(color.R * a / 255);
                byte g = (byte)(color.G * a / 255);
                byte b = (byte)(color.B * a / 255);
                pixels[y * Size + x] = (uint)((a << 24) | (r << 16) | (g << 8) | b);
            }
        }

        var bmi = new NativeMethods.BITMAPINFO
        {
            biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFO>(),
            biWidth = Size,
            biHeight = -Size, // top-down
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0, // BI_RGB
        };

        nint hdc = NativeMethods.GetDC(0);
        nint hBitmap;
        try
        {
            hBitmap = NativeMethods.CreateDIBSection(hdc, ref bmi, 0, out var bits, 0, 0);
            if (hBitmap == 0 || bits == 0) return 0;
            fixed (uint* src = pixels)
                Buffer.MemoryCopy(src, (void*)bits, pixels.Length * 4, pixels.Length * 4);
        }
        finally
        {
            NativeMethods.ReleaseDC(0, hdc);
        }

        // Mask bitmap is required by CreateIconIndirect even for 32bpp ARGB
        // sources. A 1bpp all-zero mask means "use the alpha channel".
        nint hMask = NativeMethods.CreateBitmap(Size, Size, 1, 1, 0);
        if (hMask == 0)
        {
            NativeMethods.DeleteObject(hBitmap);
            return 0;
        }

        var info = new NativeMethods.ICONINFO
        {
            fIcon = true,
            xHotspot = 0,
            yHotspot = 0,
            hbmMask = hMask,
            hbmColor = hBitmap,
        };
        nint icon = NativeMethods.CreateIconIndirect(ref info);
        NativeMethods.DeleteObject(hBitmap);
        NativeMethods.DeleteObject(hMask);
        return icon;
    }

    private static void DestroyCurrentIcon()
    {
        if (_currentIcon == 0) return;
        NativeMethods.DestroyIcon(_currentIcon);
        _currentIcon = 0;
    }

    private static nint TryGetHwnd()
    {
        try
        {
            var window = App.MainWindow;
            if (window is null) return 0;
            return WinRT.Interop.WindowNative.GetWindowHandle(window);
        }
        catch (Exception ex)
        {
            Log.Error("Overlay", ex, "resolve HWND");
            return 0;
        }
    }
}

[GeneratedComInterface]
[Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF")] // IID_ITaskbarList3
internal partial interface ITaskbarList3
{
    void HrInit();
    void AddTab(nint hwnd);
    void DeleteTab(nint hwnd);
    void ActivateTab(nint hwnd);
    void SetActiveAlt(nint hwnd);
    // ITaskbarList2
    void MarkFullscreenWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);
    // ITaskbarList3
    void SetProgressValue(nint hwnd, ulong ullCompleted, ulong ullTotal);
    void SetProgressState(nint hwnd, int tbpFlags);
    void RegisterTab(nint hwndTab, nint hwndMDI);
    void UnregisterTab(nint hwndTab);
    void SetTabOrder(nint hwndTab, nint hwndInsertBefore);
    void SetTabActive(nint hwndTab, nint hwndMDI, uint dwReserved);
    void ThumbBarAddButtons(nint hwnd, uint cButtons, nint pButton);
    void ThumbBarUpdateButtons(nint hwnd, uint cButtons, nint pButton);
    void ThumbBarSetImageList(nint hwnd, nint himl);
    void SetOverlayIcon(nint hwnd, nint hIcon, [MarshalAs(UnmanagedType.LPWStr)] string? pszDescription);
    void SetThumbnailTooltip(nint hwnd, [MarshalAs(UnmanagedType.LPWStr)] string? pszTip);
    void SetThumbnailClip(nint hwnd, nint prcClip);
}
