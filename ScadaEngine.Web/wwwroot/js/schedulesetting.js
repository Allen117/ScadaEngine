(function () {
    'use strict';

    var DAY_NAMES = ['', '\u4e00', '\u4e8c', '\u4e09', '\u56db', '\u4e94', '\u516d', '\u65e5'];
    var TYPE_LABELS = ['\u6bcf\u9031', 'N\u9031\u5faa\u74b0', '\u6bcf\u6708', 'N\u6708\u5faa\u74b0'];
    var schedules = [];
    var modal = null;

    // ── \u521d\u59cb\u5316 ──

    function init() {
        var data = window._scheduleInitData || {};
        schedules = data.schedules || [];
        modal = new bootstrap.Modal(document.getElementById('scheduleModal'));
        buildDomCheckboxes();
        renderTable();
    }

    function buildDomCheckboxes() {
        var container = document.getElementById('domCheckboxes');
        var html = '';
        for (var d = 1; d <= 31; d++) {
            html += '<label class="dom-check"><input type="checkbox" value="' + d + '" /><span>' + d + '</span></label>';
        }
        container.innerHTML = html;
    }

    // ══════════════════════════════════════════════════════════════════
    //  \u8868\u683c\u6e32\u67d3
    // ══════════════════════════════════════════════════════════════════

    function renderTable() {
        var tbody = document.getElementById('scheduleBody');
        var emptyMsg = document.getElementById('emptyMsg');

        if (schedules.length === 0) {
            tbody.innerHTML = '';
            emptyMsg.classList.remove('d-none');
            return;
        }
        emptyMsg.classList.add('d-none');

        var html = '';
        for (var i = 0; i < schedules.length; i++) {
            html += buildRow(schedules[i]);
        }
        tbody.innerHTML = html;
    }

    function buildRow(s) {
        var enabledHtml = s.isEnabled
            ? '<span class="badge bg-success">\u555f\u7528</span>'
            : '<span class="badge bg-secondary">\u505c\u7528</span>';

        var typeHtml = '<span class="type-badge type-' + s.recurrenceType + '">' + TYPE_LABELS[s.recurrenceType] + '</span>';

        // \u8ddf\u4f11\u53c3\u6578\u8aaa\u660e
        if (s.recurrenceType === 1 || s.recurrenceType === 3) {
            var unit = s.recurrenceType === 1 ? '\u9031' : '\u6708';
            typeHtml += '<div class="cycle-info">\u8ddf' + s.runLength + unit
                + ' \u4f11' + s.restLength + unit + '</div>';
        }

        var daysHtml = buildDateDisplay(s);
        var timeHtml = '<span class="time-display">' + escHtml(s.startTime) + ' - ' + escHtml(s.endTime) + '</span>';
        var statusHtml = getScheduleStatus(s);

        return '<tr>'
            + '<td class="text-center">' + enabledHtml + '</td>'
            + '<td>' + escHtml(s.name) + '</td>'
            + '<td>' + typeHtml + '</td>'
            + '<td>' + daysHtml + '</td>'
            + '<td>' + timeHtml + '</td>'
            + '<td>' + statusHtml + '</td>'
            + '<td>' + escHtml(s.remarks || '') + '</td>'
            + '<td>'
            + '  <button class="btn btn-sm btn-outline-primary me-1" onclick="window._schedule.edit(' + s.id + ')">'
            + '    <i class="fas fa-edit me-1"></i>\u7de8\u8f2f</button>'
            + '  <button class="btn btn-sm btn-outline-danger" onclick="window._schedule.remove(' + s.id + ')">'
            + '    <i class="fas fa-trash-alt me-1"></i>\u522a\u9664</button>'
            + '</td></tr>';
    }

    function buildDateDisplay(s) {
        if (s.recurrenceType === 0 || s.recurrenceType === 1) {
            return buildDayBadges(s.daysOfWeek);
        }
        // \u6bcf\u6708 / N\u6708
        var nums = s.daysOfMonth ? s.daysOfMonth.split(',') : [];
        var html = '';
        for (var i = 0; i < nums.length; i++) {
            html += '<span class="dom-badge">' + nums[i].trim() + '\u865f</span>';
        }
        return html;
    }

    function buildDayBadges(daysStr) {
        var all = [1, 2, 3, 4, 5, 6, 7];
        var selected = daysStr ? daysStr.split(',').map(Number) : [];
        var html = '';
        for (var i = 0; i < all.length; i++) {
            var d = all[i];
            var isActive = selected.indexOf(d) >= 0;
            html += '<span class="day-badge' + (isActive ? '' : ' day-off') + '">' + DAY_NAMES[d] + '</span>';
        }
        return html;
    }

    // ══════════════════════════════════════════════════════════════════
    //  \u5373\u6642\u72c0\u614b\u8a08\u7b97\uff08\u56db\u7a2e\u6a21\u5f0f\uff09
    // ══════════════════════════════════════════════════════════════════

    function getScheduleStatus(s) {
        if (!s.isEnabled) {
            return '<span class="status-inactive"><i class="fas fa-pause-circle me-1"></i>\u505c\u7528</span>';
        }

        var now = new Date();
        var isDateMatch = false;

        switch (s.recurrenceType) {
            case 0: // \u6bcf\u9031
                isDateMatch = checkDaysOfWeek(now, s.daysOfWeek);
                break;
            case 1: // N\u9031\u5faa\u74b0
                isDateMatch = checkWeekCycle(now, s) && checkDaysOfWeek(now, s.daysOfWeek);
                break;
            case 2: // \u6bcf\u6708
                isDateMatch = checkDaysOfMonth(now, s.daysOfMonth);
                break;
            case 3: // N\u6708\u5faa\u74b0
                isDateMatch = checkMonthCycle(now, s) && checkDaysOfMonth(now, s.daysOfMonth);
                break;
        }

        if (!isDateMatch) {
            return '<span class="status-inactive"><i class="fas fa-moon me-1"></i>\u975e\u6392\u7a0b\u65e5</span>';
        }

        if (checkTimeWindow(now, s.startTime, s.endTime)) {
            return '<span class="status-active"><i class="fas fa-check-circle me-1"></i>\u5c0e\u901a\u4e2d</span>';
        }
        return '<span class="status-inactive"><i class="fas fa-clock me-1"></i>\u7b49\u5f85\u4e2d</span>';
    }

    function checkDaysOfWeek(now, daysStr) {
        if (!daysStr) return false;
        var dotw = now.getDay(); // 0=Sun
        if (dotw === 0) dotw = 7;
        return daysStr.split(',').map(Number).indexOf(dotw) >= 0;
    }

    function checkDaysOfMonth(now, domStr) {
        if (!domStr) return false;
        return domStr.split(',').map(Number).indexOf(now.getDate()) >= 0;
    }

    function checkWeekCycle(now, s) {
        if (!s.anchorDateTime || !s.runLength || !s.restLength) return false;
        var anchor = new Date(s.anchorDateTime);
        var elapsedMs = now.getTime() - anchor.getTime();
        if (elapsedMs < 0) return false;
        var totalCycleWeeks = s.runLength + s.restLength;
        var elapsedWeeks = Math.floor(elapsedMs / (7 * 24 * 60 * 60000));
        var posInCycle = elapsedWeeks % totalCycleWeeks;
        return posInCycle < s.runLength; // \u524d runLength \u9031\u70ba\u8ddf\uff0c\u5f8c restLength \u9031\u70ba\u4f11
    }

    function checkMonthCycle(now, s) {
        if (!s.anchorDateTime || !s.runLength || !s.restLength) return false;
        var anchor = new Date(s.anchorDateTime);
        var totalMonths = (now.getFullYear() - anchor.getFullYear()) * 12 + (now.getMonth() - anchor.getMonth());
        if (totalMonths < 0) return false;
        var totalCycleMonths = s.runLength + s.restLength;
        var posInCycle = totalMonths % totalCycleMonths;
        return posInCycle < s.runLength; // \u524d runLength \u6708\u70ba\u8ddf\uff0c\u5f8c restLength \u6708\u70ba\u4f11
    }

    function checkTimeWindow(now, startStr, endStr) {
        var nowMin = now.getHours() * 60 + now.getMinutes();
        var sp = startStr.split(':'), ep = endStr.split(':');
        var startMin = parseInt(sp[0]) * 60 + parseInt(sp[1]);
        var endMin = parseInt(ep[0]) * 60 + parseInt(ep[1]);
        if (endMin <= startMin) {
            // \u8de8\u65e5\uff1a\u4f8b\u5982 22:00~06:00 \u2192 (>=22:00 OR <06:00)
            return nowMin >= startMin || nowMin < endMin;
        }
        return nowMin >= startMin && nowMin < endMin;
    }

    // ══════════════════════════════════════════════════════════════════
    //  Modal \u64cd\u4f5c
    // ══════════════════════════════════════════════════════════════════

    function onTypeChange() {
        var type = parseInt(document.getElementById('selRecurrenceType').value);
        var isCycle = (type === 1 || type === 3);
        var isWeekBased = (type === 0 || type === 1);
        var isMonthBased = (type === 2 || type === 3);

        toggle('cycleSection', isCycle);
        toggle('daysOfWeekSection', isWeekBased);
        toggle('daysOfMonthSection', isMonthBased);

        var unitText = (type === 1) ? '\u9031' : '\u6708';
        document.getElementById('runUnit').textContent = unitText;
        document.getElementById('restUnit').textContent = unitText;
    }

    function toggle(id, show) {
        var el = document.getElementById(id);
        if (show) el.classList.remove('d-none');
        else el.classList.add('d-none');
    }

    function showAddModal() {
        document.getElementById('editId').value = '';
        document.getElementById('modalTitle').textContent = '\u65b0\u589e\u6392\u7a0b';
        document.getElementById('txtName').value = '';
        document.getElementById('selRecurrenceType').value = '0';
        document.getElementById('txtRunLength').value = '1';
        document.getElementById('txtRestLength').value = '1';
        document.getElementById('txtAnchorDateTime').value = nowLocalIso();
        clearDays();
        clearDom();
        document.getElementById('txtStartTime').value = '08:00';
        document.getElementById('txtEndTime').value = '17:00';
        document.getElementById('txtRemarks').value = '';
        document.getElementById('chkEnabled').checked = true;
        onTypeChange();
        modal.show();
    }

    function edit(id) {
        var s = schedules.find(function (x) { return x.id === id; });
        if (!s) return;

        document.getElementById('editId').value = s.id;
        document.getElementById('modalTitle').textContent = '\u7de8\u8f2f\u6392\u7a0b';
        document.getElementById('txtName').value = s.name;
        document.getElementById('selRecurrenceType').value = String(s.recurrenceType);

        // \u8ddf\u4f11\u53c3\u6578
        document.getElementById('txtRunLength').value = s.runLength || 1;
        document.getElementById('txtRestLength').value = s.restLength || 1;
        document.getElementById('txtAnchorDateTime').value = s.anchorDateTime || '';

        // \u661f\u671f
        clearDays();
        if (s.daysOfWeek) {
            var selDow = s.daysOfWeek.split(',').map(Number);
            var checks = document.querySelectorAll('#dayCheckboxes input[type="checkbox"]');
            for (var i = 0; i < checks.length; i++) {
                checks[i].checked = selDow.indexOf(parseInt(checks[i].value)) >= 0;
            }
        }

        // \u65e5\u671f
        clearDom();
        if (s.daysOfMonth) {
            var selDom = s.daysOfMonth.split(',').map(Number);
            var domChecks = document.querySelectorAll('#domCheckboxes input[type="checkbox"]');
            for (var j = 0; j < domChecks.length; j++) {
                domChecks[j].checked = selDom.indexOf(parseInt(domChecks[j].value)) >= 0;
            }
        }

        document.getElementById('txtStartTime').value = s.startTime;
        document.getElementById('txtEndTime').value = s.endTime;
        document.getElementById('txtRemarks').value = s.remarks || '';
        document.getElementById('chkEnabled').checked = s.isEnabled;
        onTypeChange();
        modal.show();
    }

    // ══════════════════════════════════════════════════════════════════
    //  \u5132\u5b58 / \u522a\u9664
    // ══════════════════════════════════════════════════════════════════

    function save() {
        var name = document.getElementById('txtName').value.trim();
        if (!name) { alert('\u8acb\u8f38\u5165\u6392\u7a0b\u540d\u7a31'); return; }

        var type = parseInt(document.getElementById('selRecurrenceType').value);
        var isWeekBased = (type === 0 || type === 1);

        // \u6536\u96c6\u661f\u671f / \u65e5\u671f
        var daysOfWeek = null, daysOfMonth = null;
        if (isWeekBased) {
            var dowChecked = document.querySelectorAll('#dayCheckboxes input:checked');
            if (dowChecked.length === 0) { alert('\u8acb\u81f3\u5c11\u9078\u64c7\u4e00\u5929'); return; }
            var dowArr = [];
            for (var i = 0; i < dowChecked.length; i++) dowArr.push(dowChecked[i].value);
            daysOfWeek = dowArr.join(',');
        } else {
            var domChecked = document.querySelectorAll('#domCheckboxes input:checked');
            if (domChecked.length === 0) { alert('\u8acb\u81f3\u5c11\u9078\u64c7\u4e00\u500b\u65e5\u671f'); return; }
            var domArr = [];
            for (var j = 0; j < domChecked.length; j++) domArr.push(domChecked[j].value);
            daysOfMonth = domArr.join(',');
        }

        // \u8ddf\u4f11\u53c3\u6578\u9a57\u8b49
        var runLength = null, restLength = null, anchorDateTime = null;
        if (type === 1 || type === 3) {
            runLength = parseInt(document.getElementById('txtRunLength').value);
            if (!runLength || runLength < 1) { alert('\u8ddf\u7684\u9031/\u6708\u6578\u5fc5\u9808 \u2265 1'); return; }
            restLength = parseInt(document.getElementById('txtRestLength').value);
            if (!restLength || restLength < 1) { alert('\u4f11\u7684\u9031/\u6708\u6578\u5fc5\u9808 \u2265 1'); return; }
            anchorDateTime = document.getElementById('txtAnchorDateTime').value;
            if (!anchorDateTime) { alert('\u8acb\u8a2d\u5b9a\u9031\u671f\u8d77\u7b97\u6642\u9593'); return; }
        }

        var startTime = document.getElementById('txtStartTime').value;
        var endTime = document.getElementById('txtEndTime').value;
        if (!startTime || !endTime) { alert('\u8acb\u8a2d\u5b9a\u958b\u59cb\u548c\u7d50\u675f\u6642\u9593'); return; }
        if (startTime === endTime) { alert('\u958b\u59cb\u548c\u7d50\u675f\u6642\u9593\u4e0d\u53ef\u76f8\u540c'); return; }

        var editId = document.getElementById('editId').value;
        var dto = {
            id: editId ? parseInt(editId) : null,
            name: name,
            recurrenceType: type,
            runLength: runLength,
            restLength: restLength,
            anchorDateTime: anchorDateTime,
            daysOfWeek: daysOfWeek,
            daysOfMonth: daysOfMonth,
            startTime: startTime,
            endTime: endTime,
            isEnabled: document.getElementById('chkEnabled').checked,
            remarks: document.getElementById('txtRemarks').value.trim() || null
        };

        fetch('/api/schedules', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(dto)
        })
        .then(function (r) { return r.json(); })
        .then(function (result) {
            if (result.success) {
                modal.hide();
                location.reload();
            } else {
                alert(result.message || '\u5132\u5b58\u5931\u6557');
            }
        })
        .catch(function () { alert('\u7db2\u8def\u932f\u8aa4'); });
    }

    function remove(id) {
        if (!confirm('\u78ba\u5b9a\u8981\u522a\u9664\u6b64\u6392\u7a0b\uff1f')) return;

        fetch('/api/schedules/' + id, { method: 'DELETE' })
            .then(function (r) { return r.json(); })
            .then(function (result) {
                if (result.success) location.reload();
                else alert(result.message || '\u522a\u9664\u5931\u6557');
            })
            .catch(function () { alert('\u7db2\u8def\u932f\u8aa4'); });
    }

    // ══════════════════════════════════════════════════════════════════
    //  \u661f\u671f / \u65e5\u671f\u5feb\u6377\u9078\u64c7
    // ══════════════════════════════════════════════════════════════════

    function selectWeekdays() {
        var checks = document.querySelectorAll('#dayCheckboxes input[type="checkbox"]');
        for (var i = 0; i < checks.length; i++) {
            checks[i].checked = (parseInt(checks[i].value) >= 1 && parseInt(checks[i].value) <= 5);
        }
    }

    function selectAll() {
        var checks = document.querySelectorAll('#dayCheckboxes input[type="checkbox"]');
        for (var i = 0; i < checks.length; i++) checks[i].checked = true;
    }

    function clearDays() {
        var checks = document.querySelectorAll('#dayCheckboxes input[type="checkbox"]');
        for (var i = 0; i < checks.length; i++) checks[i].checked = false;
    }

    function selectDom(str) {
        clearDom();
        var nums = str.split(',').map(Number);
        var checks = document.querySelectorAll('#domCheckboxes input[type="checkbox"]');
        for (var i = 0; i < checks.length; i++) {
            if (nums.indexOf(parseInt(checks[i].value)) >= 0) checks[i].checked = true;
        }
    }

    function clearDom() {
        var checks = document.querySelectorAll('#domCheckboxes input[type="checkbox"]');
        for (var i = 0; i < checks.length; i++) checks[i].checked = false;
    }

    function selectAllDom() {
        var checks = document.querySelectorAll('#domCheckboxes input[type="checkbox"]');
        for (var i = 0; i < checks.length; i++) checks[i].checked = true;
    }

    // ── \u5de5\u5177 ──

    function nowLocalIso() {
        var d = new Date();
        var pad = function (n) { return n < 10 ? '0' + n : String(n); };
        return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate())
            + 'T' + pad(d.getHours()) + ':' + pad(d.getMinutes());
    }

    function escHtml(s) {
        if (!s) return '';
        return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    // ── \u72c0\u614b\u5237\u65b0\u8a08\u6642\u5668 ──

    setInterval(function () {
        var rows = document.querySelectorAll('#scheduleBody tr');
        if (rows.length !== schedules.length) return;
        for (var i = 0; i < schedules.length; i++) {
            var statusCell = rows[i].cells[5];
            if (statusCell) statusCell.innerHTML = getScheduleStatus(schedules[i]);
        }
    }, 10000);

    // ── \u521d\u59cb\u5316 ──

    document.addEventListener('DOMContentLoaded', init);

    // ── \u516c\u958b\u4ecb\u9762 ──

    window._schedule = {
        showAddModal: showAddModal,
        edit: edit,
        save: save,
        remove: remove,
        onTypeChange: onTypeChange,
        selectWeekdays: selectWeekdays,
        selectAll: selectAll,
        clearDays: clearDays,
        selectDom: selectDom,
        selectAllDom: selectAllDom,
        clearDom: clearDom
    };
})();
