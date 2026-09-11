using System.Net;
using System.Net.Sockets;
using FluentModbus;
using ScadaEngine.ModbusServer.Core;

namespace ScadaEngine.Tests.ModbusServer;

/// <summary>
/// FluentModbus loopback 整合測試：驗證「寫進 server buffer 的位元組 = 線路位元組」，
/// 把 word order 的正確性釘死在真實 TCP 請求/回應上（loopback、隨機空埠、跑完即關）。
/// </summary>
public class FluentModbusWireFormatTests
{
    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var nPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return nPort;
    }

    [Fact]
    public void 寫入Buffer後_FC4讀回的線路位元組正確_含NaN()
    {
        var nPort = GetFreeTcpPort();
        using var server = new ModbusTcpServer();
        // 與正式服務相同配置：註冊 unit 1、移除預設 unit 0（FluentModbus 對未註冊 unit id 直接斷線）
        server.AddUnit(1);
        server.RemoveUnit(0);
        server.Start(new IPEndPoint(IPAddress.Loopback, nPort));
        try
        {
            lock (server.Lock)
            {
                var buffer = server.GetInputRegisterBuffer(1);
                // 123.456f = 0x42F6E979（A=42 B=F6 C=E9 D=79）
                FloatRegisterEncoder.WriteToBuffer(buffer, 0, 123.456f, FloatWordOrder.CDAB);
                FloatRegisterEncoder.WriteToBuffer(buffer, 2, 123.456f, FloatWordOrder.ABCD);
                FloatRegisterEncoder.WriteToBuffer(buffer, 4, FloatRegisterEncoder.QuietNaN, FloatWordOrder.CDAB);
            }

            var client = new ModbusTcpClient();
            client.Connect(new IPEndPoint(IPAddress.Loopback, nPort), ModbusEndianness.BigEndian);
            try
            {
                // 低階 API 回傳 FC4 回應的原始資料位元組（= 線路位元組）
                var raw = client.ReadInputRegisters(unitIdentifier: 1, startingAddress: 0, quantity: 6).ToArray();

                Assert.Equal(12, raw.Length);
                // CDAB：低字組在前 → E9 79 42 F6
                Assert.Equal(new byte[] { 0xE9, 0x79, 0x42, 0xF6 }, raw[0..4]);
                // ABCD：高字組在前 → 42 F6 E9 79
                Assert.Equal(new byte[] { 0x42, 0xF6, 0xE9, 0x79 }, raw[4..8]);
                // NaN 0x7FC00000 CDAB → 00 00 7F C0
                Assert.Equal(new byte[] { 0x00, 0x00, 0x7F, 0xC0 }, raw[8..12]);
            }
            finally
            {
                client.Disconnect();
            }
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public void 批次讀取_只有壞點呈NaN_其餘正常()
    {
        var nPort = GetFreeTcpPort();
        using var server = new ModbusTcpServer();
        server.AddUnit(1);
        server.RemoveUnit(0);
        server.Start(new IPEndPoint(IPAddress.Loopback, nPort));
        try
        {
            lock (server.Lock)
            {
                var buffer = server.GetInputRegisterBuffer(1);
                FloatRegisterEncoder.WriteToBuffer(buffer, 0, 11.5f, FloatWordOrder.CDAB);
                FloatRegisterEncoder.WriteToBuffer(buffer, 2, FloatRegisterEncoder.QuietNaN, FloatWordOrder.CDAB);
                FloatRegisterEncoder.WriteToBuffer(buffer, 4, 33.25f, FloatWordOrder.CDAB);
            }

            var client = new ModbusTcpClient();
            client.Connect(new IPEndPoint(IPAddress.Loopback, nPort), ModbusEndianness.BigEndian);
            try
            {
                // 一次讀 6 registers（3 個 float）：整包成功、只有中間點是 NaN
                var raw = client.ReadInputRegisters(unitIdentifier: 1, startingAddress: 0, quantity: 6).ToArray();
                var fValues = new float[3];
                for (int i = 0; i < 3; i++)
                {
                    // CDAB → 還原 big-endian A B C D = raw[i*4+2], raw[i*4+3], raw[i*4], raw[i*4+1]
                    var nBits = (raw[i * 4 + 2] << 24) | (raw[i * 4 + 3] << 16) | (raw[i * 4] << 8) | raw[i * 4 + 1];
                    fValues[i] = BitConverter.Int32BitsToSingle(nBits);
                }

                Assert.Equal(11.5f, fValues[0]);
                Assert.True(float.IsNaN(fValues[1]));
                Assert.Equal(33.25f, fValues[2]);
            }
            finally
            {
                client.Disconnect();
            }
        }
        finally
        {
            server.Stop();
        }
    }
}
