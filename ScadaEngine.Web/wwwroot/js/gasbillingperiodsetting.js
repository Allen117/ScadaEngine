// 氣費月結週期設定頁邏輯
// 清單只列「實際存在」的期別（兩月一期時只有 6 列）：起訖日期逐期編輯、級聯預覽、
// 空窗/重疊即時警告（不阻擋儲存）、結束 < 起始硬性阻擋。
//
// ⚠️ 與電/水版（billingperiodsetting.js / waterbillingperiodsetting.js）的差異：多「刪除此期 / 復原」。
//    刪除此期 = 該期消失、日數併入前一期（該年第一期則由下一期向前吸收）；
//    已刪除的期別收在下方摺疊區，按「復原」即拆回原狀。日後請勿「照水版對稱修正」而移除這段。
(function () {
    'use strict';

    var MS_DAY = 86400000;
    var g_year = new Date().getFullYear();
    var g_rows = [];      // 該年實際存在的期別（含使用者未儲存的編輯值）
    var g_skipped = [];   // 已刪除的期別（可復原）
    var g_firstGapBase = 0; // 第一列 vs 上一個存在期別的 server gap（上一期可能在去年，僅能以差值修正）
    var g_firstStartOrig = null;

    function t(key, args) {
        return (window.i18n && window.i18n.t) ? window.i18n.t(key, args) : key;
    }

    document.addEventListener('DOMContentLoaded', function () {
        g_year = (window._gbpInit && window._gbpInit.year) || g_year;
        var yearInput = document.getElementById('gbpYear');
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
        document.getElementById('gbpYear').value = y;
        loadList();
    }

    async function loadList() {
        var tbody = document.getElementById('gbpTableBody');
        tbody.innerHTML = '<tr><td colspan="7" class="text-center text-muted py-4">' +
            '<div class="spinner-border spinner-border-sm text-primary me-1"></div>' +
            escapeHtml(t('gasbillingperiod.table.loading')) + '</td></tr>';
        try {
            var res = await fetch('/GasBillingPeriodSetting/api/list?year=' + g_year);
            if (!res.ok) throw new Error((await res.json().catch(function () { return {}; })).message || res.statusText);
            var data = await res.json();
            g_rows = data.periods || [];
            g_skipped = data.skipped || [];
            g_rows.forEach(function (r) { r.dirty = false; });
            g_firstGapBase = g_rows.length ? g_rows[0].gapDays : 0;
            g_firstStartOrig = g_rows.length ? g_rows[0].start : null;
            renderTable();
            renderSkipped();
        } catch (err) {
            tbody.innerHTML = '<tr><td colspan="7" class="text-center text-danger py-4">' +
                escapeHtml(t('gasbillingperiod.msg.load_fail', { 0: err.message })) + '</td></tr>';
        }
    }

    function renderTable() {
        var tbody = document.getElementById('gbpTableBody');
        if (g_rows.length === 0) {
            tbody.innerHTML = '<tr><td colspan="7" class="text-center text-muted py-4">' +
                escapeHtml(t('gasbillingperiod.table.empty')) + '</td></tr>';
            return;
        }
        var canDelete = g_rows.length > 1;   // 該年僅剩一期時不可再刪（後端亦擋）
        tbody.innerHTML = g_rows.map(function (r, i) {
            var szPeriod = r.year + '-' + pad2(r.month);
            var szBadge = r.isCustomized
                ? '<span class="badge bg-primary">' + escapeHtml(t('gasbillingperiod.status.customized')) + '</span>'
                : '<span class="badge bg-secondary">' + escapeHtml(t('gasbillingperiod.status.default')) + '</span>';
            // 非自然月（含兩月一期）額外顯示完整期間標籤，讓「1 月期其實是 1/01~2/28」一眼可辨
            var szLabelHint = r.isNatural ? ''
                : '<div class="small text-muted">' + escapeHtml(r.label) + '</div>';
            return '<tr data-idx="' + i + '">' +
                '<td class="fw-semibold">' + szPeriod + szLabelHint + '</td>' +
                '<td><input type="date" class="form-control form-control-sm gbp-start" value="' + r.start + '"></td>' +
                '<td><input type="date" class="form-control form-control-sm gbp-end" value="' + r.end + '"></td>' +
                '<td class="text-end gbp-days">' + r.days + '</td>' +
                '<td class="gbp-status">' + szBadge + '</td>' +
                '<td class="gbp-warn small"></td>' +
                '<td class="text-nowrap">' +
                    '<button type="button" class="btn btn-sm btn-primary gbp-btn-save" disabled ' +
                        'onclick="window._gbp.save(' + i + ')"><i class="fas fa-save me-1"></i>' +
                        escapeHtml(t('gasbillingperiod.button.save')) + '</button> ' +
                    '<button type="button" class="btn btn-sm btn-outline-secondary gbp-btn-reset"' +
                        (r.isCustomized ? '' : ' style="display:none"') +
                        ' onclick="window._gbp.reset(' + i + ')"><i class="fas fa-undo me-1"></i>' +
                        escapeHtml(t('gasbillingperiod.button.reset')) + '</button> ' +
                    '<button type="button" class="btn btn-sm btn-outline-danger"' + (canDelete ? '' : ' disabled') +
                        ' onclick="window._gbp.skip(' + i + ')"><i class="fas fa-calendar-minus me-1"></i>' +
                        escapeHtml(t('gasbillingperiod.button.skip')) + '</button>' +
                '</td></tr>';
        }).join('');

        tbody.querySelectorAll('tr[data-idx]').forEach(function (tr) {
            var nIdx = parseInt(tr.getAttribute('data-idx'), 10);
            tr.querySelector('.gbp-start').addEventListener('change', function () { onEdit(nIdx); });
            tr.querySelector('.gbp-end').addEventListener('change', function () { onEdit(nIdx); });
        });
        refreshWarnings();
    }

    // 已刪除期別摺疊區（可復原）
    function renderSkipped() {
        var footer = document.getElementById('gbpSkippedFooter');
        var list = document.getElementById('gbpSkippedList');
        var count = document.getElementById('gbpSkippedCount');
        if (!footer || !list) return;
        if (g_skipped.length === 0) {
            footer.style.display = 'none';
            list.innerHTML = '';
            return;
        }
        footer.style.display = '';
        count.textContent = String(g_skipped.length);
        list.innerHTML = g_skipped.map(function (s, i) {
            return '<span class="gbp-skipped-item">' +
                '<span class="gbp-skipped-label">' + escapeHtml(s.year + '-' + pad2(s.month)) + '</span>' +
                '<button type="button" class="btn btn-sm btn-outline-primary py-0" onclick="window._gbp.unskip(' + i + ')">' +
                '<i class="fas fa-rotate-left me-1"></i>' + escapeHtml(t('gasbillingperiod.button.unskip')) + '</button>' +
                '</span>';
        }).join('');
    }

    // 使用者編輯第 nIdx 期 → 標記 dirty + 級聯預覽（未自訂且未編輯的後續「存在」期依序帶入）
    function onEdit(nIdx) {
        var tr = rowEl(nIdx);
        var r = g_rows[nIdx];
        r.start = tr.querySelector('.gbp-start').value || r.start;
        r.end = tr.querySelector('.gbp-end').value || r.end;
        r.dirty = true;
        tr.querySelector('.gbp-btn-save').disabled = false;

        // 級聯預覽：起始 = 前期結束 +1 天、結束 = 起始 +1 個月 −1 天，遇自訂或已編輯期停止
        // （被刪除的期別已不在清單中，其日數由伺服器端吸收 — 這裡的預覽只是近似值，儲存後以伺服器為準重載）
        var dtPrevEnd = parseDate(r.end);
        for (var i = nIdx + 1; i < g_rows.length && dtPrevEnd; i++) {
            var next = g_rows[i];
            if (next.isCustomized || next.dirty) break;
            var dtStart = addDays(dtPrevEnd, 1);
            var dtEnd = addDays(addMonths(dtStart, 1), -1);
            next.start = fmtDate(dtStart);
            next.end = fmtDate(dtEnd);
            var trNext = rowEl(i);
            trNext.querySelector('.gbp-start').value = next.start;
            trNext.querySelector('.gbp-end').value = next.end;
            dtPrevEnd = dtEnd;
        }
        refreshWarnings();
    }

    // 全表重算：天數、結束 < 起始錯誤、與上期空窗/重疊警告
    function refreshWarnings() {
        g_rows.forEach(function (r, i) {
            var tr = rowEl(i);
            if (!tr) return;
            var dtStart = parseDate(r.start);
            var dtEnd = parseDate(r.end);
            var warnEl = tr.querySelector('.gbp-warn');
            var saveBtn = tr.querySelector('.gbp-btn-save');

            if (!dtStart || !dtEnd || dtEnd < dtStart) {
                tr.querySelector('.gbp-days').textContent = '--';
                warnEl.innerHTML = '<span class="text-danger"><i class="fas fa-ban me-1"></i>' +
                    escapeHtml(t('gasbillingperiod.warn.invalid')) + '</span>';
                saveBtn.disabled = true;
                return;
            }
            tr.querySelector('.gbp-days').textContent = String(Math.round((dtEnd - dtStart) / MS_DAY) + 1);
            saveBtn.disabled = !r.dirty;

            // 與上一個存在期別比對：i=0 以 server gap + 起始位移修正（上一期可能在去年，不在本頁）
            var nGap;
            if (i === 0) {
                var nShift = g_firstStartOrig ? Math.round((dtStart - parseDate(g_firstStartOrig)) / MS_DAY) : 0;
                nGap = g_firstGapBase + nShift;
            } else {
                var dtPrevEnd = parseDate(g_rows[i - 1].end);
                nGap = dtPrevEnd ? Math.round((dtStart - addDays(dtPrevEnd, 1)) / MS_DAY) : 0;
            }
            if (nGap > 0) {
                warnEl.innerHTML = '<span class="gbp-warn-gap"><i class="fas fa-exclamation-triangle me-1"></i>' +
                    escapeHtml(t('gasbillingperiod.warn.gap', { 0: nGap })) + '</span>';
            } else if (nGap < 0) {
                warnEl.innerHTML = '<span class="gbp-warn-overlap"><i class="fas fa-exclamation-triangle me-1"></i>' +
                    escapeHtml(t('gasbillingperiod.warn.overlap', { 0: -nGap })) + '</span>';
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
            alert(t('gasbillingperiod.warn.invalid'));
            return;
        }
        try {
            var res = await fetch('/GasBillingPeriodSetting/api/save', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ year: r.year, month: r.month, start: r.start, end: r.end })
            });
            if (!res.ok) throw new Error((await res.json().catch(function () { return {}; })).message || res.statusText);
            await loadList(); // 伺服器為準重載（含級聯推導 + 空窗/重疊 + 吸收）
        } catch (err) {
            alert(t('gasbillingperiod.msg.save_fail', { 0: err.message }));
        }
    }

    async function reset(nIdx) {
        var r = g_rows[nIdx];
        if (!confirm(t('gasbillingperiod.confirm.reset', { 0: r.year + '-' + pad2(r.month) }))) return;
        try {
            var res = await fetch('/GasBillingPeriodSetting/api/reset', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ year: r.year, month: r.month })
            });
            if (!res.ok) throw new Error((await res.json().catch(function () { return {}; })).message || res.statusText);
            await loadList();
        } catch (err) {
            alert(t('gasbillingperiod.msg.reset_fail', { 0: err.message }));
        }
    }

    // 刪除此期 — 確認框明說併入哪一期（前一期；本頁第一列則為下一期向前吸收）
    async function skip(nIdx) {
        var r = g_rows[nIdx];
        var szTarget = r.year + '-' + pad2(r.month);
        var szMsg = (nIdx > 0)
            ? t('gasbillingperiod.confirm.skip_merge_prev', {
                0: szTarget,
                1: g_rows[nIdx - 1].year + '-' + pad2(g_rows[nIdx - 1].month),
                2: r.end
              })
            : t('gasbillingperiod.confirm.skip_merge_next', { 0: szTarget, 1: r.start });
        if (!confirm(szMsg)) return;
        try {
            var res = await fetch('/GasBillingPeriodSetting/api/skip', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ year: r.year, month: r.month })
            });
            if (!res.ok) throw new Error((await res.json().catch(function () { return {}; })).message || res.statusText);
            await loadList();
        } catch (err) {
            alert(t('gasbillingperiod.msg.skip_fail', { 0: err.message }));
        }
    }

    async function unskip(nIdx) {
        var s = g_skipped[nIdx];
        if (!s) return;
        if (!confirm(t('gasbillingperiod.confirm.unskip', { 0: s.year + '-' + pad2(s.month) }))) return;
        try {
            var res = await fetch('/GasBillingPeriodSetting/api/unskip', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ year: s.year, month: s.month })
            });
            if (!res.ok) throw new Error((await res.json().catch(function () { return {}; })).message || res.statusText);
            await loadList();
        } catch (err) {
            alert(t('gasbillingperiod.msg.unskip_fail', { 0: err.message }));
        }
    }

    // ── 工具函式 ─────────────────────────────────────────────
    function rowEl(nIdx) {
        return document.querySelector('#gbpTableBody tr[data-idx="' + nIdx + '"]');
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

    window._gbp = {
        save: save,
        reset: reset,
        skip: skip,
        unskip: unskip,
        prevYear: function () { stepYear(-1); },
        nextYear: function () { stepYear(1); }
    };
})();
