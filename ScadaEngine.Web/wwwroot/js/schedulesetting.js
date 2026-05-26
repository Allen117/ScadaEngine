(function () {
    'use strict';

    function t(key, args) { return (window.i18n && window.i18n.t) ? window.i18n.t(key, args) : key; }

    var DAY_KEYS = ['', 'mon', 'tue', 'wed', 'thu', 'fri', 'sat', 'sun'];
    var TYPE_KEYS = ['weekly', 'n_week_cycle', 'monthly', 'n_month_cycle'];
    function dayName(d) { return t('schedule.dayofweek.' + DAY_KEYS[d]); }
    function typeLabel(n) { return t('schedule.recurrence.' + TYPE_KEYS[n]); }
    function unitLabel(type) { return (type === 1) ? t('schedule.unit.week') : t('schedule.unit.month'); }

    var schedules = [];
    var modal = null;
    // Modal 編輯中的例外日 / 加開日清單yyyy-MM-dd 陣列
    var editExcludeDates = [];
    var editIncludeDates = [];

    // ── 初始化 ──

    function init() {
        var data = window._scheduleInitData || {};
        schedules = data.schedules || [];
        modal = new bootstrap.Modal(document.getElementById('scheduleModal'));
        buildDomCheckboxes();
        if (window.i18n && window.i18n.ready) {
            window.i18n.ready(renderTable);
        } else {
            renderTable();
        }
    }

    function buildDomCheckboxes() {
        var container = document.getElementById('domCheckboxes');
        var html = '';
        for (var d = 1; d <= 31; d++) {
            html += '<label class="dom-check"><input type="checkbox" value="' + d + '" /><span>' + d + '</span></label>';
        }
        container.innerHTML = html;
    }

    // ════════════════════════════════════════════════════════════════
    //  表格渲染
    // ════════════════════════════════════════════════════════════════

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
            ? '<span class="badge bg-success">' + escHtml(t('schedule.status.enabled')) + '</span>'
            : '<span class="badge bg-secondary">' + escHtml(t('schedule.status.disabled')) + '</span>';

        var typeHtml = '<span class="type-badge type-' + s.recurrenceType + '">' + escHtml(typeLabel(s.recurrenceType)) + '</span>';

        // 跟休參數說明
        if (s.recurrenceType === 1 || s.recurrenceType === 3) {
            var unit = unitLabel(s.recurrenceType);
            typeHtml += '<div class="cycle-info">'
                + escHtml(t('schedule.cycle.run_rest_info', { run: s.runLength, rest: s.restLength, unit: unit }))
                + '</div>';
        }

        var daysHtml = buildDateDisplay(s);

        // 例外日 / 加開日提示（僅在有資料時顯示）
        var excludeCount = s.excludeDates ? s.excludeDates.split(',').filter(Boolean).length : 0;
        var includeCount = s.includeDates ? s.includeDates.split(',').filter(Boolean).length : 0;
        if (excludeCount > 0 || includeCount > 0) {
            var hintParts = [];
            if (excludeCount > 0) hintParts.push(t('schedule.hint.exclude_count', { count: excludeCount }));
            if (includeCount > 0) hintParts.push(t('schedule.hint.include_count', { count: includeCount }));
            daysHtml += '<div class="date-override-hint">（' + escHtml(hintParts.join('｜')) + '）</div>';
        }

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
            + '    <i class="fas fa-edit me-1"></i>' + escHtml(t('schedule.button.edit')) + '</button>'
            + '  <button class="btn btn-sm btn-outline-danger" onclick="window._schedule.remove(' + s.id + ')">'
            + '    <i class="fas fa-trash-alt me-1"></i>' + escHtml(t('schedule.button.delete')) + '</button>'
            + '</td></tr>';
    }

    function buildDateDisplay(s) {
        if (s.recurrenceType === 0 || s.recurrenceType === 1) {
            return buildDayBadges(s.daysOfWeek);
        }
        // 每月 / N月
        var nums = s.daysOfMonth ? s.daysOfMonth.split(',') : [];
        var html = '';
        for (var i = 0; i < nums.length; i++) {
            html += '<span class="dom-badge">'
                + escHtml(t('schedule.dom.day_suffix', { day: nums[i].trim() }))
                + '</span>';
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
            html += '<span class="day-badge' + (isActive ? '' : ' day-off') + '">' + escHtml(dayName(d)) + '</span>';
        }
        return html;
    }

    // ════════════════════════════════════════════════════════════════
    //  即時狀態計算（四種模式 + 跨日 + 例外/加開）
    // ════════════════════════════════════════════════════════════════

    function getScheduleStatus(s) {
        if (!s.isEnabled) {
            return '<span class="status-inactive"><i class="fas fa-pause-circle me-1"></i>' + escHtml(t('schedule.status.disabled')) + '</span>';
        }

        var now = new Date();

        // 跨日時以「啟始日」為基準，凌晨段（now < endTime）屬於昨天那筆排程的延續
        var nowMin = now.getHours() * 60 + now.getMinutes();
        var sp = s.startTime.split(':'), ep = s.endTime.split(':');
        var startMin = parseInt(sp[0]) * 60 + parseInt(sp[1]);
        var endMin = parseInt(ep[0]) * 60 + parseInt(ep[1]);
        var isCrossDay = endMin <= startMin;

        var baseDate;
        if (isCrossDay) {
            if (nowMin >= startMin) {
                baseDate = new Date(now.getFullYear(), now.getMonth(), now.getDate());
            } else if (nowMin < endMin) {
                baseDate = new Date(now.getFullYear(), now.getMonth(), now.getDate() - 1);
            } else {
                return '<span class="status-inactive"><i class="fas fa-clock me-1"></i>' + escHtml(t('schedule.status.waiting')) + '</span>';
            }
        } else {
            if (nowMin < startMin || nowMin >= endMin) {
                baseDate = new Date(now.getFullYear(), now.getMonth(), now.getDate());
                if (!checkDayMatch(s, baseDate)) {
                    return '<span class="status-inactive"><i class="fas fa-moon me-1"></i>' + escHtml(t('schedule.status.not_scheduled')) + '</span>';
                }
                return '<span class="status-inactive"><i class="fas fa-clock me-1"></i>' + escHtml(t('schedule.status.waiting')) + '</span>';
            }
            baseDate = new Date(now.getFullYear(), now.getMonth(), now.getDate());
        }

        if (checkDayMatch(s, baseDate)) {
            return '<span class="status-active"><i class="fas fa-check-circle me-1"></i>' + escHtml(t('schedule.status.active')) + '</span>';
        }
        return '<span class="status-inactive"><i class="fas fa-moon me-1"></i>' + escHtml(t('schedule.status.not_scheduled')) + '</span>';
    }

    function checkDayMatch(s, baseDate) {
        var key = formatYmd(baseDate);
        if (dateListContains(s.includeDates, key)) return true;
        if (dateListContains(s.excludeDates, key)) return false;

        switch (s.recurrenceType) {
            case 0: return checkDaysOfWeek(baseDate, s.daysOfWeek);
            case 1: return checkWeekCycle(baseDate, s) && checkDaysOfWeek(baseDate, s.daysOfWeek);
            case 2: return checkDaysOfMonth(baseDate, s.daysOfMonth);
            case 3: return checkMonthCycle(baseDate, s) && checkDaysOfMonth(baseDate, s.daysOfMonth);
        }
        return false;
    }

    function dateListContains(listStr, key) {
        if (!listStr) return false;
        var arr = listStr.split(',');
        for (var i = 0; i < arr.length; i++) {
            if (arr[i].trim() === key) return true;
        }
        return false;
    }

    function checkDaysOfWeek(baseDate, daysStr) {
        if (!daysStr) return false;
        var dotw = baseDate.getDay(); // 0=Sun
        if (dotw === 0) dotw = 7;
        return daysStr.split(',').map(Number).indexOf(dotw) >= 0;
    }

    function checkDaysOfMonth(baseDate, domStr) {
        if (!domStr) return false;
        return domStr.split(',').map(Number).indexOf(baseDate.getDate()) >= 0;
    }

    function checkWeekCycle(baseDate, s) {
        if (!s.anchorDateTime || !s.runLength || !s.restLength) return false;
        var anchor = new Date(s.anchorDateTime);
        var elapsedMs = baseDate.getTime() - anchor.getTime();
        if (elapsedMs < 0) return false;
        var totalCycleWeeks = s.runLength + s.restLength;
        var elapsedWeeks = Math.floor(elapsedMs / (7 * 24 * 60 * 60000));
        var posInCycle = elapsedWeeks % totalCycleWeeks;
        return posInCycle < s.runLength;
    }

    function checkMonthCycle(baseDate, s) {
        if (!s.anchorDateTime || !s.runLength || !s.restLength) return false;
        var anchor = new Date(s.anchorDateTime);
        var totalMonths = (baseDate.getFullYear() - anchor.getFullYear()) * 12 + (baseDate.getMonth() - anchor.getMonth());
        if (totalMonths < 0) return false;
        var totalCycleMonths = s.runLength + s.restLength;
        var posInCycle = totalMonths % totalCycleMonths;
        return posInCycle < s.runLength;
    }

    // ════════════════════════════════════════════════════════════════
    //  Modal 操作
    // ════════════════════════════════════════════════════════════════

    function onTypeChange() {
        var type = parseInt(document.getElementById('selRecurrenceType').value);
        var isCycle = (type === 1 || type === 3);
        var isWeekBased = (type === 0 || type === 1);
        var isMonthBased = (type === 2 || type === 3);

        toggle('cycleSection', isCycle);
        toggle('daysOfWeekSection', isWeekBased);
        toggle('daysOfMonthSection', isMonthBased);

        var unitText = unitLabel(type);
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
        document.getElementById('modalTitle').textContent = t('schedule.modal.title_add');
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
        editExcludeDates = [];
        editIncludeDates = [];
        document.getElementById('txtExcludeDate').value = '';
        document.getElementById('txtIncludeDate').value = '';
        renderDateChips();
        onTypeChange();
        modal.show();
    }

    function edit(id) {
        var s = schedules.find(function (x) { return x.id === id; });
        if (!s) return;

        document.getElementById('editId').value = s.id;
        document.getElementById('modalTitle').textContent = t('schedule.modal.title_edit');
        document.getElementById('txtName').value = s.name;
        document.getElementById('selRecurrenceType').value = String(s.recurrenceType);

        // 跟休參數
        document.getElementById('txtRunLength').value = s.runLength || 1;
        document.getElementById('txtRestLength').value = s.restLength || 1;
        document.getElementById('txtAnchorDateTime').value = s.anchorDateTime || '';

        // 星期
        clearDays();
        if (s.daysOfWeek) {
            var selDow = s.daysOfWeek.split(',').map(Number);
            var checks = document.querySelectorAll('#dayCheckboxes input[type="checkbox"]');
            for (var i = 0; i < checks.length; i++) {
                checks[i].checked = selDow.indexOf(parseInt(checks[i].value)) >= 0;
            }
        }

        // 日期
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

        editExcludeDates = s.excludeDates ? s.excludeDates.split(',').map(function (x) { return x.trim(); }).filter(Boolean) : [];
        editIncludeDates = s.includeDates ? s.includeDates.split(',').map(function (x) { return x.trim(); }).filter(Boolean) : [];
        document.getElementById('txtExcludeDate').value = '';
        document.getElementById('txtIncludeDate').value = '';
        renderDateChips();

        onTypeChange();
        modal.show();
    }

    // ════════════════════════════════════════════════════════════════
    //  例外日 / 加開日清單管理
    // ════════════════════════════════════════════════════════════════

    function addExcludeDate() { addDateToList('exclude'); }
    function addIncludeDate() { addDateToList('include'); }

    function addDateToList(target) {
        var inputId = (target === 'exclude') ? 'txtExcludeDate' : 'txtIncludeDate';
        var raw = document.getElementById(inputId).value.trim();
        if (!raw) { alert(t('schedule.alert.input_date')); return; }

        var date = parseDateInput(raw);
        if (!date) { alert(t('schedule.alert.invalid_date_format')); return; }

        var ownList = (target === 'exclude') ? editExcludeDates : editIncludeDates;
        var otherList = (target === 'exclude') ? editIncludeDates : editExcludeDates;
        var otherLabel = (target === 'exclude') ? t('schedule.label.include_date') : t('schedule.label.exclude_date');
        var ownLabel = (target === 'exclude') ? t('schedule.label.exclude_date') : t('schedule.label.include_date');

        if (ownList.indexOf(date) >= 0) {
            alert(t('schedule.alert.date_already_in_list', { date: formatDateDisplay(date), label: ownLabel }));
            return;
        }

        if (otherList.indexOf(date) >= 0) {
            var msg = t('schedule.confirm.move_date', { other: otherLabel, own: ownLabel });
            if (!confirm(msg)) return;
            var idx = otherList.indexOf(date);
            otherList.splice(idx, 1);
        }

        ownList.push(date);
        ownList.sort();
        document.getElementById(inputId).value = '';
        renderDateChips();
    }

    function removeDate(target, date) {
        var list = (target === 'exclude') ? editExcludeDates : editIncludeDates;
        var idx = list.indexOf(date);
        if (idx >= 0) {
            list.splice(idx, 1);
            renderDateChips();
        }
    }

    function renderDateChips() {
        renderChipList('excludeDateChips', editExcludeDates, 'exclude', 'date-chip-exclude');
        renderChipList('includeDateChips', editIncludeDates, 'include', 'date-chip-include');
    }

    function renderChipList(containerId, list, target, chipClass) {
        var container = document.getElementById(containerId);
        if (list.length === 0) {
            container.innerHTML = '<small class="text-muted">' + escHtml(t('schedule.empty.no_dates')) + '</small>';
            return;
        }
        var removeTitle = escHtml(t('schedule.tooltip.remove'));
        var html = '';
        for (var i = 0; i < list.length; i++) {
            var d = list[i];
            html += '<span class="date-chip ' + chipClass + '">' + escHtml(formatDateDisplay(d))
                + '<button type="button" class="date-chip-remove" onclick="window._schedule.removeDate(\'' + target + '\', \'' + d + '\')" title="' + removeTitle + '">×</button></span>';
        }
        container.innerHTML = html;
    }

    // ════════════════════════════════════════════════════════════════
    //  儲存 / 刪除
    // ════════════════════════════════════════════════════════════════

    function save() {
        var name = document.getElementById('txtName').value.trim();
        if (!name) { alert(t('schedule.alert.input_name')); return; }

        var type = parseInt(document.getElementById('selRecurrenceType').value);
        var isWeekBased = (type === 0 || type === 1);

        // 收集星期 / 日期
        var daysOfWeek = null, daysOfMonth = null;
        if (isWeekBased) {
            var dowChecked = document.querySelectorAll('#dayCheckboxes input:checked');
            if (dowChecked.length === 0) { alert(t('schedule.alert.select_dayofweek')); return; }
            var dowArr = [];
            for (var i = 0; i < dowChecked.length; i++) dowArr.push(dowChecked[i].value);
            daysOfWeek = dowArr.join(',');
        } else {
            var domChecked = document.querySelectorAll('#domCheckboxes input:checked');
            if (domChecked.length === 0) { alert(t('schedule.alert.select_dayofmonth')); return; }
            var domArr = [];
            for (var j = 0; j < domChecked.length; j++) domArr.push(domChecked[j].value);
            daysOfMonth = domArr.join(',');
        }

        // 跟休參數驗證
        var runLength = null, restLength = null, anchorDateTime = null;
        if (type === 1 || type === 3) {
            runLength = parseInt(document.getElementById('txtRunLength').value);
            if (!runLength || runLength < 1) { alert(t('schedule.alert.run_invalid')); return; }
            restLength = parseInt(document.getElementById('txtRestLength').value);
            if (!restLength || restLength < 1) { alert(t('schedule.alert.rest_invalid')); return; }
            anchorDateTime = document.getElementById('txtAnchorDateTime').value;
            if (!anchorDateTime) { alert(t('schedule.alert.anchor_required')); return; }
        }

        var startTime = document.getElementById('txtStartTime').value;
        var endTime = document.getElementById('txtEndTime').value;
        if (!startTime || !endTime) { alert(t('schedule.alert.time_required')); return; }
        if (startTime === endTime) { alert(t('schedule.alert.time_same')); return; }

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
            excludeDates: editExcludeDates.length > 0 ? editExcludeDates.join(',') : null,
            includeDates: editIncludeDates.length > 0 ? editIncludeDates.join(',') : null,
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
                alert(result.message || t('schedule.alert.save_failed'));
            }
        })
        .catch(function () { alert(t('schedule.alert.network_failed')); });
    }

    function remove(id) {
        if (!confirm(t('schedule.confirm.delete'))) return;

        fetch('/api/schedules/' + id, { method: 'DELETE' })
            .then(function (r) { return r.json(); })
            .then(function (result) {
                if (result.success) location.reload();
                else alert(result.message || t('schedule.alert.delete_failed'));
            })
            .catch(function () { alert(t('schedule.alert.network_failed')); });
    }

    // ════════════════════════════════════════════════════════════════
    //  星期 / 日期快捷選擇
    // ════════════════════════════════════════════════════════════════

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

    // ── 工具 ──

    function nowLocalIso() {
        var d = new Date();
        var pad = function (n) { return n < 10 ? '0' + n : String(n); };
        return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate())
            + 'T' + pad(d.getHours()) + ':' + pad(d.getMinutes());
    }

    function formatYmd(d) {
        var pad = function (n) { return n < 10 ? '0' + n : String(n); };
        return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate());
    }

    /**
     * 解析使用者輸入的日期字串（接受 yyyy/MM/dd、yyyy-MM-dd、yyyy/M/d 等）。
     * 通過 Date 物件驗證日期合法性（避免 2026/02/30 這種輸入）。
     * 回傳正規化的 yyyy-MM-dd（內部格式，與後端 / Engine 對齊）；不合法回傳 null。
     */
    function parseDateInput(raw) {
        if (!raw) return null;
        var m = raw.match(/^(\d{4})[\/\-](\d{1,2})[\/\-](\d{1,2})$/);
        if (!m) return null;
        var y = parseInt(m[1], 10), mo = parseInt(m[2], 10), d = parseInt(m[3], 10);
        if (mo < 1 || mo > 12 || d < 1 || d > 31) return null;
        var dt = new Date(y, mo - 1, d);
        if (dt.getFullYear() !== y || dt.getMonth() !== mo - 1 || dt.getDate() !== d) return null;
        var pad = function (n) { return n < 10 ? '0' + n : String(n); };
        return y + '-' + pad(mo) + '-' + pad(d);
    }

    /** ISO yyyy-MM-dd → UI 顯示用 yyyy/MM/dd */
    function formatDateDisplay(iso) {
        if (!iso) return '';
        return iso.replace(/-/g, '/');
    }

    function escHtml(s) {
        if (s === null || s === undefined) return '';
        return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    // ── 狀態刷新計時器 ──

    setInterval(function () {
        var rows = document.querySelectorAll('#scheduleBody tr');
        if (rows.length !== schedules.length) return;
        for (var i = 0; i < schedules.length; i++) {
            var statusCell = rows[i].cells[5];
            if (statusCell) statusCell.innerHTML = getScheduleStatus(schedules[i]);
        }
    }, 10000);

    // ── 初始化 ──

    document.addEventListener('DOMContentLoaded', init);

    // ── 公開介面 ──

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
        clearDom: clearDom,
        addExcludeDate: addExcludeDate,
        addIncludeDate: addIncludeDate,
        removeDate: removeDate
    };
})();
