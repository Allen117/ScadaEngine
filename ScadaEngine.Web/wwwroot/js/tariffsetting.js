// 電費設定頁邏輯 — 台電各類電價方案（累進 / 單一費率 / 時間電價）檢視與編輯。
// 資料模型見 Features/TariffSetting/Models/TariffSettingModels.cs；台電預設 seed 見 Setting/tariff-taipower-defaults.json。
// 時間輸入用 flatpickr（24h）；訖時 00:00 代表 24:00（當日結束），起時晚於訖時代表跨午夜。
(function () {
    'use strict';

    var CATEGORIES = ['lighting', 'lv', 'hv', 'ehv'];
    var DAY_TYPES = ['weekday', 'sat', 'sun_offday'];
    var SEASONS = ['summer', 'nonsummer'];
    var PERIOD_ORDER = { peak: 0, semipeak: 1, offpeak: 2 };

    var g_config = null;      // 整份 TariffConfig（伺服器版）
    var g_plan = null;        // 目前顯示方案的工作副本（collect() 時由 DOM 回填）
    var g_dirty = false;

    document.addEventListener('DOMContentLoaded', function () {
        if (window.i18n) window.i18n.ready(load);
        else load();
    });

    function t(key, args) {
        return (window.i18n && window.i18n.t) ? window.i18n.t(key, args) : key;
    }

    // ── 載入 ─────────────────────────────────────────────

    async function load() {
        try {
            var res = await fetch('/TariffSetting/api/config');
            if (!res.ok) throw new Error(res.statusText);
            g_config = await res.json();
        } catch (err) {
            console.error('tariff config load failed', err);
            document.getElementById('tsPlanContainer').innerHTML =
                '<div class="text-center text-danger py-4">' + escapeHtml(t('tariffsetting.msg.load_fail')) + '</div>';
            return;
        }

        fillCategorySelect();

        // 預設選到採用方案；未設定則第一類別第一方案
        var active = findPlan(g_config.szActivePlanId);
        var initial = active || g_config.plans[0];
        if (initial) {
            document.getElementById('tsCategory').value = initial.szCategory;
            fillPlanSelect(initial.szCategory);
            document.getElementById('tsPlan').value = initial.szPlanId;
            selectPlan(initial.szPlanId);
        }
        updateActiveCard();

        document.getElementById('tsCategory').addEventListener('change', onCategoryChange);
        document.getElementById('tsPlan').addEventListener('change', onPlanChange);

        // 任一編輯 → 標記未儲存（container 常駐，掛一次即可）
        var container = document.getElementById('tsPlanContainer');
        container.addEventListener('input', function () { g_dirty = true; });
        container.addEventListener('change', function () { g_dirty = true; });
    }

    function findPlan(planId) {
        if (!planId || !g_config) return null;
        return g_config.plans.find(function (p) { return p.szPlanId === planId; }) || null;
    }

    function planLabel(p) {
        return t('tariff.category.' + p.szCategory) + '－' + t('tariff.plan.' + p.szPlanId);
    }

    // ── 方案選單 ─────────────────────────────────────────

    function fillCategorySelect() {
        var sel = document.getElementById('tsCategory');
        sel.innerHTML = CATEGORIES.map(function (c) {
            return '<option value="' + c + '">' + escapeHtml(t('tariff.category.' + c)) + '</option>';
        }).join('');
    }

    function fillPlanSelect(category) {
        var sel = document.getElementById('tsPlan');
        var activeSuffix = ' (' + t('tariffsetting.badge.active') + ')';
        sel.innerHTML = g_config.plans
            .filter(function (p) { return p.szCategory === category; })
            .map(function (p) {
                var suffix = p.szPlanId === g_config.szActivePlanId ? activeSuffix : '';
                return '<option value="' + p.szPlanId + '">' + escapeHtml(t('tariff.plan.' + p.szPlanId) + suffix) + '</option>';
            }).join('');
        document.getElementById('tsCategoryDesc').textContent = t('tariff.category_desc.' + category);
    }

    function onCategoryChange() {
        if (!confirmDiscardDirty()) {
            document.getElementById('tsCategory').value = g_plan.szCategory;
            return;
        }
        var category = document.getElementById('tsCategory').value;
        fillPlanSelect(category);
        var first = document.getElementById('tsPlan').value;
        if (first) selectPlan(first);
    }

    function onPlanChange() {
        if (!confirmDiscardDirty()) {
            document.getElementById('tsPlan').value = g_plan.szPlanId;
            return;
        }
        selectPlan(document.getElementById('tsPlan').value);
    }

    function confirmDiscardDirty() {
        if (!g_dirty) return true;
        return confirm(t('tariffsetting.confirm.switch_dirty'));
    }

    function selectPlan(planId) {
        var plan = findPlan(planId);
        if (!plan) return;
        g_plan = JSON.parse(JSON.stringify(plan));
        g_dirty = false;
        renderPlan();
    }

    function updateActiveCard() {
        var active = findPlan(g_config.szActivePlanId);
        document.getElementById('tsActivePlanName').textContent = active ? planLabel(active) : t('tariffsetting.card.no_active');
        var isActive = g_plan && g_plan.szPlanId === g_config.szActivePlanId;
        document.getElementById('tsActiveBadge').classList.toggle('d-none', !isActive);
        document.getElementById('btnTsSetActive').classList.toggle('d-none', !!isActive);
    }

    // ── 渲染 ─────────────────────────────────────────────

    function renderPlan() {
        var p = g_plan;
        document.getElementById('tsPlanTitle').textContent = planLabel(p);
        document.getElementById('tsTypeBadge').innerHTML =
            '<span class="badge ' + (p.szType === 'tou' ? 'bg-primary' : 'bg-secondary') + '">' +
            escapeHtml(t('tariff.type.' + p.szType)) + '</span>';

        var html = '';
        if (p.szNoteKey) {
            html += '<div class="alert alert-info py-2 ts-note"><i class="fas fa-info-circle me-1"></i>' +
                escapeHtml(t(p.szNoteKey)) + '</div>';
        }
        html += renderSummerRange(p);

        if (p.szType === 'progressive') {
            html += renderTiers(p);
        } else {
            if (p.baseFees.length > 0) html += renderBaseFees(p);
            if (p.szType === 'flat') html += renderFlatRate(p);
            if (p.szType === 'tou') html += renderFlowRates(p);
            if (p.surcharge) html += renderSurcharge(p);
        }

        var container = document.getElementById('tsPlanContainer');
        container.innerHTML = html;

        // flatpickr 綁定（禁原生 time input — CLAUDE.md 規範）
        container.querySelectorAll('.ts-time').forEach(function (el) {
            if (window._fpInit) window._fpInit.time(el);
        });
        // 夏月日下拉依月份重建
        container.querySelectorAll('.ts-summer-month').forEach(function (el) {
            el.addEventListener('change', function () { rebuildDayOptions(el); });
        });

        updateActiveCard();
    }

    function renderSummerRange(p) {
        var start = splitMonthDay(p.szSummerStart);
        var end = splitMonthDay(p.szSummerEnd);
        return '<div class="ts-summer-bar mb-3">' +
            '<span class="fw-semibold me-2"><i class="fas fa-sun me-1 text-warning"></i>' + escapeHtml(t('tariffsetting.label.summer_range')) + '</span>' +
            monthDaySelects('tsSummerStart', start) +
            '<span class="mx-2">~</span>' +
            monthDaySelects('tsSummerEnd', end) +
            '</div>';
    }

    function splitMonthDay(s) {
        var parts = (s || '').split('-');
        return { m: parseInt(parts[0], 10) || 1, d: parseInt(parts[1], 10) || 1 };
    }

    function monthDaySelects(idPrefix, val) {
        var mOpts = '', dOpts = '';
        for (var m = 1; m <= 12; m++)
            mOpts += '<option value="' + m + '"' + (m === val.m ? ' selected' : '') + '>' + m + '</option>';
        var maxDay = daysInMonth(val.m);
        for (var d = 1; d <= maxDay; d++)
            dOpts += '<option value="' + d + '"' + (d === val.d ? ' selected' : '') + '>' + d + '</option>';
        return '<select id="' + idPrefix + 'M" class="form-select form-select-sm ts-md-select ts-summer-month" data-day-select="' + idPrefix + 'D">' + mOpts + '</select>' +
            '<span class="mx-1">' + escapeHtml(t('tariffsetting.label.month')) + '</span>' +
            '<select id="' + idPrefix + 'D" class="form-select form-select-sm ts-md-select">' + dOpts + '</select>' +
            '<span class="ms-1">' + escapeHtml(t('tariffsetting.label.day')) + '</span>';
    }

    function daysInMonth(m) {
        return new Date(2000, m, 0).getDate();   // 2000 為閏年 → 2 月 29 天
    }

    function rebuildDayOptions(monthSel) {
        var daySel = document.getElementById(monthSel.getAttribute('data-day-select'));
        var prev = parseInt(daySel.value, 10) || 1;
        var maxDay = daysInMonth(parseInt(monthSel.value, 10));
        var opts = '';
        for (var d = 1; d <= maxDay; d++)
            opts += '<option value="' + d + '"' + (d === Math.min(prev, maxDay) ? ' selected' : '') + '>' + d + '</option>';
        daySel.innerHTML = opts;
    }

    function priceInput(value, attrs) {
        return '<input type="number" class="form-control form-control-sm ts-price" step="0.01" min="0" value="' +
            (value == null ? '' : value) + '" ' + attrs + '>';
    }

    // 累進級距表
    function renderTiers(p) {
        var rows = p.tiers.map(function (tier, i) {
            var isLast = i === p.tiers.length - 1;
            var rangeCell;
            if (i === 0) {
                rangeCell = '<span class="text-nowrap">' +
                    '<input type="number" class="form-control form-control-sm ts-tier-to d-inline-block" step="1" min="1" value="' + tier.nTo + '" data-tier="' + i + '" data-field="to"> ' +
                    escapeHtml(t('tariffsetting.tier.kwh_below')) + '</span>';
            } else if (isLast) {
                rangeCell = '<span class="text-nowrap">' + tier.nFrom + ' ' + escapeHtml(t('tariffsetting.tier.kwh_above')) + '</span>';
            } else {
                rangeCell = '<span class="text-nowrap">' + tier.nFrom + ' ~ ' +
                    '<input type="number" class="form-control form-control-sm ts-tier-to d-inline-block" step="1" min="1" value="' + tier.nTo + '" data-tier="' + i + '" data-field="to"> ' +
                    escapeHtml(t('tariffsetting.tier.kwh')) + '</span>';
            }
            return '<tr>' +
                '<td>' + rangeCell + '</td>' +
                '<td>' + priceInput(tier.dSummer, 'data-tier="' + i + '" data-field="summer"') + '</td>' +
                '<td>' + priceInput(tier.dNonSummer, 'data-tier="' + i + '" data-field="nonsummer"') + '</td>' +
                '</tr>';
        }).join('');

        return sectionCard('tariffsetting.section.tiers',
            '<div class="table-responsive"><table class="table table-sm table-bordered align-middle mb-0 ts-table">' +
            '<thead class="table-light"><tr>' +
            '<th>' + escapeHtml(t('tariffsetting.col.tier_range')) + '</th>' +
            '<th>' + escapeHtml(t('tariffsetting.col.summer')) + ' (' + escapeHtml(t('tariffsetting.unit.per_kwh')) + ')</th>' +
            '<th>' + escapeHtml(t('tariffsetting.col.nonsummer')) + ' (' + escapeHtml(t('tariffsetting.unit.per_kwh')) + ')</th>' +
            '</tr></thead><tbody>' + rows + '</tbody></table></div>');
    }

    // 基本電費表
    function renderBaseFees(p) {
        var rows = p.baseFees.map(function (fee, i) {
            return '<tr>' +
                '<td>' + escapeHtml(t('tariff.basefee.' + fee.szKey)) + '</td>' +
                '<td class="text-muted">' + escapeHtml(t('tariff.unit.' + fee.szUnit)) + '</td>' +
                '<td>' + (fee.dSummer == null
                    ? '<span class="text-muted">' + escapeHtml(t('tariffsetting.na')) + '</span>'
                    : priceInput(fee.dSummer, 'data-fee="' + i + '" data-field="summer"')) + '</td>' +
                '<td>' + (fee.dNonSummer == null
                    ? '<span class="text-muted">' + escapeHtml(t('tariffsetting.na')) + '</span>'
                    : priceInput(fee.dNonSummer, 'data-fee="' + i + '" data-field="nonsummer"')) + '</td>' +
                '</tr>';
        }).join('');

        return sectionCard('tariffsetting.section.base_fees',
            '<div class="table-responsive"><table class="table table-sm table-bordered align-middle mb-0 ts-table">' +
            '<thead class="table-light"><tr>' +
            '<th>' + escapeHtml(t('tariffsetting.col.base_item')) + '</th>' +
            '<th>' + escapeHtml(t('tariffsetting.col.base_unit')) + '</th>' +
            '<th>' + escapeHtml(t('tariffsetting.col.summer')) + '</th>' +
            '<th>' + escapeHtml(t('tariffsetting.col.nonsummer')) + '</th>' +
            '</tr></thead><tbody>' + rows + '</tbody></table></div>');
    }

    // 單一費率（低壓非時間電價）
    function renderFlatRate(p) {
        return sectionCard('tariffsetting.section.flat',
            '<div class="row g-3">' +
            '<div class="col-auto"><label class="form-label small mb-1">' + escapeHtml(t('tariffsetting.col.summer')) + ' (' + escapeHtml(t('tariffsetting.unit.per_kwh')) + ')</label>' +
            priceInput(p.flatRate.dSummer, 'id="tsFlatSummer"') + '</div>' +
            '<div class="col-auto"><label class="form-label small mb-1">' + escapeHtml(t('tariffsetting.col.nonsummer')) + ' (' + escapeHtml(t('tariffsetting.unit.per_kwh')) + ')</label>' +
            priceInput(p.flatRate.dNonSummer, 'id="tsFlatNonsummer"') + '</div>' +
            '</div>');
    }

    // 流動電費時段（tou）— 依日別分區塊，列 = 季節 × 時段別
    function renderFlowRates(p) {
        var html = '';
        DAY_TYPES.forEach(function (dayType) {
            var idxRows = [];
            p.flowRates.forEach(function (r, i) {
                if (r.szDayType === dayType) idxRows.push({ r: r, i: i });
            });
            if (idxRows.length === 0) return;
            idxRows.sort(function (a, b) {
                var s = SEASONS.indexOf(a.r.szSeason) - SEASONS.indexOf(b.r.szSeason);
                return s !== 0 ? s : (PERIOD_ORDER[a.r.szPeriod] || 9) - (PERIOD_ORDER[b.r.szPeriod] || 9);
            });

            var rows = idxRows.map(function (x) {
                var r = x.r, i = x.i;
                var seasonBadge = r.szSeason === 'summer'
                    ? '<span class="badge bg-warning text-dark">' + escapeHtml(t('tariffsetting.season.summer')) + '</span>'
                    : '<span class="badge bg-info text-dark">' + escapeHtml(t('tariffsetting.season.nonsummer')) + '</span>';
                var defaultName = t('tariff.period.' + r.szPeriod);
                var ranges = r.ranges.map(function (range, j) {
                    var parts = range.split('-');
                    var start = parts[0] || '00:00';
                    var end = parts[1] === '24:00' ? '00:00' : (parts[1] || '00:00');
                    return '<span class="ts-range-group">' +
                        '<input type="text" class="form-control form-control-sm ts-time" autocomplete="off" value="' + escapeHtml(start) + '" data-flow="' + i + '" data-range="' + j + '" data-part="start">' +
                        '<span class="mx-1">-</span>' +
                        '<input type="text" class="form-control form-control-sm ts-time" autocomplete="off" value="' + escapeHtml(end) + '" data-flow="' + i + '" data-range="' + j + '" data-part="end">' +
                        (r.ranges.length > 1
                            ? '<button type="button" class="btn btn-outline-danger btn-sm ts-range-del" title="' + escapeHtml(t('tariffsetting.button.del_range')) + '" onclick="window._ts.removeRange(' + i + ',' + j + ')"><i class="fas fa-times"></i></button>'
                            : '') +
                        '</span>';
                }).join('');
                ranges += '<button type="button" class="btn btn-outline-secondary btn-sm ts-range-add" onclick="window._ts.addRange(' + i + ')">' +
                    '<i class="fas fa-plus me-1"></i>' + escapeHtml(t('tariffsetting.button.add_range')) + '</button>';

                return '<tr>' +
                    '<td class="text-nowrap">' + seasonBadge + '</td>' +
                    '<td><input type="text" class="form-control form-control-sm ts-name" maxlength="20" value="' + escapeHtml(r.szName || '') + '" placeholder="' + escapeHtml(defaultName) + '" data-flow="' + i + '" data-field="name"></td>' +
                    '<td class="ts-ranges-cell">' + ranges + '</td>' +
                    '<td>' + priceInput(r.dPrice, 'data-flow="' + i + '" data-field="price"') + '</td>' +
                    '</tr>';
            }).join('');

            html += '<div class="ts-daytype-block mb-3">' +
                '<div class="ts-daytype-title"><i class="fas fa-calendar-day me-1"></i>' + escapeHtml(t('tariffsetting.daytype.' + dayType)) + '</div>' +
                '<div class="table-responsive"><table class="table table-sm table-bordered align-middle mb-0 ts-table">' +
                '<thead class="table-light"><tr>' +
                '<th class="ts-col-season">' + escapeHtml(t('tariffsetting.col.season')) + '</th>' +
                '<th class="ts-col-name">' + escapeHtml(t('tariffsetting.col.period_name')) + '</th>' +
                '<th>' + escapeHtml(t('tariffsetting.col.time_ranges')) + '</th>' +
                '<th class="ts-col-price">' + escapeHtml(t('tariffsetting.col.price')) + '</th>' +
                '</tr></thead><tbody>' + rows + '</tbody></table></div></div>';
        });

        return sectionCard('tariffsetting.section.flow',
            '<div class="text-muted small mb-2"><i class="fas fa-lightbulb me-1"></i>' + escapeHtml(t('tariffsetting.hint.midnight')) + '</div>' + html);
    }

    // 超額加價（簡易型）
    function renderSurcharge(p) {
        return sectionCard('tariffsetting.section.surcharge',
            '<div class="row g-3 align-items-end">' +
            '<div class="col-auto"><label class="form-label small mb-1">' + escapeHtml(t('tariffsetting.surcharge.over')) + '</label>' +
            '<input type="number" class="form-control form-control-sm ts-price" step="1" min="1" value="' + p.surcharge.nOverKwh + '" id="tsSurOver"></div>' +
            '<div class="col-auto"><label class="form-label small mb-1">' + escapeHtml(t('tariffsetting.surcharge.price')) + '</label>' +
            priceInput(p.surcharge.dPrice, 'id="tsSurPrice"') + '</div>' +
            '</div>');
    }

    function sectionCard(titleKey, bodyHtml) {
        return '<div class="ts-section mb-3">' +
            '<div class="ts-section-title">' + escapeHtml(t(titleKey)) + '</div>' +
            bodyHtml + '</div>';
    }

    // ── DOM → g_plan 回填 ────────────────────────────────

    function collect() {
        var p = g_plan;
        p.szSummerStart = pad2(val('tsSummerStartM')) + '-' + pad2(val('tsSummerStartD'));
        p.szSummerEnd = pad2(val('tsSummerEndM')) + '-' + pad2(val('tsSummerEndD'));

        document.querySelectorAll('input[data-tier]').forEach(function (el) {
            var tier = p.tiers[parseInt(el.getAttribute('data-tier'), 10)];
            var field = el.getAttribute('data-field');
            if (field === 'to') tier.nTo = intOrNull(el.value);
            else if (field === 'summer') tier.dSummer = numOr0(el.value);
            else tier.dNonSummer = numOr0(el.value);
        });
        // 級距下限自動接續上一級上限 +1
        for (var i = 1; i < p.tiers.length; i++) {
            if (p.tiers[i - 1].nTo != null) p.tiers[i].nFrom = p.tiers[i - 1].nTo + 1;
        }

        if (p.flatRate) {
            p.flatRate.dSummer = numOr0(val('tsFlatSummer'));
            p.flatRate.dNonSummer = numOr0(val('tsFlatNonsummer'));
        }

        document.querySelectorAll('input[data-fee]').forEach(function (el) {
            var fee = p.baseFees[parseInt(el.getAttribute('data-fee'), 10)];
            if (el.getAttribute('data-field') === 'summer') fee.dSummer = numOr0(el.value);
            else fee.dNonSummer = numOr0(el.value);
        });

        document.querySelectorAll('input[data-flow]').forEach(function (el) {
            var rate = p.flowRates[parseInt(el.getAttribute('data-flow'), 10)];
            var field = el.getAttribute('data-field');
            if (field === 'name') rate.szName = el.value.trim() || null;
            else if (field === 'price') rate.dPrice = numOr0(el.value);
            else {
                var j = parseInt(el.getAttribute('data-range'), 10);
                var parts = (rate.ranges[j] || '00:00-24:00').split('-');
                var v = el.value.trim() || '00:00';
                if (el.getAttribute('data-part') === 'start') parts[0] = v;
                else parts[1] = (v === '00:00') ? '24:00' : v;   // 訖時 00:00 = 當日結束 24:00
                rate.ranges[j] = parts[0] + '-' + parts[1];
            }
        });

        if (p.surcharge) {
            p.surcharge.nOverKwh = parseInt(val('tsSurOver'), 10) || 0;
            p.surcharge.dPrice = numOr0(val('tsSurPrice'));
        }
        return p;
    }

    function val(id) { var el = document.getElementById(id); return el ? el.value : ''; }
    function pad2(n) { n = parseInt(n, 10) || 0; return n < 10 ? '0' + n : String(n); }
    function intOrNull(v) { var n = parseInt(v, 10); return isNaN(n) ? null : n; }
    function numOr0(v) { var n = parseFloat(v); return isNaN(n) ? 0 : n; }

    // ── 前端驗證（與後端同規則） ──────────────────────────

    function validate(p) {
        if (p.szType === 'progressive') return validateTiers(p.tiers);
        if (p.szType === 'tou') return validateFlow(p.flowRates);
        return null;
    }

    function validateTiers(tiers) {
        for (var i = 0; i < tiers.length; i++) {
            var isLast = i === tiers.length - 1;
            if (!isLast) {
                if (tiers[i].nTo == null || tiers[i].nTo <= tiers[i].nFrom)
                    return t('tariffsetting.err.tier_order');
            }
            if (tiers[i].dSummer < 0 || tiers[i].dNonSummer < 0)
                return t('tariffsetting.err.price_negative');
        }
        return null;
    }

    function validateFlow(flowRates) {
        for (var d = 0; d < DAY_TYPES.length; d++) {
            for (var s = 0; s < SEASONS.length; s++) {
                var group = flowRates.filter(function (r) {
                    return r.szDayType === DAY_TYPES[d] && r.szSeason === SEASONS[s];
                });
                if (group.length === 0) continue;   // seed 結構固定，缺組交後端擋
                var where = t('tariffsetting.daytype.' + DAY_TYPES[d]) + ' × ' + t('tariffsetting.season.' + SEASONS[s]);
                var err = checkCoverage(group, where);
                if (err) return err;
            }
        }
        return null;
    }

    function checkCoverage(group, where) {
        var intervals = [];
        for (var i = 0; i < group.length; i++) {
            for (var j = 0; j < group[i].ranges.length; j++) {
                var parts = group[i].ranges[j].split('-');
                var start = toMin(parts[0]), end = toMin(parts[1]);
                if (start == null || end == null || start === end)
                    return t('tariffsetting.err.range_format', { 0: where });
                if (start < end) intervals.push([start, end]);
                else { intervals.push([start, 1440]); if (end > 0) intervals.push([0, end]); }
            }
        }
        intervals.sort(function (a, b) { return a[0] - b[0]; });
        var cursor = 0;
        for (var k = 0; k < intervals.length; k++) {
            if (intervals[k][0] < cursor) return t('tariffsetting.err.group_overlap', { 0: where, 1: toHHmm(intervals[k][0]) });
            if (intervals[k][0] > cursor) return t('tariffsetting.err.group_gap', { 0: where, 1: toHHmm(cursor) + '~' + toHHmm(intervals[k][0]) });
            cursor = intervals[k][1];
        }
        if (cursor !== 1440) return t('tariffsetting.err.group_gap', { 0: where, 1: toHHmm(cursor) + '~24:00' });
        return null;
    }

    function toMin(s) {
        if (!s) return null;
        var m = /^(\d{1,2}):(\d{2})$/.exec(s.trim());
        if (!m) return null;
        var h = parseInt(m[1], 10), mi = parseInt(m[2], 10);
        if (h === 24 && mi === 0) return 1440;
        if (h > 23 || mi > 59) return null;
        return h * 60 + mi;
    }

    function toHHmm(min) { return pad2(Math.floor(min / 60)) + ':' + pad2(min % 60); }

    // ── 動作 ─────────────────────────────────────────────

    async function savePlan() {
        var p = collect();
        var err = validate(p);
        if (err) { alert(err); return; }

        try {
            var res = await fetch('/TariffSetting/api/plan', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(p)
            });
            if (!res.ok) throw new Error((await res.json().catch(function () { return {}; })).message || res.statusText);
            // 寫回本地整份設定
            var idx = g_config.plans.findIndex(function (x) { return x.szPlanId === p.szPlanId; });
            if (idx >= 0) g_config.plans[idx] = JSON.parse(JSON.stringify(p));
            g_dirty = false;
            alert(t('tariffsetting.msg.saved'));
        } catch (e) {
            alert(t('tariffsetting.msg.save_fail', { 0: e.message }));
        }
    }

    async function setActive() {
        if (!g_plan) return;
        try {
            var res = await fetch('/TariffSetting/api/active', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ planId: g_plan.szPlanId })
            });
            if (!res.ok) throw new Error((await res.json().catch(function () { return {}; })).message || res.statusText);
            g_config.szActivePlanId = g_plan.szPlanId;
            fillPlanSelect(g_plan.szCategory);
            document.getElementById('tsPlan').value = g_plan.szPlanId;
            updateActiveCard();
        } catch (e) {
            alert(t('tariffsetting.msg.active_fail', { 0: e.message }));
        }
    }

    async function resetPlan() {
        if (!g_plan) return;
        if (!confirm(t('tariffsetting.confirm.reset', { 0: t('tariff.plan.' + g_plan.szPlanId) }))) return;
        try {
            var res = await fetch('/TariffSetting/api/reset', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ planId: g_plan.szPlanId })
            });
            if (!res.ok) throw new Error((await res.json().catch(function () { return {}; })).message || res.statusText);
            var restored = await res.json();
            var idx = g_config.plans.findIndex(function (x) { return x.szPlanId === restored.szPlanId; });
            if (idx >= 0) g_config.plans[idx] = restored;
            g_plan = JSON.parse(JSON.stringify(restored));
            g_dirty = false;
            renderPlan();
        } catch (e) {
            alert(t('tariffsetting.msg.reset_fail', { 0: e.message }));
        }
    }

    // 時段區間增刪 — 先 collect 保留使用者已輸入內容再重繪
    function addRange(flowIdx) {
        collect();
        g_plan.flowRates[flowIdx].ranges.push('08:00-12:00');
        g_dirty = true;
        renderPlan();
    }

    function removeRange(flowIdx, rangeIdx) {
        collect();
        g_plan.flowRates[flowIdx].ranges.splice(rangeIdx, 1);
        g_dirty = true;
        renderPlan();
    }

    // ── 工具 ─────────────────────────────────────────────

    function escapeHtml(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    window._ts = {
        savePlan: savePlan,
        setActive: setActive,
        resetPlan: resetPlan,
        addRange: addRange,
        removeRange: removeRange
    };
})();
