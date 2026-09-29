// ============================================================
// scadapage/widget-trend.js — 即時曲線 widget（滾動趨勢圖）執行期
// ============================================================
// 不包 IIFE、global scope，與其餘 js/scadapage/ 模組同模式；
// 共享狀態（_trendCharts / _nTrendSeq / TREND_BACKFILL_CONCURRENCY）在 state.js。
//
// 資料來源分兩段（plan 決策 1）：
//   1. 掛載時打一次 /api/history/data 補滿「近 N 分鐘」→ 曲線一開始就是滿的
//   2. 之後完全靠 ScadaPage 既有的每秒 /api/realtime/latest 迴圈 append
// 穩態下整頁零 DB 查詢 —— 即時值本來就已經在前端手上，再去查 DB 是重複做功。
//
// 「滾動」由 tickTrendCharts() 每秒推進 x 軸 min/max 實現（右端進、左端出）。
// Engine 停擺時點位 timestamp 不前進 → _trendAppend 直接拒收，曲線停在最後有效時間，
// 不會畫出一條「持續有值」的平直假線（plan 決策 3）。
// ============================================================

    // 畫面重繪節奏（ms）。資料收集固定跟著 1 秒即時輪詢，這裡只管「多久重畫一次」。
    // 一頁多圖實測若 CPU 吃緊，把這個數字調大即可（資料完整性不受影響）。
    var TREND_REDRAW_MS = 1000;

    // 時間軸標籤格式：時間窗短看到秒、長看到時分
    var TREND_TIME_DISPLAY_FORMATS = {
        second: 'HH:mm:ss',
        minute: 'HH:mm',
        hour:   'HH:mm'
    };

    var _trendScale      = 1;      // 畫布等比縮放倍率（_applyCanvasScale 同步進來）
    var _nTrendLastDraw  = 0;      // 上次重繪時間戳（節流用）
    var _arrTrendQueue   = [];     // 待 backfill 的 widget id
    var _nTrendInFlight  = 0;      // 進行中的 backfill 請求數

    // ── 時間字串 → epoch ms ──
    // 即時 API 給 'yyyy-MM-dd HH:mm:ss'、歷史 API 給 'yyyy-MM-ddTHH:mm:ss'，
    // 兩者皆為「伺服器本地時間、無時區標記」→ 統一補 T 後交給 Date 以本地時區解析。
    function _trendParseTs(szTs) {
        if (!szTs || szTs === '--') return NaN;
        return new Date(String(szTs).replace(' ', 'T')).getTime();
    }

    // Date → 'yyyy-MM-ddTHH:mm:ss'（本地時間；不可用 toISOString，那會轉成 UTC 差 8 小時）
    function _trendFmtLocal(dt) {
        function p2(n) { return (n < 10 ? '0' : '') + n; }
        return dt.getFullYear() + '-' + p2(dt.getMonth() + 1) + '-' + p2(dt.getDate()) +
               'T' + p2(dt.getHours()) + ':' + p2(dt.getMinutes()) + ':' + p2(dt.getSeconds());
    }

    // ============================================================
    // 建立 / 銷毀
    // ============================================================
    // 由 renderScadaCanvas 在整批 widget 都 append 進畫布之後呼叫（Chart.js 需要
    // canvas 已在 DOM 內才量得到容器尺寸）。冪等：已建立過的 widget 不重複建。
    function initTrendCharts(canvasEl) {
        var arrEls = (canvasEl || document).querySelectorAll('.scada-trend');
        for (var i = 0; i < arrEls.length; i++) {
            var el = arrEls[i];
            if (!el.id || _trendCharts[el.id]) continue;
            createTrendChart(el, el._trendProps || {});
        }
    }

    function createTrendChart(el, props) {
        var szSid = props.szSid || '';
        var cv    = el.querySelector('canvas.scada-trend-canvas');
        if (!cv) return;

        // 未綁定點位（例如整頁複製後 szSid 被清空）→ 不建 Chart、不查 DB，只留提示
        if (!szSid) {
            cv.style.display = 'none';
            var tip = el.querySelector('.scada-trend-unbound');
            if (tip) tip.style.display = '';
            return;
        }

        var nWindowSec = Math.max(60, parseInt(props.nWindowSec, 10) || 1800);
        var nSampleSec = window.TrendWindow.effectiveSampleSec(nWindowSec, props.nSampleSec);
        var szLine     = props.szLineColor || '#0d6efd';
        var szGrid     = props.szGridColor || '#cccccc';
        var nGridCount = Math.max(0, Math.min(10, props.nGridCount != null ? props.nGridCount : 5));
        var szUnit     = props.szUnit || '';
        var szLabel    = props.szPointName || props.szTitle || szSid;

        var arrData = [];   // 與 chart.data.datasets[0].data 同一個陣列物件（就地增刪即反映）
        var nNow    = Date.now();

        var yScale = {
            grid:   { color: szGrid, drawTicks: false },
            border: { display: false },
            ticks:  {
                // nGridCount 條「區隔線」+ 上下邊界 = count
                count: nGridCount + 2,
                font: { size: 9 },
                color: '#868e96',
                callback: function (v) { return _trendFmtTick(v); }
            }
        };
        // 留空 = 自動：沿用歷史趨勢頁慣例，上下各留 10% 餘裕免得線貼邊
        if (props.fYMin != null && props.fYMin !== '') yScale.min = +props.fYMin;
        if (props.fYMax != null && props.fYMax !== '') yScale.max = +props.fYMax;
        if (yScale.min == null && yScale.max == null) yScale.grace = '10%';

        // Chart.js responsive 量的是 plot 層 → 建 Chart 前先把 plot 定好螢幕像素尺寸
        _trendSnapPlot(el, _trendScale);

        var chart = new Chart(cv.getContext('2d'), {
            type: 'line',
            data: {
                datasets: [{
                    label:           szLabel + (szUnit ? ' (' + szUnit + ')' : ''),
                    data:            arrData,
                    borderColor:     szLine,
                    backgroundColor: szLine,
                    borderWidth:     Math.max(1, props.nLineWidth || 2),
                    pointRadius:     0,          // 滾動圖不畫點，只有線
                    pointHitRadius:  6,
                    tension:         0.25,
                    fill:            false,
                    // 品質 BAD 期間以 y=null 斷開，不用 0 或前值補 —— 把故障畫成正常比沒有曲線更糟
                    spanGaps:        false
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                // 每秒重繪，任何動畫都是純浪費
                animation: false,
                // data 已是排序好的 {x,y}，關掉 parsing / 標記 normalized 省掉每次 update 的重解析
                parsing: false,
                normalized: true,
                // devicePixelRatio 不指定（= 螢幕原生 DPR）：canvas 已由 plot 層抵銷畫布縮放，
                // 以螢幕像素 1:1 渲染；字級 / 線寬另由 _trendApplyScaleOpts 乘上縮放倍率
                interaction: { mode: 'nearest', axis: 'x', intersect: false },
                layout: { padding: { top: 4, right: 6, bottom: 0, left: 0 } },
                plugins: {
                    legend: {
                        display: !!props.isShowLegend,
                        labels: { boxWidth: 10, boxHeight: 2, font: { size: 10 }, color: '#495057' }
                    },
                    tooltip: {
                        displayColors: false,
                        callbacks: {
                            label: function (ctx) {
                                return _trendFmtTick(ctx.parsed.y) + (szUnit ? ' ' + szUnit : '');
                            }
                        }
                    }
                },
                scales: {
                    x: {
                        type: 'time',
                        min:  nNow - nWindowSec * 1000,
                        max:  nNow,
                        time: { tooltipFormat: 'yyyy-MM-dd HH:mm:ss', displayFormats: TREND_TIME_DISPLAY_FORMATS },
                        // 垂直格線不畫：規格只要求 Y 軸區隔線數，也讓執行期外觀與 Designer 預覽一致
                        grid:   { display: false },
                        border: { color: szGrid },
                        ticks:  { maxTicksLimit: 6, maxRotation: 0, autoSkipPadding: 12,
                                  font: { size: 9 }, color: '#868e96' }
                    },
                    y: yScale
                }
            }
        });

        var nLineWidth = Math.max(1, props.nLineWidth || 2);
        _trendApplyScaleOpts(chart, _trendScale, nLineWidth);
        chart.update('none');

        _trendCharts[el.id] = {
            chart:      chart,
            el:         el,
            nLineWidth: nLineWidth,
            szSid:      szSid,
            nWindowSec: nWindowSec,
            // 允許 10% 抖動：Engine 每 5 秒一筆時，5.0s/4.9s 交替不該被丟掉一半
            nStepMs:    Math.max(1, nSampleSec) * 900,
            arr:        arrData,
            bBackfilled: false
        };

        _enqueueTrendBackfill(el.id);
    }

    // 數值顯示：整數不補小數、否則兩位（Y 軸刻度與 tooltip 共用）
    function _trendFmtTick(v) {
        if (v == null || isNaN(v)) return '--';
        var f = Number(v);
        return Number.isInteger(f) ? String(f) : f.toFixed(2);
    }

    // 切頁 / 重渲染前一律呼叫：Chart.js 實例不 destroy 就會連同 resize listener 一起洩漏
    function destroyAllTrendCharts() {
        for (var szId in _trendCharts) {
            var entry = _trendCharts[szId];
            if (entry && entry.chart) {
                try { entry.chart.destroy(); } catch (_) { /* canvas 已被移除 */ }
            }
        }
        _trendCharts   = {};
        _arrTrendQueue = [];   // 尚未發出的 backfill 一併作廢，避免打完回來找不到 widget
    }

    // ============================================================
    // Backfill（掛載時補滿時間窗，只做一次）
    // ============================================================
    // 一頁 N 張圖 = N 個 /api/history/data 同時發 → 首載尖峰打 DB。限流 3 併發。
    function _enqueueTrendBackfill(szElId) {
        _arrTrendQueue.push(szElId);
        _pumpTrendBackfill();
    }

    function _pumpTrendBackfill() {
        while (_nTrendInFlight < TREND_BACKFILL_CONCURRENCY && _arrTrendQueue.length > 0) {
            var szElId = _arrTrendQueue.shift();
            _nTrendInFlight++;
            backfillTrendChart(szElId).finally(function () {
                _nTrendInFlight--;
                _pumpTrendBackfill();
            });
        }
    }

    async function backfillTrendChart(szElId) {
        var entry = _trendCharts[szElId];
        if (!entry || entry.bBackfilled) return;

        var dtEnd   = new Date();
        var dtStart = new Date(dtEnd.getTime() - entry.nWindowSec * 1000);
        var szUrl = '/api/history/data?szSID=' + encodeURIComponent(entry.szSid) +
                    '&szStart=' + encodeURIComponent(_trendFmtLocal(dtStart)) +
                    '&szEnd='   + encodeURIComponent(_trendFmtLocal(dtEnd)) +
                    '&nInterval=0';
        try {
            var resp = await fetch(szUrl);
            if (!resp.ok) return;
            var result = await resp.json();
            // 切頁可能已把 widget 銷毀（await 期間 DOM 重建）
            if (_trendCharts[szElId] !== entry) return;
            if (!result.success || !result.data) return;

            // 歷史原始資料可能每秒一筆 → 依畫點間隔下採樣
            var arrOld = [];
            for (var i = 0; i < result.data.length; i++) {
                var d    = result.data[i];
                var nMs  = _trendParseTs(d.t);
                if (!isFinite(nMs)) continue;
                var bBad = (d.q !== 'Good');
                var last = arrOld.length ? arrOld[arrOld.length - 1] : null;
                if (bBad) {
                    if (last && last.y === null) continue;
                    arrOld.push({ x: nMs, y: null });
                    continue;
                }
                if (last && (nMs - last.x) < entry.nStepMs) continue;
                arrOld.push({ x: nMs, y: d.v });
            }

            // backfill 是 async，期間每秒輪詢可能已 append 了較新的點 →
            // 只把「早於現有最舊點」的歷史接到前面，不覆蓋既有資料
            var nFirstLive = entry.arr.length ? entry.arr[0].x : Infinity;
            var arrPrepend = arrOld.filter(function (p) { return p.x < nFirstLive; });
            if (arrPrepend.length > 0) {
                Array.prototype.unshift.apply(entry.arr, arrPrepend);
                entry.chart.update('none');
            }
        } catch (err) {
            console.warn('即時曲線 backfill 失敗：', entry.szSid, err && err.message);
        } finally {
            // 成功或失敗都不重試 —— 之後靠即時值慢慢長，不做週期性 DB 查詢
            if (_trendCharts[szElId] === entry) entry.bBackfilled = true;
        }
    }

    // ============================================================
    // 即時 append（由 updateScadaWidgets 每秒呼叫）
    // ============================================================
    // 以「點位自身 timestamp」去重：時間沒前進就不收，Engine 停擺自然停在最後有效點。
    function _trendAppend(entry, nMs, fVal, bBad) {
        var arr  = entry.arr;
        var last = arr.length ? arr[arr.length - 1] : null;
        if (last && nMs <= last.x) return false;      // 時間未前進 → 拒收（不灌重複假點）
        if (bBad) {
            if (last && last.y === null) return false;   // 已處於斷點狀態，不重複插 null
            arr.push({ x: nMs, y: null });
            return true;
        }
        if (last && (nMs - last.x) < entry.nStepMs) return false;   // 未滿畫點間隔
        arr.push({ x: nMs, y: fVal });
        return true;
    }

    // szElId = widget 容器 DOM id；szQuality 為大寫品質字串（'BAD' 代表斷線）
    function pushTrendPoint(szElId, szTs, rawVal, szQuality) {
        var entry = _trendCharts[szElId];
        if (!entry) return;

        var bBad = (szQuality === 'BAD');
        var nMs  = _trendParseTs(szTs);
        if (!isFinite(nMs)) return;                   // 點位尚無資料（timestamp '--'）

        if (bBad) { _trendAppend(entry, nMs, null, true); return; }

        if (rawVal === undefined || rawVal === null || rawVal === '--') return;
        var fVal = parseFloat(rawVal);
        if (isNaN(fVal)) return;
        _trendAppend(entry, nMs, fVal, false);
    }

    // 每秒推進時間窗 + 丟棄超窗舊點 + 重繪（曲線持續左移）。
    // 即使沒有新點也要推進，否則 Engine 停擺時看不出「右端沒有新資料」。
    function tickTrendCharts() {
        var nNow = Date.now();
        if (nNow - _nTrendLastDraw < TREND_REDRAW_MS) return;
        _nTrendLastDraw = nNow;

        for (var szId in _trendCharts) {
            var entry = _trendCharts[szId];
            if (!entry || !entry.chart) continue;
            var nMin = nNow - entry.nWindowSec * 1000;
            var arr  = entry.arr;

            // 多保留 1 筆界外點，讓左邊緣的線段畫到邊界而不是斷在邊界內側
            var nDrop = 0;
            while (nDrop + 1 < arr.length && arr[nDrop + 1].x < nMin) nDrop++;
            if (nDrop > 0) arr.splice(0, nDrop);

            entry.chart.options.scales.x.min = nMin;
            entry.chart.options.scales.x.max = nNow;
            entry.chart.update('none');
        }
    }

    // ============================================================
    // 畫布等比縮放（transform: scale）下的清晰度
    // ============================================================
    // 畫布用 transform: scale（刻意避開 CSS zoom 放大系統游標）。canvas 是 bitmap，
    // 只要 bitmap 與螢幕像素不是「整數尺寸 + 整數起點」一對一，瀏覽器就會整張重取樣 → 糊。
    // 舊做法把 devicePixelRatio 乘上縮放倍率，但 Chart.js 會把尺寸 floor 成整數、
    // translate(-50%) 又讓起點落在小數像素（實測 525px bitmap 攤在 525.36px、起點 x=331.68），
    // 格線與文字全被抹成 2px 灰邊。
    // 現做法：plot 層 scale(1/fScale) 抵銷畫布縮放，canvas 以螢幕像素渲染，
    // 尺寸取「× DPR 恰為整數」的整數 CSS px，再以 translate 把起點吸附到整數裝置像素。
    // Chart.js 事件座標走 offsetX（元素本地座標），抵銷後即 chart 座標，tooltip 命中不需補償。
    function applyTrendChartScale(fScale) {
        if (!isFinite(fScale) || fScale <= 0) fScale = 1;
        _trendScale = fScale;
        for (var szId in _trendCharts) {
            var entry = _trendCharts[szId];
            if (!entry || !entry.chart) continue;
            // 即使倍率沒變也要重新吸附：側欄收合等會讓畫布位移、起點小數改變
            try {
                _trendSnapPlot(entry.el, fScale);
                _trendApplyScaleOpts(entry.chart, fScale, entry.nLineWidth);
                entry.chart.resize();          // 依 plot 新尺寸重配 bitmap
                entry.chart.update('none');
            } catch (_) { /* canvas 已被移除 */ }
        }
    }

    // plot 層：抵銷畫布縮放 + 尺寸 / 起點吸附到整數裝置像素
    function _trendSnapPlot(el, fScale) {
        var box  = el.querySelector('.scada-trend-box');
        var plot = el.querySelector('.scada-trend-plot');
        if (!box || !plot || box.clientWidth <= 0) return;   // 尚未顯示，量不到尺寸
        var fDpr = window.devicePixelRatio || 1;
        var fInv = 1 / fScale;
        plot.style.width     = _trendFitPx(box.clientWidth  * fScale, fDpr) + 'px';
        plot.style.height    = _trendFitPx(box.clientHeight * fScale, fDpr) + 'px';
        plot.style.transform = 'scale(' + fInv + ')';
        var r  = plot.getBoundingClientRect();
        var dx = (Math.round(r.left * fDpr) - r.left * fDpr) / fDpr;   // 螢幕 CSS px
        var dy = (Math.round(r.top  * fDpr) - r.top  * fDpr) / fDpr;
        // translate 寫在 scale 前，單位是 box 座標（會再被畫布放大 fScale 倍）→ 除回去
        if (dx || dy) {
            plot.style.transform = 'translate(' + (dx * fInv) + 'px,' + (dy * fInv) + 'px) scale(' + fInv + ')';
        }
    }

    // 取 ≤ fCss 的整數 CSS px，且 × DPR 恰為整數（Chart.js 會把 canvas 寬高 floor 成整數，
    // DPR 1.25 / 1.5 時若乘出小數，bitmap 與顯示差零點幾像素仍會重取樣）。找不到就退回 floor。
    function _trendFitPx(fCss, fDpr) {
        var n = Math.floor(fCss);
        for (var k = 0; k < 8 && n - k > 0; k++) {
            var fDev = (n - k) * fDpr;
            if (Math.abs(fDev - Math.round(fDev)) < 0.01) return n - k;
        }
        return n;
    }

    // canvas 改以螢幕像素渲染後，字級 / 線寬 / 內距要自己乘上畫布縮放倍率，
    // 外觀才與 Designer 設計尺寸等比（格線維持 1px，縮放後反而最銳利）
    function _trendApplyScaleOpts(chart, fScale, nLineWidth) {
        var o  = chart.config.options;
        var fS = fScale;
        o.layout.padding = { top: 4 * fS, right: 6 * fS, bottom: 0, left: 0 };
        o.scales.x.ticks.font    = { size: 9 * fS };
        o.scales.y.ticks.font    = { size: 9 * fS };
        o.scales.x.ticks.padding = 3 * fS;
        o.scales.y.ticks.padding = 3 * fS;
        var lbl = o.plugins.legend.labels;
        lbl.font = { size: 10 * fS }; lbl.boxWidth = 10 * fS; lbl.boxHeight = 2 * fS;
        var tip = o.plugins.tooltip;
        tip.titleFont = { size: 12 * fS, weight: 'bold' };
        tip.bodyFont  = { size: 12 * fS };
        tip.padding   = 6 * fS;
        var ds = chart.data.datasets[0];
        ds.borderWidth    = nLineWidth * fS;
        ds.pointHitRadius = 6 * fS;
    }
