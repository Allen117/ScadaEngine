using Dapper;
using Microsoft.Data.SqlClient;
using ScadaEngine.Common.Data.Services;
using ScadaEngine.Web.Features.ScheduleSetting.Models;

namespace ScadaEngine.Web.Services
{
    /// <summary>
    /// 時間排程 CRUD 服務 — 對應 TimeSchedules 資料表
    /// </summary>
    public class ScheduleSettingService
    {
        private readonly ILogger<ScheduleSettingService> _logger;
        private readonly DatabaseConfigService _configService;
        private string _szConnectionString = string.Empty;

        public ScheduleSettingService(ILogger<ScheduleSettingService> logger, DatabaseConfigService configService)
        {
            _logger = logger;
            _configService = configService;
        }

        private async Task EnsureConnectionStringAsync()
        {
            if (string.IsNullOrEmpty(_szConnectionString))
                _szConnectionString = await _configService.GetConnectionStringAsync();
        }

        /// <summary>取得所有排程</summary>
        public async Task<IEnumerable<TimeScheduleModel>> GetAllSchedulesAsync()
        {
            await EnsureConnectionStringAsync();
            try
            {
                const string szSql = @"
                    SELECT Id              AS nId,
                           Name            AS szName,
                           RecurrenceType  AS nRecurrenceType,
                           RunLength       AS nRunLength,
                           RestLength      AS nRestLength,
                           AnchorDateTime  AS dtAnchorDateTime,
                           DaysOfWeek      AS szDaysOfWeek,
                           DaysOfMonth     AS szDaysOfMonth,
                           StartTime       AS szStartTime,
                           EndTime         AS szEndTime,
                           ExcludeDates    AS szExcludeDates,
                           IncludeDates    AS szIncludeDates,
                           IsEnabled       AS isEnabled,
                           Remarks         AS szRemarks,
                           CreatedAt       AS dtCreatedAt,
                           UpdatedAt       AS dtUpdatedAt
                    FROM TimeSchedules
                    ORDER BY Name, StartTime";

                using var connection = new SqlConnection(_szConnectionString);
                await connection.OpenAsync();
                return await connection.QueryAsync<TimeScheduleModel>(szSql);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取得所有排程失敗");
                return Enumerable.Empty<TimeScheduleModel>();
            }
        }

        /// <summary>新增或更新排程</summary>
        public async Task<bool> SaveScheduleAsync(ScheduleSaveDto dto)
        {
            await EnsureConnectionStringAsync();
            try
            {
                // 解析 anchorDateTime 字串
                DateTime? dtAnchor = null;
                if (!string.IsNullOrWhiteSpace(dto.anchorDateTime) &&
                    DateTime.TryParse(dto.anchorDateTime, out var parsed))
                    dtAnchor = parsed;

                string szSql;
                if (dto.id.HasValue && dto.id.Value > 0)
                {
                    szSql = @"
                        UPDATE TimeSchedules SET
                            Name = @Name, RecurrenceType = @RecurrenceType,
                            RunLength = @RunLength, RestLength = @RestLength,
                            AnchorDateTime = @AnchorDateTime,
                            DaysOfWeek = @DaysOfWeek, DaysOfMonth = @DaysOfMonth,
                            StartTime = @StartTime, EndTime = @EndTime,
                            ExcludeDates = @ExcludeDates, IncludeDates = @IncludeDates,
                            IsEnabled = @IsEnabled, Remarks = @Remarks,
                            UpdatedAt = GETDATE()
                        WHERE Id = @Id";
                }
                else
                {
                    szSql = @"
                        INSERT INTO TimeSchedules
                            (Name, RecurrenceType, RunLength, RestLength, AnchorDateTime,
                             DaysOfWeek, DaysOfMonth, StartTime, EndTime,
                             ExcludeDates, IncludeDates, IsEnabled, Remarks)
                        VALUES
                            (@Name, @RecurrenceType, @RunLength, @RestLength, @AnchorDateTime,
                             @DaysOfWeek, @DaysOfMonth, @StartTime, @EndTime,
                             @ExcludeDates, @IncludeDates, @IsEnabled, @Remarks)";
                }

                using var connection = new SqlConnection(_szConnectionString);
                await connection.OpenAsync();
                var nAffected = await connection.ExecuteAsync(szSql, new
                {
                    Id              = dto.id ?? 0,
                    Name            = dto.name,
                    RecurrenceType  = dto.recurrenceType,
                    RunLength       = dto.runLength,
                    RestLength      = dto.restLength,
                    AnchorDateTime  = dtAnchor,
                    DaysOfWeek      = dto.daysOfWeek,
                    DaysOfMonth     = dto.daysOfMonth,
                    StartTime       = dto.startTime,
                    EndTime         = dto.endTime,
                    ExcludeDates    = string.IsNullOrWhiteSpace(dto.excludeDates) ? null : dto.excludeDates,
                    IncludeDates    = string.IsNullOrWhiteSpace(dto.includeDates) ? null : dto.includeDates,
                    IsEnabled       = dto.isEnabled,
                    Remarks         = dto.remarks
                });

                _logger.LogInformation("儲存排程: Name={Name}, Type={Type}, Affected={Count}",
                    dto.name, dto.recurrenceType, nAffected);
                return nAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "儲存排程失敗: Name={Name}", dto.name);
                return false;
            }
        }

        /// <summary>刪除排程</summary>
        public async Task<bool> DeleteScheduleAsync(int nId)
        {
            await EnsureConnectionStringAsync();
            try
            {
                const string szSql = "DELETE FROM TimeSchedules WHERE Id = @Id";
                using var connection = new SqlConnection(_szConnectionString);
                await connection.OpenAsync();
                var nAffected = await connection.ExecuteAsync(szSql, new { Id = nId });
                _logger.LogInformation("刪除排程: Id={Id}, Affected={Count}", nId, nAffected);
                return nAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "刪除排程失敗: Id={Id}", nId);
                return false;
            }
        }
    }
}
