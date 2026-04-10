(function () {
    'use strict';

    var _coordinators = window._alarmInitData.coordinators || [];
    var _points = window._alarmInitData.points || [];
    var _rules = window._alarmInitData.rules || [];

    var _severityLabels = ['\u7dca\u6025', '\u9ad8', '\u4e2d', '\u4f4e'];
    var _modal = null;

    var CALC_DEVICE_ID = -999;

    function isCalcSid(sid) { return sid && sid.indexOf('CALC-') === 0; }

    // ── 多 ID 設備判斷 ──

    function isMultiIdCoord(coord) {
        return coord && coord.modbusId && coord.modbusId.includes(',');
    }

    function getSubDevices(coord) {
        var modbusIds = coord.modbusId.split(',').map(function (s) { return s.trim(); }).filter(Boolean);
        var deviceNames = (coord.deviceName || '').split(',').map(function (s) { return s.trim(); });
        return modbusIds.map(function (mid, i) {
            return { modbusId: parseInt(mid), name: deviceNames[i] || mid };
        });
    }

    // ── SID 反推 Coordinator / SubDevice ──

    function findCoordForSid(sid) {
        var hyphen = sid.indexOf('-');
        if (hyphen < 0) return null;
        var num = parseInt(sid.substring(0, hyphen));
        return _coordinators.find(function (c) {
            return num >= c.id * 65536 && num < (c.id + 1) * 65536;
        }) || null;
    }

    function findSubModbusIdForSid(sid) {
        var num = parseInt(sid.substring(0, sid.indexOf('-')));
        return Math.floor(((num - 1) % 65536) / 256);
    }

    // ── 點位篩選 ──

    function filterPointsByCoord(nDbId) {
        return _points.filter(function (p) {
            var num = parseInt(p.sid.substring(0, p.sid.indexOf('-')));
            return num >= nDbId * 65536 && num < (nDbId + 1) * 65536;
        });
    }

    function filterPointsBySubDevice(nDbId, nSubModbusId) {
        var rangeBase = nDbId * 65536 + nSubModbusId * 256;
        var rangeEnd = rangeBase + 256;
        return _points.filter(function (p) {
            var num = parseInt(p.sid.substring(0, p.sid.indexOf('-')));
            return num >= rangeBase && num < rangeEnd;
        });
    }

    // ── 設備標籤（仿 Trend 格式）──

    function getDeviceLabelForSid(sid) {
        if (isCalcSid(sid)) return '\u8a08\u7b97\u9ede\u4f4d';
        var coord = findCoordForSid(sid);
        if (!coord) return '';
        if (isMultiIdCoord(coord)) {
            var subId = findSubModbusIdForSid(sid);
            var subs = getSubDevices(coord);
            var sub = subs.find(function (s) { return s.modbusId === subId; });
            return sub ? sub.name : coord.name;
        }
        return coord.name;
    }

    function getFullPointLabel(sid, pointName) {
        var label = getDeviceLabelForSid(sid);
        return label ? label + ' / ' + (pointName || sid) : (pointName || sid);
    }

    function fillPointDropdown(pts) {
        var sel = document.getElementById('selPoint');
        sel.innerHTML = '<option value="">-- \u8acb\u9078\u64c7 --</option>';
        pts.forEach(function (p) {
            var opt = document.createElement('option');
            opt.value = p.sid;
            opt.textContent = p.name;
            opt.dataset.dataType = p.dataType;
            sel.appendChild(opt);
        });
    }

    // ── 子設備欄位顯示/隱藏 ──

    function showSubDeviceCol(show) {
        var subCol = document.getElementById('subDeviceCol');
        if (show) {
            subCol.classList.remove('d-none');
        } else {
            subCol.classList.add('d-none');
        }
    }

    // ── 初始化 ──

    function init() {
        _modal = new bootstrap.Modal(document.getElementById('ruleModal'));
        populateCoordinators();
        renderTable();
        toggleSection('high');
        toggleSection('low');
        toggleSection('di');
    }

    function populateCoordinators() {
        var sel = document.getElementById('selCoord');
        sel.innerHTML = '<option value="">-- \u8acb\u9078\u64c7 --</option>';
        _coordinators.forEach(function (c) {
            var opt = document.createElement('option');
            opt.value = c.id;
            opt.textContent = c.name;
            sel.appendChild(opt);
        });
        // 計算點位
        var calcOpt = document.createElement('option');
        calcOpt.value = CALC_DEVICE_ID;
        calcOpt.textContent = '\u8a08\u7b97\u9ede\u4f4d';
        sel.appendChild(calcOpt);
    }

    // ── 設備變更 ──

    function onCoordChange() {
        var nDbId = parseInt(document.getElementById('selCoord').value) || 0;
        var selSub = document.getElementById('selSubDevice');
        document.getElementById('txtSid').value = '';
        fillPointDropdown([]);

        if (!nDbId) {
            showSubDeviceCol(false);
            selSub.innerHTML = '<option value="">-- \u8acb\u9078\u64c7\u5b50\u8a2d\u5099 --</option>';
            return;
        }

        // 計算點位
        if (nDbId === CALC_DEVICE_ID) {
            showSubDeviceCol(false);
            var calcPts = _points.filter(function (p) { return isCalcSid(p.sid); });
            fillPointDropdown(calcPts);
            return;
        }

        var coord = _coordinators.find(function (c) { return c.id === nDbId; });

        if (coord && isMultiIdCoord(coord)) {
            showSubDeviceCol(true);
            selSub.innerHTML = '<option value="">-- \u8acb\u9078\u64c7\u5b50\u8a2d\u5099 --</option>';
            getSubDevices(coord).forEach(function (s) {
                var opt = document.createElement('option');
                opt.value = s.modbusId;
                opt.textContent = s.name;
                selSub.appendChild(opt);
            });
        } else {
            showSubDeviceCol(false);
            fillPointDropdown(filterPointsByCoord(nDbId));
        }
    }

    // ── 子設備變更 ──

    function onSubDeviceChange() {
        var nDbId = parseInt(document.getElementById('selCoord').value) || 0;
        var nSubId = parseInt(document.getElementById('selSubDevice').value);
        document.getElementById('txtSid').value = '';

        if (!isNaN(nSubId)) {
            fillPointDropdown(filterPointsBySubDevice(nDbId, nSubId));
        } else {
            fillPointDropdown([]);
        }
    }

    function onPointChange() {
        var sel = document.getElementById('selPoint');
        document.getElementById('txtSid').value = sel.value;
    }

    // ── 設定表單：從 SID 反推設備/子設備/點位 ──

    function setupSelectorsForSid(sid) {
        // 計算點位
        if (isCalcSid(sid)) {
            document.getElementById('selCoord').value = CALC_DEVICE_ID;
            showSubDeviceCol(false);
            var calcPts = _points.filter(function (p) { return isCalcSid(p.sid); });
            fillPointDropdown(calcPts);
            document.getElementById('selPoint').value = sid;
            document.getElementById('txtSid').value = sid;
            return;
        }

        var coord = findCoordForSid(sid);
        if (!coord) {
            document.getElementById('selCoord').value = '';
            showSubDeviceCol(false);
            fillPointDropdown([]);
            return;
        }

        document.getElementById('selCoord').value = coord.id;

        if (isMultiIdCoord(coord)) {
            showSubDeviceCol(true);
            var selSub = document.getElementById('selSubDevice');
            selSub.innerHTML = '<option value="">-- \u8acb\u9078\u64c7\u5b50\u8a2d\u5099 --</option>';
            getSubDevices(coord).forEach(function (s) {
                var opt = document.createElement('option');
                opt.value = s.modbusId;
                opt.textContent = s.name;
                selSub.appendChild(opt);
            });
            var subId = findSubModbusIdForSid(sid);
            selSub.value = subId;
            fillPointDropdown(filterPointsBySubDevice(coord.id, subId));
        } else {
            showSubDeviceCol(false);
            fillPointDropdown(filterPointsByCoord(coord.id));
        }

        document.getElementById('selPoint').value = sid;
        document.getElementById('txtSid').value = sid;
    }

    function toggleSection(type) {
        var isChecked;
        var ids;
        if (type === 'high') {
            isChecked = document.getElementById('chkAlarmHigh').checked;
            ids = ['secHighValue', 'secHighDeadband', 'secHighSeverity'];
        } else if (type === 'low') {
            isChecked = document.getElementById('chkAlarmLow').checked;
            ids = ['secLowValue', 'secLowDeadband', 'secLowSeverity'];
        } else {
            isChecked = document.getElementById('chkDiAlarm').checked;
            ids = ['secDiTrigger', 'secDiSeverity', 'secDiLabels'];
        }
        ids.forEach(function (id) {
            document.getElementById(id).style.display = isChecked ? '' : 'none';
        });
    }

    function severityBadge(n) {
        return '<span class="badge badge-severity-' + n + '">' + (_severityLabels[n] || n) + '</span>';
    }

    function renderTable() {
        var tbody = document.getElementById('rulesBody');
        var emptyMsg = document.getElementById('emptyMsg');

        if (_rules.length === 0) {
            tbody.innerHTML = '';
            emptyMsg.classList.remove('d-none');
            return;
        }
        emptyMsg.classList.add('d-none');

        var html = '';
        _rules.forEach(function (r) {
            var highHtml = r.isAlarmHigh
                ? '<span class="alarm-tag alarm-tag-high">\u2265 ' + r.alarmHighValue + ' ' + severityBadge(r.alarmHighSeverity) + '</span>'
                : '<span class="alarm-tag alarm-tag-off">-</span>';

            var lowHtml = r.isAlarmLow
                ? '<span class="alarm-tag alarm-tag-low">\u2264 ' + r.alarmLowValue + ' ' + severityBadge(r.alarmLowSeverity) + '</span>'
                : '<span class="alarm-tag alarm-tag-off">-</span>';

            var diHtml = r.isDiAlarm
                ? '<span class="alarm-tag alarm-tag-di">' + r.diTriggerState + ' ' + severityBadge(r.diAlarmSeverity) + '</span>'
                : '<span class="alarm-tag alarm-tag-off">-</span>';

            var fullLabel = getFullPointLabel(r.sid, r.pointName);

            html += '<tr' + (r.isEnabled ? '' : ' class="table-secondary"') + '>'
                + '<td><input type="checkbox" class="form-check-input"' + (r.isEnabled ? ' checked' : '')
                + ' onchange="window._alarm.toggleEnabled(' + r.id + ', this.checked)" /></td>'
                + '<td>' + escHtml(fullLabel) + '</td>'
                + '<td>' + highHtml + '</td>'
                + '<td>' + lowHtml + '</td>'
                + '<td>' + diHtml + '</td>'
                + '<td>' + escHtml(r.remarks) + '</td>'
                + '<td>'
                + '<button class="btn btn-sm btn-outline-primary me-1" onclick="window._alarm.editRule(' + r.id + ')"><i class="fas fa-pen"></i></button>'
                + '<button class="btn btn-sm btn-outline-danger" onclick="window._alarm.deleteRule(' + r.id + ')"><i class="fas fa-trash"></i></button>'
                + '</td></tr>';
        });
        tbody.innerHTML = html;
    }

    function escHtml(s) {
        if (!s) return '';
        var div = document.createElement('div');
        div.appendChild(document.createTextNode(s));
        return div.innerHTML;
    }

    function showAddModal() {
        document.getElementById('ruleModalTitle').textContent = '\u65b0\u589e\u8b66\u5831\u898f\u5247';
        document.getElementById('editId').value = '';
        document.getElementById('selCoord').value = '';
        showSubDeviceCol(false);
        document.getElementById('selSubDevice').innerHTML = '<option value="">-- \u8acb\u9078\u64c7\u5b50\u8a2d\u5099 --</option>';
        document.getElementById('selPoint').innerHTML = '<option value="">-- \u8acb\u5148\u9078\u64c7\u8a2d\u5099 --</option>';
        document.getElementById('txtSid').value = '';
        document.getElementById('chkAlarmHigh').checked = false;
        document.getElementById('txtAlarmHighValue').value = '';
        document.getElementById('txtDeadbandHigh').value = '0';
        document.getElementById('selHighSeverity').value = '1';
        document.getElementById('chkAlarmLow').checked = false;
        document.getElementById('txtAlarmLowValue').value = '';
        document.getElementById('txtDeadbandLow').value = '0';
        document.getElementById('selLowSeverity').value = '1';
        document.getElementById('chkDiAlarm').checked = false;
        document.getElementById('selDiTrigger').value = 'ON';
        document.getElementById('selDiSeverity').value = '1';
        document.getElementById('txtDiOnLabel').value = '';
        document.getElementById('txtDiOffLabel').value = '';
        document.getElementById('txtRemarks').value = '';
        document.getElementById('chkEnabled').checked = true;
        toggleSection('high');
        toggleSection('low');
        toggleSection('di');
        _modal.show();
    }

    function editRule(id) {
        var rule = _rules.find(function (r) { return r.id === id; });
        if (!rule) return;

        document.getElementById('ruleModalTitle').textContent = '\u7de8\u8f2f\u8b66\u5831\u898f\u5247';
        document.getElementById('editId').value = rule.id;

        setupSelectorsForSid(rule.sid);

        document.getElementById('chkAlarmHigh').checked = rule.isAlarmHigh;
        document.getElementById('txtAlarmHighValue').value = rule.alarmHighValue != null ? rule.alarmHighValue : '';
        document.getElementById('txtDeadbandHigh').value = rule.deadbandHigh != null ? rule.deadbandHigh : 0;
        document.getElementById('selHighSeverity').value = rule.alarmHighSeverity;

        document.getElementById('chkAlarmLow').checked = rule.isAlarmLow;
        document.getElementById('txtAlarmLowValue').value = rule.alarmLowValue != null ? rule.alarmLowValue : '';
        document.getElementById('txtDeadbandLow').value = rule.deadbandLow != null ? rule.deadbandLow : 0;
        document.getElementById('selLowSeverity').value = rule.alarmLowSeverity;

        document.getElementById('chkDiAlarm').checked = rule.isDiAlarm;
        document.getElementById('selDiTrigger').value = rule.diTriggerState || 'ON';
        document.getElementById('selDiSeverity').value = rule.diAlarmSeverity;
        document.getElementById('txtDiOnLabel').value = rule.diOnLabel || '';
        document.getElementById('txtDiOffLabel').value = rule.diOffLabel || '';

        document.getElementById('txtRemarks').value = rule.remarks || '';
        document.getElementById('chkEnabled').checked = rule.isEnabled;

        toggleSection('high');
        toggleSection('low');
        toggleSection('di');
        _modal.show();
    }

    function save() {
        var sid = document.getElementById('txtSid').value.trim();
        if (!sid) { alert('\u8acb\u9078\u64c7\u9ede\u4f4d'); return; }

        var isHigh = document.getElementById('chkAlarmHigh').checked;
        var isLow = document.getElementById('chkAlarmLow').checked;
        var isDi = document.getElementById('chkDiAlarm').checked;

        var editId = document.getElementById('editId').value;

        var dto = {
            id: editId ? parseInt(editId) : null,
            sid: sid,
            isEnabled: document.getElementById('chkEnabled').checked,
            isAlarmHigh: isHigh,
            alarmHighValue: isHigh ? parseFloat(document.getElementById('txtAlarmHighValue').value) || null : null,
            deadbandHigh: isHigh ? parseFloat(document.getElementById('txtDeadbandHigh').value) || 0 : 0,
            alarmHighSeverity: isHigh ? parseInt(document.getElementById('selHighSeverity').value) : 1,
            isAlarmLow: isLow,
            alarmLowValue: isLow ? parseFloat(document.getElementById('txtAlarmLowValue').value) || null : null,
            deadbandLow: isLow ? parseFloat(document.getElementById('txtDeadbandLow').value) || 0 : 0,
            alarmLowSeverity: isLow ? parseInt(document.getElementById('selLowSeverity').value) : 1,
            isDiAlarm: isDi,
            diTriggerState: isDi ? document.getElementById('selDiTrigger').value : null,
            diAlarmSeverity: isDi ? parseInt(document.getElementById('selDiSeverity').value) : 1,
            diOnLabel: isDi ? document.getElementById('txtDiOnLabel').value.trim() || null : null,
            diOffLabel: isDi ? document.getElementById('txtDiOffLabel').value.trim() || null : null,
            remarks: document.getElementById('txtRemarks').value.trim()
        };

        fetch('/api/alarm-rules', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(dto)
        })
        .then(function (r) { return r.json(); })
        .then(function (res) {
            if (res.success) {
                _modal.hide();
                location.reload();
            } else {
                alert(res.message || '\u5132\u5b58\u5931\u6557');
            }
        })
        .catch(function (e) { alert('\u5132\u5b58\u5931\u6557: ' + e.message); });
    }

    function deleteRule(id) {
        if (!confirm('\u78ba\u5b9a\u8981\u522a\u9664\u6b64\u8b66\u5831\u898f\u5247\uff1f')) return;

        fetch('/api/alarm-rules/' + id, { method: 'DELETE' })
        .then(function (r) { return r.json(); })
        .then(function (res) {
            if (res.success) location.reload();
            else alert(res.message || '\u522a\u9664\u5931\u6557');
        })
        .catch(function (e) { alert('\u522a\u9664\u5931\u6557: ' + e.message); });
    }

    function toggleEnabled(id, isEnabled) {
        var rule = _rules.find(function (r) { return r.id === id; });
        if (!rule) return;

        var dto = {
            id: rule.id,
            sid: rule.sid,
            isEnabled: isEnabled,
            isAlarmHigh: rule.isAlarmHigh,
            alarmHighValue: rule.alarmHighValue,
            deadbandHigh: rule.deadbandHigh,
            alarmHighSeverity: rule.alarmHighSeverity,
            isAlarmLow: rule.isAlarmLow,
            alarmLowValue: rule.alarmLowValue,
            deadbandLow: rule.deadbandLow,
            alarmLowSeverity: rule.alarmLowSeverity,
            isDiAlarm: rule.isDiAlarm,
            diTriggerState: rule.diTriggerState,
            diAlarmSeverity: rule.diAlarmSeverity,
            diOnLabel: rule.diOnLabel,
            diOffLabel: rule.diOffLabel,
            remarks: rule.remarks
        };

        fetch('/api/alarm-rules', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(dto)
        })
        .then(function (r) { return r.json(); })
        .then(function (res) {
            if (res.success) {
                rule.isEnabled = isEnabled;
                renderTable();
            }
        });
    }

    // 初始化
    document.addEventListener('DOMContentLoaded', init);

    // 公開介面
    window._alarm = {
        showAddModal: showAddModal,
        editRule: editRule,
        save: save,
        deleteRule: deleteRule,
        toggleEnabled: toggleEnabled,
        onCoordChange: onCoordChange,
        onSubDeviceChange: onSubDeviceChange,
        onPointChange: onPointChange,
        toggleSection: toggleSection
    };
})();
