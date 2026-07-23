using System.Collections.Concurrent;
using ScadaEngine.Web.Features.ScadaPage.Models;

namespace ScadaEngine.Web.Services;

/// <summary>
/// ScadaPage 迴路指標元件的程序內結果快取（Singleton）。
/// 指標計算比單點累積重（虛擬迴路遞迴葉子 + boundary 查詢 + 電費彙總），
/// 以 60s TTL 結果快取 + per-key 計算鎖（防 stampede）把 30s 輪詢負載壓到與 EMS 首頁同量級。
/// key = "{circuitId}|{metric}"
/// </summary>
public class WidgetCircuitMetricCache
{
    private readonly ConcurrentDictionary<string, (CircuitMetricResultDto dto, DateTime dtExpire)> _results = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public static string ResultKey(CircuitMetricQueryItem item) => $"{item.nCircuitId}|{item.szMetric}";

    public bool TryGetResult(string szKey, DateTime dtNow, out CircuitMetricResultDto dto)
    {
        dto = null!;
        if (!_results.TryGetValue(szKey, out var entry)) return false;
        if (entry.dtExpire <= dtNow)
        {
            _results.TryRemove(szKey, out _);
            return false;
        }
        dto = entry.dto;
        return true;
    }

    public void SetResult(string szKey, CircuitMetricResultDto dto, DateTime dtExpire)
        => _results[szKey] = (dto, dtExpire);

    public SemaphoreSlim GetLock(string szKey) => _locks.GetOrAdd(szKey, _ => new SemaphoreSlim(1, 1));
}
