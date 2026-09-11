namespace ScadaEngine.ModbusServer.Core;

/// <summary>float32 的 word order（ModScan 術語對照見 docs/功能說明書_ModbusServerGateway.md）</summary>
public enum FloatWordOrder
{
    /// <summary>高字組在前（ModScan「Swapped FP」；本專案採集端 DataType=SWAPPEDFP 同義）</summary>
    ABCD,

    /// <summary>低字組在前（ModScan「Floating Pt」慣例，預設）</summary>
    CDAB,
}

/// <summary>
/// float32 → 2 個 Modbus register 的編碼（純邏輯，無 I/O）。
///
/// 定義：float 的 IEEE754 big-endian 四位元組記為 A B C D（A=最高位元組）。
/// Modbus 線路上每個 register 為 2 bytes big-endian，故：
/// - ABCD：reg[n]=(A,B)、reg[n+1]=(C,D)
/// - CDAB：reg[n]=(C,D)、reg[n+1]=(A,B)
///
/// 品質不良統一用 quiet NaN 0x7FC00000（非 Modbus 例外 — 批次讀取整包成功，
/// 僅壞點自己的 2 個 register 呈 NaN 位元樣式）。
/// </summary>
public static class FloatRegisterEncoder
{
    /// <summary>規格指定的 quiet NaN（0x7FC00000）。注意 .NET float.NaN 是 0xFFC00000，不可直接用。</summary>
    public static readonly float QuietNaN = BitConverter.Int32BitsToSingle(0x7FC00000);

    /// <summary>編碼為兩個 register 數值（reg0 = 低位址 register）</summary>
    public static (ushort r0, ushort r1) Encode(float fValue, FloatWordOrder order)
    {
        var nBits = (uint)BitConverter.SingleToInt32Bits(fValue);
        byte a = (byte)(nBits >> 24), b = (byte)(nBits >> 16), c = (byte)(nBits >> 8), d = (byte)nBits;
        ushort ab = (ushort)((a << 8) | b), cd = (ushort)((c << 8) | d);
        return order == FloatWordOrder.ABCD ? (ab, cd) : (cd, ab);
    }

    /// <summary>
    /// 將 float 寫入 FluentModbus 的 register buffer（buffer 位元組序 = 線路位元組序，
    /// 已由 ScadaEngine.Tests 以 loopback client 實測驗證）。
    /// </summary>
    /// <param name="buffer">GetInputRegisterBuffer() 取得的原始位元組 buffer</param>
    /// <param name="nAddress">0-based register 位址（每點佔 nAddress 與 nAddress+1）</param>
    public static void WriteToBuffer(Span<byte> buffer, int nAddress, float fValue, FloatWordOrder order)
    {
        var (r0, r1) = Encode(fValue, order);
        int nOffset = nAddress * 2;
        buffer[nOffset] = (byte)(r0 >> 8);
        buffer[nOffset + 1] = (byte)r0;
        buffer[nOffset + 2] = (byte)(r1 >> 8);
        buffer[nOffset + 3] = (byte)r1;
    }

    public static FloatWordOrder ParseWordOrder(string? szWordOrder)
    {
        return string.Equals(szWordOrder, "ABCD", StringComparison.OrdinalIgnoreCase)
            ? FloatWordOrder.ABCD
            : FloatWordOrder.CDAB;
    }
}
