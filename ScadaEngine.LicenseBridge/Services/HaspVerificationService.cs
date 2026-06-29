using System.IO.Pipes;
using ScadaEngine.LicenseBridge;

namespace ScadaEngine.LicenseBridge.Services;

public class HaspVerificationService : BackgroundService
{
    private const string PIPE_NAME = "ScadaEngineLicense";

    private const string VENDOR_CODE =
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

    private readonly ILogger<HaspVerificationService> _logger;

    public HaspVerificationService(ILogger<HaspVerificationService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("HASP License Bridge 啟動 (Named Pipe: {Pipe})", PIPE_NAME);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await HandleOnePipeConnectionAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pipe 連線處理錯誤，1 秒後重試");
                try { await Task.Delay(1000, stoppingToken); } catch { break; }
            }
        }

        _logger.LogInformation("HASP License Bridge 停止");
    }

    private async Task HandleOnePipeConnectionAsync(CancellationToken ct)
    {
        using var server = new NamedPipeServerStream(
            PIPE_NAME,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        await server.WaitForConnectionAsync(ct);

        using var reader = new StreamReader(server, leaveOpen: true);
        using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };

        var command = await reader.ReadLineAsync(ct);
        string reply;

        if (command?.Trim() == "verify")
        {
            var (ok, reason) = VerifyHasp();
            reply = ok ? "ok" : $"fail:{reason}";
            _logger.LogInformation("verify → {Reply}", reply);
        }
        else
        {
            reply = "fail:未知指令";
            _logger.LogWarning("未知指令: {Command}", command);
        }

        await writer.WriteLineAsync(reply);
    }

    private static (bool ok, string reason) VerifyHasp()
    {
        uint status = HaspNative.hasp_login(HaspNative.HASP_DEFAULT_FID, VENDOR_CODE, out uint handle);
        if (status != HaspNative.HASP_STATUS_OK)
            return (false, $"HASP Login 失敗: {status}");

        HaspNative.hasp_logout(handle);
        return (true, "");
    }
}
