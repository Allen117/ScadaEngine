// 水費設定頁邏輯 — 台水流動水費方案（分段累進，含生效日多版本）檢視與編輯。
// 資料模型見 Features/WaterTariffSetting/Models/WaterTariffSettingModels.cs；台水預設 seed 見 Setting/water-tariff-taiwater-defaults.json。
// 整份載入整份儲存：方案版本增刪在前端操作 g_config，「儲存」一次 POST api/config 存回。
// 生效日為 date-only → 用原生 <input type="date">（無 AM/PM 問題，不需 flatpickr）。
(function () {
    'use strict';

    var SEED_PLAN_ID = 'taiwater-flow-default';

    var g_config = null;      // 整份 WaterTariffConfig（工作副本，儲存時整份送回）
    var g_planId = null;      // 目前顯示方案 Id
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
            var res = await fetch('/WaterTariffSetting/api/config');
            if (!res.ok) throw new Error(res.statusText);
            g_config = await res.json();
        } catch (err) {
            console.error('water tariff config load failed', err);
            document.getElementById('wtPlanContainer').innerHTML =
                '<div class="text-center text-danger py-4">' + escapeHtml(t('watertariff.msg.load_fail')) + '</div>';
            return;
        }

        sortPlans();
        if (g_config.plans.length > 0) {
            g_planId = g_config.plans[g_config.plans.length - 1].szPlanId;   // 預設選生效日最新版本
            fillPlanSelect();
            renderPlan();
        }

        document.getElementById('wtPlan').addEventListener('change', onPlanChange);

        // 任一編輯 → 標記未儲存（container 常駐，掛一次即可）
        var container = document.getElementById('wtPlanContainer');
        container.addEventListener('input', function () { g_dirty = true; });
        container.addEventListener('change', function () { g_dirty = true; });
    }

    function sortPlans() {
        g_config.plans.sort(function (a, b) {
            return (a.szEffectiveDate || '').localeCompare(b.szEffectiveDate || '');
        });
    }

    function findPlan(planId) {
        return g_config.plans.find(function (p) { return p.szPlanId === planId; }) || null;
    }

    function planLabel(p) {
        return (p.szName || p.szPlanId) + '（' + t('watertariff.label.effective_from', { 0: p.szEffectiveDate }) + '）';
    }

    // ── 方案選單 ─────────────────────────────────────────

    function fillPlanSelect() {
        var sel = document.getElementById('wtPlan');
        sel.innerHTML = g_config.plans.map(function (p) {
            return '<option value="' + escapeHtml(p.szPlanId) + '"' +
                (p.szPlanId === g_planId ? ' selected' : '') + '>' + escapeHtml(planLabel(p)) + '</option>';
        }).join('');
        // 預設 seed 方案不可刪（GetConfigAsync 載入時會自動補回，刪了也是白刪）
        var btn = document.getElementById('btnWtDelPlan');
        btn.disabled = g_planId === SEED_PLAN_ID;
        btn.title = g_planId === SEED_PLAN_ID ? t('watertariff.msg.seed_undeletable') : '';
    }

    function onPlanChange() {
        collect();   // 切換前保留當前編輯內容到 g_config（整份儲存制，不彈確認）
        g_planId = document.getElementById('wtPlan').value;
        fillPlanSelect();
        renderPlan();
    }

    // ── 渲染 ─────────────────────────────────────────────

    function renderPlan() {
        var p = findPlan(g_planId);
        if (!p) return;
        document.getElementById('wtPlanTitle').textContent = planLabel(p);

        var html =
            '<div class="row g-3 mb-3">' +
            '<div class="col-md-5"><label class="form-label small mb-1 fw-semibold">' + escapeHtml(t('watertariff.label.name')) + '</label>' +
            '<input type="text" id="wtName" class="form-control form-control-sm" maxlength="50" value="' + escapeHtml(p.szName) + '"></div>' +
            '<div class="col-md-3"><label class="form-label small mb-1 fw-semibold">' + escapeHtml(t('watertariff.label.effective_date')) + '</label>' +
            '<input type="date" id="wtEffectiveDate" class="form-control form-control-sm" value="' + escapeHtml(p.szEffectiveDate) + '"></div>' +
            '</div>' +
            renderTiers(p);

        document.getElementById('wtPlanContainer').innerHTML = html;
    }

    function renderTiers(p) {
        var rows = p.tiers.map(function (tier, i) {
            var isLast = i === p.tiers.length - 1;
            var rangeCell;
            if (isLast) {
                rangeCell = '<span class="text-nowrap">' + tier.nFrom + ' ' + escapeHtml(t('watertariff.tier.above')) + '</span>';
            } else {
                rangeCell = '<span class="text-nowrap">' + tier.nFrom + ' ~ ' +
                    '<input type="number" class="form-control form-control-sm wt-tier-to d-inline-block" step="1" min="' + tier.nFrom + '" value="' + tier.nTo + '" data-tier="' + i + '" data-field="to"> ' +
                    escapeHtml(t('watertariff.tier.unit')) + '</span>';
            }
            return '<tr>' +
                '<td>' + rangeCell + '</td>' +
                '<td><input type="number" class="form-control form-control-sm wt-price" step="0.001" min="0" value="' + tier.dPrice + '" data-tier="' + i + '" data-field="price"></td>' +
                '<td class="text-center">' +
                '<button type="button" class="btn btn-outline-danger btn-sm"' + (p.tiers.length <= 1 ? ' disabled' : '') +
                ' title="' + escapeHtml(t('watertariff.button.del_tier')) + '" onclick="window._wt.removeTier(' + i + ')"><i class="fas fa-times"></i></button>' +
                '</td>' +
                '</tr>';
        }).join('');

        return '<div class="wt-section">' +
            '<div class="wt-section-title">' + escapeHtml(t('watertariff.section.tiers')) + '</div>' +
            '<div class="table-responsive"><table class="table table-sm table-bordered align-middle mb-2 wt-table">' +
            '<thead class="table-light"><tr>' +
            '<th>' + escapeHtml(t('watertariff.col.tier_range')) + '</th>' +
            '<th class="wt-col-price">' + escapeHtml(t('watertariff.col.price')) + '</th>' +
            '<th class="wt-col-actions">' + escapeHtml(t('watertariff.col.actions')) + '</th>' +
            '</tr></thead><tbody>' + rows + '</tbody></table></div>' +
            '<button type="button" class="btn btn-outline-secondary btn-sm" onclick="window._wt.addTier()">' +
            '<i class="fas fa-plus me-1"></i>' + escapeHtml(t('watertariff.button.add_tier')) + '</button>' +
            '</div>';
    }

    // ── DOM → g_config 回填 ──────────────────────────────

    function collect() {
        var p = findPlan(g_planId);
        if (!p) return null;
        var nameEl = document.getElementById('wtName');
        if (!nameEl) return p;   // 尚未渲染

        p.szName = nameEl.value.trim();
        p.szEffectiveDate = document.getElementById('wtEffectiveDate').value;

        document.querySelectorAll('input[data-tier]').forEach(function (el) {
            var tier = p.tiers[parseInt(el.getAttribute('data-tier'), 10)];
            if (el.getAttribute('data-field') === 'to') tier.nTo = intOrNull(el.value);
            else tier.dPrice = numOr0(el.value);
        });
        rechain(p.tiers);
        return p;
    }

    // 級距鏈重算：第一級 nFrom=1、後續 nFrom = 上一級 nTo+1、最後一級 nTo=null
    function rechain(tiers) {
        for (var i = 0; i < tiers.length; i++) {
            tiers[i].nFrom = i === 0 ? 1 : (tiers[i - 1].nTo || 0) + 1;
            if (i === tiers.length - 1) tiers[i].nTo = null;
        }
    }

    function intOrNull(v) { var n = parseInt(v, 10); return isNaN(n) ? null : n; }
    function numOr0(v) { var n = parseFloat(v); return isNaN(n) ? 0 : n; }

    // ── 前端驗證（與後端 WaterTariffService.ValidatePlan 同規則） ──

    function validatePlan(p) {
        if (!p.szName) return t('watertariff.err.name_empty');
        if (!/^\d{4}-\d{2}-\d{2}$/.test(p.szEffectiveDate || '')) return t('watertariff.err.date_format');
        if (p.tiers.length === 0) return t('watertariff.err.no_tier');
        for (var i = 0; i < p.tiers.length - 1; i++) {
            if (p.tiers[i].nTo == null || p.tiers[i].nTo < p.tiers[i].nFrom)
                return t('watertariff.err.tier_order');
        }
        for (var j = 0; j < p.tiers.length; j++) {
            if (p.tiers[j].dPrice < 0) return t('watertariff.err.price_negative');
        }
        return null;
    }

    // ── 級距增刪 ─────────────────────────────────────────

    function addTier() {
        var p = collect();
        if (!p) return;
        var last = p.tiers[p.tiers.length - 1];
        if (last) {
            last.nTo = last.nFrom + 9;   // 舊末級補上限（預設 +10 度區間，使用者可再改）
            p.tiers.push({ nFrom: last.nTo + 1, nTo: null, dPrice: last.dPrice });
        } else {
            p.tiers.push({ nFrom: 1, nTo: null, dPrice: 0 });
        }
        g_dirty = true;
        renderPlan();
    }

    function removeTier(idx) {
        var p = collect();
        if (!p || p.tiers.length <= 1) return;
        p.tiers.splice(idx, 1);
        rechain(p.tiers);
        g_dirty = true;
        renderPlan();
    }

    // ── 方案版本增刪 ─────────────────────────────────────

    function todayStr() {
        var d = new Date();
        var mm = String(d.getMonth() + 1).padStart(2, '0');
        var dd = String(d.getDate()).padStart(2, '0');
        return d.getFullYear() + '-' + mm + '-' + dd;
    }

    // 新增方案版本 = 複製目前方案、生效日改今天（使用者再調整），儲存後依期別起日自動選版
    function addVersion() {
        var p = collect();
        if (!p) return;
        var copy = JSON.parse(JSON.stringify(p));
        copy.szPlanId = 'water-' + Date.now();
        copy.szEffectiveDate = todayStr();
        g_config.plans.push(copy);
        sortPlans();
        g_planId = copy.szPlanId;
        g_dirty = true;
        fillPlanSelect();
        renderPlan();
    }

    function deletePlan() {
        var p = findPlan(g_planId);
        if (!p) return;
        if (p.szPlanId === SEED_PLAN_ID) {
            alert(t('watertariff.msg.seed_undeletable'));
            return;
        }
        if (g_config.plans.length <= 1) {
            alert(t('watertariff.msg.last_plan'));
            return;
        }
        if (!confirm(t('watertariff.confirm.del_plan', { 0: planLabel(p) }))) return;
        g_config.plans = g_config.plans.filter(function (x) { return x.szPlanId !== g_planId; });
        g_planId = g_config.plans[g_config.plans.length - 1].szPlanId;
        g_dirty = true;
        fillPlanSelect();
        renderPlan();
    }

    // ── 儲存 / 還原 ──────────────────────────────────────

    async function saveAll() {
        collect();
        for (var i = 0; i < g_config.plans.length; i++) {
            var err = validatePlan(g_config.plans[i]);
            if (err) {
                alert((g_config.plans[i].szName || g_config.plans[i].szPlanId) + '：' + err);
                return;
            }
        }
        try {
            var res = await fetch('/WaterTariffSetting/api/config', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(g_config)
            });
            if (!res.ok) throw new Error((await res.json().catch(function () { return {}; })).message || res.statusText);
            g_dirty = false;
            sortPlans();
            fillPlanSelect();
            renderPlan();
            alert(t('watertariff.msg.saved'));
        } catch (e) {
            alert(t('watertariff.msg.save_fail', { 0: e.message }));
        }
    }

    async function resetSeed() {
        if (!confirm(t('watertariff.confirm.reset'))) return;
        try {
            var res = await fetch('/WaterTariffSetting/api/reset', { method: 'POST' });
            if (!res.ok) throw new Error((await res.json().catch(function () { return {}; })).message || res.statusText);
            g_config = await res.json();
            sortPlans();
            g_planId = g_config.plans.length > 0 ? g_config.plans[g_config.plans.length - 1].szPlanId : null;
            g_dirty = false;
            fillPlanSelect();
            renderPlan();
        } catch (e) {
            alert(t('watertariff.msg.reset_fail', { 0: e.message }));
        }
    }

    // ── 工具 ─────────────────────────────────────────────

    function escapeHtml(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    window._wt = {
        addTier: addTier,
        removeTier: removeTier,
        addVersion: addVersion,
        deletePlan: deletePlan,
        saveAll: saveAll,
        resetSeed: resetSeed
    };
})();
