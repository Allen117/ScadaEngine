namespace ScadaEngine.Web.Features.TariffSetting.Models;

/// <summary>POST api/recalculate — 以目前生效方案重算指定區間電費</summary>
public class RecalculateRequest
{
    /// <summary>起日（yyyy-MM-dd，含）</summary>
    public string start { get; set; } = string.Empty;

    /// <summary>訖日（yyyy-MM-dd，含 — 後端展開為隔日 00:00 exclusive）</summary>
    public string end { get; set; } = string.Empty;
}
