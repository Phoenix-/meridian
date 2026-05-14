using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Meridian.Diagnostics;
using Microsoft.UI.Dispatching;

namespace Meridian.Services;

// Implements the COM end of Windows' "toast activator" protocol. When the user
// clicks a toast or interacts with one of its buttons, the shell looks up the
// ToastActivatorCLSID attached to the AUMID, instantiates a COM object that
// implements INotificationActivationCallback, and calls Activate(appUserModelId,
// args, data, dataCount). For an unpackaged app this is also the registration
// step that makes the AUMID appear in Settings → Notifications — without it
// scheduled toasts are silently filtered out by Action Center.
//
// AOT story:
//   * Interface is declared with [GeneratedComInterface] so the source-gen
//     produces an AOT-safe vtable shim (no reflection-based RCW/CCW).
//   * Activator class is [GeneratedComClass] for the same reason on the CCW
//     side. Strata-Generated COM is the supported path under PublishAot.
//   * A single ClassFactory is registered once via CoRegisterClassObject; the
//     ROT entry survives for the life of the process.
//
// The CLSID is a stable per-app GUID generated once and burned in. Bump it
// only if you fork the activator into something the user must distinguish in
// the registry from the previous build.
internal static class ToastActivatorIds
{
    // Stable Meridian toast-activator CLSID. Picked once; do not change.
    public const string ClsidString = "8A1B6C7D-9E3F-4A2B-B5C8-7D4E9F1A2C3D";
    public static readonly Guid Clsid = new(ClsidString);
}

[GeneratedComInterface]
[Guid("53E31837-6600-4A81-9395-75CFFE746F94")]
internal partial interface INotificationActivationCallback
{
    // Called by Windows on the COM RPC thread when a toast is interacted with.
    // appUserModelId — our AUMID. invokedArgs — the toast's launch attribute
    // (or any "arguments" key on the clicked action). data/dataCount — value
    // pairs from input fields; we don't use them.
    void Activate(
        [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string invokedArgs,
        [MarshalUsing(CountElementName = nameof(dataCount))] NOTIFICATION_USER_INPUT_DATA[] data,
        uint dataCount);
}

[NativeMarshalling(typeof(NotificationUserInputDataMarshaller))]
internal struct NOTIFICATION_USER_INPUT_DATA
{
    public string Key;
    public string Value;
}

// Hand-written marshaller because the native struct is two LPWSTR pointers
// passed by value, which the source-gen marshaller cannot infer from the
// managed string fields alone.
[CustomMarshaller(typeof(NOTIFICATION_USER_INPUT_DATA), MarshalMode.Default,
                  typeof(NotificationUserInputDataMarshaller))]
internal static unsafe class NotificationUserInputDataMarshaller
{
    public struct Native
    {
        public IntPtr Key;
        public IntPtr Value;
    }

    public static Native ConvertToUnmanaged(NOTIFICATION_USER_INPUT_DATA managed) => new()
    {
        Key = Marshal.StringToCoTaskMemUni(managed.Key),
        Value = Marshal.StringToCoTaskMemUni(managed.Value),
    };

    public static NOTIFICATION_USER_INPUT_DATA ConvertToManaged(Native native) => new()
    {
        Key = Marshal.PtrToStringUni(native.Key) ?? "",
        Value = Marshal.PtrToStringUni(native.Value) ?? "",
    };

    public static void Free(Native native)
    {
        if (native.Key != IntPtr.Zero) Marshal.FreeCoTaskMem(native.Key);
        if (native.Value != IntPtr.Zero) Marshal.FreeCoTaskMem(native.Value);
    }
}

[GeneratedComClass]
internal partial class MeridianToastActivator : INotificationActivationCallback
{
    public static event Action<string>? Invoked;

    public void Activate(string appUserModelId, string invokedArgs,
                         NOTIFICATION_USER_INPUT_DATA[] data, uint dataCount)
    {
        Log.Write("Toast", $"activated: args='{invokedArgs}'");
        try { Invoked?.Invoke(invokedArgs ?? ""); }
        catch (Exception ex) { Log.Error("Toast", ex, "Activate handler"); }
    }
}

// Owns the lifecycle of the COM class object registration. EnsureRegistered
// is called at startup; the registration cookie is kept alive for the
// duration of the process via the static field.
internal static partial class ToastActivatorHost
{
    private const uint CLSCTX_LOCAL_SERVER = 0x4;
    private const uint REGCLS_MULTIPLEUSE = 0x1;
    private const uint REGCLS_SUSPENDED  = 0x4;

    private static uint _cookie;
    private static StrategyBasedComWrappers? _wrappers;
    private static MeridianToastActivator? _instance;

    public static void EnsureRegistered()
    {
        if (_cookie != 0) return;
        try
        {
            _wrappers = new StrategyBasedComWrappers();
            _instance = new MeridianToastActivator();
            var unk = _wrappers.GetOrCreateComInterfaceForObject(_instance, CreateComInterfaceFlags.None);
            try
            {
                var clsid = ToastActivatorIds.Clsid;
                Marshal.ThrowExceptionForHR(
                    CoRegisterClassObject(ref clsid, unk, CLSCTX_LOCAL_SERVER,
                        REGCLS_MULTIPLEUSE | REGCLS_SUSPENDED, out _cookie));
                Marshal.ThrowExceptionForHR(CoResumeClassObjects());
                Log.Write("Toast", $"activator class registered cookie={_cookie}");
            }
            finally
            {
                Marshal.Release(unk);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Toast", ex, "ToastActivatorHost.EnsureRegistered");
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoRegisterClassObject(
        ref Guid rclsid, IntPtr pUnk, uint dwClsContext, uint flags, out uint lpdwRegister);

    [LibraryImport("ole32.dll")]
    private static partial int CoResumeClassObjects();
}
