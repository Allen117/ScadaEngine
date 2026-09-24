// 班別設定頁邏輯 — 整份載入 / 整份儲存（SystemSettings.shift_schedule）
(function () {
    'use strict';

    // 顯示順序：週一 ~ 週六、週日（值對齊 .NET DayOfWeek：0=週日 .. 6=週六）
    const WEEKDAY_ORDER = [1, 2, 3, 4, 5, 6, 0];
    const WEEKDAY_KEYS = {
        0: 'shiftsetting.weekday.sun', 1: 'shiftsetting.weekday.mon', 2: 'shiftsetting.weekday.tue',
        3: 'shiftsetting.weekday.wed', 4: 'shiftsetting.weekday.thu', 5: 'shiftsetting.weekday.fri',
        6: 'shiftsetting.weekday.sat'
    };

    let g_shifts = [];

    document.addEventListener('DOMContentLoaded', () => {
        if (window.i18n) {
            window.i18n.ready(load);
        } else {
            load();
        }
    });

    function t(key, args) {
        return (window.i18n && window.i18n.t) ? window.i18n.t(key, args) : key;
    }

    async function load() {
        try {
            const res = await fetch('/ShiftSetting/api/config');
            if (!res.ok) throw new Error(res.statusText);
            const cfg = await res.json();
            g_shifts = (cfg.shifts || []).map(s => ({
                szName: s.szName || '',
                nStartHour: s.nStartHour | 0,
                nEndHour: s.nEndHour | 0,
                weekdays: Array.isArray(s.weekdays) ? s.weekdays.slice() : []
            }));
            render();
        } catch (err) {
            showAlert('danger', t('shiftsetting.msg.load_failed', { 0: err.message }));
            document.getElementById('ssBody').innerHTML =
                `<tr><td colspan="6" class="text-center text-danger py-4">${escapeHtml(t('shiftsetting.msg.load_failed', { 0: err.message }))}</td></tr>`;
        }
    }

    function hourOptions(nSelected) {
        return Array.from({ length: 24 }, (_, i) =>
            `<option value="${i}"${i === nSelected ? ' selected' : ''}>${String(i).padStart(2, '0')}:00</option>`
        ).join('');
    }

    // 時數 + 跨日徽章：訖 <= 起視為跨日（訖 00:00 = 當日結束，不算跨日）
    function durationHtml(s) {
        const dur = ((s.nEndHour - s.nStartHour) + 24) % 24;
        if (dur === 0) return `<span class="text-danger">--</span>`;
        const crossDay = s.nStartHour + dur > 24;
        const badge = crossDay ? ` <span class="badge bg-warning text-dark">${escapeHtml(t('shiftsetting.badge.crossday'))}</span>` : '';
        return escapeHtml(t('shiftsetting.duration.hours', { 0: dur })) + badge;
    }

    function render() {
        const tbody = document.getElementById('ssBody');
        if (g_shifts.length === 0) {
            tbody.innerHTML = `<tr><td colspan="6" class="text-center text-muted py-4">${escapeHtml(t('shiftsetting.empty'))}</td></tr>`;
            return;
        }
        tbody.innerHTML = g_shifts.map((s, i) => {
            const weekdayChecks = WEEKDAY_ORDER.map(w => `
                <label class="ss-weekday form-check-label">
                    <input type="checkbox" class="form-check-input" ${s.weekdays.includes(w) ? 'checked' : ''}
                           onchange="window._ss.toggleWeekday(${i}, ${w}, this.checked)">
                    <span>${escapeHtml(t(WEEKDAY_KEYS[w]))}</span>
                </label>`).join('');
            return `
            <tr>
                <td><input type="text" class="form-control form-control-sm" value="${escapeHtml(s.szName)}"
                           maxlength="20" placeholder="${escapeHtml(t('shiftsetting.placeholder.name'))}"
                           onchange="window._ss.setName(${i}, this.value)"></td>
                <td><select class="form-select form-select-sm" onchange="window._ss.setStart(${i}, this.value)">${hourOptions(s.nStartHour)}</select></td>
                <td><select class="form-select form-select-sm" onchange="window._ss.setEnd(${i}, this.value)">${hourOptions(s.nEndHour)}</select></td>
                <td class="text-nowrap" id="ssDur${i}">${durationHtml(s)}</td>
                <td><div class="ss-weekday-group">${weekdayChecks}</div></td>
                <td class="text-center">
                    <button class="btn btn-outline-danger btn-sm" title="${escapeHtml(t('shiftsetting.button.delete'))}"
                            onclick="window._ss.remove(${i})"><i class="fas fa-trash"></i></button>
                </td>
            </tr>`;
        }).join('');
    }

    function setName(i, v) { g_shifts[i].szName = v; }
    function setStart(i, v) {
        g_shifts[i].nStartHour = parseInt(v, 10);
        document.getElementById('ssDur' + i).innerHTML = durationHtml(g_shifts[i]);
    }
    function setEnd(i, v) {
        g_shifts[i].nEndHour = parseInt(v, 10);
        document.getElementById('ssDur' + i).innerHTML = durationHtml(g_shifts[i]);
    }
    function toggleWeekday(i, w, checked) {
        const arr = g_shifts[i].weekdays;
        const idx = arr.indexOf(w);
        if (checked && idx < 0) arr.push(w);
        if (!checked && idx >= 0) arr.splice(idx, 1);
    }

    function add() {
        // 預設：08:00~16:00、週一至週五
        g_shifts.push({ szName: '', nStartHour: 8, nEndHour: 16, weekdays: [1, 2, 3, 4, 5] });
        render();
    }

    function remove(i) {
        g_shifts.splice(i, 1);
        render();
    }

    async function save() {
        const btn = document.getElementById('ssBtnSave');
        btn.disabled = true;
        try {
            const res = await fetch('/ShiftSetting/api/config', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ shifts: g_shifts })
            });
            if (!res.ok) {
                const err = await res.json().catch(() => ({}));
                throw new Error(err.message || res.statusText);
            }
            showAlert('success', t('shiftsetting.msg.saved'));
        } catch (err) {
            showAlert('danger', t('shiftsetting.msg.save_failed', { 0: err.message }));
        } finally {
            btn.disabled = false;
        }
    }

    function showAlert(type, msg) {
        const el = document.getElementById('ssAlert');
        el.className = `alert alert-${type} py-2 mb-2`;
        el.textContent = msg;
        if (type === 'success') {
            setTimeout(() => el.classList.add('d-none'), 3000);
        }
    }

    function escapeHtml(s) {
        if (s == null) return '';
        return String(s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;')
            .replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#039;');
    }

    window._ss = { add, remove, save, setName, setStart, setEnd, toggleWeekday };
})();
