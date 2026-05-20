(function () {
    'use strict';

    var _coordinators = window._alarmInitData.coordinators || [];
    var _points = window._alarmInitData.points || [];
    var _rules = window._alarmInitData.rules || [];
    var _lineTargets = window._alarmInitData.lineTargets || [];
    var _diLabels = window._alarmInitData.diLabels || {};

    // 由 Designer 設定取得 SID 對應的 DI ON/OFF 標籤；找不到則回傳預設 ON/OFF
    function getDiLabelsForSid(sid) {
        var entry = sid ? _diLabels[sid] : null;
        return {
            onLabel: (entry && entry.onLabel) ? entry.onLabel : 'ON',
            offLabel: (entry && entry.offLabel) ? entry.offLabel : 'OFF'
        };
    }

    // 將指定 SID 的 Designer DI 標籤套到 Modal 唯讀欄位
    function applyDiLabelsForSid(sid) {
        var labels = getDiLabelsForSid(sid);
        document.getElementById('txtDiOnLabel').value = labels.onLabel;
        document.getElementById('txtDiOffLabel').value = labels.offLabel;
    }

    var _severityLabels = ['緊急', '高', '中', '低'];
    // 0=只收緊急, 1=緊急+高, 2=+中, 3=全收
    var _maxSeverityLabels = [
        '只收 緊急',
        '緊急 + 高',
        '緊急 + 高 + 中',
        '全收'
    ];
    var _modal = null;
    var _lineModal = null;

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

    // ── 設備標籤 ──

    function getDeviceLabelForSid(sid) {
        if (isCalcSid(sid)) return '計算點位';
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
        sel.innerHTML = '<option value="">-- 請選擇 --</option>';
        pts.forEach(function (p) {
            var opt = document.createElement('option');
            opt.value = p.sid;
            opt.textContent = p.name;
            opt.dataset.dataType = p.dataType;
            sel.appendChild(opt);
        });
    }

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
        _lineModal = new bootstrap.Modal(document.getElementById('lineTargetModal'));
        populateCoordinators();
        renderTable();
        renderLineTable();
        toggleSection('high');
        toggleSection('low');
        toggleSection('di');

        // Tab 切換時換按鈕
        var rulesTabBtn = document.getElementById('tab-rules-btn');
        var lineTabBtn = document.getElementById('tab-line-btn');
        var btnAddRule = document.getElementById('btnAddRule');
        var btnAddLine = document.getElementById('btnAddLineTarget');
        if (rulesTabBtn) rulesTabBtn.addEventListener('shown.bs.tab', function () {
            btnAddRule.classList.remove('d-none');
            btnAddLine.classList.add('d-none');
        });
        if (lineTabBtn) lineTabBtn.addEventListener('shown.bs.tab', function () {
            btnAddRule.classList.add('d-none');
            btnAddLine.classList.remove('d-none');
        });
    }

    function populateCoordinators() {
        var sel = document.getElementById('selCoord');
        sel.innerHTML = '<option value="">-- 請選擇 --</option>';
        _coordinators.forEach(function (c) {
            var opt = document.createElement('option');
            opt.value = c.id;
            opt.textContent = c.name;
            sel.appendChild(opt);
        });
        var calcOpt = document.createElement('option');
        calcOpt.value = CALC_DEVICE_ID;
        calcOpt.textContent = '計算點位';
        sel.appendChild(calcOpt);
    }

    function onCoordChange() {
        var nDbId = parseInt(document.getElementById('selCoord').value) || 0;
        var selSub = document.getElementById('selSubDevice');
        document.getElementById('txtSid').value = '';
        fillPointDropdown([]);

        if (!nDbId) {
            showSubDeviceCol(false);
            selSub.innerHTML = '<option value="">-- 請選擇子設備 --</option>';
            return;
        }

        if (nDbId === CALC_DEVICE_ID) {
            showSubDeviceCol(false);
            var calcPts = _points.filter(function (p) { return isCalcSid(p.sid); });
            fillPointDropdown(calcPts);
            return;
        }

        var coord = _coordinators.find(function (c) { return c.id === nDbId; });

        if (coord && isMultiIdCoord(coord)) {
            showSubDeviceCol(true);
            selSub.innerHTML = '<option value="">-- 請選擇子設備 --</option>';
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
        applyDiLabelsForSid(sel.value);
    }

    function setupSelectorsForSid(sid) {
        if (isCalcSid(sid)) {
            document.getElementById('selCoord').value = CALC_DEVICE_ID;
            showSubDeviceCol(false);
            var calcPts = _points.filter(function (p) { return isCalcSid(p.sid); });
            fillPointDropdown(calcPts);
            document.getElementById('selPoint').value = sid;
            document.getElementById('txtSid').value = sid;
            applyDiLabelsForSid(sid);
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
            selSub.innerHTML = '<option value="">-- 請選擇子設備 --</option>';
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
        applyDiLabelsForSid(sid);
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
                ? '<span class="alarm-tag alarm-tag-high">≥ ' + r.alarmHighValue + ' ' + severityBadge(r.alarmHighSeverity) + '</span>'
                : '<span class="alarm-tag alarm-tag-off">-</span>';

            var lowHtml = r.isAlarmLow
                ? '<span class="alarm-tag alarm-tag-low">≤ ' + r.alarmLowValue + ' ' + severityBadge(r.alarmLowSeverity) + '</span>'
                : '<span class="alarm-tag alarm-tag-off">-</span>';

            var diHtml;
            if (r.isDiAlarm) {
                var diLabels = getDiLabelsForSid(r.sid);
                var triggerLabel = r.diTriggerState === 'OFF' ? diLabels.offLabel : diLabels.onLabel;
                diHtml = '<span class="alarm-tag alarm-tag-di">' + escHtml(triggerLabel) + ' ' + severityBadge(r.diAlarmSeverity) + '</span>';
            } else {
                diHtml = '<span class="alarm-tag alarm-tag-off">-</span>';
            }

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
        if (s === null || s === undefined) return '';
        var div = document.createElement('div');
        div.appendChild(document.createTextNode(String(s)));
        return div.innerHTML;
    }

    function showAddModal() {
        document.getElementById('ruleModalTitle').textContent = '新增警報規則';
        document.getElementById('editId').value = '';
        document.getElementById('selCoord').value = '';
        showSubDeviceCol(false);
        document.getElementById('selSubDevice').innerHTML = '<option value="">-- 請選擇子設備 --</option>';
        document.getElementById('selPoint').innerHTML = '<option value="">-- 請先選擇設備 --</option>';
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
        document.getElementById('txtDiOnLabel').value = 'ON';
        document.getElementById('txtDiOffLabel').value = 'OFF';
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

        document.getElementById('ruleModalTitle').textContent = '編輯警報規則';
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
        // ON/OFF 標籤一律以 Designer 設定為準（覆蓋規則中過時的快照）
        applyDiLabelsForSid(rule.sid);

        document.getElementById('txtRemarks').value = rule.remarks || '';
        document.getElementById('chkEnabled').checked = rule.isEnabled;

        toggleSection('high');
        toggleSection('low');
        toggleSection('di');
        _modal.show();
    }

    function save() {
        var sid = document.getElementById('txtSid').value.trim();
        if (!sid) { alert('請選擇點位'); return; }

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
                alert(res.message || '儲存失敗');
            }
        })
        .catch(function (e) { alert('儲存失敗: ' + e.message); });
    }

    function deleteRule(id) {
        if (!confirm('確定要刪除此警報規則？')) return;

        fetch('/api/alarm-rules/' + id, { method: 'DELETE' })
        .then(function (r) { return r.json(); })
        .then(function (res) {
            if (res.success) location.reload();
            else alert(res.message || '刪除失敗');
        })
        .catch(function (e) { alert('刪除失敗: ' + e.message); });
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

    // ── Line 通知設定 Tab ──

    function renderLineTable() {
        var tbody = document.getElementById('lineTargetsBody');
        var emptyMsg = document.getElementById('lineEmptyMsg');

        if (_lineTargets.length === 0) {
            tbody.innerHTML = '';
            emptyMsg.classList.remove('d-none');
            return;
        }
        emptyMsg.classList.add('d-none');

        var html = '';
        _lineTargets.forEach(function (t) {
            var pillText = _maxSeverityLabels[t.maxSeverity] || ('上限 ' + t.maxSeverity);
            html += '<tr' + (t.isEnabled ? '' : ' class="table-secondary"') + '>'
                + '<td><input type="checkbox" class="form-check-input"'
                + (t.isEnabled ? ' checked' : '')
                + ' onchange="window._alarmSetting.toggleLineEnabled(' + t.id + ', this.checked)" /></td>'
                + '<td>' + escHtml(t.label) + '</td>'
                + '<td><span class="group-id-cell">' + escHtml(t.groupId) + '</span></td>'
                + '<td><span class="line-severity-pill">' + escHtml(pillText) + '</span></td>'
                + '<td>'
                + '<button class="btn btn-sm btn-line-test me-1" data-line-id="' + t.id + '" onclick="window._alarmSetting.testLineSend(' + t.id + ', this)">'
                + '<i class="fas fa-paper-plane me-1"></i>測試發送</button>'
                + '<button class="btn btn-sm btn-outline-primary me-1" onclick="window._alarmSetting.editLineTarget(' + t.id + ')"><i class="fas fa-pen"></i></button>'
                + '<button class="btn btn-sm btn-outline-danger" onclick="window._alarmSetting.deleteLineTarget(' + t.id + ')"><i class="fas fa-trash"></i></button>'
                + '</td></tr>';
        });
        tbody.innerHTML = html;
    }

    function showAddLineModal() {
        document.getElementById('lineTargetModalTitle').textContent = '新增 Line 群組';
        document.getElementById('lineTargetEditId').value = '';
        document.getElementById('txtLineLabel').value = '';
        document.getElementById('txtLineGroupId').value = '';
        document.getElementById('selLineMaxSeverity').value = '3';
        document.getElementById('chkLineEnabled').checked = true;
        _lineModal.show();
    }

    function editLineTarget(id) {
        var t = _lineTargets.find(function (x) { return x.id === id; });
        if (!t) return;
        document.getElementById('lineTargetModalTitle').textContent = '編輯 Line 群組';
        document.getElementById('lineTargetEditId').value = t.id;
        document.getElementById('txtLineLabel').value = t.label;
        document.getElementById('txtLineGroupId').value = t.groupId;
        document.getElementById('selLineMaxSeverity').value = String(t.maxSeverity);
        document.getElementById('chkLineEnabled').checked = t.isEnabled;
        _lineModal.show();
    }

    function saveLineTarget() {
        var label = document.getElementById('txtLineLabel').value.trim();
        var groupId = document.getElementById('txtLineGroupId').value.trim();
        if (!label) { alert('請填寫標籤'); return; }
        if (!groupId) { alert('請填寫 GroupID'); return; }

        var editId = document.getElementById('lineTargetEditId').value;
        var dto = {
            id: editId ? parseInt(editId) : null,
            label: label,
            groupId: groupId,
            maxSeverity: parseInt(document.getElementById('selLineMaxSeverity').value),
            isEnabled: document.getElementById('chkLineEnabled').checked
        };

        fetch('/api/line-targets', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(dto)
        })
        .then(function (r) { return r.json().then(function (b) { return { ok: r.ok, body: b }; }); })
        .then(function (res) {
            if (res.ok && res.body.success) {
                _lineModal.hide();
                location.reload();
            } else {
                alert(res.body.message || '儲存失敗');
            }
        })
        .catch(function (e) { alert('儲存失敗: ' + e.message); });
    }

    function deleteLineTarget(id) {
        if (!confirm('確定要刪除此 Line 群組？')) return;
        fetch('/api/line-targets/' + id, { method: 'DELETE' })
        .then(function (r) { return r.json(); })
        .then(function (res) {
            if (res.success) location.reload();
            else alert(res.message || '刪除失敗');
        })
        .catch(function (e) { alert('刪除失敗: ' + e.message); });
    }

    function toggleLineEnabled(id, isEnabled) {
        fetch('/api/line-targets/' + id + '/toggle', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ isEnabled: isEnabled })
        })
        .then(function (r) { return r.json(); })
        .then(function (res) {
            if (res.success) {
                var t = _lineTargets.find(function (x) { return x.id === id; });
                if (t) t.isEnabled = isEnabled;
                renderLineTable();
            } else {
                alert(res.message || '切換失敗');
                location.reload();
            }
        });
    }

    function testLineSend(id, btn) {
        if (btn) {
            btn.disabled = true;
            btn.dataset.origHtml = btn.innerHTML;
            btn.innerHTML = '<i class="fas fa-spinner fa-spin me-1"></i>發送中';
        }
        fetch('/api/line-targets/' + id + '/test', { method: 'POST' })
        .then(function (r) {
            return r.json().then(function (b) { return { status: r.status, body: b }; });
        })
        .then(function (res) {
            if (res.status === 200 && res.body.success) {
                showToastSafe('測試訊息已送出，請檢查群組', 'success');
            } else if (res.status === 429) {
                alert(res.body.message || '請稍候再試');
            } else {
                alert('發送失敗：' + (res.body.message || res.status));
            }
        })
        .catch(function (e) { alert('發送失敗: ' + e.message); })
        .finally(function () {
            if (btn) {
                btn.disabled = false;
                if (btn.dataset.origHtml) btn.innerHTML = btn.dataset.origHtml;
            }
        });
    }

    function showToastSafe(msg, kind) {
        // 簡易 toast：預設用 alert；如未來導入 Bootstrap toast 可在此替換
        if (kind === 'success') {
            // 不強制跳 alert，使用 console + 湮出提示
            var div = document.createElement('div');
            div.className = 'alert alert-success position-fixed';
            div.style.top = '80px';
            div.style.right = '20px';
            div.style.zIndex = '9999';
            div.innerHTML = '<i class="fas fa-check-circle me-1"></i>' + escHtml(msg);
            document.body.appendChild(div);
            setTimeout(function () { div.remove(); }, 3500);
        } else {
            alert(msg);
        }
    }

    document.addEventListener('DOMContentLoaded', init);

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

    window._alarmSetting = {
        showAddLineModal: showAddLineModal,
        editLineTarget: editLineTarget,
        saveLineTarget: saveLineTarget,
        deleteLineTarget: deleteLineTarget,
        toggleLineEnabled: toggleLineEnabled,
        testLineSend: testLineSend
    };
})();
