(function () {
    'use strict';

    var DAY_NAMES = ['', '一', '二', '三', '四', '五', '六', '日'];
    var TYPE_LABELS = ['每週', 'N週循環', '每月', 'N月循環'];
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
            ? '<span class="badge bg-success">啟用</span>'
            : '<span class="badge bg-secondary">停用</span>';

        var typeHtml = '<span class="type-badge type-' + s.recurrenceType + '">' + TYPE_LABELS[s.recurrenceType] + '</span>';

        // 跟休參數說明
        if (s.recurrenceType === 1 || s.recurrenceType === 3) {
            var unit = s.recurrenceType === 1 ? '週' : '月';
            typeHtml += '<div class="cycle-info">跟' + s.runLength + unit
                + ' 休' + s.restLength + unit + '</div>';
        }

        var daysHtml = buildDateDisplay(s);

        // 例外日 / 加開日提示（僅在有資料時顯示）
        var excludeCount = s.excludeDates ? s.excludeDates.split(',').filter(Boolean).length : 0;
        var includeCount = s.includeDates ? s.includeDates.split(',').filter(Boolean).length : 0;
        if (excludeCount > 0 || includeCount > 0) {
            var hintParts = [];
            if (excludeCount > 0) hintParts.push('例外 ' + excludeCount + ' 日');
            if (includeCount > 0) hintParts.push('加開 ' + includeCount + ' 日');
            daysHtml += '<div class="date-override-hint">（' + hintParts.join('｜') + '）</div>';
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
            + '    <i class="fas fa-edit me-1"></i>編輯</button>'
            + '  <button class="btn btn-sm btn-outline-danger" onclick="window._schedule.remove(' + s.id + ')">'
            + '    <i class="fas fa-trash-alt me-1"></i>刪除</button>'
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
            html += '<span class="dom-badge">' + nums[i].trim() + '號</span>';
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

    // ════════════════════════════════════════════════════════════════
    //  即時狀態計算（四種模式 + 跨日 + 例外/加開）
    // ════════════════════════════════════════════════════════════════

    function getScheduleStatus(s) {
        if (!s.isEnabled) {
            return '<span class="status-inactive"><i class="fas fa-pause-circle me-1"></i>停用</span>';
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
                return '<span class="status-inactive"><i class="fas fa-clock me-1"></i>等待中</span>';
            }
        } else {
            if (nowMin < startMin || nowMin >= endMin) {
                baseDate = new Date(now.getFullYear(), now.getMonth(), now.getDate());
                if (!checkDayMatch(s, baseDate)) {
                    return '<span class="status-inactive"><i class="fas fa-moon me-1"></i>非排程日</span>';
                }
                return '<span class="status-inactive"><i class="fas fa-clock me-1"></i>等待中</span>';
            }
            baseDate = new Date(now.getFullYear(), now.getMonth(), now.getDate());
        }

        if (checkDayMatch(s, baseDate)) {
            return '<span class="status-active"><i class="fas fa-check-circle me-1"></i>導通中</span>';
        }
        return '<span class="status-inactive"><i class="fas fa-moon me-1"></i>非排程日</span>';
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

        var unitText = (type === 1) ? '週' : '月';
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
        document.getElementById('modalTitle').textContent = '新增排程';
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
        document.getElementById('modalTitle').textContent = '編輯排程';
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
        if (!raw) { alert('請先輸入日期'); return; }

        var date = parseDateInput(raw);
        if (!date) { alert('日期格式錯誤，請輸入 yyyy/MM/dd（例如 2026/05/15）'); return; }

        var ownList = (target === 'exclude') ? editExcludeDates : editIncludeDates;
        var otherList = (target === 'exclude') ? editIncludeDates : editExcludeDates;
        var otherLabel = (target === 'exclude') ? '加開日' : '例外日';
        var ownLabel = (target === 'exclude') ? '例外日' : '加開日';

        if (ownList.indexOf(date) >= 0) {
            alert(formatDateDisplay(date) + ' 已在' + ownLabel + '清單中');
            return;
        }

        if (otherList.indexOf(date) >= 0) {
            var msg = '此日期已在' + otherLabel + '，是否改為' + ownLabel + '？（會自動從' + otherLabel + '移除）';
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
            container.innerHTML = '<small class="text-muted">尚未加入</small>';
            return;
        }
        var html = '';
        for (var i = 0; i < list.length; i++) {
            var d = list[i];
            html += '<span class="date-chip ' + chipClass + '">' + escHtml(formatDateDisplay(d))
                + '<button type="button" class="date-chip-remove" onclick="window._schedule.removeDate(\'' + target + '\', \'' + d + '\')" title="移除">×</button></span>';
        }
        container.innerHTML = html;
    }

    // ════════════════════════════════════════════════════════════════
    //  儲存 / 刪除
    // ════════════════════════════════════════════════════════════════

    function save() {
        var name = document.getElementById('txtName').value.trim();
        if (!name) { alert('請輸入排程名稱'); return; }

        var type = parseInt(document.getElementById('selRecurrenceType').value);
        var isWeekBased = (type === 0 || type === 1);

        // 收集星期 / 日期
        var daysOfWeek = null, daysOfMonth = null;
        if (isWeekBased) {
            var dowChecked = document.querySelectorAll('#dayCheckboxes input:checked');
            if (dowChecked.length === 0) { alert('請至少選擇一天'); return; }
            var dowArr = [];
            for (var i = 0; i < dowChecked.length; i++) dowArr.push(dowChecked[i].value);
            daysOfWeek = dowArr.join(',');
        } else {
            var domChecked = document.querySelectorAll('#domCheckboxes input:checked');
            if (domChecked.length === 0) { alert('請至少選擇一個日期'); return; }
            var domArr = [];
            for (var j = 0; j < domChecked.length; j++) domArr.push(domChecked[j].value);
            daysOfMonth = domArr.join(',');
        }

        // 跟休參數驗證
        var runLength = null, restLength = null, anchorDateTime = null;
        if (type === 1 || type === 3) {
            runLength = parseInt(document.getElementById('txtRunLength').value);
            if (!runLength || runLength < 1) { alert('跟的週/月數必須 ≥ 1'); return; }
            restLength = parseInt(document.getElementById('txtRestLength').value);
            if (!restLength || restLength < 1) { alert('休的週/月數必須 ≥ 1'); return; }
            anchorDateTime = document.getElementById('txtAnchorDateTime').value;
            if (!anchorDateTime) { alert('請設定週期起算時間'); return; }
        }

        var startTime = document.getElementById('txtStartTime').value;
        var endTime = document.getElementById('txtEndTime').value;
        if (!startTime || !endTime) { alert('請設定開始和結束時間'); return; }
        if (startTime === endTime) { alert('開始和結束時間不可相同'); return; }

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
                alert(result.message || '儲存失敗');
            }
        })
        .catch(function () { alert('網路錯誤'); });
    }

    function remove(id) {
        if (!confirm('確定要刪除此排程？')) return;

        fetch('/api/schedules/' + id, { method: 'DELETE' })
            .then(function (r) { return r.json(); })
            .then(function (result) {
                if (result.success) location.reload();
                else alert(result.message || '刪除失敗');
            })
            .catch(function () { alert('網路錯誤'); });
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
        if (!s) return '';
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
