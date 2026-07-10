// 國定假日設定頁 — 12 個月年曆點選標註/取消，整年批次覆蓋儲存。
// 標註日在 TOU 計價時以 sun_offday（週日及離峰日）費率落段；
// 儲存成功後提示「歷史區間如需回溯請至電費設定執行重新計算」。
(function () {
    'use strict';

    var g_year = new Date().getFullYear();
    var g_marked = {};          // 'yyyy-MM-dd' → true（目前年度工作副本）
    var g_dirty = false;

    document.addEventListener('DOMContentLoaded', function () {
        if (window.i18n) window.i18n.ready(init);
        else init();
    });

    function t(key, args) {
        return (window.i18n && window.i18n.t) ? window.i18n.t(key, args) : key;
    }

    function init() {
        loadYear(g_year);
        window.addEventListener('beforeunload', function (e) {
            if (g_dirty) { e.preventDefault(); e.returnValue = ''; }
        });
    }

    // ── 載入 ─────────────────────────────────────────────

    async function loadYear(year) {
        try {
            var res = await fetch('/HolidaySetting/api/holidays?year=' + year);
            if (!res.ok) throw new Error(res.statusText);
            var data = await res.json();
            g_year = year;
            g_marked = {};
            (data.dates || []).forEach(function (d) { g_marked[d] = true; });
            g_dirty = false;
            render();
        } catch (err) {
            console.error('holidays load failed', err);
            document.getElementById('hsCalendarGrid').innerHTML =
                '<div class="text-center text-danger py-4 w-100">' + escapeHtml(t('holidaysetting.msg.load_fail')) + '</div>';
        }
    }

    function confirmDiscardDirty() {
        if (!g_dirty) return true;
        return confirm(t('holidaysetting.confirm.switch_dirty'));
    }

    function prevYear() {
        if (!confirmDiscardDirty()) return;
        loadYear(g_year - 1);
    }

    function nextYear() {
        if (!confirmDiscardDirty()) return;
        loadYear(g_year + 1);
    }

    // ── 渲染 ─────────────────────────────────────────────

    function pad2(n) { return n < 10 ? '0' + n : String(n); }

    function dateKey(y, m, d) { return y + '-' + pad2(m) + '-' + pad2(d); }

    function todayKey() {
        var now = new Date();
        return dateKey(now.getFullYear(), now.getMonth() + 1, now.getDate());
    }

    function render() {
        document.getElementById('hsYearLabel').textContent = g_year;

        var weekdayNames = [
            t('holidaysetting.week.sun'), t('holidaysetting.week.mon'), t('holidaysetting.week.tue'),
            t('holidaysetting.week.wed'), t('holidaysetting.week.thu'), t('holidaysetting.week.fri'),
            t('holidaysetting.week.sat')
        ];
        var szToday = todayKey();
        var html = '';

        for (var m = 1; m <= 12; m++) {
            var firstDow = new Date(g_year, m - 1, 1).getDay();        // 0=日
            var daysInMonth = new Date(g_year, m, 0).getDate();

            var head = weekdayNames.map(function (w, i) {
                return '<div class="hs-dow' + (i === 0 || i === 6 ? ' hs-dow-weekend' : '') + '">' + escapeHtml(w) + '</div>';
            }).join('');

            var cells = '';
            for (var b = 0; b < firstDow; b++) cells += '<div class="hs-day hs-day-blank"></div>';
            for (var d = 1; d <= daysInMonth; d++) {
                var key = dateKey(g_year, m, d);
                var dow = (firstDow + d - 1) % 7;
                var cls = 'hs-day';
                if (dow === 0 || dow === 6) cls += ' hs-day-weekend';
                if (g_marked[key]) cls += ' hs-day-holiday';
                if (key === szToday) cls += ' hs-day-today';
                cells += '<div class="' + cls + '" data-date="' + key + '">' + d + '</div>';
            }

            html += '<div class="hs-month card shadow-sm">' +
                '<div class="hs-month-title">' + escapeHtml(t('holidaysetting.month.' + m)) + '</div>' +
                '<div class="hs-month-grid">' + head + cells + '</div>' +
                '</div>';
        }

        var grid = document.getElementById('hsCalendarGrid');
        grid.innerHTML = html;
        grid.querySelectorAll('.hs-day[data-date]').forEach(function (el) {
            el.addEventListener('click', function () { toggle(el.getAttribute('data-date'), el); });
        });

        updateCountLabel();
        updateDirtyHint();
    }

    function toggle(key, el) {
        if (g_marked[key]) delete g_marked[key];
        else g_marked[key] = true;
        el.classList.toggle('hs-day-holiday', !!g_marked[key]);
        g_dirty = true;
        updateCountLabel();
        updateDirtyHint();
    }

    function updateCountLabel() {
        document.getElementById('hsCountLabel').textContent =
            t('holidaysetting.label.count', { 0: Object.keys(g_marked).length });
    }

    function updateDirtyHint() {
        document.getElementById('hsDirtyHint').classList.toggle('d-none', !g_dirty);
    }

    // ── 儲存 ─────────────────────────────────────────────

    async function save() {
        var btn = document.getElementById('btnHsSave');
        btn.disabled = true;
        try {
            var res = await fetch('/HolidaySetting/api/holidays', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ year: g_year, dates: Object.keys(g_marked).sort() })
            });
            if (!res.ok) throw new Error((await res.json().catch(function () { return {}; })).message || res.statusText);
            g_dirty = false;
            updateDirtyHint();
            alert(t('holidaysetting.msg.saved'));
        } catch (e) {
            alert(t('holidaysetting.msg.save_fail', { 0: e.message }));
        } finally {
            btn.disabled = false;
        }
    }

    // ── 工具 ─────────────────────────────────────────────

    function escapeHtml(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    window._hs = {
        prevYear: prevYear,
        nextYear: nextYear,
        save: save
    };
})();
