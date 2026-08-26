// ============================================================
// scadapage/state.js — 共享可變狀態 + 跨模組 helper（i18n t / 權限 / 頁面樹狀態 / 摺疊 / 警報規則 / 手動值 / 排程快取）
// ============================================================
// 純搬移自 scadapage.js（plan 2026-08-06-scadapage-js-split）。
// 不包 IIFE、global scope，與 js/designer/ 拆分同模式；
// 共享狀態集中於 state.js。原始 4-space 縮排刻意保留，
// 以保證 template literal 內容 byte-level 不變（純搬移原則）。
// 本檔必須最先載入。
// ============================================================

    function t(key, args) { return (window.i18n && window.i18n.t) ? window.i18n.t(key, args) : key; }

    // ── 權限檢查（由 cshtml inline script 設定全域變數 _isAdmin, _scadaPagePerms）──
    function _canViewPage(szPageSid) {
        if (window._isAdmin) return true;
        var p = window._scadaPagePerms[szPageSid];
        return p && p.canView;
    }
    function _canControlPage(szPageSid) {
        if (window._isAdmin) return true;
        var p = window._scadaPagePerms[szPageSid];
        return p && p.canControl;
    }

    var scadaPageTree  = [];
    var scadaCurrentId = null;
    var lastData       = [];

    // ── 頁面樹摺疊狀態（持久化於 localStorage）──
    var COLLAPSED_KEY = 'scadaPage_collapsed_v1';
    var collapsedSet = (function () {
        try {
            var raw = localStorage.getItem(COLLAPSED_KEY);
            return new Set(raw ? JSON.parse(raw) : []);
        } catch (_) { return new Set(); }
    })();
    function _saveCollapsedSet() {
        try { localStorage.setItem(COLLAPSED_KEY, JSON.stringify(Array.from(collapsedSet))); } catch (_) {}
    }
    function _toggleCollapsed(szId) {
        if (collapsedSet.has(szId)) collapsedSet.delete(szId);
        else collapsedSet.add(szId);
        _saveCollapsedSet();
        renderScadaPageTree();
    }

    // ── 警報規則快取 ──
    var _alarmRuleMap = {};
    async function _loadAlarmRules() {
        try {
            var resp = await fetch('/api/alarm-rules');
            if (resp.ok) {
                var arr = await resp.json();
                _alarmRuleMap = {};
                arr.filter(function (r) { return r.isEnabled; }).forEach(function (r) { _alarmRuleMap[r.szSID] = r; });
            }
        } catch (_) { /* ignore */ }
    }

    // ── 手動控制值快取 ──
    var _aoManualValueMap = {};
    async function _loadManualControlValues() {
        try {
            var resp = await fetch('/api/control/manual-values');
            if (resp.ok) _aoManualValueMap = await resp.json();
        } catch (_) { /* ignore */ }
    }

    // ── 排程快取（DI 點位綁定排程用，lazy 載入）──
    // 切到包含排程型 DI 的頁面時 fetch 一次，後續即時更新迴圈直接讀此快取
    var _scheduleCache = null;
    var _scheduleCacheLoading = null;
    async function _ensureScheduleCache() {
        if (_scheduleCache) return _scheduleCache;
        if (_scheduleCacheLoading) return _scheduleCacheLoading;
        _scheduleCacheLoading = (async function () {
            try {
                var resp = await fetch('/api/schedules');
                if (resp.ok) {
                    _scheduleCache = await resp.json();
                } else {
                    _scheduleCache = [];
                }
            } catch (_) {
                _scheduleCache = [];
            } finally {
                _scheduleCacheLoading = null;
            }
            return _scheduleCache;
        })();
        return _scheduleCacheLoading;
    }
    function _pageHasScheduleDi(page) {
        if (!page || !page.arrWidgetState) return false;
        return page.arrWidgetState.some(function (ws) {
            return ws.szType === 'diPoint' && ws.props && ws.props.nScheduleId != null;
        });
    }
