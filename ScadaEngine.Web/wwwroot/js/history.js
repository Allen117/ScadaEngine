/* ── History / Trend 頁面邏輯（IIFE 封裝）── */
(function () {
    'use strict';

    // ── 從 Razor 注入的設定讀取資料 ──────────────────────────────────────
    var cfg = window.__historyConfig || {};
    var allPoints       = cfg.points       || [];
    var allCoordinators = cfg.coordinators || [];
    var currentCoordinatorId = cfg.initialCoordId || 0;
    var currentModbusId      = null;
    var currentCalcGroup     = null;   // null = 全部計算點位, string = 指定群組

    // ── 為每個點位計算所屬設備/子設備名稱，作為顯示前綴 ──────────────────
    function isCalcSid(sid) { return sid && sid.indexOf('CALC-') === 0; }

    (function enrichPointsWithDeviceLabel() {
        var getSidPfx = function (sid) { var m = sid.match(/^(\d+)-S\d+$/); return m ? parseInt(m[1], 10) : -1; };
        var isOfDev   = function (sid, nId) { var n = getSidPfx(sid); return n >= nId * 65536 && n < (nId + 1) * 65536; };
        allPoints.forEach(function (p) {
            if (isCalcSid(p.sid)) {
                var grp = p.groupName || '\u8a08\u7b97\u9ede\u4f4d';
                p.deviceLabel = grp;
                p.fullName = grp + ' / ' + p.name;
                return;
            }
            var nPfx = getSidPfx(p.sid);
            var szLabel = '';
            for (var di = 0; di < allCoordinators.length; di++) {
                var d = allCoordinators[di];
                if (!isOfDev(p.sid, d.nId)) continue;
                var modbusIds   = (d.szModbusID || '').split(',').map(function (s) { return s.trim(); }).filter(Boolean);
                var deviceNames = (d.szDeviceName || '').split(',').map(function (s) { return s.trim(); });
                if (modbusIds.length > 1) {
                    for (var j = 0; j < modbusIds.length; j++) {
                        var mid  = parseInt(modbusIds[j], 10);
                        var base = d.nId * 65536 + mid * 256;
                        if (nPfx >= base && nPfx < base + 256) {
                            szLabel = (j < deviceNames.length && deviceNames[j]) ? deviceNames[j] : d.szName;
                            break;
                        }
                    }
                } else {
                    szLabel = d.szName;
                }
                break;
            }
            p.deviceLabel = szLabel;
            p.fullName    = szLabel ? szLabel + ' / ' + p.name : p.name;
        });
    })();

    // ── 常數 ─────────────────────────────────────────────────────────────
    var CHART_COLORS = [
        { border: 'rgba(13,110,253,0.9)',  bg: 'rgba(13,110,253,0.12)'  },
        { border: 'rgba(220,53,69,0.9)',   bg: 'rgba(220,53,69,0.12)'   },
        { border: 'rgba(25,135,84,0.9)',   bg: 'rgba(25,135,84,0.12)'   },
        { border: 'rgba(255,193,7,0.9)',   bg: 'rgba(255,193,7,0.12)'   },
        { border: 'rgba(13,202,240,0.9)',  bg: 'rgba(13,202,240,0.12)'  },
        { border: 'rgba(111,66,193,0.9)',  bg: 'rgba(111,66,193,0.12)'  },
        { border: 'rgba(253,126,20,0.9)',  bg: 'rgba(253,126,20,0.12)'  },
        { border: 'rgba(102,16,242,0.9)',  bg: 'rgba(102,16,242,0.12)'  }
    ];
    var MAX_BASKET = 8;

    // ── DOM refs ─────────────────────────────────────────────────────────
    var pointListContainer = document.getElementById('pointListContainer');
    var basketContainer    = document.getElementById('basketContainer');
    var dtStart            = document.getElementById('dtStart');
    var dtEnd              = document.getElementById('dtEnd');
    var btnQuery           = document.getElementById('btnQuery');
    var btnClear           = document.getElementById('btnClear');
    var chartContainer     = document.getElementById('chartContainer');
    var noDataMsg          = document.getElementById('noDataMsg');
    var loadingSpinner     = document.getElementById('loadingSpinner');
    var limitWarning       = document.getElementById('limitWarning');
    var statsRow           = document.getElementById('statsRow');
    var multiStatsWrapper  = document.getElementById('multiStatsWrapper');
    var chartTitle         = document.getElementById('chartTitle');
    var dataTableCard      = document.getElementById('dataTableCard');
    var tableBodyEl        = document.getElementById('tableBody');
    var dataTableBody      = document.getElementById('dataTableBody');
    var btnToggleTable     = document.getElementById('btnToggleTable');

    // ── 點位選取清單 ─────────────────────────────────────────────────────
    var getN = function (sid) { var m = sid.match(/-S(\d+)$/); return m ? parseInt(m[1]) : Infinity; };

    function renderPointList(nDbId, nModbusId, szCalcGroup) {
        var pts;
        if (nDbId === -999) {
            // 計算點位 — 依群組篩選
            pts = allPoints.filter(function (p) {
                if (!isCalcSid(p.sid)) return false;
                if (szCalcGroup != null) {
                    return (p.groupName || '') === szCalcGroup;
                }
                return true;
            });
        } else if (nDbId <= 0) {
            pts = allPoints.slice();
        } else {
            pts = allPoints.filter(function (p) {
                var h = p.sid.indexOf('-');
                if (h < 0) return false;
                var num = parseInt(p.sid.substring(0, h));
                if (isNaN(num)) return false;
                if (nModbusId != null) {
                    var base = nDbId * 65536 + nModbusId * 256;
                    return num >= base && num < base + 256;
                }
                return num >= nDbId * 65536 && num < (nDbId + 1) * 65536;
            });
        }
        pts.sort(function (a, b) { return getN(a.sid) - getN(b.sid); });

        pointListContainer.innerHTML = pts.map(function (p, i) {
            var szDisplay = p.fullName || p.name;
            return '<div class="form-check py-1">'
                + '<input class="form-check-input point-checkbox" type="checkbox"'
                + ' value="' + p.sid + '" id="pt_' + i + '"'
                + ' data-name="' + szDisplay.replace(/"/g, '&quot;') + '"'
                + ' data-unit="' + p.unit.replace(/"/g, '&quot;') + '">'
                + '<label class="form-check-label small" for="pt_' + i + '">'
                + (p.deviceLabel ? '<span style="color:#0d6efd;">' + p.deviceLabel + '</span><span style="color:#999;margin:0 3px;">/</span>' : '')
                + p.name
                + '</label></div>';
        }).join('') || '<div class="text-muted small text-center py-3">\u7121\u9ede\u4f4d\u8cc7\u6599</div>';
    }

    // ── 待查詢清單（Basket）─────────────────────────────────────────────
    var basket = [];

    function renderBasket() {
        document.getElementById('basketCount').textContent = basket.length;
        var hint = document.getElementById('basketHint');

        if (basket.length === 0) {
            basketContainer.innerHTML = '';
            var emptyDiv = document.createElement('div');
            emptyDiv.id = 'basketEmpty';
            emptyDiv.className = 'text-muted text-center small py-4';
            emptyDiv.innerHTML = '<i class="fas fa-inbox d-block mb-1 opacity-50"></i>\u5c1a\u672a\u52a0\u5165\u4efb\u4f55\u9ede\u4f4d';
            basketContainer.appendChild(emptyDiv);
            hint.style.display = 'none';
            return;
        }

        hint.style.display = '';
        basketContainer.innerHTML = basket.map(function (p, i) {
            var c = CHART_COLORS[i % CHART_COLORS.length];
            return '<div class="basket-item" data-sid="' + p.sid + '">'
                + '<div class="d-flex align-items-center gap-2 overflow-hidden">'
                + '<span class="color-dot" style="background:' + c.border + '"></span>'
                + '<span class="text-truncate">' + p.name + '</span>'
                + '</div>'
                + '<button class="btn-remove" onclick="window._history.removeFromBasket(\'' + p.sid + '\')" title="\u79fb\u9664">'
                + '<i class="fas fa-times"></i>'
                + '</button></div>';
        }).join('');
    }

    function addToBasket(items) {
        var added = 0;
        for (var idx = 0; idx < items.length; idx++) {
            var item = items[idx];
            if (basket.some(function (b) { return b.sid === item.sid; })) continue;
            if (basket.length >= MAX_BASKET) {
                showAlert('\u6700\u591a\u53ea\u80fd\u52a0\u5165 ' + MAX_BASKET + ' \u500b\u9ede\u4f4d', 'warning');
                break;
            }
            basket.push(item);
            added++;
        }
        if (added > 0) renderBasket();
        else if (items.length > 0 && added === 0) showAlert('\u9078\u53d6\u7684\u9ede\u4f4d\u5df2\u5168\u90e8\u5728\u6e05\u55ae\u4e2d', 'info');
    }

    function removeFromBasket(sid) {
        basket = basket.filter(function (b) { return b.sid !== sid; });
        renderBasket();
    }

    // ── Chart ────────────────────────────────────────────────────────────
    var trendChart    = null;
    var stackedCharts = [];
    var currentChartType = 'line';
    var isSingleAxis  = false;
    var lastResults   = null;

    function destroyAllCharts() {
        if (trendChart) { trendChart.destroy(); trendChart = null; }
        stackedCharts.forEach(function (c) { c.destroy(); });
        stackedCharts = [];
    }

    // Bad 品質點位：圖表位置沿用前一個 Good 值，原始值保留供 tooltip 顯示
    function holdLastGoodValue(data, qualities) {
        var processed    = [];
        var originalVals = [];
        var lastGoodY    = null;
        for (var i = 0; i < data.length; i++) {
            var pt = data[i];
            originalVals.push(pt.y);
            if (qualities[i] === 'Good') {
                lastGoodY = pt.y;
                processed.push(pt);
            } else {
                processed.push({ x: pt.x, y: lastGoodY });
            }
        }
        return { data: processed, originalVals: originalVals };
    }

    function buildChart(datasets) {
        destroyAllCharts();
        var stackedContainer = document.getElementById('stackedChartsContainer');

        if (datasets.length >= 3) {
            chartContainer.style.display = 'none';
            buildStackedCharts(datasets);
            return;
        }

        stackedContainer.style.display = 'none';
        chartContainer.style.display = 'block';
        var ctx = document.getElementById('trendChart').getContext('2d');

        var isSingle = datasets.length === 1;
        var isDual   = datasets.length === 2 && !isSingleAxis;

        function niceRange(ds) {
            var vals = ds.data.map(function (d) { return d.y; }).filter(function (v) { return v != null; });
            if (!vals.length) return {};
            var lo = Math.min.apply(null, vals), hi = Math.max.apply(null, vals);
            var pad = (hi - lo) * 0.1 || Math.abs(lo) * 0.1 || 1;
            return { min: lo - pad, max: hi + pad };
        }

        var chartDatasets = datasets.map(function (ds, i) {
            var c = CHART_COLORS[i % CHART_COLORS.length];
            var ptColors = ds.qualities.map(function (q) {
                return q === 'Good' ? c.border : 'rgba(220,53,69,0.85)';
            });
            return {
                label:                ds.label + (ds.szUnit ? ' (' + ds.szUnit + ')' : ''),
                data:                 ds.data,
                yAxisID:              isDual ? (i === 0 ? 'y' : 'y1') : 'y',
                borderColor:          c.border,
                backgroundColor:      c.bg,
                pointBackgroundColor: isSingle ? ptColors : c.border,
                pointBorderColor:     'transparent',
                pointRadius:          ds.data.length > 500 ? 0 : 3,
                pointHoverRadius:     6,
                borderWidth:          2,
                fill:                 currentChartType === 'line' && isSingle,
                tension:              0.3
            };
        });

        var maxLen = Math.max.apply(null, datasets.map(function (d) { return d.data.length; }));

        var scaleX = {
            type: 'time',
            time: {
                tooltipFormat: 'yyyy-MM-dd HH:mm:ss',
                displayFormats: {
                    second: 'HH:mm:ss',
                    minute: 'MM-dd HH:mm',
                    hour:   'MM-dd HH:mm',
                    day:    'MM-dd'
                }
            },
            ticks: { maxTicksLimit: 10, maxRotation: 20 },
            grid:  { color: 'rgba(0,0,0,0.06)' }
        };

        var scaleY = {
            position: 'left',
            grid: { color: 'rgba(0,0,0,0.06)' },
            title: isDual ? {
                display: true,
                text: datasets[0].szUnit || datasets[0].label,
                color: CHART_COLORS[0].border,
                font: { size: 11, weight: 'bold' }
            } : { display: false }
        };
        if (isDual) Object.assign(scaleY, niceRange(datasets[0]));

        var scales = { x: scaleX, y: scaleY };

        if (isDual) {
            scales.y1 = Object.assign({
                position: 'right',
                grid: { drawOnChartArea: false },
                title: {
                    display: true,
                    text: datasets[1].szUnit || datasets[1].label,
                    color: CHART_COLORS[1].border,
                    font: { size: 11, weight: 'bold' }
                }
            }, niceRange(datasets[1]));
        }

        trendChart = new Chart(ctx, {
            type: currentChartType,
            data: { datasets: chartDatasets },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                animation: { duration: maxLen > 1000 ? 0 : 400 },
                interaction: { mode: 'index', intersect: false },
                plugins: {
                    legend: { display: true, position: 'top' },
                    tooltip: {
                        callbacks: {
                            label: function (ctx) {
                                var dsIdx = ctx.datasetIndex;
                                var ds    = datasets[dsIdx];
                                var q     = ds.qualities[ctx.dataIndex];
                                var origY = ds.originalVals ? ds.originalVals[ctx.dataIndex] : ctx.parsed.y;
                                var val   = ' ' + ctx.dataset.label + ': ' + Number(origY).toFixed(3);
                                return val + '  [' + q + ']';
                            }
                        }
                    }
                },
                scales: scales
            }
        });
    }

    // ── 堆疊小圖（3 個以上點位）─────────────────────────────────────────
    function buildStackedCharts(datasets) {
        var container = document.getElementById('stackedChartsContainer');
        container.innerHTML = '';
        stackedCharts = [];

        datasets.forEach(function (ds, i) {
            var c      = CHART_COLORS[i % CHART_COLORS.length];
            var isLast = (i === datasets.length - 1);

            var vals = ds.data.map(function (d) { return d.y; }).filter(function (v) { return v != null; });
            var yRange = {};
            if (vals.length) {
                var lo  = Math.min.apply(null, vals), hi = Math.max.apply(null, vals);
                var pad = (hi - lo) * 0.1 || Math.abs(lo) * 0.1 || 1;
                yRange = { min: lo - pad, max: hi + pad };
            }

            var unitLabel = ds.szUnit ? ' (' + ds.szUnit + ')' : '';
            var wrapper   = document.createElement('div');
            wrapper.className = 'mini-chart-wrapper';
            wrapper.innerHTML =
                '<div class="d-flex align-items-center gap-2 px-3 pt-2 pb-1">'
                + '<span style="width:10px;height:10px;border-radius:50%;background:' + c.border + ';flex-shrink:0;display:inline-block;"></span>'
                + '<span class="fw-semibold small">' + ds.label + unitLabel + '</span>'
                + '</div>'
                + '<div style="position:relative; height:170px; padding:0 0.5rem ' + (isLast ? '0.5rem' : '0') + ';">'
                + '<canvas id="miniChart_' + i + '"></canvas>'
                + '</div>';
            container.appendChild(wrapper);

            var canvas   = document.getElementById('miniChart_' + i);
            var ptColors = ds.qualities.map(function (q) {
                return q === 'Good' ? c.border : 'rgba(220,53,69,0.85)';
            });

            var chart = new Chart(canvas.getContext('2d'), {
                type: currentChartType,
                data: {
                    datasets: [{
                        data:                 ds.data,
                        borderColor:          c.border,
                        backgroundColor:      c.bg,
                        pointBackgroundColor: ptColors,
                        pointBorderColor:     'transparent',
                        pointRadius:          ds.data.length > 500 ? 0 : 2,
                        pointHoverRadius:     5,
                        borderWidth:          1.5,
                        fill:                 currentChartType === 'line',
                        tension:              0.3
                    }]
                },
                options: {
                    responsive:          true,
                    maintainAspectRatio: false,
                    animation:           { duration: ds.data.length > 1000 ? 0 : 300 },
                    interaction:         { mode: 'index', intersect: false },
                    plugins: {
                        legend: { display: false },
                        tooltip: {
                            callbacks: {
                                label: function (ctx) {
                                    var origY = ds.originalVals ? ds.originalVals[ctx.dataIndex] : ctx.parsed.y;
                                    var val   = ' ' + Number(origY).toFixed(3) + (ds.szUnit ? ' ' + ds.szUnit : '');
                                    var q     = ds.qualities[ctx.dataIndex];
                                    return val + '  [' + q + ']';
                                }
                            }
                        }
                    },
                    scales: {
                        x: {
                            type: 'time',
                            time: {
                                tooltipFormat:  'yyyy-MM-dd HH:mm:ss',
                                displayFormats: {
                                    second: 'HH:mm:ss',
                                    minute: 'MM-dd HH:mm',
                                    hour:   'MM-dd HH:mm',
                                    day:    'MM-dd'
                                }
                            },
                            ticks: { maxTicksLimit: 6, maxRotation: 0, display: isLast },
                            grid:  { color: 'rgba(0,0,0,0.06)' }
                        },
                        y: Object.assign({
                            position: 'left',
                            grid:     { color: 'rgba(0,0,0,0.06)' },
                            ticks:    { maxTicksLimit: 4 }
                        }, yRange)
                    }
                }
            });
            stackedCharts.push(chart);
        });

        container.style.display = '';
    }

    // ── 查詢 ─────────────────────────────────────────────────────────────
    function doQuery() {
        var szStart = dtStart.value;
        var szEnd   = dtEnd.value;

        if (basket.length === 0) { showAlert('\u8acb\u5148\u5c07\u9ede\u4f4d\u52a0\u5165\u5f85\u67e5\u8a62\u6e05\u55ae', 'warning'); return; }
        if (!szStart || !szEnd)  { showAlert('\u8acb\u8a2d\u5b9a\u67e5\u8a62\u6642\u9593\u7bc4\u570d', 'warning'); return; }
        if (szStart >= szEnd)    { showAlert('\u958b\u59cb\u6642\u9593\u5fc5\u9808\u65e9\u65bc\u7d50\u675f\u6642\u9593', 'warning'); return; }

        noDataMsg.classList.add('d-none');
        chartContainer.style.display = 'none';
        document.getElementById('stackedChartsContainer').style.display = 'none';
        loadingSpinner.classList.remove('d-none');
        statsRow.style.display = 'none';
        multiStatsWrapper.style.display = 'none';
        limitWarning.classList.add('d-none');
        dataTableCard.classList.add('d-none');
        btnQuery.disabled = true;

        Promise.all(basket.map(function (p) {
            return fetch('/api/history/data?szSID=' + encodeURIComponent(p.sid)
                + '&szStart=' + encodeURIComponent(szStart)
                + '&szEnd=' + encodeURIComponent(szEnd))
                .then(function (r) { return r.json(); });
        })).then(function (results) {
            loadingSpinner.classList.add('d-none');
            btnQuery.disabled = false;
            lastResults = results;

            var failed = results.find(function (r) { return !r.success; });
            if (failed) {
                noDataMsg.innerHTML = '<i class="fas fa-exclamation-circle fa-3x mb-3 d-block text-danger opacity-50"></i><div>' + failed.message + '</div>';
                noDataMsg.classList.remove('d-none'); return;
            }
            if (!results.some(function (r) { return r.nCount > 0; })) {
                noDataMsg.innerHTML = '<i class="fas fa-inbox fa-3x mb-3 d-block opacity-25"></i><div>\u8a72\u6642\u6bb5\u5167\u7121\u6b77\u53f2\u8cc7\u6599</div>';
                noDataMsg.classList.remove('d-none'); return;
            }

            if (results.some(function (r) { return r.isLimited; })) limitWarning.classList.remove('d-none');

            var btnAxis = document.getElementById('btnSingleAxis');
            var validCount = results.filter(function (r) { return r.nCount > 0; }).length;
            if (validCount === 2) {
                btnAxis.classList.remove('d-none');
            } else {
                btnAxis.classList.add('d-none');
                isSingleAxis = false;
                btnAxis.classList.remove('pressed');
            }

            chartTitle.innerHTML = results.length === 1
                ? '<i class="fas fa-chart-area text-primary me-2"></i>' + results[0].szName
                : '<i class="fas fa-chart-area text-primary me-2"></i>\u5df2\u67e5\u8a62 ' + results.length + ' \u500b\u9ede\u4f4d';

            if (results.length === 1) {
                var json = results[0];
                var fmt = function (v) { return v != null ? Number(v).toFixed(3) + (json.szUnit ? ' ' + json.szUnit : '') : '--'; };
                document.getElementById('statMin').textContent    = fmt(json.dMin);
                document.getElementById('statMax').textContent    = fmt(json.dMax);
                document.getElementById('statAvg').textContent    = fmt(json.dAvg);
                document.getElementById('statStdDev').textContent = fmt(json.dStdDev);
                statsRow.style.display = '';
            } else {
                document.getElementById('multiStatsTbody').innerHTML = results
                    .filter(function (r) { return r.nCount > 0; })
                    .map(function (json) {
                        var fmt = function (v) { return v != null ? Number(v).toFixed(3) + (json.szUnit ? ' ' + json.szUnit : '') : '--'; };
                        return '<tr>'
                            + '<td class="fw-semibold">' + json.szName + '</td>'
                            + '<td class="text-end">' + fmt(json.dMin) + '</td>'
                            + '<td class="text-end">' + fmt(json.dMax) + '</td>'
                            + '<td class="text-end">' + fmt(json.dAvg) + '</td>'
                            + '<td class="text-end">' + fmt(json.dStdDev) + '</td>'
                            + '</tr>';
                    }).join('');
                multiStatsWrapper.style.display = '';
            }

            buildChart(results.filter(function (r) { return r.nCount > 0; }).map(function (json) {
                var rawData = json.data.map(function (d) { return { x: d.t, y: d.v }; });
                var quals   = json.data.map(function (d) { return d.q; });
                var held    = holdLastGoodValue(rawData, quals);
                return {
                    label:        json.szName,
                    szUnit:       json.szUnit,
                    data:         held.data,
                    qualities:    quals,
                    originalVals: held.originalVals
                };
            }));

            document.getElementById('btnExportExcel').classList.remove('d-none');
            document.getElementById('footerQueryTime').textContent =
                '\u67e5\u8a62\u5b8c\u6210: ' + new Date().toLocaleTimeString();

            if (results.length === 1) {
                var json2 = results[0];
                document.getElementById('tableCount').textContent = json2.nCount.toLocaleString();
                dataTableBody.innerHTML = json2.data.map(function (d) {
                    return '<tr>'
                        + '<td><small>' + d.t.replace('T', ' ') + '</small></td>'
                        + '<td class="text-end fw-bold">' + Number(d.v).toFixed(3) + '</td>'
                        + '<td><span class="' + (d.q === 'Good' ? 'text-success' : 'text-danger') + '">' + d.q + '</span></td>'
                        + '</tr>';
                }).join('');
                dataTableCard.classList.remove('d-none');
            }

        }).catch(function (err) {
            loadingSpinner.classList.add('d-none');
            btnQuery.disabled = false;
            noDataMsg.innerHTML = '<i class="fas fa-exclamation-circle fa-3x mb-3 d-block text-danger opacity-50"></i><div>\u67e5\u8a62\u5931\u6557\uff1a' + err.message + '</div>';
            noDataMsg.classList.remove('d-none');
        });
    }

    // ── 匯出 Excel ──────────────────────────────────────────────────────
    function exportToExcel() {
        if (!lastResults || !lastResults.some(function (r) { return r.nCount > 0; })) {
            showAlert('\u7121\u53ef\u532f\u51fa\u7684\u8cc7\u6599', 'warning');
            return;
        }

        var wb = XLSX.utils.book_new();

        lastResults.filter(function (r) { return r.nCount > 0; }).forEach(function (json) {
            var sheetName = (json.szName || json.szSID)
                .replace(/[[\]:*?/\\]/g, '_')
                .substring(0, 31);
            var finalName = sheetName;
            var counter   = 1;
            while (wb.SheetNames.includes(finalName)) {
                finalName = sheetName.substring(0, 28) + '(' + (counter++) + ')';
            }

            var unitSuffix = json.szUnit ? ' (' + json.szUnit + ')' : '';
            var rows = [['\u6642\u9593\u6233\u8a18', '\u6578\u503c' + unitSuffix, '\u54c1\u8cea']];
            json.data.forEach(function (d) { rows.push([d.t.replace('T', ' '), d.v, d.q]); });

            var ws = XLSX.utils.aoa_to_sheet(rows);
            ws['!cols'] = [{ wch: 22 }, { wch: 15 }, { wch: 8 }];
            XLSX.utils.book_append_sheet(wb, ws, finalName);
        });

        if (lastResults.filter(function (r) { return r.nCount > 0; }).length > 1) {
            var summaryRows = [['\u9ede\u4f4d\u540d\u7a31', '\u55ae\u4f4d', '\u7b46\u6578', '\u6700\u5c0f\u503c', '\u6700\u5927\u503c', '\u5e73\u5747\u503c', '\u6a19\u6e96\u5dee']];
            lastResults.filter(function (r) { return r.nCount > 0; }).forEach(function (json) {
                var f = function (v) { return v != null ? Number(v) : ''; };
                summaryRows.push([json.szName, json.szUnit, json.nCount,
                    f(json.dMin), f(json.dMax), f(json.dAvg), f(json.dStdDev)]);
            });
            var wsSummary = XLSX.utils.aoa_to_sheet(summaryRows);
            wsSummary['!cols'] = [{ wch: 20 }, { wch: 8 }, { wch: 8 },
                                   { wch: 14 }, { wch: 14 }, { wch: 14 }, { wch: 14 }];
            XLSX.utils.book_append_sheet(wb, wsSummary, '\u7d71\u8a08\u6458\u8981');
        }

        var p     = function (n) { return String(n).padStart(2, '0'); };
        var now   = new Date();
        var fname = '\u6b77\u53f2\u8da8\u52e2_' + now.getFullYear() + p(now.getMonth()+1) + p(now.getDate()) + '_' + p(now.getHours()) + p(now.getMinutes()) + '.xlsx';
        XLSX.writeFile(wb, fname);
    }

    // ── 工具函式 ─────────────────────────────────────────────────────────
    function fmtDtLocal(dt) {
        var p = function (n) { return String(n).padStart(2, '0'); };
        return dt.getFullYear() + '-' + p(dt.getMonth()+1) + '-' + p(dt.getDate()) + 'T' + p(dt.getHours()) + ':' + p(dt.getMinutes());
    }

    function showAlert(msg, type) {
        type = type || 'warning';
        var el = document.createElement('div');
        el.className = 'alert alert-' + type + ' alert-dismissible fade show mt-2';
        el.innerHTML = '<i class="fas fa-exclamation-triangle me-2"></i>' + msg + '<button type="button" class="btn-close" data-bs-dismiss="alert"></button>';
        var container = document.querySelector('.container-fluid');
        container.insertBefore(el, container.firstChild);
        setTimeout(function () { el.remove(); }, 3000);
    }

    // ── 事件綁定 ─────────────────────────────────────────────────────────
    document.addEventListener('DOMContentLoaded', function () {

        renderPointList(currentCoordinatorId, currentModbusId);
        renderBasket();

        // ── 外部導航預載（ScadaPage 趨勢圖右鍵選單）──
        (function checkTrendPreload() {
            try {
                var raw = localStorage.getItem('SCADA_TREND_PRELOAD');
                if (!raw) return;
                localStorage.removeItem('SCADA_TREND_PRELOAD');
                var payload = JSON.parse(raw);
                if (!payload.sids || !payload.sids.length) return;
                var items = payload.sids.map(function (s) {
                    var match = allPoints.find(function (p) { return p.sid === s.sid; });
                    return {
                        sid:  s.sid,
                        name: match ? (match.fullName || match.name) : (s.name || s.sid),
                        unit: match ? match.unit : (s.unit || '')
                    };
                });
                addToBasket(items);
            } catch (e) {
                console.warn('Trend preload parse error:', e);
            }
        })();

        function clearAllCoordinatorActive() {
            document.querySelectorAll('.coordinator-item, .coordinator-toggle, .sub-item')
                .forEach(function (el) { el.classList.remove('active'); });
        }

        document.querySelectorAll('.coordinator-item').forEach(function (item) {
            item.addEventListener('click', function (e) {
                e.preventDefault();
                clearAllCoordinatorActive();
                this.classList.add('active');
                currentCoordinatorId = parseInt(this.dataset.id) || 0;
                currentModbusId = null;
                currentCalcGroup = null;
                renderPointList(currentCoordinatorId);
            });
        });

        document.querySelectorAll('.coordinator-toggle').forEach(function (toggle) {
            toggle.addEventListener('click', function (e) {
                e.preventDefault();
                var subMenu = this.nextElementSibling;
                var icon = this.querySelector('.toggle-icon');
                if (subMenu.style.display === 'none') {
                    subMenu.style.display = '';
                    icon.classList.add('open');
                } else {
                    subMenu.style.display = 'none';
                    icon.classList.remove('open');
                }
                clearAllCoordinatorActive();
                this.classList.add('active');
                currentCoordinatorId = parseInt(this.dataset.id) || 0;
                currentModbusId = null;
                currentCalcGroup = null;
                renderPointList(currentCoordinatorId);
            });
        });

        document.querySelectorAll('.sub-item').forEach(function (item) {
            item.addEventListener('click', function (e) {
                e.preventDefault();
                clearAllCoordinatorActive();
                this.classList.add('active');
                currentCoordinatorId = parseInt(this.dataset.id) || 0;
                currentModbusId = parseInt(this.dataset.modbusid) || null;
                // 計算點位群組篩選
                currentCalcGroup = this.dataset.calcgroup != null ? this.dataset.calcgroup : null;
                renderPointList(currentCoordinatorId, currentModbusId, currentCalcGroup);
            });
        });

        document.getElementById('btnCheckAll').addEventListener('click', function () {
            pointListContainer.querySelectorAll('.point-checkbox').forEach(function (cb) { cb.checked = true; });
        });
        document.getElementById('btnUncheckAll').addEventListener('click', function () {
            pointListContainer.querySelectorAll('.point-checkbox').forEach(function (cb) { cb.checked = false; });
        });

        document.getElementById('btnAddToBasket').addEventListener('click', function () {
            var checked = Array.from(pointListContainer.querySelectorAll('.point-checkbox:checked'));
            if (checked.length === 0) { showAlert('\u8acb\u5148\u52fe\u9078\u9ede\u4f4d', 'warning'); return; }
            addToBasket(checked.map(function (cb) { return { sid: cb.value, name: cb.dataset.name, unit: cb.dataset.unit }; }));
            checked.forEach(function (cb) { cb.checked = false; });
        });

        document.getElementById('btnClearBasket').addEventListener('click', function () {
            basket = [];
            renderBasket();
        });

        btnQuery.addEventListener('click', doQuery);

        btnClear.addEventListener('click', function () {
            destroyAllCharts();
            lastResults = null;
            isSingleAxis = false;
            var btnAxis = document.getElementById('btnSingleAxis');
            btnAxis.classList.add('d-none');
            btnAxis.classList.remove('pressed');
            btnAxis.innerHTML = '<i class="fas fa-arrows-alt-v me-1"></i>\u96d9\u8ef8';
            document.getElementById('btnExportExcel').classList.add('d-none');
            chartContainer.style.display = 'none';
            document.getElementById('stackedChartsContainer').style.display = 'none';
            noDataMsg.innerHTML = '<i class="fas fa-chart-line fa-3x mb-3 d-block opacity-25"></i><div>\u8acb\u52a0\u5165\u9ede\u4f4d\u4e26\u8a2d\u5b9a\u6642\u9593\u7bc4\u570d\u5f8c\u6309\u4e0b\u67e5\u8a62</div>';
            noDataMsg.classList.remove('d-none');
            statsRow.style.display = 'none';
            multiStatsWrapper.style.display = 'none';
            dataTableCard.classList.add('d-none');
            limitWarning.classList.add('d-none');
            chartTitle.innerHTML = '<i class="fas fa-chart-area text-primary me-2"></i>\u8acb\u52a0\u5165\u9ede\u4f4d\u5f8c\u6309\u4e0b\u67e5\u8a62';
            document.getElementById('footerQueryTime').textContent = '';
        });

        document.querySelectorAll('.quick-range').forEach(function (btn) {
            btn.addEventListener('click', function () {
                var h = parseInt(this.dataset.hours);
                var now = new Date();
                dtStart.value = fmtDtLocal(new Date(now - h * 3600000));
                dtEnd.value   = fmtDtLocal(now);
            });
        });

        document.querySelectorAll('#chartTypeGroup button').forEach(function (btn) {
            btn.addEventListener('click', function () {
                document.querySelectorAll('#chartTypeGroup button').forEach(function (b) { b.classList.remove('active'); });
                this.classList.add('active');
                currentChartType = this.dataset.type;
                if (trendChart || stackedCharts.length > 0) doQuery();
            });
        });

        document.getElementById('btnSingleAxis').addEventListener('click', function () {
            isSingleAxis = !isSingleAxis;
            this.classList.toggle('pressed', isSingleAxis);
            this.innerHTML = isSingleAxis
                ? '<i class="fas fa-arrows-alt-v me-1"></i>\u55ae\u8ef8'
                : '<i class="fas fa-arrows-alt-v me-1"></i>\u96d9\u8ef8';
            if (lastResults) {
                buildChart(lastResults.filter(function (r) { return r.nCount > 0; }).map(function (json) {
                    var rawData = json.data.map(function (d) { return { x: d.t, y: d.v }; });
                    var quals   = json.data.map(function (d) { return d.q; });
                    var held    = holdLastGoodValue(rawData, quals);
                    return {
                        label:        json.szName,
                        szUnit:       json.szUnit,
                        data:         held.data,
                        qualities:    quals,
                        originalVals: held.originalVals
                    };
                }));
            }
        });

        btnToggleTable.addEventListener('click', function () {
            var hidden = tableBodyEl.classList.contains('d-none');
            tableBodyEl.classList.toggle('d-none', !hidden);
            btnToggleTable.innerHTML = hidden
                ? '<i class="fas fa-chevron-up me-1"></i>\u6536\u5408'
                : '<i class="fas fa-chevron-down me-1"></i>\u5c55\u958b';
        });
    });

    // ── 對外介面（供 onclick 等屬性呼叫）──────────────────────────────────
    window._history = {
        exportToExcel:   exportToExcel,
        removeFromBasket: removeFromBasket
    };

})();
