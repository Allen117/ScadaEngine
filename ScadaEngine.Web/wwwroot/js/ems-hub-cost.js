/* EMS Hub — 電費狀態卡。
   依採用方案型態自適應：tou（各時段 kWh/電費）/ progressive（累計 kWh + 級距）/ flat（累計 kWh × 當季單價）。
   可下拉切換迴路（預設主要電表/根迴路）；金額只含流動電費（不含基本電費）。
   資料源 GET /EMS/api/electricity-cost?circuitId=，60s 輪詢。 */
(function () {
    'use strict';

    var REFRESH_MS = 60000;

    var _circuitId = null;      // null = 後端預設（主要電表/根迴路）
    var _timer = null;
    var _selectFilled = false;

    document.addEventListener('DOMContentLoaded', function () {
        if (window.i18n) window.i18n.ready(init);
        else init();
    });

    function t(key, args) {
        return (window.i18n && window.i18n.t) ? window.i18n.t(key, args) : key;
    }

    function escHtml(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function fmt(v, digits) {
        if (v == null) return '--';
        return v.toLocaleString('en-US', { minimumFractionDigits: digits, maximumFractionDigits: digits });
    }

    function init() {
        var sel = document.getElementById('costCircuitSelect');
        if (!sel) return; // 電費卡由 /EmsCardSetting 關閉（DOM 不渲染）→ 跳過 init 與輪詢
        load();
        _timer = setInterval(load, REFRESH_MS);
        sel.addEventListener('change', function () {
            _circuitId = this.value ? parseInt(this.value, 10) : null;
            load();
        });
    }

    async function load() {
        try {
            var url = '/EMS/api/electricity-cost' + (_circuitId != null ? '?circuitId=' + _circuitId : '');
            var res = await fetch(url);
            if (!res.ok) throw new Error(res.statusText);
            var data = await res.json();
            render(data);
            if (data.hasPlan && data.hasCircuit && !_selectFilled) {
                _selectFilled = true;
                fillCircuitSelect(data.circuitId);
            }
        } catch (e) {
            console.error('[ems-hub-cost] 載入電費狀態失敗', e);
        }
    }

    // 迴路下拉 — flat 清單組樹（縮排顯示層級），預設選到後端回覆的迴路
    async function fillCircuitSelect(selectedId) {
        try {
            var res = await fetch('/EMS/api/circuit-tree');
            if (!res.ok) return;
            var nodes = await res.json();
            if (!nodes.length) return;

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

            var sel = document.getElementById('costCircuitSelect');
            sel.innerHTML = opts.join('');
            sel.value = String(selectedId);
            sel.style.display = '';
        } catch (e) {
            console.error('[ems-hub-cost] 載入迴路清單失敗', e);
        }
    }

    // ── 渲染 ─────────────────────────────────────────────

    function render(d) {
        var body = document.getElementById('costCardBody');
        var foot = document.getElementById('costCardFoot');
        var periodText = document.getElementById('costPeriodText');

        if (!d.hasPlan) {
            periodText.textContent = '';
            foot.innerHTML = '';
            body.innerHTML = '<div class="text-center text-muted py-4">' +
                '<i class="fas fa-file-invoice-dollar fa-2x mb-2 d-block opacity-50"></i>' +
                escHtml(t('ems.cost.no_plan')) +
                '<div class="mt-2"><a href="/TariffSetting" class="btn btn-sm btn-outline-primary">' +
                escHtml(t('ems.cost.goto_tariff')) + '</a></div></div>';
            return;
        }
        if (!d.hasCircuit) {
            periodText.textContent = '';
            foot.innerHTML = '';
            body.innerHTML = '<div class="text-center text-muted py-4">' + escHtml(t('ems.cost.no_circuit')) + '</div>';
            return;
        }

        periodText.textContent = t('ems.cost.period', { 0: d.periodLabel });

        var planName = t('tariff.category.' + d.planCategory) + '－' + t('tariff.plan.' + d.planId);
        var html = '<div class="small text-muted mb-1 text-truncate" title="' + escHtml(planName) + '">' +
            '<i class="fas fa-check-circle me-1"></i>' + escHtml(planName) + '</div>';

        // 累計金額 + 度數（大字）
        html += '<div class="d-flex align-items-baseline justify-content-center gap-2 py-1">' +
            '<span class="fw-bold" style="font-size:1.8rem;line-height:1;color:#2e7d32;">' +
            (d.totalCost == null ? '--' : fmt(d.totalCost, 0)) + '</span>' +
            '<span class="small text-muted">' + escHtml(t('ems.cost.unit_ntd')) + '</span>' +
            (d.isEstimated ? '<span class="badge bg-warning text-dark">' + escHtml(t('ems.cost.estimated')) + '</span>' : '') +
            '</div>';
        html += '<div class="text-center small text-muted mb-2">' +
            escHtml(t('ems.cost.total_kwh', { 0: fmt(d.totalKwh, 1) })) + '</div>';

        if (d.planType === 'tou') html += renderTou(d);
        else if (d.planType === 'progressive') html += renderProgressive(d);
        else if (d.planType === 'flat') html += renderFlat(d);

        // 今日小計
        html += '<div class="d-flex justify-content-between align-items-baseline px-1 mt-2">' +
            '<span class="small text-muted">' + escHtml(t('ems.cost.today')) + '</span>' +
            '<span class="small">' + escHtml(fmt(d.todayKwh, 1)) + ' kWh' +
            (d.todayCost != null ? '<span class="fw-semibold ms-2">' + escHtml(fmt(d.todayCost, 0)) + ' ' + escHtml(t('ems.cost.unit_ntd')) + '</span>' : '') +
            '</span></div>';

        body.innerHTML = html;

        // 底部註記：不含基本電費 + 估算註記 + 資料時間
        var notes = ['<i class="fas fa-info-circle me-1"></i>' + escHtml(t('ems.cost.exclude_base'))];
        if (d.isEstimated) notes.push(escHtml(t('ems.cost.estimated_note')));
        if (d.lastHour) notes.push(escHtml(t('ems.cost.data_until', { 0: d.lastHour })));
        foot.innerHTML = notes.join('<br>');
    }

    // tou：各時段明細表（+ 超額加價列）
    function renderTou(d) {
        var rows = (d.periods || []).map(function (p) {
            return '<tr>' +
                '<td>' + escHtml(t('tariff.period.' + p.period)) + '</td>' +
                '<td class="text-end">' + escHtml(fmt(p.kwh, 1)) + '</td>' +
                '<td class="text-end">' + escHtml(fmt(p.cost, 0)) + '</td>' +
                '</tr>';
        }).join('');

        if (d.surcharge) {
            rows += '<tr>' +
                '<td class="text-warning">' + escHtml(t('ems.cost.surcharge', { 0: d.surcharge.overKwh })) + '</td>' +
                '<td class="text-end">--</td>' +
                '<td class="text-end text-warning">' + escHtml(fmt(d.surcharge.amount, 0)) + '</td>' +
                '</tr>';
        }

        return '<div class="table-responsive"><table class="table table-sm align-middle mb-0 ems-cost-table">' +
            '<thead><tr>' +
            '<th>' + escHtml(t('ems.cost.col_period')) + '</th>' +
            '<th class="text-end">kWh</th>' +
            '<th class="text-end">' + escHtml(t('ems.cost.col_cost')) + '</th>' +
            '</tr></thead><tbody>' + rows + '</tbody></table></div>';
    }

    // progressive：級距落點
    function renderProgressive(d) {
        if (!d.progressive) return '';
        var p = d.progressive;
        var range = p.tierTo == null
            ? t('ems.cost.tier_above', { 0: p.tierFrom })
            : t('ems.cost.tier_range', { 0: p.tierFrom, 1: p.tierTo });
        return '<div class="d-flex justify-content-between align-items-baseline px-1">' +
            '<span class="small text-muted">' + escHtml(t('ems.cost.current_tier')) + '</span>' +
            '<span class="small fw-semibold">' + escHtml(t('ems.cost.tier_n', { 0: p.tierIndex + 1 })) +
            ' <span class="text-muted">(' + escHtml(range) + ')</span></span></div>';
    }

    // flat：當季單價
    function renderFlat(d) {
        if (!d.flat) return '';
        return '<div class="d-flex justify-content-between align-items-baseline px-1">' +
            '<span class="small text-muted">' + escHtml(t('ems.cost.flat_price')) + '</span>' +
            '<span class="small fw-semibold">' + escHtml(fmt(d.flat.unitPrice, 4)) + ' ' + escHtml(t('ems.cost.per_kwh')) +
            ' <span class="text-muted">(' + escHtml(t('tariffsetting.season.' + d.flat.season)) + ')</span></span></div>';
    }
})();
