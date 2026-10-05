// Chart.js 全站共用預設（各頁在 chart.umd.min.js 之後、頁面 JS 之前載入）
// ============================================================
// 長條寬度上限：柱寬預設 = 繪圖區寬 ÷ 柱數 × 0.72，柱數少時（年 12 柱、SEU 5 棟）
// 會粗到 95~246px。設上限只壓粗柱，柱數多時（31 天、24 時、並排比較）照舊自動變細。
// 個別圖若要不同寬度，在該 dataset 自行指定 maxBarThickness / barThickness 即可覆寫。
(function () {
    if (typeof Chart === 'undefined') return;
    Chart.defaults.datasets.bar.maxBarThickness = 40;
})();
