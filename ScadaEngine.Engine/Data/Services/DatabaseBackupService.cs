using Dapper;
using Microsoft.Data.SqlClient;
using ScadaEngine.Common.Data.Services;
using ScadaEngine.Engine.Models;

namespace ScadaEngine.Engine.Data.Services;

/// <summary>
/// 每週資料庫自動備份背景服務。
/// 依 DbMaintenanceSetting.json 排程（預設週日 03:00）執行 BACKUP DATABASE — 純 T-SQL，
/// 不依賴 SQL Server Agent（Express 版無 Agent），db_owner 權限即可執行。
///
/// 備份檔採 A/B 兩檔輪替（{db}_A.bak / {db}_B.bak，WITH INIT 覆寫較舊者），
/// 恆保留「本次 + 上一次」兩份，磁碟用量可預測。
/// 備份壓縮依版本自動判斷：Standard/Enterprise 加 WITH COMPRESSION，Express 不支援則略過。
///
/// 注意：備份檔由 SQL Server 服務進程寫入，BackupFolder 需授權 SQL 服務帳號
/// （由 Setting/install-db.ps1 處理）。結果寫入 EventLog（EventType=3，SID=_system）。
/// </summary>
public class DatabaseBackupService : BackgroundService
{
    private readonly ILogger<DatabaseBackupService> _logger;
    private readonly DatabaseConfigService _configService;

    /// <summary>備份失敗後的重試間隔 — 當日內每小時重試，跨日則等下週排程</summary>
    private static readonly TimeSpan RETRY_INTERVAL = TimeSpan.FromHours(1);

    public DatabaseBackupService(
        ILogger<DatabaseBackupService> logger,
        DatabaseConfigService configService)
    {
        _logger = logger;
        _configService = configService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var setting = DbMaintenanceSettingModel.LoadFromDefaultPaths(_logger);
        if (!setting.BackupEnabled)
        {
            _logger.LogInformation("每週資料庫備份已停用（BackupEnabled=false），排程服務閒置");
            return;
        }

        var dayOfWeek = setting.GetBackupDayOfWeek();
        var triggerTime = setting.GetBackupTime();
        _logger.LogInformation(
            "每週資料庫備份服務啟動: {Day} {Time}，備份路徑={Folder}",
            dayOfWeek, triggerTime.ToString(@"hh\:mm"), setting.BackupFolder);

        await StartupSelfCheckAsync(setting);

        // 主迴圈：每 30 秒檢查是否到達排定時點（同 EnergyLeafAggregationService 模式）
        DateTime? dtLastBackupDate = null;
        var dtNextAllowedAttempt = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dtNow = DateTime.Now;
                var isDue = dtNow.DayOfWeek == dayOfWeek
                            && dtNow.TimeOfDay >= triggerTime
                            && dtLastBackupDate != dtNow.Date
                            && dtNow >= dtNextAllowedAttempt;

                if (isDue)
                {
                    var isSuccess = await RunBackupAsync(setting, stoppingToken);
                    if (isSuccess)
                    {
                        dtLastBackupDate = dtNow.Date;
                    }
                    else
                    {
                        // 失敗 → 當日內每小時重試；跨日後 isDue 的 DayOfWeek 條件自然失效，等下週
                        dtNextAllowedAttempt = dtNow.Add(RETRY_INTERVAL);
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "資料庫備份主迴圈發生錯誤");
                try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        _logger.LogInformation("每週資料庫備份服務已停止");
    }

    /// <summary>啟動自檢：備份資料夾缺失或最新備份逾期時提前示警，避免每週靜默失敗</summary>
    private Task StartupSelfCheckAsync(DbMaintenanceSettingModel setting)
    {
        try
        {
            if (!Directory.Exists(setting.BackupFolder))
            {
                _logger.LogWarning(
                    "備份資料夾 {Folder} 不存在 — 請以系統管理員執行 Setting/install-db.ps1 建立資料夾並授權 SQL 服務帳號",
                    setting.BackupFolder);
                return Task.CompletedTask;
            }

            var dtNewest = GetBackupFileCandidates(setting)
                .Where(File.Exists)
                .Select(f => File.GetLastWriteTime(f))
                .OrderByDescending(t => t)
                .FirstOrDefault();

            if (dtNewest == default)
            {
                _logger.LogInformation("備份資料夾 {Folder} 尚無備份檔，將於下次排定時間執行首次備份", setting.BackupFolder);
            }
            else if (DateTime.Now - dtNewest > TimeSpan.FromDays(8))
            {
                _logger.LogWarning(
                    "最新資料庫備份為 {Newest}，已超過 8 天 — Engine 可能曾在排定時間停機，將於下次排定時間補上",
                    dtNewest);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "備份啟動自檢失敗（不影響排程運作）");
        }
        return Task.CompletedTask;
    }

    /// <summary>執行一次完整備份（A/B 輪替），成功/失敗都寫 EventLog</summary>
    private async Task<bool> RunBackupAsync(DbMaintenanceSettingModel setting, CancellationToken stoppingToken)
    {
        var config = await _configService.LoadConfigAsync();
        var szDbName = config.szDataBaseName;
        var szTargetFile = PickRotationTarget(setting, szDbName);

        _logger.LogInformation("開始資料庫備份: {Db} → {File}", szDbName, szTargetFile);
        var dtStart = DateTime.Now;

        try
        {
            using var connection = new SqlConnection(config.BuildConnectionString());
            await connection.OpenAsync(stoppingToken);

            // EngineEdition: 2=Standard, 3=Enterprise 支援備份壓縮；4=Express 不支援
            var nEdition = await connection.ExecuteScalarAsync<int>("SELECT CAST(SERVERPROPERTY('EngineEdition') AS int)");
            var isCompressionSupported = nEdition == 2 || nEdition == 3;

            var szSafeName = szDbName.Replace("]", "]]");
            var szOptions = isCompressionSupported ? "WITH INIT, FORMAT, COMPRESSION" : "WITH INIT, FORMAT";
            await connection.ExecuteAsync(
                $"BACKUP DATABASE [{szSafeName}] TO DISK = @Path {szOptions}",
                new { Path = szTargetFile },
                commandTimeout: 3600);

            var duration = DateTime.Now - dtStart;
            var szSize = TryGetFileSizeText(szTargetFile);
            var szMessage = $"資料庫備份成功: {Path.GetFileName(szTargetFile)}{szSize}，耗時 {duration.TotalSeconds:F0} 秒" +
                            (isCompressionSupported ? "（壓縮）" : string.Empty);

            _logger.LogInformation("{Message}", szMessage);
            await WriteEventLogAsync(szMessage, nSeverity: 3);
            return true;
        }
        catch (Exception ex)
        {
            var szMessage = $"資料庫備份失敗: {Path.GetFileName(szTargetFile)} — {ex.Message}";
            _logger.LogError(ex, "資料庫備份失敗: {Db} → {File}（請確認備份資料夾存在且 SQL 服務帳號有寫入權，可執行 Setting/install-db.ps1 修復）",
                szDbName, szTargetFile);
            await WriteEventLogAsync(szMessage, nSeverity: 2);
            return false;
        }
    }

    /// <summary>A/B 輪替：優先寫不存在的檔，兩檔都在則覆寫較舊者</summary>
    private static string PickRotationTarget(DbMaintenanceSettingModel setting, string szDbName)
    {
        var candidates = GetBackupFileCandidates(setting, szDbName);
        foreach (var szFile in candidates)
        {
            if (!File.Exists(szFile)) return szFile;
        }
        return candidates.OrderBy(f => File.GetLastWriteTime(f)).First();
    }

    private static string[] GetBackupFileCandidates(DbMaintenanceSettingModel setting, string? szDbName = null)
    {
        // 自檢階段 DB 名尚未載入時，以萬用字元列出既有 .bak
        if (szDbName == null)
        {
            return Directory.Exists(setting.BackupFolder)
                ? Directory.GetFiles(setting.BackupFolder, "*.bak")
                : Array.Empty<string>();
        }

        return new[]
        {
            Path.Combine(setting.BackupFolder, $"{szDbName}_A.bak"),
            Path.Combine(setting.BackupFolder, $"{szDbName}_B.bak")
        };
    }

    /// <summary>備份檔由 SQL 服務進程寫入，Engine 帳號不一定可讀 — 讀不到就省略大小資訊</summary>
    private static string TryGetFileSizeText(string szFile)
    {
        try
        {
            var info = new FileInfo(szFile);
            if (!info.Exists) return string.Empty;
            return $"（{info.Length / 1024.0 / 1024.0:F1} MB）";
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 寫入備份結果 EventLog（EventType=3 資訊；成功 Severity=3 低、失敗 Severity=2 中）。
    /// SID 沿用系統級事件慣例 "_system"（同 NotifyDeliveryLogger 測試寄送）。
    /// </summary>
    private async Task WriteEventLogAsync(string szMessage, int nSeverity)
    {
        try
        {
            var szConnectionString = await _configService.GetConnectionStringAsync();
            const string szSql = @"
                INSERT INTO EventLog (SID, EventType, Severity, Message, OccurredAt)
                VALUES ('_system', 3, @Severity, @Message, GETDATE())";

            using var connection = new SqlConnection(szConnectionString);
            await connection.OpenAsync();
            await connection.ExecuteAsync(szSql, new
            {
                Severity = nSeverity,
                Message = szMessage.Length <= 500 ? szMessage : szMessage.Substring(0, 497) + "..."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "寫入備份結果 EventLog 失敗");
        }
    }
}
