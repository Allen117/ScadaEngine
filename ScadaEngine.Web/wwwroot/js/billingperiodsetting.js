// 月結週期設定頁邏輯
// 一年 12 期列表：起訖日期逐期編輯、級聯預覽（上期結束 +1 天帶入未自訂的下期）、
// 空窗/重疊即時警告（不阻擋儲存）、結束 < 起始硬性阻擋。
(function () {
    'use strict';

    var MS_DAY = 86400000;
    var g_year = new Date().getFullYear();
    var g_rows = [];      // 伺服器清單（含使用者未儲存的編輯值）
    var g_janGapBase = 0; // 1 月期 vs 去年 12 月期的 server gap（去年不在本頁，僅能以差值修正）
    var g_janStartOrig = null;

    function t(key, args) {
        return (window.i18n && window.i18n.t) ? window.i18n.t(key, args) : key;
    }

    document.addEventListener('DOMContentLoaded', function () {
        g_year = (window._bpInit && window._bpInit.year) || g_year;
        var yearInput = document.getElementById('bpYear');
        yearInput.value = g_year;
        yearInput.addEventListener('change', function () {
            var y = parseInt(this.value, 10);
            if (!y || y < 2000 || y > 2100) { this.value = g_year; return; }
            g_year = y;
            loadList();
        });
        if (window.i18n) window.i18n.ready(loadList);
        else loadList();
    });

    function stepYear(nDelta) {
        var y = g_year + nDelta;
        if (y < 2000 || y > 2100) return;
        g_year = y;
        document.getElementById('bpYear').value = y;
        loadList();
    }

    async function loadList() {
        var tbody = document.getElementById('bpTableBody');
        tbody.innerHTML = '<tr><td colspan="7" class="text-center text-muted py-4">' +
            '<div class="spinner-border spinner-border-sm text-primary me-1"></div>' +
            escapeHtml(t('billingperiod.table.loading')) + '</td></tr>';
        try {
            var res = await fetch('/BillingPeriodSetting/api/list?year=' + g_year);
            if (!res.ok) throw new Error((await res.json().catch(function () { return {}; })).message || res.statusText);
            g_rows = await res.json();
            g_rows.forEach(function (r) { r.dirty = false; });
            g_janGapBase = g_rows.length ? g_rows[0].gapDays : 0;
            g_janStartOrig = g_rows.length ? g_rows[0].start : null;
            renderTable();
        } catch (err) {
            tbody.innerHTML = '<tr><td colspan="7" class="text-center text-danger py-4">' +
                escapeHtml(t('billingperiod.msg.load_fail', { 0: err.message })) + '</td></tr>';
        }
    }

    function renderTable() {
        var tbody = document.getElementById('bpTableBody');
        tbody.innerHTML = g_rows.map(function (r, i) {
            var szPeriod = r.year + '-' + pad2(r.month);
            var szBadge = r.isCustomized
                ? '<span class="badge bg-primary">' + escapeHtml(t('billingperiod.status.customized')) + '</span>'
                : '<span class="badge bg-secondary">' + escapeHtml(t('billingperiod.status.default')) + '</span>';
            return '<tr data-idx="' + i + '">' +
                '<td class="fw-semibold">' + szPeriod + '</td>' +
                '<td><input type="date" class="form-control form-control-sm bp-start" value="' + r.start + '"></td>' +
                '<td><input type="date" class="form-control form-control-sm bp-end" value="' + r.end + '"></td>' +
                '<td class="text-end bp-days">' + r.days + '</td>' +
                '<td class="bp-status">' + szBadge + '</td>' +
                '<td class="bp-warn small"></td>' +
                '<td class="text-nowrap">' +
                    '<button type="button" class="btn btn-sm btn-primary bp-btn-save" disabled ' +
                        'onclick="window._bp.save(' + i + ')"><i class="fas fa-save me-1"></i>' +
                        escapeHtml(t('billingperiod.button.save')) + '</button> ' +
                    '<button type="button" class="btn btn-sm btn-outline-secondary bp-btn-reset"' +
                        (r.isCustomized ? '' : ' style="display:none"') +
                        ' onclick="window._bp.reset(' + i + ')"><i class="fas fa-undo me-1"></i>' +
                        escapeHtml(t('billingperiod.button.reset')) + '</button>' +
                '</td></tr>';
        }).join('');

        tbody.querySelectorAll('tr').forEach(function (tr) {
            var nIdx = parseInt(tr.getAttribute('data-idx'), 10);
            tr.querySelector('.bp-start').addEventListener('change', function () { onEdit(nIdx); });
            tr.querySelector('.bp-end').addEventListener('change', function () { onEdit(nIdx); });
        });
        refreshWarnings();
    }

    // 使用者編輯第 nIdx 期 → 標記 dirty + 級聯預覽（未自訂且未編輯的後續期依序帶入）
    function onEdit(nIdx) {
        var tr = rowEl(nIdx);
        var r = g_rows[nIdx];
        r.start = tr.querySelector('.bp-start').value || r.start;
        r.end = tr.querySelector('.bp-end').value || r.end;
        r.dirty = true;
        tr.querySelector('.bp-btn-save').disabled = false;

        // 級聯預覽：起始 = 前期結束 +1 天、結束 = 起始 +1 個月 −1 天，遇自訂或已編輯期停止
        var dtPrevEnd = parseDate(r.end);
        for (var i = nIdx + 1; i < g_rows.length && dtPrevEnd; i++) {
            var next = g_rows[i];
            if (next.isCustomized || next.dirty) break;
            var dtStart = addDays(dtPrevEnd, 1);
            var dtEnd = addDays(addMonths(dtStart, 1), -1);
            next.start = fmtDate(dtStart);
            next.end = fmtDate(dtEnd);
            var trNext = rowEl(i);
            trNext.querySelector('.bp-start').value = next.start;
            trNext.querySelector('.bp-end').value = next.end;
            dtPrevEnd = dtEnd;
        }
        refreshWarnings();
    }

    // 全表重算：天數、結束 < 起始錯誤、與上期空窗/重疊警告
    function refreshWarnings() {
        g_rows.forEach(function (r, i) {
            var tr = rowEl(i);
            var dtStart = parseDate(r.start);
            var dtEnd = parseDate(r.end);
            var warnEl = tr.querySelector('.bp-warn');
            var saveBtn = tr.querySelector('.bp-btn-save');

            if (!dtStart || !dtEnd || dtEnd < dtStart) {
                tr.querySelector('.bp-days').textContent = '--';
                warnEl.innerHTML = '<span class="text-danger"><i class="fas fa-ban me-1"></i>' +
                    escapeHtml(t('billingperiod.warn.invalid')) + '</span>';
                saveBtn.disabled = true;
                return;
            }
            tr.querySelector('.bp-days').textContent = String(Math.round((dtEnd - dtStart) / MS_DAY) + 1);
            saveBtn.disabled = !r.dirty;

            // 與上期比對：i=0 以 server gap + 起始位移修正（去年 12 月期不在本頁）
            var nGap;
            if (i === 0) {
                var nShift = g_janStartOrig ? Math.round((dtStart - parseDate(g_janStartOrig)) / MS_DAY) : 0;
                nGap = g_janGapBase + nShift;
            } else {
                var dtPrevEnd = parseDate(g_rows[i - 1].end);
                nGap = dtPrevEnd ? Math.round((dtStart - addDays(dtPrevEnd, 1)) / MS_DAY) : 0;
            }
            if (nGap > 0) {
                warnEl.innerHTML = '<span class="bp-warn-gap"><i class="fas fa-exclamation-triangle me-1"></i>' +
                    escapeHtml(t('billingperiod.warn.gap', { 0: nGap })) + '</span>';
            } else if (nGap < 0) {
                warnEl.innerHTML = '<span class="bp-warn-overlap"><i class="fas fa-exclamation-triangle me-1"></i>' +
                    escapeHtml(t('billingperiod.warn.overlap', { 0: -nGap })) + '</span>';
            } else {
                warnEl.innerHTML = '';
            }
        });
    }

    async function save(nIdx) {
        var r = g_rows[nIdx];
        var dtStart = parseDate(r.start);
        var dtEnd = parseDate(r.end);
        if (!dtStart || !dtEnd || dtEnd < dtStart) {
            alert(t('billingperiod.warn.invalid'));
            return;
        }
        try {
            var res = await fetch('/BillingPeriodSetting/api/save', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ year: r.year, month: r.month, start: r.start, end: r.end })
            });
            if (!res.ok) throw new Error((await res.json().catch(function () { return {}; })).message || res.statusText);
            await loadList(); // 伺服器為準重載（含級聯推導 + 空窗/重疊）
        } catch (err) {
            alert(t('billingperiod.msg.save_fail', { 0: err.message }));
        }
    }

    async function reset(nIdx) {
        var r = g_rows[nIdx];
        if (!confirm(t('billingperiod.confirm.reset', { 0: r.year + '-' + pad2(r.month) }))) return;
        try {
            var res = await fetch('/BillingPeriodSetting/api/reset', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ year: r.year, month: r.month })
            });
            if (!res.ok) throw new Error((await res.json().catch(function () { return {}; })).message || res.statusText);
            await loadList();
        } catch (err) {
            alert(t('billingperiod.msg.reset_fail', { 0: err.message }));
        }
    }

    // ── 工具函式 ─────────────────────────────────────────────
    function rowEl(nIdx) {
        return document.querySelector('#bpTableBody tr[data-idx="' + nIdx + '"]');
    }

    function pad2(n) { return n < 10 ? '0' + n : String(n); }

    function parseDate(s) {
        if (!s) return null;
        var p = s.split('-');
        if (p.length !== 3) return null;
        return new Date(parseInt(p[0], 10), parseInt(p[1], 10) - 1, parseInt(p[2], 10));
    }

    function fmtDate(d) {
        return d.getFullYear() + '-' + pad2(d.getMonth() + 1) + '-' + pad2(d.getDate());
    }

    function addDays(d, n) { return new Date(d.getFullYear(), d.getMonth(), d.getDate() + n); }

    // 對齊後端 DateTime.AddMonths：1/31 +1 月 → 2/28（月底 clamp）
    function addMonths(d, n) {
        var y = d.getFullYear();
        var m = d.getMonth() + n;
        var lastDay = new Date(y, m + 1, 0).getDate();
        return new Date(y, m, Math.min(d.getDate(), lastDay));
    }

    function escapeHtml(s) {
        if (s == null) return '';
        return String(s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;')
            .replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#039;');
    }

    window._bp = {
        save: save,
        reset: reset,
        prevYear: function () { stepYear(-1); },
        nextYear: function () { stepYear(1); }
    };
})();
