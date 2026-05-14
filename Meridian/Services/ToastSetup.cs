using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Meridian.Diagnostics;

namespace Meridian.Services;

// Wires up the prerequisites the Windows shell demands before it will display
// toast notifications from an unpackaged app:
//   1. An AUMID set on the current process. We choose "Meridian.App".
//   2. A Start Menu shortcut whose System.AppUserModel.ID property matches that
//      AUMID. Without the shortcut, ToastNotificationManager.Show() silently
//      drops toasts and ScheduledToastNotification never fires.
// Both operations are idempotent: SetCurrentProcessExplicitAppUserModelID is
// safe to call repeatedly, and the shortcut is rewritten only when its target
// path or AUMID drifts (e.g. the .exe moved).
//
// AOT note: COM activation goes through CoCreateInstance + GetObjectForIUnknown
// rather than `new CShellLink()` (which is reflection-based and gets trimmed
// under PublishAot=true). PROPVARIANT is declared with explicit layout so the
// VT_LPWSTR pointer sits at the right offset.
internal static partial class ToastSetup
{
    // The AUMID we want the shell to use. Written into the .lnk's
    // System.AppUserModel.ID property. On a clean Windows install where the
    // shell reads that property cleanly, this is also what ResolvedAumid ends
    // up being. On Win11 25H2 (build 26200) we observed that the shell does
    // NOT read AppUserModel-properties from .lnk files; instead it caches a
    // synthetic Microsoft.Explorer.Notification.{GUID} per shortcut and routes
    // toasts under that AUMID. ResolvedAumid below asks the shell which AUMID
    // it actually associates with our shortcut — that is the only string
    // ScheduledToastNotification will accept.
    public const string PreferredAumid = "Meridian.App";

    // The AUMID the shell will actually route toasts to. Computed once at
    // startup via IApplicationResolver::GetAppIDForShortcut. Falls back to
    // PreferredAumid if the resolver call fails (e.g. shortcut isn't there
    // yet on the very first launch, before EnsureStartMenuShortcut runs).
    public static string ResolvedAumid { get; private set; } = PreferredAumid;

    // Older name kept for callers that don't need the distinction. Resolves
    // lazily so post-EnsureRegistered call sites see the shell-resolved value.
    public static string AppUserModelId => ResolvedAumid;

    private const string ShortcutFileName = "Meridian.lnk";
    private const string DisplayName = "Meridian";

    // Bump when the shortcut generation logic changes in a way that needs
    // existing shortcuts overwritten on next launch — e.g. switching from
    // CLR-marshalled IPropertyStore.SetValue to direct vtable invocation
    // (the former silently dropped the AUMID property on Win11 26200), or
    // adding the ToastActivatorCLSID property (v3).
    // The stamp file lives next to other Meridian data so users can delete
    // %APPDATA%\Meridian to fully reset.
    private const int ShortcutSchemaVersion = 4;

    public static void EnsureRegistered()
    {
        try
        {
            // Order matters. The AppUserModelId registry block is the single
            // most important step on Win10/11: without it the Windows Push
            // Notification Platform never allocates an endpoint for our AUMID,
            // so AddToSchedule succeeds but the toast is silently dropped
            // (Event 3049 "Endpoint 0x0 cleared" in PushNotification log).
            // The .lnk path is legacy Win8 — kept for Start Menu presence and
            // taskbar consistency, but not what WNP actually keys off.
            EnsureClsidRegistered();
            EnsureAumidRegistered();
            var created = EnsureStartMenuShortcut();

            // Even though the AUMID registry block is authoritative, asking
            // IApplicationResolver still gives us the canonical string the
            // shell associates with this user — kept for diagnostic value.
            var shortcutPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                ShortcutFileName);
            var resolved = ResolveAumidForShortcut(shortcutPath);
            if (!string.IsNullOrEmpty(resolved)) ResolvedAumid = resolved!;

            SetCurrentProcessExplicitAppUserModelID(ResolvedAumid);
            ToastActivatorHost.EnsureRegistered();

            Log.Write("Toast",
                $"setup: preferred={PreferredAumid} resolved={ResolvedAumid} shortcut={(created ? "wrote" : "ok")}");
        }
        catch (Exception ex)
        {
            // Toasts are non-critical — never break startup over them.
            Log.Error("Toast", ex, "EnsureRegistered");
        }
    }

    // Writes the AUMID registration block that Win10/11's notification
    // platform looks up to find the activator and DisplayName for an
    // unpackaged app. Without these values the AUMID is invisible to WNP
    // even though the shell sees it via IApplicationResolver.
    //
    // Same shape that Microsoft.Toolkit.Uwp.Notifications' Compat layer writes.
    // Idempotent — values are only set when they differ from what's there.
    private static void EnsureAumidRegistered()
    {
        var subKey = $@"Software\Classes\AppUserModelId\{PreferredAumid}";
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(subKey, writable: true);
        if (key is null) return;

        SetIfDifferent(key, "DisplayName", DisplayName);
        SetIfDifferent(key, "CustomActivator", $"{{{ToastActivatorIds.ClsidString}}}");
        SetIfDifferent(key, "IconBackgroundColor", "FFDDDDDD");

        var iconPath = ResolveIconPath();
        if (!string.IsNullOrEmpty(iconPath))
            SetIfDifferent(key, "IconUri", iconPath);
    }

    private static void SetIfDifferent(Microsoft.Win32.RegistryKey key, string name, string value)
    {
        var current = key.GetValue(name) as string;
        if (!string.Equals(current, value, StringComparison.Ordinal))
            key.SetValue(name, value, Microsoft.Win32.RegistryValueKind.String);
    }

    private static string? ResolveIconPath()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return null;
        var dir = Path.GetDirectoryName(exe);
        if (string.IsNullOrEmpty(dir)) return null;
        var candidate = Path.Combine(dir, "Assets", "icon.ico");
        return File.Exists(candidate) ? candidate : null;
    }

    // Asks the shell which AUMID it associates with this .lnk. Uses the
    // undocumented IApplicationResolver::GetAppIDForShortcut — the same path
    // taskbar pinning and Action Center use internally. Returns null on any
    // failure (resolver absent, shortcut missing, shell shrug). Cited in
    // Process Hacker's phlib/appresolver.c.
    private static string? ResolveAumidForShortcut(string shortcutPath)
    {
        if (!File.Exists(shortcutPath)) return null;

        IntPtr pResolver = IntPtr.Zero;
        IntPtr pItem = IntPtr.Zero;
        IntPtr pszAppId = IntPtr.Zero;
        try
        {
            var clsidResolver = CLSID_ApplicationResolver;
            var iidResolver = IID_IApplicationResolver;
            var hr = CoCreateInstance(ref clsidResolver, IntPtr.Zero, CLSCTX_INPROC_SERVER,
                                       ref iidResolver, out pResolver);
            if (hr < 0) { Log.Write("Toast", $"resolve: CoCreate IApplicationResolver hr=0x{hr:x8}"); return null; }

            var iidShellItem = IID_IShellItem;
            hr = SHCreateItemFromParsingName(shortcutPath, IntPtr.Zero, ref iidShellItem, out pItem);
            if (hr < 0) { Log.Write("Toast", $"resolve: SHCreateItemFromParsingName hr=0x{hr:x8}"); return null; }

            // IApplicationResolver vtable: 0–2 IUnknown, 3 GetAppIDForShortcut,
            // 4 GetAppIDForShortcutObject, ... — we only need slot 3.
            hr = InvokeGetAppIDForShortcut(pResolver, pItem, out pszAppId);
            if (hr < 0)
            {
                Log.Write("Toast", $"resolve: GetAppIDForShortcut hr=0x{hr:x8}");
                return null;
            }

            return pszAppId == IntPtr.Zero ? null : Marshal.PtrToStringUni(pszAppId);
        }
        catch (Exception ex)
        {
            Log.Error("Toast", ex, "ResolveAumidForShortcut");
            return null;
        }
        finally
        {
            if (pszAppId != IntPtr.Zero) Marshal.FreeCoTaskMem(pszAppId);
            if (pItem != IntPtr.Zero) Marshal.Release(pItem);
            if (pResolver != IntPtr.Zero) Marshal.Release(pResolver);
        }
    }

    private static unsafe int InvokeGetAppIDForShortcut(IntPtr pResolver, IntPtr pItem, out IntPtr pszAppId)
    {
        var vtbl = *(IntPtr**)pResolver;
        // HRESULT GetAppIDForShortcut(IShellItem*, LPWSTR*)
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)vtbl[3];
        IntPtr local;
        var hr = fn(pResolver, pItem, &local);
        pszAppId = local;
        return hr;
    }

    // Registers the toast-activator CLSID under HKCU\Software\Classes\CLSID so
    // Windows can find and launch our process when the user clicks a toast.
    // LocalServer32 points at the current .exe; the shell will spawn it with
    // the standard "-Embedding" flag. We're idempotent: if the value is
    // already correct we do nothing, which avoids unnecessary registry churn.
    private static void EnsureClsidRegistered()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return;

        var subKey = $@"Software\Classes\CLSID\{{{ToastActivatorIds.ClsidString}}}\LocalServer32";
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(subKey, writable: true);
        if (key is null) return;

        var existing = key.GetValue(null) as string;
        // The shell appends arbitrary RPC arguments after the path, so the
        // command line must be in quotes. Single set, written verbatim.
        var desired = $"\"{exePath}\"";
        if (!string.Equals(existing, desired, StringComparison.OrdinalIgnoreCase))
            key.SetValue(null, desired);
    }

    // Returns true if the shortcut was (re)written this call, false if the
    // existing one was already up to date.
    private static bool EnsureStartMenuShortcut()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return false;

        var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        if (string.IsNullOrEmpty(startMenu)) return false;

        Directory.CreateDirectory(startMenu);
        var shortcutPath = Path.Combine(startMenu, ShortcutFileName);

        // Two-layer freshness check:
        //   1. A versioned stamp file forces a rewrite whenever the shortcut
        //      generation logic changes, even if path/AUMID appear correct to
        //      ShortcutMatches (which can lie when the property roundtrips
        //      through CLR's broken VARIANT marshalling).
        //   2. ShortcutMatches still avoids needless writes on the steady
        //      state — version unchanged, path correct, AUMID present.
        var stamped = HasCurrentVersionStamp();
        if (stamped && File.Exists(shortcutPath) && ShortcutMatches(shortcutPath, exePath))
            return false;

        WriteShortcut(shortcutPath, exePath);
        WriteVersionStamp();
        return true;
    }

    private static string VersionStampPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Meridian", "toast-setup.version");

    private static bool HasCurrentVersionStamp()
    {
        try
        {
            return File.Exists(VersionStampPath)
                && int.TryParse(File.ReadAllText(VersionStampPath).Trim(), out var v)
                && v == ShortcutSchemaVersion;
        }
        catch { return false; }
    }

    private static void WriteVersionStamp()
    {
        try
        {
            var path = VersionStampPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, ShortcutSchemaVersion.ToString());
        }
        catch { /* best-effort */ }
    }

    // Cheap pre-check: if the existing .lnk already points at exePath and has
    // the right AUMID + ToastActivatorCLSID properties, leave it alone.
    // Avoids re-writing on every launch (which churns mtime and forces shell
    // re-indexing of the AUMID — slow and sometimes flaky).
    //
    // GetValue is invoked through the raw vtable for the same reason SetValue
    // is: CLR's [ComImport] RCW marshals PROPVARIANT as OLE VARIANT, which
    // produces garbage when reading a VT_LPWSTR/VT_CLSID-typed property and
    // makes us mistakenly believe the shortcut is wrong on every launch.
    // Verifies an existing .lnk looks like ours.
    //
    // We tried reading the property store two ways: via fresh-ShellLink+Load
    // and via SHGetPropertyStoreFromParsingName. Both returned VT_EMPTY for
    // AUMID on Win11 26200 even though the property is present in the on-disk
    // bytes (confirmed with hex dump and WScript.Shell). Whatever shell quirk
    // this is, we cannot rely on it for the steady-state check.
    //
    // Pragmatic fallback: check the legacy target path via IShellLink.GetPath
    // (which does work), then byte-scan the file for our AUMID UTF-16 string
    // and the binary GUID layout of our ToastActivatorCLSID. If both signatures
    // are present in the right file, we own it. Collision risk is negligible
    // — the AUMID is unique to this app and the CLSID even more so.
    private static bool ShortcutMatches(string shortcutPath, string exePath)
    {
        // ── path check via classic ShellLink ─────────────────────────────────
        IntPtr pUnk = IntPtr.Zero;
        IShellLinkW? link = null;
        IPersistFile? pf = null;
        try
        {
            var clsid = CLSID_ShellLink;
            var iidShellLink = IID_IShellLinkW;
            var hr = CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER,
                                       ref iidShellLink, out pUnk);
            if (hr < 0) { Log.Write("Toast", $"match: CoCreate failed 0x{hr:x8}"); return false; }

            link = (IShellLinkW)Marshal.GetObjectForIUnknown(pUnk);
            pf = (IPersistFile)link;
            try { pf.Load(shortcutPath, 0); }
            catch (Exception ex) { Log.Write("Toast", $"match: Load failed: {ex.Message}"); return false; }

            var sb = new StringBuilder(260);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            var gotPath = sb.ToString();
            if (!string.Equals(gotPath, exePath, StringComparison.OrdinalIgnoreCase))
            {
                Log.Write("Toast", $"match: path mismatch got='{gotPath}' want='{exePath}'");
                return false;
            }
        }
        catch (Exception ex)
        {
            Log.Write("Toast", $"match: unexpected (path) {ex.GetType().Name}: {ex.Message}");
            return false;
        }
        finally
        {
            if (pf != null) Marshal.ReleaseComObject(pf);
            if (link != null) Marshal.ReleaseComObject(link);
            if (pUnk != IntPtr.Zero) Marshal.Release(pUnk);
        }

        // ── binary signature check ───────────────────────────────────────────
        try
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(shortcutPath); }
            catch (Exception ex) { Log.Write("Toast", $"match: read .lnk failed: {ex.Message}"); return false; }

            // Sanity-scan for the AUMID we *wrote* (PreferredAumid), not the
            // resolved one — the shell may translate it on read, but the
            // bytes we authored remain.
            var aumidBytes = System.Text.Encoding.Unicode.GetBytes(PreferredAumid);
            if (IndexOf(bytes, aumidBytes) < 0)
            {
                Log.Write("Toast", "match: AUMID UTF-16 sequence not present");
                return false;
            }

            var clsidBytes = ToastActivatorIds.Clsid.ToByteArray();
            if (IndexOf(bytes, clsidBytes) < 0)
            {
                Log.Write("Toast", "match: ActivatorCLSID GUID bytes not present");
                return false;
            }

            Log.Write("Toast", "match: ok");
            return true;
        }
        catch (Exception ex)
        {
            Log.Write("Toast", $"match: unexpected (scan) {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || needle.Length > haystack.Length) return -1;
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { match = false; break; }
            }
            if (match) return i;
        }
        return -1;
    }

    private static unsafe int InvokeGetValue(IntPtr pStore, ref PROPERTYKEY key, out PROPVARIANT pv)
    {
        var vtbl = *(IntPtr**)pStore;
        // slot 5: HRESULT GetValue(IPropertyStore*, REFPROPERTYKEY, PROPVARIANT*)
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, PROPERTYKEY*, PROPVARIANT*, int>)vtbl[5];
        pv = default;
        fixed (PROPERTYKEY* pKey = &key)
        fixed (PROPVARIANT* pVar = &pv)
            return fn(pStore, pKey, pVar);
    }

    private static void WriteShortcut(string shortcutPath, string exePath)
    {
        // We deliberately stay at the IUnknown* level and call IPropertyStore
        // vtable slots through function-pointer thunks. Going through the CLR
        // [ComImport] IPropertyStore RCW means the runtime marshals our
        // PROPVARIANT struct as if it were OLE VARIANT, which produces a
        // shortcut that Shell silently ignores (no AUMID stored). Observed on
        // Win11 26200: ps.SetValue returns S_OK but the property never lands
        // in the file. Direct vtable invocation bypasses that variant coercion.
        IntPtr pUnk = IntPtr.Zero, pStore = IntPtr.Zero;
        IShellLinkW? link = null;
        IPersistFile? pf = null;
        PROPVARIANT pv = default;
        bool pvOwned = false;
        try
        {
            // Create the ShellLink coclass and grab IShellLinkW + IPropertyStore
            // from the same instance.
            var clsid = CLSID_ShellLink;
            var iidShellLink = IID_IShellLinkW;
            var iidPropertyStore = IID_IPropertyStore;
            Marshal.ThrowExceptionForHR(
                CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, ref iidShellLink, out pUnk));
            link = (IShellLinkW)Marshal.GetObjectForIUnknown(pUnk);
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(pUnk, in iidPropertyStore, out pStore));

            link.SetPath(exePath);
            var dir = Path.GetDirectoryName(exePath);
            if (!string.IsNullOrEmpty(dir)) link.SetWorkingDirectory(dir);
            link.SetDescription(DisplayName);

            // 1. AUMID (VT_LPWSTR). Write the PreferredAumid; the shell may
            // map it to a different runtime AUMID (see ResolvedAumid), but
            // what lands in the file is what we author here.
            var keyAumid = PKEY_AppUserModel_ID;
            pv = MakeStringPropVariant(PreferredAumid);
            pvOwned = true;
            // IPropertyStore vtable: 0–2 IUnknown, 3 GetCount, 4 GetAt,
            // 5 GetValue, 6 SetValue, 7 Commit.
            Marshal.ThrowExceptionForHR(InvokeSetValue(pStore, ref keyAumid, ref pv));
            PropVariantClear(ref pv);
            pvOwned = false;

            // 2. ToastActivatorCLSID (VT_CLSID). Required for unpackaged apps:
            //    without this property Shell silently filters scheduled toasts
            //    and the AUMID never appears in Settings → Notifications.
            var keyActivator = PKEY_AppUserModel_ToastActivatorCLSID;
            pv = MakeClsidPropVariant(ToastActivatorIds.Clsid);
            pvOwned = true;
            Marshal.ThrowExceptionForHR(InvokeSetValue(pStore, ref keyActivator, ref pv));
            PropVariantClear(ref pv);
            pvOwned = false;

            Marshal.ThrowExceptionForHR(InvokeCommit(pStore));

            pf = (IPersistFile)link;
            pf.Save(shortcutPath, fRemember: true);

            // Notify shell about the new/updated shortcut. Without this Windows
            // sometimes does not rescan AppUserModel properties on existing
            // .lnk files, which means the AUMID never registers in Action
            // Center and Settings → Notifications stays missing our entry.
            // SHCNE_CREATE = 0x2; SHCNF_PATH = 0x1.
            var pathPtr = Marshal.StringToCoTaskMemUni(shortcutPath);
            try { SHChangeNotify(0x2, 0x1, pathPtr, IntPtr.Zero); }
            finally { Marshal.FreeCoTaskMem(pathPtr); }
        }
        finally
        {
            if (pvOwned) PropVariantClear(ref pv);
            if (pf != null) Marshal.ReleaseComObject(pf);
            if (link != null) Marshal.ReleaseComObject(link);
            if (pStore != IntPtr.Zero) Marshal.Release(pStore);
            if (pUnk != IntPtr.Zero) Marshal.Release(pUnk);
        }
    }

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private static unsafe int InvokeSetValue(IntPtr pStore, ref PROPERTYKEY key, ref PROPVARIANT pv)
    {
        var vtbl = *(IntPtr**)pStore;
        // slot 6: HRESULT SetValue(IPropertyStore*, REFPROPERTYKEY, REFPROPVARIANT)
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, PROPERTYKEY*, PROPVARIANT*, int>)vtbl[6];
        fixed (PROPERTYKEY* pKey = &key)
        fixed (PROPVARIANT* pVar = &pv)
            return fn(pStore, pKey, pVar);
    }

    private static unsafe int InvokeCommit(IntPtr pStore)
    {
        var vtbl = *(IntPtr**)pStore;
        // slot 7: HRESULT Commit(IPropertyStore*)
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, int>)vtbl[7];
        return fn(pStore);
    }

    // InitPropVariantFromString is documented as living in propsys.dll but is
    // missing on some Windows builds (observed: Win11 26200). Build the
    // VT_LPWSTR variant by hand: a CoTaskMem-allocated wide string, which is
    // exactly what PropVariantClear knows how to release.
    private static PROPVARIANT MakeStringPropVariant(string value)
    {
        var ptr = Marshal.StringToCoTaskMemUni(value);
        return new PROPVARIANT { vt = VT_LPWSTR, p = ptr };
    }

    // VT_CLSID stores a pointer to a 16-byte GUID in CoTaskMem. PropVariantClear
    // frees it for us. Used for System.AppUserModel.ToastActivatorCLSID.
    private static unsafe PROPVARIANT MakeClsidPropVariant(Guid clsid)
    {
        var ptr = Marshal.AllocCoTaskMem(sizeof(Guid));
        *(Guid*)ptr = clsid;
        return new PROPVARIANT { vt = VT_CLSID, p = ptr };
    }

    // AOT-safe activation: CoCreateInstance returns a raw IUnknown that we
    // wrap into an RCW. Avoids the [ComImport] coclass + Activator path.
    private static IShellLinkW CreateShellLink()
    {
        var clsid = CLSID_ShellLink;
        var iid = IID_IShellLinkW;
        Marshal.ThrowExceptionForHR(
            CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, ref iid, out var ptr));
        try
        {
            return (IShellLinkW)Marshal.GetObjectForIUnknown(ptr);
        }
        finally
        {
            Marshal.Release(ptr);
        }
    }

    // ── P/Invoke + COM ────────────────────────────────────────────────────────

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SetCurrentProcessExplicitAppUserModelID(string AppID);

    private const uint CLSCTX_INPROC_SERVER = 0x1;

    private static Guid CLSID_ShellLink = new("00021401-0000-0000-C000-000000000046");
    private static Guid IID_IShellLinkW = new("000214F9-0000-0000-C000-000000000046");
    private static readonly Guid IID_IPropertyStore = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");

    // Undocumented IApplicationResolver — the shell uses this internally to
    // map a shortcut to its AUMID. Stable since Windows 7. CLSID and IID from
    // Process Hacker's phlib/appresolver.c.
    private static Guid CLSID_ApplicationResolver = new("660B90C8-73A9-4B58-8CAE-355B7F55341B");
    private static Guid IID_IApplicationResolver  = new("DE25675A-72DE-44B4-9373-05170450C140");
    private static Guid IID_IShellItem            = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHCreateItemFromParsingName(
        string pszPath, IntPtr pbc, ref Guid riid, out IntPtr ppv);

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(
        ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
                     int cch, IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath,
                             int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PROPERTYKEY pkey);
        void GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        void SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    // Win32 PROPVARIANT is a 16-byte union: 2-byte vt + 6 bytes of padding/
    // reserved fields, then an 8-byte union body. We only care about VT_LPWSTR,
    // whose payload (an LPWSTR pointer) lives at offset 8.
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr p;
    }

    private const ushort VT_LPWSTR = 31;
    private const ushort VT_CLSID = 72;

    private static PROPERTYKEY PKEY_AppUserModel_ID = new()
    {
        fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        pid = 5,
    };

    private static PROPERTYKEY PKEY_AppUserModel_ToastActivatorCLSID = new()
    {
        fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        pid = 26,
    };

    [LibraryImport("ole32.dll")]
    private static partial int PropVariantClear(ref PROPVARIANT pvar);

    private static string? PropVariantToString(PROPVARIANT pv)
    {
        if (pv.vt == VT_LPWSTR && pv.p != IntPtr.Zero) return Marshal.PtrToStringUni(pv.p);
        return null;
    }
}
