/* EMS Hub — 氣表三卡片（用氣量長條圖 / 子迴路用氣占比圓餅圖 / 氣費狀態卡）。
   長條圖與圓餅圖各自一組日/月/年切換；UI 粒度對應後端：日→hour、月→day、年→month
   （同 /EMS/api/gas-usage、/EMS/api/gas-breakdown 協定；月檢視取的是**氣費期別**實際起訖，可為兩月一期）。
   氣費卡資料源 GET /EMS/api/gas-cost?circuitId=，60s 輪詢（仿 ems-hub-water.js）。
   三卡可由 /EmsCardSetting 個別關閉（DOM 不渲染）→ 各卡以根元素存在與否防呆，三卡全關整支不動作。 */
(function () {
    'use strict';

    var REFRESH_MS = 60000;

    var _hasRoot = false;       // 是否已建立氣表根迴路
    var _rootId = null;
    var _barGran = 'hour';      // 長條圖粒度（獨立）
    var _pieGran = 'hour';      // 圓餅圖粒度（獨立）
    var _barChart = null;
    var _pieChart = null;
    var _refreshTimer = null;

    var _costCircuitId = null;  // null = 後端預設（根迴路）
    var _costTimer = null;
    var _costSelectFilled = false;

    // 圓餅色盤 — 首色改火橘（天然氣識別色），其餘跨全色相分佈以利分辨相鄰扇形
    var PIE_COLORS = ['#e65100', '#43a047', '#0288d1', '#8e24aa', '#e53935',
                      '#00acc1', '#fdd835', '#6d4c41', '#ec407a', '#26a69a',
                      '#5c6bc0', '#c0ca33'];

    // ── 工具函式 ─────────────────────────────────────────────
    function t(key, args) {
        return (window.i18n && window.i18n.t) ? window.i18n.t(key, args) : key;
    }

    function pad2(n) { return n < 10 ? '0' + n : String(n); }

    function todayStr() {
        var d = new Date();
        return d.getFullYear() + '-' + pad2(d.getMonth() + 1) + '-' + pad2(d.getDate());
    }

    function thisMonthStr() {
        var d = new Date();
        return d.getFullYear() + '-' + pad2(d.getMonth() + 1);
    }

    function thisYearStr() { return String(new Date().getFullYear()); }

    function escHtml(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function fmt(v, digits) {
        if (v == null) return '--';
        return v.toLocaleString('en-US', { minimumFractionDigits: digits, maximumFractionDigits: digits });
    }

    // ── 粒度切換元件（一組按鈕 + 三個 pivot 輸入框；同 ems-hub-water.js） ──
    function setupGranGroup(groupId, ids, onChange) {
        var group = document.getElementById(groupId);
        if (!group) return null;
        var inputs = {
            hour:  document.getElementById(ids.date),
            day:   document.getElementById(ids.month),
            month: document.getElementById(ids.year)
        };
        inputs.hour.value  = todayStr();
        inputs.day.value   = thisMonthStr();
        inputs.month.value = thisYearStr();

        group.querySelectorAll('.ems-gran-btn').forEach(function (btn) {
            btn.addEventListener('click', function () {
                group.querySelectorAll('.ems-gran-btn').forEach(function (b) { b.classList.remove('active'); });
                btn.classList.add('active');
                var gran = btn.dataset.gran;
                Object.keys(inputs).forEach(function (k) {
                    inputs[k].style.display = (k === gran) ? '' : 'none';
                });
                onChange(gran);
            });
        });
        Object.keys(inputs).forEach(function (k) {
            inputs[k].addEventListener('change', function () {
                if (this.value) onChange(k);
            });
        });
        return inputs;
    }

    var _barInputs, _pieInputs;

    function pivotOf(inputs, gran) {
        // inputs=null（粒度控制所在卡片被關閉）→ 回今日/當月/今年預設
        var v = inputs && inputs[gran] ? inputs[gran].value : '';
        if (v) return v;
        return gran === 'hour' ? todayStr() : gran === 'day' ? thisMonthStr() : thisYearStr();
    }

    // ── 初始化 ───────────────────────────────────────────────
    function init() {
        var hasBar  = !!document.getElementById('gasBarChartWrap');
        var hasPie  = !!document.getElementById('gasPieChartWrap');
        var hasCost = !!document.getElementById('gasCostCardBody');
        if (!hasBar && !hasPie && !hasCost) return;

        _barInputs = setupGranGroup('gasBarGranGroup',
            { date: 'gasBarPivotDate', month: 'gasBarPivotMonth', year: 'gasBarPivotYear' },
            function (gran) { _barGran = gran; loadBar(); });
        _pieInputs = setupGranGroup('gasPieGranGroup',
            { date: 'gasPiePivotDate', month: 'gasPiePivotMonth', year: 'gasPiePivotYear' },
            function (gran) { _pieGran = gran; loadPie(); });

        // 氣費卡獨立啟動（不依賴迴路樹；未建迴路由 API 回應處理）
        if (hasCost) {
            loadCost();
            _costTimer = setInterval(loadCost, REFRESH_MS);
            document.getElementById('gasCostCircuitSelect').addEventListener('change', function () {
                _costCircuitId = this.value ? parseInt(this.value, 10) : null;
                loadCost();
            });
        }

        if (!hasBar && !hasPie) return;

        fetch('/EMS/api/gas-circuit-tree')
            .then(function (r) { return r.json(); })
            .then(function (nodes) {
                var roots = (nodes || []).filter(function (n) { return n.parentId == null; })
                    .sort(function (a, b) { return a.sortOrder - b.sortOrder || a.id - b.id; });
                if (roots.length === 0) {
                    showNoRoot();
                    return;
                }
                _hasRoot = true;
                _rootId = roots[0].id;
                var elName = document.getElementById('gasBarCircuitName');
                if (elName) elName.textContent = roots[0].name;
                loadBar();
                loadPie();
                startAutoRefresh();
            })
            .catch(function (e) { console.error('[ems-hub-gas] 載入氣表迴路樹失敗', e); });
    }

    // ── 未建立氣表根迴路：長條 / 圓餅統一提示（被關閉的卡片元素不存在 → 逐一防呆跳過）──
    function showNoRoot() {
        [['gasBarEmpty', 'gasBarChartWrap'], ['gasPieEmpty', 'gasPieChartWrap']]
            .forEach(function (pair) {
                var empty = document.getElementById(pair[0]);
                var body  = document.getElementById(pair[1]);
                if (!empty || !body) return;
                empty.textContent = t('ems.gas.no_root');
                empty.style.display = '';
                body.style.display = 'none';
            });
        ['gasBarGranGroup', 'gasPieGranGroup'].forEach(function (id) {
            var group = document.getElementById(id);
            if (!group) return;
            group.querySelectorAll('.ems-gran-btn').forEach(function (b) { b.disabled = true; });
        });
        ['gasBarPivotDate', 'gasBarPivotMonth', 'gasBarPivotYear',
         'gasPiePivotDate', 'gasPiePivotMonth', 'gasPiePivotYear']
            .forEach(function (id) {
                var el = document.getElementById(id);
                if (el) el.disabled = true;
            });
    }

    // ── 用氣量長條圖 ─────────────────────────────────────────
    function loadBar() {
        if (!_hasRoot || !document.getElementById('gasBarChartWrap')) return;
        var pivot = pivotOf(_barInputs, _barGran);
        // circuitId 不帶 → 後端取根迴路（全廠）
        var url = '/EMS/api/gas-usage?granularity=' + encodeURIComponent(_barGran) +
                  '&pivot=' + encodeURIComponent(pivot);
        fetch(url)
            .then(function (r) { return r.json(); })
            .then(function (data) {
                renderBar(data.labels || [], data.values || []);
                var warn = document.getElementById('gasBarWarnNote');
                if (warn) {
                    warn.textContent = data.hasWarning ? t('ems.gas.stale_note') : '';
                    warn.style.display = data.hasWarning ? '' : 'none';
                }
            })
            .catch(function (e) { console.error('[ems-hub-gas] 長條圖載入失敗', e); });
    }

    function renderBar(labels, values) {
        var canvas = document.getElementById('gasBarChart');
        if (!canvas || !window.Chart) return;

        if (_barChart) {
            _barChart.data.labels           = labels;
            _barChart.data.datasets[0].data = values;
            _barChart.update('none');
            return;
        }

        _barChart = new Chart(canvas.getContext('2d'), {
            type: 'bar',
            data: {
                labels: labels,
                datasets: [{
                    data: values,
                    backgroundColor: 'rgba(230,81,0,0.55)',
                    borderColor: '#e65100',
                    borderWidth: 1,
                    borderRadius: 3
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        callbacks: {
                            label: function (ctx) {
                                var v = ctx.parsed.y;
                                return (v != null ? v.toFixed(2) : '0') + ' m³';
                            }
                        }
                    }
                },
                scales: {
                    x: {
                        ticks: { font: { size: 11 }, color: '#757575', maxTicksLimit: 16 },
                        grid: { display: false }
                    },
                    y: {
                        beginAtZero: true,
                        ticks: {
                            font: { size: 11 },
                            color: '#757575',
                            maxTicksLimit: 6,
                            // 依數值級距自動決定小數位數，避免 toFixed(0) 把 0.5 壓成 0 造成整排重複
                            callback: function (v) {
                                if (v === 0) return '0';
                                var abs = Math.abs(v);
                                if (abs >= 10) return v.toFixed(0);
                                if (abs >= 1)  return (+v.toFixed(1)).toString();
                                return (+v.toFixed(2)).toString();
                            }
                        },
                        grid: { color: 'rgba(0,0,0,0.05)' }
                    }
                }
            }
        });
    }

    // ── 子迴路用氣占比圓餅圖 ─────────────────────────────────
    function loadPie() {
        if (!_hasRoot || !document.getElementById('gasPieChartWrap')) return;
        var pivot = pivotOf(_pieInputs, _pieGran);
        var url = '/EMS/api/gas-breakdown?granularity=' + encodeURIComponent(_pieGran) +
                  '&pivot=' + encodeURIComponent(pivot);
        fetch(url)
            .then(function (r) { return r.json(); })
            .then(function (data) { renderPie(data.items || []); })
            .catch(function (e) { console.error('[ems-hub-gas] 圓餅圖載入失敗', e); });
    }

    function renderPie(items) {
        var empty   = document.getElementById('gasPieEmpty');
        var wrap    = document.getElementById('gasPieChartWrap');
        var negNote = document.getElementById('gasPieNegNote');

        // 圓餅無法表達負值：負值迴路（Sign=-1 扣減）改列於下方小字，不入餅
        var positives = items.filter(function (it) { return it.m3 > 0; });
        var negatives = items.filter(function (it) { return it.m3 < 0; });

        negNote.innerHTML = negatives.map(function (it) {
            return escHtml(t('ems.gas.neg_excluded', { 0: it.name, 1: fmt(it.m3, 2) }));
        }).join('<br>');

        if (positives.length === 0) {
            empty.textContent = t('ems.gas.no_data');
            empty.style.display = '';
            wrap.style.display = 'none';
            return;
        }
        empty.style.display = 'none';
        wrap.style.display = '';

        var labels = positives.map(function (it) { return it.name; });
        var values = positives.map(function (it) { return it.m3; });
        var colors = positives.map(function (_, i) { return PIE_COLORS[i % PIE_COLORS.length]; });

        if (_pieChart) {
            _pieChart.data.labels                      = labels;
            _pieChart.data.datasets[0].data            = values;
            _pieChart.data.datasets[0].backgroundColor = colors;
            _pieChart.update('none');
            return;
        }

        _pieChart = new Chart(document.getElementById('gasPieChart').getContext('2d'), {
            type: 'pie',
            data: {
                labels: labels,
                datasets: [{
                    data: values,
                    backgroundColor: colors,
                    borderColor: '#fff',
                    borderWidth: 1.5
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: {
                        position: 'bottom',
                        labels: { font: { size: 11 }, boxWidth: 12, color: '#555' }
                    },
                    tooltip: {
                        callbacks: {
                            label: function (ctx) {
                                var total = ctx.dataset.data.reduce(function (a, b) { return a + b; }, 0);
                                var pct = total > 0 ? (ctx.parsed / total * 100).toFixed(1) : '0';
                                return ctx.label + ': ' + ctx.parsed.toFixed(2) + ' m³ (' + pct + '%)';
                            }
                        }
                    }
                }
            }
        });
    }

    // ── 氣費狀態卡 ───────────────────────────────────────────
    function loadCost() {
        if (!document.getElementById('gasCostCardBody')) return;
        var url = '/EMS/api/gas-cost' + (_costCircuitId != null ? '?circuitId=' + _costCircuitId : '');
        fetch(url)
            .then(function (r) {
                if (!r.ok) throw new Error(r.statusText);
                return r.json();
            })
            .then(function (data) {
                renderCost(data);
                if (data.hasPlan && data.circuitId != null && !_costSelectFilled) {
                    _costSelectFilled = true;
                    fillCostCircuitSelect(data.circuitId);
                }
            })
            .catch(function (e) { console.error('[ems-hub-gas] 載入氣費狀態失敗', e); });
    }

    // 迴路下拉 — flat 清單組樹（縮排顯示層級），預設選到後端回覆的迴路
    function fillCostCircuitSelect(selectedId) {
        fetch('/EMS/api/gas-circuit-tree')
            .then(function (r) { return r.json(); })
            .then(function (nodes) {
                if (!nodes || !nodes.length) return;

                var byParent = {};
                nodes.forEach(function (n) {
                    var key = n.parentId == null ? 'root' : n.parentId;
                    (byParent[key] = byParent[key] || []).push(n);
                });
                Object.keys(byParent).forEach(function (k) {
                    byParent[k].sort(function (a, b) { return a.sortOrder - b.sortOrder || a.id - b.id; });
                });

                var opts = [];
                (function walk(parentKey, depth) {
                    (byParent[parentKey] || []).forEach(function (n) {
                        var indent = new Array(depth + 1).join('　');   // 全形空白縮排
                        opts.push('<option value="' + n.id + '">' + escHtml(indent + n.name) + '</option>');
                        walk(n.id, depth + 1);
                    });
                })('root', 0);

                var sel = document.getElementById('gasCostCircuitSelect');
                sel.innerHTML = opts.join('');
                sel.value = String(selectedId);
                sel.style.display = '';
            })
            .catch(function (e) { console.error('[ems-hub-gas] 載入氣表迴路清單失敗', e); });
    }

    function renderCost(d) {
        var body = document.getElementById('gasCostCardBody');
        var foot = document.getElementById('gasCostCardFoot');
        var periodText = document.getElementById('gasCostPeriodText');

        if (!d.hasPlan) {
            periodText.textContent = '';
            foot.innerHTML = '';
            body.innerHTML = '<div class="text-center text-muted py-4">' +
                '<i class="fas fa-fire fa-2x mb-2 d-block opacity-50"></i>' +
                escHtml(t('ems.gas.cost.no_plan')) +
                '<div class="mt-2"><a href="/GasTariffSetting" class="btn btn-sm btn-outline-primary">' +
                escHtml(t('ems.gas.cost.goto_tariff')) + '</a></div></div>';
            return;
        }
        if (d.circuitId == null) {
            periodText.textContent = '';
            foot.innerHTML = '';
            body.innerHTML = '<div class="text-center text-muted py-4">' + escHtml(t('ems.gas.no_root')) + '</div>';
            return;
        }

        periodText.textContent = t('ems.gas.cost.period', { 0: d.periodLabel });

        var html = '<div class="small text-muted mb-1 text-truncate" title="' + escHtml(d.planName) + '">' +
            '<i class="fas fa-check-circle me-1"></i>' + escHtml(d.planName) + '</div>';

        // 累計金額 + 度數（大字，火橘）
        html += '<div class="d-flex align-items-baseline justify-content-center gap-2 py-1">' +
            '<span class="fw-bold" style="font-size:1.8rem;line-height:1;color:#e65100;">' +
            (d.totalCost == null ? '--' : fmt(d.totalCost, 0)) + '</span>' +
            '<span class="small text-muted">' + escHtml(t('ems.gas.cost.unit_ntd')) + '</span>' +
            (d.isStale ? '<span class="badge bg-warning text-dark">' + escHtml(t('ems.gas.cost.stale')) + '</span>' : '') +
            '</div>';
        html += '<div class="text-center small text-muted mb-2">' +
            escHtml(t('ems.gas.cost.total_m3', { 0: fmt(d.totalM3, 2) })) + '</div>';

        html += renderTiers(d.tiers || []);

        body.innerHTML = html;

        // 底部註記：方案生效資訊 + 資料不完整註記
        var notes = ['<i class="fas fa-info-circle me-1"></i>' +
            escHtml(t('ems.gas.cost.effective', { 0: d.planName, 1: d.effectiveDate }))];
        if (d.isStale) notes.push(escHtml(t('ems.gas.cost.stale_note')));
        foot.innerHTML = notes.join('<br>');
    }

    // 級距明細表（from~to m³ × 單價 → 分段 m³ / 分段氣費）
    function renderTiers(tiers) {
        if (!tiers.length) return '';
        var rows = tiers.map(function (tr) {
            var range = tr.to == null
                ? t('ems.gas.cost.tier_above', { 0: tr.from })
                : t('ems.gas.cost.tier_range', { 0: tr.from, 1: tr.to });
            return '<tr>' +
                '<td>' + escHtml(range) +
                '<div class="small text-muted">' + escHtml(fmt(tr.price, 2)) + ' ' + escHtml(t('ems.gas.cost.per_m3')) + '</div></td>' +
                '<td class="text-end">' + escHtml(fmt(tr.sliceM3, 2)) + '</td>' +
                '<td class="text-end">' + escHtml(fmt(tr.sliceCost, 0)) + '</td>' +
                '</tr>';
        }).join('');

        return '<div class="table-responsive"><table class="table table-sm align-middle mb-0 ems-cost-table">' +
            '<thead><tr>' +
            '<th>' + escHtml(t('ems.gas.cost.col_tier')) + '</th>' +
            '<th class="text-end">m³</th>' +
            '<th class="text-end">' + escHtml(t('ems.gas.cost.col_cost')) + '</th>' +
            '</tr></thead><tbody>' + rows + '</tbody></table></div>';
    }

    // ── 自動刷新（僅長條圖：日模式且選的是今天；長條卡關閉時 _barInputs=null 不刷）──
    function startAutoRefresh() {
        clearTimeout(_refreshTimer);
        _refreshTimer = setTimeout(function tick() {
            if (_barGran === 'hour' && _barInputs && _barInputs.hour.value === todayStr()) {
                loadBar();
            }
            _refreshTimer = setTimeout(tick, REFRESH_MS);
        }, REFRESH_MS);
    }

    window._emsHubGas = { reloadBar: loadBar, reloadPie: loadPie, reloadCost: loadCost };

    document.addEventListener('DOMContentLoaded', function () {
        if (window.i18n && window.i18n.ready) window.i18n.ready(init);
        else init();
    });
})();
