using System.Runtime.InteropServices;
using System.Text;

const uint HASP_DEFAULT_FID = 0;
const uint HASP_STATUS_OK   = 0;
const string DLL_NAME = "hasp_windows_103223.dll";   // 32-bit DLL

const string VENDOR_CODE =
    "vOQe8R+qGUoa2bEpSBuAvAXpZIUxLChqWSRanfNkQANQvydXGOaV1gdH0ES7PzaKEVVb9tWbEyQ1gZWE" +
    "R/ZPoiRHAZkOU5GGuzRU4s6JwqMq4S4B2rkbcQP3F/JBX2AvIXX4vc5PFXxV9k9/O5OtjG78/awN2BgW" +
    "8dvK1vh0ugrbQ1PIBT8maq0WGyTif2Y5EN3y3dWJ41DOlpT245VfRqMn5gYYf0Cm6Zxp4nIC4UzSgRXL" +
    "YAyTvnLUgLgMAqY4MqAqES9jCNNRm24OJYhtQEjBdUnl4JNtZk0KDtylc0b8Zs1YQ1TmQ0MeTweDf77F" +
    "P7PG+9QrFgvU8I/piY0lKzdv1Js7B5IWniGKoYhLS/Br1lSt2GUTxpqjayDsEJMPIQ71FuDtBhxgzMgd" +
    "M7hcmd20rp8hwfopQFaCHH0w6lqXjJYMMqmp0Q/Z59finTPZ+CrAO2JU7l11WjC9TB+brJ5t4XwW/LBn" +
    "4l6yCk34HrHKEbanSJLh1fs8RkiCdsKAa6Nion86Pg4jS6a6s2hT6pfzVred6BkmK/Z4dEuAlexbk78W" +
    "4sNSqz46JRmHXvICZvB8YfY1QIVqPEqx5vveM2rbjy18E0IyvrseqSkl/6HXk3WFQqVD//gRBelyjsEf" +
    "Us7KbwJvZTo4Dtv2pDT7kzEHoQVqEXb/TlhcvEUF4sIppcbCMJI8oeMraXBn0/8ixq5i/ThqHhHIYWSp" +
    "ECldcsauhV4KICV2m3uGFZhDAjFAfwKLqiaA8YiCF57pGBeYjoGNTzfAtcihazWUp4rE3MZuXeQmxID5" +
    "Wg2Yf/4rcKR80jTwV+hejlpNfD+KhrPdWXVEbdpf6Sw0X/ts7jd0Gczzp0863EXuzv67u8VMY1RQQNoP" +
    "n2E35HAKSRFrwXv0pgNvh3HrxPwLUkayEyFmabGA1H3/5a+iyoGMFrEIXAjmIw598VOGBHEEaRzFC/JN" +
    "jK8WK8TLJbe9+78LpcJFjg==";

// ── 載入 DLL ──────────────────────────────────────────────────────────────────
var dllPath = Path.Combine(AppContext.BaseDirectory, DLL_NAME);
NativeLibrary.TryLoad(dllPath, out IntPtr lib);
NativeLibrary.TryGetExport(lib, "hasp_login",    out IntPtr loginPtr);
NativeLibrary.TryGetExport(lib, "hasp_logout",   out IntPtr logoutPtr);
NativeLibrary.TryGetExport(lib, "hasp_read",     out IntPtr readPtr);
NativeLibrary.TryGetExport(lib, "hasp_get_size", out IntPtr getSizePtr);

// ── 試法 A：handle = uint (32-bit，原本的做法) ────────────────────────────────
Console.WriteLine("=== 試法 A：handle = uint (32-bit) ===");
{
    var fnLogin   = Marshal.GetDelegateForFunctionPointer<Login32Fn>(loginPtr);
    var fnLogout  = Marshal.GetDelegateForFunctionPointer<Logout32Fn>(logoutPtr);
    var fnGetSize = Marshal.GetDelegateForFunctionPointer<GetSize32Fn>(getSizePtr);
    var fnRead    = Marshal.GetDelegateForFunctionPointer<Read32Fn>(readPtr);

    uint st = fnLogin(HASP_DEFAULT_FID, VENDOR_CODE, out uint h32);
    Console.WriteLine($"  login status={st}  handle=0x{h32:X8} ({h32})");

    if (st == HASP_STATUS_OK)
    {
        TryReadAll32(fnGetSize, fnRead, h32);
        fnLogout(h32);
    }
}

Console.WriteLine();

// ── 試法 B：handle = ulong (64-bit) ──────────────────────────────────────────
Console.WriteLine("=== 試法 B：handle = ulong (64-bit) ===");
{
    var fnLogin   = Marshal.GetDelegateForFunctionPointer<Login64Fn>(loginPtr);
    var fnLogout  = Marshal.GetDelegateForFunctionPointer<Logout64Fn>(logoutPtr);
    var fnGetSize = Marshal.GetDelegateForFunctionPointer<GetSize64Fn>(getSizePtr);
    var fnRead    = Marshal.GetDelegateForFunctionPointer<Read64Fn>(readPtr);

    uint st = fnLogin(HASP_DEFAULT_FID, VENDOR_CODE, out ulong h64);
    Console.WriteLine($"  login status={st}  handle=0x{h64:X16} ({h64})");

    if (st == HASP_STATUS_OK)
    {
        TryReadAll64(fnGetSize, fnRead, h64);
        fnLogout(h64);
    }
}

NativeLibrary.Free(lib);
Console.WriteLine("\n完成。");

// ── 輔助：掃描 fileId 0..15 (32-bit handle) ──────────────────────────────────
static void TryReadAll32(GetSize32Fn fnGetSize, Read32Fn fnRead, uint handle)
{
    Console.WriteLine("  hasp_get_size / hasp_read (fileId 0..15):");
    for (uint fid = 0; fid <= 15; fid++)
    {
        uint szSt = fnGetSize(handle, fid, out uint sz);
        var buf = new byte[64];
        uint rdSt = fnRead(handle, fid, 0, 48, buf);

        if (szSt == 0 || rdSt == 0)
        {
            var ascii = System.Text.Encoding.ASCII.GetString(
                buf.Take(20).Select(b => b is >= 32 and < 127 ? b : (byte)'.').ToArray());
            Console.WriteLine($"    fid={fid,2}  get_size={szSt}(sz={sz})  read={rdSt}  ascii=[{ascii}]");
        }
        else
        {
            Console.WriteLine($"    fid={fid,2}  get_size={szSt}  read={rdSt}");
        }
    }
}

// ── 輔助：掃描 fileId 0..15 (64-bit handle) ──────────────────────────────────
static void TryReadAll64(GetSize64Fn fnGetSize, Read64Fn fnRead, ulong handle)
{
    Console.WriteLine("  hasp_get_size / hasp_read (fileId 0..15):");
    for (uint fid = 0; fid <= 15; fid++)
    {
        uint szSt = fnGetSize(handle, fid, out uint sz);
        var buf = new byte[64];
        uint rdSt = fnRead(handle, fid, 0, 48, buf);

        if (szSt == 0 || rdSt == 0)
        {
            var ascii = System.Text.Encoding.ASCII.GetString(
                buf.Take(20).Select(b => b is >= 32 and < 127 ? b : (byte)'.').ToArray());
            Console.WriteLine($"    fid={fid,2}  get_size={szSt}(sz={sz})  read={rdSt}  ascii=[{ascii}]");
        }
        else
        {
            Console.WriteLine($"    fid={fid,2}  get_size={szSt}  read={rdSt}");
        }
    }
}

// ── 型別宣告 ──────────────────────────────────────────────────────────────────

// 試法 A：handle = uint
[UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
delegate uint Login32Fn(uint feature, string vendorCode, out uint handle);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate uint Logout32Fn(uint handle);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate uint GetSize32Fn(uint handle, uint fileId, out uint size);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate uint Read32Fn(uint handle, uint fileId, uint offset, uint length, byte[] buffer);

// 試法 B：handle = ulong
[UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
delegate uint Login64Fn(uint feature, string vendorCode, out ulong handle);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate uint Logout64Fn(ulong handle);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate uint GetSize64Fn(ulong handle, uint fileId, out uint size);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate uint Read64Fn(ulong handle, uint fileId, uint offset, uint length, byte[] buffer);
