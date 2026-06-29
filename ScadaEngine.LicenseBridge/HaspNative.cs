using System.Runtime.InteropServices;

namespace ScadaEngine.LicenseBridge;

internal static class HaspNative
{
    private const string Dll = "hasp_windows_103223";

    public const uint HASP_DEFAULT_FID = 0;
    public const uint HASP_STATUS_OK   = 0;

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern uint hasp_login(uint feature, string vendorCode, out uint handle);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint hasp_logout(uint handle);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint hasp_read(uint handle, uint fileId, uint offset, uint length, byte[] buffer);
}
