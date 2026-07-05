using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// OPC UA Client 共用工具 — ApplicationConfiguration 建立、Session 建立、值轉換。
/// Engine 的 OpcUaCommunicationService 與 Web 的「測試讀取」共用。
/// Phase 1 僅走 SecurityPolicy None + 帳號密碼（或 Anonymous），憑證自動接受。
/// </summary>
public static class OpcUaClientHelper
{
    private static ApplicationConfiguration? _cachedConfig;
    private static readonly SemaphoreSlim _configGate = new(1, 1);
    private static readonly DefaultSessionFactory _sessionFactory = new((ITelemetryContext?)null);

    /// <summary>
    /// 建立（並快取）OPC UA Client ApplicationConfiguration。
    /// 憑證存於 BaseDirectory/OpcUaPki（首次啟動自動產生自簽憑證）。
    /// </summary>
    public static async Task<ApplicationConfiguration> GetConfigurationAsync()
    {
        if (_cachedConfig != null)
            return _cachedConfig;

        await _configGate.WaitAsync();
        try
        {
            if (_cachedConfig != null)
                return _cachedConfig;

            var szPkiRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OpcUaPki");

            var config = new ApplicationConfiguration
            {
                ApplicationName = "ScadaEngine OPC UA Client",
                ApplicationUri = Utils.Format("urn:{0}:ScadaEngine:OpcUaClient", System.Net.Dns.GetHostName()),
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(szPkiRoot, "own"),
                        SubjectName = "CN=ScadaEngine OPC UA Client"
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(szPkiRoot, "issuer")
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(szPkiRoot, "trusted")
                    },
                    RejectedCertificateStore = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(szPkiRoot, "rejected")
                    },
                    AutoAcceptUntrustedCertificates = true,
                    AddAppCertToTrustedStore = true
                },
                TransportConfigurations = new TransportConfigurationCollection(),
                TransportQuotas = new TransportQuotas { OperationTimeout = 15000 },
                ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60000 }
            };

            await config.ValidateAsync(ApplicationType.Client);

            // Phase 1：SecurityPolicy None，Server 憑證一律接受（憑證信任鏈列 Phase 2）
            config.CertificateValidator.CertificateValidation += (s, e) => { e.Accept = true; };

            // 產生/檢查自簽 Client 憑證（部分 Server 即使 SecurityPolicy None 也要求 Client 出示憑證）
            var application = new ApplicationInstance(config, null);
            try
            {
                await application.CheckApplicationInstanceCertificatesAsync(true, 2048);
            }
            catch
            {
                // 憑證產生失敗不阻擋 — SecurityPolicy None 多數 Server 不驗 Client 憑證
            }

            _cachedConfig = config;
            return config;
        }
        finally
        {
            _configGate.Release();
        }
    }

    /// <summary>
    /// 建立 OPC UA Session（SecurityPolicy None）。
    /// </summary>
    /// <param name="szEndpointUrl">opc.tcp://host:port</param>
    /// <param name="szUsername">空字串 = Anonymous</param>
    /// <param name="szPassword">密碼</param>
    /// <param name="nTimeoutMs">連線/操作逾時（毫秒）</param>
    /// <param name="szSessionName">Session 顯示名稱</param>
    public static async Task<ISession> CreateSessionAsync(
        string szEndpointUrl, string szUsername, string szPassword, int nTimeoutMs, string szSessionName,
        CancellationToken ct = default)
    {
        var config = await GetConfigurationAsync();
        var nOperationTimeout = Math.Max(nTimeoutMs, 5000);
        config.TransportQuotas.OperationTimeout = nOperationTimeout;

        // useSecurity: false → 選 SecurityPolicy None 的 endpoint
        var selectedEndpoint = await CoreClientUtils.SelectEndpointAsync(
            config, szEndpointUrl, false, nOperationTimeout, (ITelemetryContext?)null, ct);
        var endpointConfiguration = EndpointConfiguration.Create(config);
        endpointConfiguration.OperationTimeout = nOperationTimeout;
        var endpoint = new ConfiguredEndpoint(null, selectedEndpoint, endpointConfiguration);

        // 1.5.378 起密碼參數改為 byte[]（UTF-8）
        var identity = string.IsNullOrWhiteSpace(szUsername)
            ? new UserIdentity(new AnonymousIdentityToken())
            : new UserIdentity(szUsername, System.Text.Encoding.UTF8.GetBytes(szPassword ?? string.Empty));

        var session = await _sessionFactory.CreateAsync(
            config, endpoint, false, szSessionName, 60000, identity, null, ct);

        return session;
    }

    /// <summary>
    /// 讀取單一節點目前值（取代 1.5.378 已移除的 ReadValue 同步 API）
    /// </summary>
    public static async Task<DataValue?> ReadSingleValueAsync(ISession session, NodeId nodeId, CancellationToken ct = default)
    {
        var response = await session.ReadAsync(null, 0, TimestampsToReturn.Neither,
            new ReadValueIdCollection
            {
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }
            }, ct);
        return response.Results.Count > 0 ? response.Results[0] : null;
    }

    /// <summary>
    /// 安全關閉並釋放 Session（連線已斷時的例外一律吞掉）
    /// </summary>
    public static async Task CloseSessionSafelyAsync(ISession? session)
    {
        if (session == null) return;
        try
        {
            await session.CloseAsync(CancellationToken.None);
        }
        catch
        {
            // 連線已斷時 Close 會丟例外，忽略
        }
        finally
        {
            try { session.Dispose(); } catch { }
        }
    }

    /// <summary>
    /// 將 OPC UA 讀回的原始值轉為 double（bool → 1/0；陣列/不支援型別回 false）
    /// </summary>
    public static bool TryConvertToDouble(object? value, out double dResult)
    {
        dResult = 0;
        if (value == null) return false;

        try
        {
            switch (value)
            {
                case bool b:
                    dResult = b ? 1.0 : 0.0;
                    return true;
                case sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal:
                    dResult = Convert.ToDouble(value);
                    return true;
                case string s:
                    return double.TryParse(s, out dResult);
                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 依 Server 端節點目前值的型別，把要寫入的 raw double 轉成對應 CLR 型別
    /// （寫入值型別不符 Server 會回 Bad_TypeMismatch）。
    /// currentValue 為 null 時原樣回傳 double。
    /// </summary>
    public static object ConvertToServerType(object? currentValue, double dRawValue)
    {
        return currentValue switch
        {
            bool => dRawValue > 0.5,
            sbyte => (sbyte)Math.Round(dRawValue),
            byte => (byte)Math.Round(dRawValue),
            short => (short)Math.Round(dRawValue),
            ushort => (ushort)Math.Round(dRawValue),
            int => (int)Math.Round(dRawValue),
            uint => (uint)Math.Round(dRawValue),
            long => (long)Math.Round(dRawValue),
            ulong => (ulong)Math.Round(dRawValue),
            float => (float)dRawValue,
            double => dRawValue,
            decimal => (decimal)dRawValue,
            string => dRawValue.ToString("G"),
            _ => dRawValue
        };
    }
}
