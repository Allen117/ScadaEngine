(function () {
    'use strict';

    var REFRESH_MS = 60000;
    var _sid = '';
    var _refreshTimer = null;
    var _chart = null;
    var _dotTooltip = null;
    var _metas = []; // 與 chart labels 同步的狀態陣列：'ok' | 'bad' | 'missing' | 'future'

    // ── 初始化 ───────────────────────────────────────────────
    function init() {
        var dot = document.getElementById('demandStatusDot');
        if (dot && window.bootstrap) {
            _dotTooltip = new bootstrap.Tooltip(dot, { trigger: 'hover', placement: 'right' });
        }

        loadCircuits();

        document.getElementById('demandCircuitSelect').addEventListener('change', function () {
            _sid = this.value;
            clearTimeout(_refreshTimer);
            if (_sid) refresh();
            else clearDisplay();
        });
    }

    // ── 載入迴路下拉 ─────────────────────────────────────────
    function loadCircuits() {
        fetch('/EMS/api/demand-circuits')
            .then(function (r) { return r.json(); })
            .then(function (list) {
                var sel = document.getElementById('demandCircuitSelect');
                if (!list || list.length === 0) {
                    sel.innerHTML = '<option value="">尚無設定需量的迴路</option>';
                    return;
                }
                sel.innerHTML = list.map(function (c) {
                    return '<option value="' + escAttr(c.sid) + '">' + escHtml(c.name) + '</option>';
                }).join('');
                _sid = list[0].sid;
                refresh();
            })
            .catch(function (e) { console.error('載入需量迴路失敗', e); });
    }

    // ── 刷新（今日數值 + 趨勢圖同時更新）────────────────────
    function refresh() {
        if (!_sid) return;
        clearTimeout(_refreshTimer);

        Promise.all([
            fetch('/EMS/api/demand-today?sid=' + encodeURIComponent(_sid)).then(function (r) { return r.json(); }),
            fetch('/EMS/api/demand-trend?sid=' + encodeURIComponent(_sid)).then(function (r) { return r.json(); })
        ]).then(function (res) {
            renderValue(res[0]);
            renderChart(res[1]);
        }).catch(function (e) {
            console.error('刷新需量失敗', e);
        }).finally(function () {
            _refreshTimer = setTimeout(refresh, REFRESH_MS);
        });
    }

    // ── 渲染即時數值 ─────────────────────────────────────────
    function renderValue(data) {
        var elKW    = document.getElementById('demandCurrentKW');
        var elMax   = document.getElementById('demandMaxKW');
        var elMaxAt = document.getElementById('demandMaxAt');
        var elDot   = document.getElementById('demandStatusDot');

        if (!data.hasData) {
            elKW.textContent    = '--';
            elMax.textContent   = '--';
            elMaxAt.textContent = '';
            setDot(elDot, '#bdbdbd', '今日尚無資料');
            return;
        }

        elKW.textContent    = data.currentKW != null ? data.currentKW.toFixed(1) : '--';
        elMax.textContent   = data.maxKW != null ? data.maxKW.toFixed(1) + ' kW' : '--';
        elMaxAt.textContent = data.maxAt ? ' (' + data.maxAt + ')' : '';

        if (data.quality === 1) {
            setDot(elDot, '#66bb6a', '資料正常（15 分鐘滾動加權平均）');
        } else {
            setDot(elDot, '#ffb300', '資料不足（視窗內有效樣本 < 5）');
        }
    }

    function setDot(el, color, tipText) {
        if (!el) return;
        el.style.background = color;
        el.setAttribute('data-bs-original-title', tipText);
    }

    // ── 建立全日 1440 點序列（含 step-hold 補值）────────────
    function buildSeries(points) {
        // 以 "HH:mm" 為 key 的快速查找表
        var map = {};
        points.forEach(function (p) { map[p.t] = p; });

        var now     = new Date();
        var nowMin  = now.getHours() * 60 + now.getMinutes();
        var labels  = [];
        var values  = [];
        var metas   = [];
        var lastGood = null; // 最近一次 Quality=1 的值（用於 forward-fill）

        // 前向填充：逐分鐘建立全日序列
        for (var m = 0; m <= 1439; m++) {
            var lbl = pad2(Math.floor(m / 60)) + ':' + pad2(m % 60);
            labels.push(lbl);

            if (m > nowMin) {
                values.push(null);
                metas.push('future');
                continue;
            }

            var pt = map[lbl];
            if (!pt) {
                // 無資料：沿用前一個好值（水平線），可能為 null（圖線斷開）
                values.push(lastGood);
                metas.push('missing');
            } else if (pt.q === 1) {
                lastGood = pt.v;
                values.push(pt.v);
                metas.push('ok');
            } else {
                // Quality=0（資料不足）：沿用前一個好值，不更新 lastGood
                values.push(lastGood);
                metas.push('bad');
            }
        }

        // 後向填充：將最初段的 null（尚無任何好值前）補成第一個好值
        var firstGood = null;
        for (var i = 0; i < values.length; i++) {
            if (metas[i] !== 'future' && values[i] !== null) { firstGood = values[i]; break; }
        }
        if (firstGood !== null) {
            for (var i = 0; i < values.length; i++) {
                if (metas[i] === 'future') break;
                if (values[i] === null) values[i] = firstGood;
                else break;
            }
        }

        return { labels: labels, values: values, metas: metas };
    }

    // ── 渲染趨勢折線圖 ───────────────────────────────────────
    function renderChart(points) {
        var canvas = document.getElementById('demandTrendChart');
        if (!canvas || !window.Chart) return;

        var series = buildSeries(points);
        _metas = series.metas; // 供 tooltip callback 讀取

        if (_chart) {
            _chart.data.labels             = series.labels;
            _chart.data.datasets[0].data   = series.values;
            _chart.update('none');
            return;
        }

        _chart = new Chart(canvas.getContext('2d'), {
            type: 'line',
            data: {
                labels: series.labels,
                datasets: [{
                    data: series.values,
                    borderColor: '#43a047',
                    backgroundColor: 'rgba(67,160,71,0.08)',
                    fill: true,
                    stepped: 'before',   // 水平延伸到下一點再垂直跳
                    pointRadius: 0,
                    pointHoverRadius: 3,
                    borderWidth: 1.5,
                    spanGaps: false      // null（未來時段）處斷線
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                interaction: { mode: 'index', intersect: false },
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        callbacks: {
                            label: function (ctx) {
                                if (ctx.parsed.y === null || ctx.parsed.y === undefined) return null;
                                var status = _metas[ctx.dataIndex];
                                if (status === 'missing') return '無資料';   // 無資料
                                if (status === 'bad')     return '資料不足'; // 資料不足
                                return ctx.parsed.y.toFixed(1) + ' kW';
                            }
                        }
                    }
                },
                scales: {
                    x: {
                        ticks: {
                            maxTicksLimit: 8,
                            font: { size: 10 },
                            color: '#9e9e9e'
                        },
                        grid: { display: false }
                    },
                    y: {
                        ticks: {
                            font: { size: 10 },
                            color: '#9e9e9e',
                            callback: function (v) { return v != null ? v.toFixed(0) : ''; }
                        },
                        grid: { color: 'rgba(0,0,0,0.04)' }
                    }
                }
            }
        });
    }

    // ── 清空顯示 ─────────────────────────────────────────────
    function clearDisplay() {
        var elKW  = document.getElementById('demandCurrentKW');
        var elMax = document.getElementById('demandMaxKW');
        var elAt  = document.getElementById('demandMaxAt');
        if (elKW)  elKW.textContent  = '--';
        if (elMax) elMax.textContent = '--';
        if (elAt)  elAt.textContent  = '';
        setDot(document.getElementById('demandStatusDot'), '#bdbdbd', '');

        if (_chart) {
            _chart.data.labels           = [];
            _chart.data.datasets[0].data = [];
            _metas = [];
            _chart.update('none');
        }
    }

    // ── 工具函式 ─────────────────────────────────────────────
    function pad2(n) { return n < 10 ? '0' + n : String(n); }

    function escHtml(s) {
        return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

    function escAttr(s) {
        return String(s).replace(/"/g, '&quot;');
    }

    window._ems = { refresh: refresh };

    document.addEventListener('DOMContentLoaded', init);
})();
