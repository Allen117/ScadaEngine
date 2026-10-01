// ============================================================
// scadapage/quick-trend.js — 右鍵「快速趨勢」浮動小窗（一次一窗，再點別的點即替換）
// ============================================================
// 不包 IIFE、global scope，與其餘 js/scadapage/ 模組同模式；
// 時間解析 / 格式化沿用 widget-trend.js 的 _trendParseTs / _trendFmtLocal / _trendFmtTick（須先載入）。
//
// 資料來源與即時曲線 widget 同模式：
//   1. 開窗 / 切時間範圍時打一次 /api/history/data backfill
//   2. 之後靠 ScadaPage 每秒 /api/realtime/latest 迴圈接續（updateScadaWidgets → _quickTrendOnData）
// 浮窗在畫布外（fixed 掛 body），不受畫布 transform: scale 影響。切頁時由 selectScadaPage 關閉。
// ============================================================

    // nInterval：歷史 API 取樣間隔（分鐘），0 = 原始；8 小時原始資料可能破 5000 筆上限 → 1 分鐘取樣
    var QT_RANGES = [
        { nSec: 900,   szKey: 'scadapage.qt.range_15m', nInterval: 0 },
        { nSec: 3600,  szKey: 'scadapage.qt.range_1h',  nInterval: 0 },
        { nSec: 28800, szKey: 'scadapage.qt.range_8h',  nInterval: 1 }
    ];
    var QT_DEFAULT_SEC = 3600;

    var _qt = null;   // { el, sid, name, unit, range, arr, chart, nToken }

    function _openQuickTrend(info) {
        if (!_qt) _qtBuildWindow();
        _qt.sid  = info.sid;
        _qt.name = info.name || info.sid;
        _qt.unit = info.unit || '';
        _qt.el.querySelector('.scada-qt-title').textContent = _qt.name + (_qt.unit ? ' (' + _qt.unit + ')' : '');
        _qt.el.querySelector('.scada-qt-title').title = _qt.name;
        _qtSetRange(QT_DEFAULT_SEC);
    }

    function _quickTrendClose() {
        if (!_qt) return;
        _qtDestroyChart();
        _qt.el.remove();
        _qt = null;
    }

    function _qtBuildWindow() {
        var el = document.createElement('div');
        el.className = 'scada-qt';
        var szBtns = QT_RANGES.map(function (r) {
            return '<button type="button" class="btn btn-outline-secondary scada-qt-range" data-sec="' + r.nSec + '">' +
                   escViewHtml(t(r.szKey)) + '</button>';
        }).join('');
        el.innerHTML =
            '<div class="scada-qt-head">' +
                '<i class="fas fa-chart-area scada-qt-icon"></i>' +
                '<span class="scada-qt-title"></span>' +
                '<div class="btn-group btn-group-sm scada-qt-ranges">' + szBtns + '</div>' +
                (window._canHistory
                    ? '<button type="button" class="btn btn-sm btn-link scada-qt-history" title="' +
                      escViewHtml(t('scadapage.ctx.history_new_tab')) + '"><i class="fas fa-external-link-alt"></i></button>'
                    : '') +
                '<button type="button" class="btn-close scada-qt-close" aria-label="' + escViewHtml(t('scadapage.qt.close')) + '"></button>' +
            '</div>' +
            '<div class="scada-qt-body">' +
                '<canvas></canvas>' +
                '<div class="scada-qt-msg" style="display:none;"></div>' +
            '</div>';
        document.body.appendChild(el);

        el.querySelectorAll('.scada-qt-range').forEach(function (btn) {
            btn.addEventListener('click', function () { _qtSetRange(parseInt(btn.dataset.sec, 10)); });
        });
        el.querySelector('.scada-qt-close').addEventListener('click', _quickTrendClose);
        var btnHist = el.querySelector('.scada-qt-history');
        if (btnHist) {
            btnHist.addEventListener('click', function () {
                window.open('/HistoryData?sid=' + encodeURIComponent(_qt.sid) +
                            '&hours=' + (_qt.range.nSec / 3600), '_blank', 'noopener');
            });
        }
        _qtMakeDraggable(el, el.querySelector('.scada-qt-head'));

        _qt = { el: el, sid: '', name: '', unit: '', range: null, arr: [], chart: null, nToken: 0 };
    }

    // 拖曳：抓標題列移動，限制在視窗內
    function _qtMakeDraggable(el, handle) {
        handle.addEventListener('pointerdown', function (ev) {
            if (ev.button !== 0 || ev.target.closest('button')) return;
            var rect = el.getBoundingClientRect();
            var dx = ev.clientX - rect.left;
            var dy = ev.clientY - rect.top;
            handle.setPointerCapture(ev.pointerId);
            function onMove(e) {
                var x = Math.min(Math.max(0, e.clientX - dx), window.innerWidth  - el.offsetWidth);
                var y = Math.min(Math.max(0, e.clientY - dy), window.innerHeight - el.offsetHeight);
                el.style.left = x + 'px';
                el.style.top  = y + 'px';
                el.style.right = 'auto';
                el.style.bottom = 'auto';
            }
            function onUp() {
                handle.removeEventListener('pointermove', onMove);
                handle.removeEventListener('pointerup', onUp);
                handle.removeEventListener('pointercancel', onUp);
            }
            handle.addEventListener('pointermove', onMove);
            handle.addEventListener('pointerup', onUp);
            handle.addEventListener('pointercancel', onUp);
        });
    }

    function _qtDestroyChart() {
        if (_qt && _qt.chart) {
            try { _qt.chart.destroy(); } catch (_) { /* canvas 已被移除 */ }
            _qt.chart = null;
        }
    }

    function _qtSetRange(nSec) {
        var range = QT_RANGES.find(function (r) { return r.nSec === nSec; }) || QT_RANGES[1];
        _qt.range = range;
        _qt.nToken++;
        _qt.el.querySelectorAll('.scada-qt-range').forEach(function (b) {
            b.classList.toggle('active', parseInt(b.dataset.sec, 10) === range.nSec);
        });
        _qtDestroyChart();
        _qt.arr = [];
        // 1 分鐘取樣時即時值也每分鐘留一點，與 backfill 密度一致；原始則每筆都收（10% 抖動容忍）
        _qt.nStepMs = range.nInterval > 0 ? range.nInterval * 60 * 900 : 900;
        _qtShowMsg(t('scadapage.qt.loading'));

        var nNow = Date.now();
        var szUnit = _qt.unit;
        _qt.chart = new Chart(_qt.el.querySelector('canvas').getContext('2d'), {
            type: 'line',
            data: {
                datasets: [{
                    data: _qt.arr,
                    borderColor: '#0d6efd',
                    backgroundColor: 'rgba(13,110,253,.08)',
                    borderWidth: 1.5,
                    pointRadius: 0,
                    pointHitRadius: 6,
                    tension: 0.2,
                    fill: true,
                    spanGaps: false   // 品質 BAD 以 null 斷線，不補值
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                animation: false,
                parsing: false,
                normalized: true,
                interaction: { mode: 'nearest', axis: 'x', intersect: false },
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        displayColors: false,
                        callbacks: {
                            label: function (ctx) { return _trendFmtTick(ctx.parsed.y) + (szUnit ? ' ' + szUnit : ''); }
                        }
                    }
                },
                scales: {
                    x: {
                        type: 'time',
                        min: nNow - range.nSec * 1000,
                        max: nNow,
                        time: { tooltipFormat: 'yyyy-MM-dd HH:mm:ss', displayFormats: TREND_TIME_DISPLAY_FORMATS },
                        grid: { display: false },
                        ticks: { maxTicksLimit: 6, maxRotation: 0, font: { size: 10 }, color: '#868e96' }
                    },
                    y: {
                        grace: '10%',
                        ticks: { font: { size: 10 }, color: '#868e96', callback: function (v) { return _trendFmtTick(v); } }
                    }
                }
            }
        });
        _qtBackfill(_qt.nToken);
    }

    async function _qtBackfill(nToken) {
        var qt = _qt;
        var dtEnd   = new Date();
        var dtStart = new Date(dtEnd.getTime() - qt.range.nSec * 1000);
        var szUrl = '/api/history/data?szSID=' + encodeURIComponent(qt.sid) +
                    '&szStart=' + encodeURIComponent(_trendFmtLocal(dtStart)) +
                    '&szEnd='   + encodeURIComponent(_trendFmtLocal(dtEnd)) +
                    '&nInterval=' + qt.range.nInterval;
        try {
            var resp = await fetch(szUrl);
            var result = resp.ok ? await resp.json() : null;
            // 等待期間可能已關窗 / 換點 / 換範圍
            if (_qt !== qt || qt.nToken !== nToken) return;
            var arrOld = [];
            (result && result.success && result.data ? result.data : []).forEach(function (d) {
                var nMs = _trendParseTs(d.t);
                if (!isFinite(nMs)) return;
                var last = arrOld.length ? arrOld[arrOld.length - 1] : null;
                if (d.q !== 'Good') {
                    if (!last || last.y !== null) arrOld.push({ x: nMs, y: null });
                    return;
                }
                arrOld.push({ x: nMs, y: d.v });
            });
            // backfill 期間即時迴圈可能已 append 較新的點 → 只接「早於現有最舊點」的歷史
            var nFirstLive = qt.arr.length ? qt.arr[0].x : Infinity;
            var arrPrepend = arrOld.filter(function (p) { return p.x < nFirstLive; });
            if (arrPrepend.length) Array.prototype.unshift.apply(qt.arr, arrPrepend);
            _qtShowMsg(qt.arr.length ? '' : t('scadapage.qt.no_data'));
            qt.chart.update('none');
        } catch (err) {
            if (_qt === qt && qt.nToken === nToken) _qtShowMsg(t('scadapage.qt.load_failed'));
        }
    }

    function _qtShowMsg(szMsg) {
        var el = _qt.el.querySelector('.scada-qt-msg');
        el.textContent = szMsg || '';
        el.style.display = szMsg ? '' : 'none';
    }

    // 由 updateScadaWidgets 每秒呼叫：接續即時值 + 推進時間窗
    function _quickTrendOnData(sidTsMap, sidValueMap, sidQualityMap) {
        if (!_qt || !_qt.chart) return;
        var sid = _qt.sid;
        var nMs = _trendParseTs(sidTsMap[sid]);
        if (isFinite(nMs)) {
            var arr  = _qt.arr;
            var last = arr.length ? arr[arr.length - 1] : null;
            if (!last || nMs > last.x) {                       // 以點位自身時間戳去重
                if (sidQualityMap[sid] === 'BAD') {
                    if (!last || last.y !== null) arr.push({ x: nMs, y: null });
                } else {
                    var fVal = parseFloat(sidValueMap[sid]);
                    if (!isNaN(fVal) && (!last || (nMs - last.x) >= _qt.nStepMs)) arr.push({ x: nMs, y: fVal });
                }
                if (arr.length) _qtShowMsg('');
            }
        }
        var nNow = Date.now();
        var nMin = nNow - _qt.range.nSec * 1000;
        var arrQ = _qt.arr;
        var nDrop = 0;
        while (nDrop + 1 < arrQ.length && arrQ[nDrop + 1].x < nMin) nDrop++;   // 留 1 筆界外點畫到邊界
        if (nDrop > 0) arrQ.splice(0, nDrop);
        _qt.chart.options.scales.x.min = nMin;
        _qt.chart.options.scales.x.max = nNow;
        _qt.chart.update('none');
    }
