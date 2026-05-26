(function () {
    'use strict';

    function t(key, args) { return (window.i18n && window.i18n.t) ? window.i18n.t(key, args) : key; }

    var _coordinators = window._alarmInitData.coordinators || [];
    var _points = window._alarmInitData.points || [];
    var _rules = window._alarmInitData.rules || [];
    var _lineTargets = window._alarmInitData.lineTargets || [];
    var _diLabels = window._alarmInitData.diLabels || {};
    var _emailGroups = window._alarmInitData.emailGroups || [];
    var _emailSenderConfig = window._alarmInitData.emailSenderConfig || {};
    var _alarmRulesForRouting = window._alarmInitData.alarmRulesForRouting || [];

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

    function severityLabel(n) {
        var keys = ['critical', 'high', 'medium', 'low'];
        return t('alarm.severity.' + (keys[n] || 'high'));
    }

    function maxSeverityPillLabel(n) {
        return t('alarm.max_severity_pill.' + n);
    }

    var _modal = null;
    var _lineModal = null;
    var _emailGroupModal = null;
    var _emailRecipientModal = null;
    var _emailConfigModal = null;
    var _emailRulesModal = null;
    var _currentEmailGroupRecipients = [];  // 編輯群組 modal 時的暫存收件人清單

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
        if (isCalcSid(sid)) return t('alarm.option.calc_point');
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
        sel.innerHTML = '<option value="">' + t('alarm.select.placeholder') + '</option>';
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
        _emailGroupModal = new bootstrap.Modal(document.getElementById('emailGroupModal'));
        _emailRecipientModal = new bootstrap.Modal(document.getElementById('emailRecipientModal'));
        _emailConfigModal = new bootstrap.Modal(document.getElementById('emailConfigModal'));
        _emailRulesModal = new bootstrap.Modal(document.getElementById('emailRulesModal'));

        if (window.i18n && window.i18n.ready) {
            window.i18n.ready(function () {
                populateCoordinators();
                renderTable();
                renderLineTable();
                renderEmailTable();
            });
        } else {
            populateCoordinators();
            renderTable();
            renderLineTable();
            renderEmailTable();
        }

        toggleSection('high');
        toggleSection('low');
        toggleSection('di');

        // Tab 切換時換按鈕
        var rulesTabBtn = document.getElementById('tab-rules-btn');
        var lineTabBtn = document.getElementById('tab-line-btn');
        var emailTabBtn = document.getElementById('tab-email-btn');
        var btnAddRule = document.getElementById('btnAddRule');
        var btnAddLine = document.getElementById('btnAddLineTarget');
        var btnAddEmail = document.getElementById('btnAddEmailGroup');
        var btnEmailConfig = document.getElementById('btnEmailConfig');

        function setActiveButtons(tab) {
            btnAddRule.classList.toggle('d-none', tab !== 'rules');
            btnAddLine.classList.toggle('d-none', tab !== 'line');
            btnAddEmail.classList.toggle('d-none', tab !== 'email');
            btnEmailConfig.classList.toggle('d-none', tab !== 'email');
        }

        // 用 URL hash 記住當前 tab，重整後（saveGroup / saveConfig 等都會 location.reload）才不會跳回預設的「警報規則」
        var TAB_HASHES = { '#tab-line': lineTabBtn, '#tab-email': emailTabBtn, '#tab-rules': rulesTabBtn };
        function updateHash(hash) {
            if (window.history && window.history.replaceState) {
                window.history.replaceState(null, '', window.location.pathname + window.location.search + hash);
            } else {
                window.location.hash = hash;
            }
        }
        if (rulesTabBtn) rulesTabBtn.addEventListener('shown.bs.tab', function () { setActiveButtons('rules'); updateHash('#tab-rules'); });
        if (lineTabBtn) lineTabBtn.addEventListener('shown.bs.tab', function () { setActiveButtons('line'); updateHash('#tab-line'); });
        if (emailTabBtn) emailTabBtn.addEventListener('shown.bs.tab', function () { setActiveButtons('email'); updateHash('#tab-email'); });

        var targetBtn = TAB_HASHES[window.location.hash];
        if (targetBtn) {
            bootstrap.Tab.getOrCreateInstance(targetBtn).show();
        }
    }

    function populateCoordinators() {
        var sel = document.getElementById('selCoord');
        sel.innerHTML = '<option value="">' + t('alarm.select.placeholder') + '</option>';
        _coordinators.forEach(function (c) {
            var opt = document.createElement('option');
            opt.value = c.id;
            opt.textContent = c.name;
            sel.appendChild(opt);
        });
        var calcOpt = document.createElement('option');
        calcOpt.value = CALC_DEVICE_ID;
        calcOpt.textContent = t('alarm.option.calc_point');
        sel.appendChild(calcOpt);
    }

    function onCoordChange() {
        var nDbId = parseInt(document.getElementById('selCoord').value) || 0;
        var selSub = document.getElementById('selSubDevice');
        document.getElementById('txtSid').value = '';
        fillPointDropdown([]);

        if (!nDbId) {
            showSubDeviceCol(false);
            selSub.innerHTML = '<option value="">' + t('alarm.select.sub_device_placeholder') + '</option>';
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
            selSub.innerHTML = '<option value="">' + t('alarm.select.sub_device_placeholder') + '</option>';
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
            selSub.innerHTML = '<option value="">' + t('alarm.select.sub_device_placeholder') + '</option>';
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
        return '<span class="badge badge-severity-' + n + '">' + severityLabel(n) + '</span>';
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
        document.getElementById('ruleModalTitle').textContent = t('alarm.modal.title_add');
        document.getElementById('editId').value = '';
        document.getElementById('selCoord').value = '';
        showSubDeviceCol(false);
        document.getElementById('selSubDevice').innerHTML = '<option value="">' + t('alarm.select.sub_device_placeholder') + '</option>';
        document.getElementById('selPoint').innerHTML = '<option value="">' + t('alarm.select.point_placeholder') + '</option>';
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

        document.getElementById('ruleModalTitle').textContent = t('alarm.modal.title_edit');
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
        if (!sid) { alert(t('alarm.alert.select_point')); return; }

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
                alert(res.message || t('alarm.alert.save_failed'));
            }
        })
        .catch(function (e) { alert(t('alarm.alert.save_failed') + ': ' + e.message); });
    }

    function deleteRule(id) {
        if (!confirm(t('alarm.confirm.delete_rule'))) return;

        fetch('/api/alarm-rules/' + id, { method: 'DELETE' })
        .then(function (r) { return r.json(); })
        .then(function (res) {
            if (res.success) location.reload();
            else alert(res.message || t('alarm.alert.delete_failed'));
        })
        .catch(function (e) { alert(t('alarm.alert.delete_failed') + ': ' + e.message); });
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
        _lineTargets.forEach(function (t1) {
            var pillText = maxSeverityPillLabel(t1.maxSeverity);
            html += '<tr' + (t1.isEnabled ? '' : ' class="table-secondary"') + '>'
                + '<td><input type="checkbox" class="form-check-input"'
                + (t1.isEnabled ? ' checked' : '')
                + ' onchange="window._alarmSetting.toggleLineEnabled(' + t1.id + ', this.checked)" /></td>'
                + '<td>' + escHtml(t1.label) + '</td>'
                + '<td><span class="group-id-cell">' + escHtml(t1.groupId) + '</span></td>'
                + '<td>' + escHtml(t1.language || 'zh-TW') + '</td>'
                + '<td><span class="line-severity-pill">' + escHtml(pillText) + '</span></td>'
                + '<td>'
                + '<button class="btn btn-sm btn-line-test me-1" data-line-id="' + t1.id + '" onclick="window._alarmSetting.testLineSend(' + t1.id + ', this)">'
                + '<i class="fas fa-paper-plane me-1"></i>' + t('alarm.button.test_send') + '</button>'
                + '<button class="btn btn-sm btn-outline-primary me-1" onclick="window._alarmSetting.editLineTarget(' + t1.id + ')"><i class="fas fa-pen"></i></button>'
                + '<button class="btn btn-sm btn-outline-danger" onclick="window._alarmSetting.deleteLineTarget(' + t1.id + ')"><i class="fas fa-trash"></i></button>'
                + '</td></tr>';
        });
        tbody.innerHTML = html;
    }

    function showAddLineModal() {
        document.getElementById('lineTargetModalTitle').textContent = t('alarm.line_modal.title_add');
        document.getElementById('lineTargetEditId').value = '';
        document.getElementById('txtLineLabel').value = '';
        document.getElementById('txtLineGroupId').value = '';
        document.getElementById('selLineMaxSeverity').value = '3';
        document.getElementById('selLineLanguage').value = 'zh-TW';
        document.getElementById('chkLineEnabled').checked = true;
        _lineModal.show();
    }

    function editLineTarget(id) {
        var target = _lineTargets.find(function (x) { return x.id === id; });
        if (!target) return;
        document.getElementById('lineTargetModalTitle').textContent = t('alarm.line_modal.title_edit');
        document.getElementById('lineTargetEditId').value = target.id;
        document.getElementById('txtLineLabel').value = target.label;
        document.getElementById('txtLineGroupId').value = target.groupId;
        document.getElementById('selLineMaxSeverity').value = String(target.maxSeverity);
        document.getElementById('selLineLanguage').value = target.language || 'zh-TW';
        document.getElementById('chkLineEnabled').checked = target.isEnabled;
        _lineModal.show();
    }

    function saveLineTarget() {
        var label = document.getElementById('txtLineLabel').value.trim();
        var groupId = document.getElementById('txtLineGroupId').value.trim();
        if (!label) { alert(t('alarm.alert.input_label')); return; }
        if (!groupId) { alert(t('alarm.alert.input_group_id')); return; }

        var editId = document.getElementById('lineTargetEditId').value;
        var dto = {
            id: editId ? parseInt(editId) : null,
            label: label,
            groupId: groupId,
            maxSeverity: parseInt(document.getElementById('selLineMaxSeverity').value),
            language: document.getElementById('selLineLanguage').value,
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
                alert(res.body.message || t('alarm.alert.save_failed'));
            }
        })
        .catch(function (e) { alert(t('alarm.alert.save_failed') + ': ' + e.message); });
    }

    function deleteLineTarget(id) {
        if (!confirm(t('alarm.confirm.delete_line_group'))) return;
        fetch('/api/line-targets/' + id, { method: 'DELETE' })
        .then(function (r) { return r.json(); })
        .then(function (res) {
            if (res.success) location.reload();
            else alert(res.message || t('alarm.alert.delete_failed'));
        })
        .catch(function (e) { alert(t('alarm.alert.delete_failed') + ': ' + e.message); });
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
                var target = _lineTargets.find(function (x) { return x.id === id; });
                if (target) target.isEnabled = isEnabled;
                renderLineTable();
            } else {
                alert(res.message || t('alarm.alert.toggle_failed'));
                location.reload();
            }
        });
    }

    function testLineSend(id, btn) {
        if (btn) {
            btn.disabled = true;
            btn.dataset.origHtml = btn.innerHTML;
            btn.innerHTML = '<i class="fas fa-spinner fa-spin me-1"></i>' + t('alarm.button.sending');
        }
        fetch('/api/line-targets/' + id + '/test', { method: 'POST' })
        .then(function (r) {
            return r.json().then(function (b) { return { status: r.status, body: b }; });
        })
        .then(function (res) {
            if (res.status === 200 && res.body.success) {
                showToastSafe(t('alarm.toast.test_sent'), 'success');
            } else if (res.status === 429) {
                alert(res.body.message || t('alarm.alert.retry_later'));
            } else {
                alert(t('alarm.alert.send_failed_with_msg', { message: (res.body.message || res.status) }));
            }
        })
        .catch(function (e) { alert(t('alarm.alert.send_failed') + ': ' + e.message); })
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

    // ── Email 通知設定 Tab ──

    function renderEmailTable() {
        var tbody = document.getElementById('emailGroupsBody');
        var emptyMsg = document.getElementById('emailEmptyMsg');
        if (!_emailGroups || _emailGroups.length === 0) {
            tbody.innerHTML = '';
            emptyMsg.classList.remove('d-none');
            return;
        }
        emptyMsg.classList.add('d-none');
        var html = '';
        _emailGroups.forEach(function (g) {
            var pillText = maxSeverityPillLabel(g.maxSeverity);
            html += '<tr' + (g.isEnabled ? '' : ' class="table-secondary"') + '>'
                + '<td><input type="checkbox" class="form-check-input"'
                + (g.isEnabled ? ' checked' : '')
                + ' onchange="window._alarmEmail.toggleGroupEnabled(' + g.id + ', this.checked)" /></td>'
                + '<td><b>' + escHtml(g.label) + '</b>'
                + '<br/><small class="text-muted">' + escHtml(g.name) + '</small></td>'
                + '<td>' + (g.recipients ? g.recipients.length : 0) + '</td>'
                + '<td>' + escHtml(g.language || 'zh-TW') + '</td>'
                + '<td><span class="line-severity-pill">' + escHtml(pillText) + '</span></td>'
                + '<td>'
                + '<button class="btn btn-sm btn-outline-secondary me-1" onclick="window._alarmEmail.showRulesModal(' + g.id + ')" title="' + t('alarm.email.button.rules') + '"><i class="fas fa-link"></i></button>'
                + '<button class="btn btn-sm btn-outline-primary me-1" onclick="window._alarmEmail.editGroup(' + g.id + ')"><i class="fas fa-pen"></i></button>'
                + '<button class="btn btn-sm btn-outline-danger" onclick="window._alarmEmail.deleteGroup(' + g.id + ')"><i class="fas fa-trash"></i></button>'
                + '</td></tr>';
        });
        tbody.innerHTML = html;
    }

    function showConfigModal() {
        var c = _emailSenderConfig || {};
        document.getElementById('txtSmtpHost').value = c.smtpHost || '';
        document.getElementById('txtSmtpPort').value = c.smtpPort || 587;
        document.getElementById('chkSmtpStartTls').checked = c.useStartTls !== false;
        document.getElementById('chkSmtpSsl').checked = !!c.useSsl;
        document.getElementById('txtSmtpUsername').value = c.username || '';
        document.getElementById('txtSmtpPassword').value = '';
        document.getElementById('smtpPasswordHint').textContent = c.hasPassword
            ? t('alarm.field.smtp_password_already_set')
            : t('alarm.field.smtp_password_not_set');
        document.getElementById('txtFromAddress').value = c.fromAddress || '';
        document.getElementById('txtFromDisplayName').value = c.fromDisplayName || 'SCADA Engine';
        document.getElementById('txtEmailRatePerMinute').value = c.ratePerMinute || 10;
        document.getElementById('txtEmailTestThrottle').value = c.testSendThrottleSeconds || 10;
        document.getElementById('chkEmailEnableNotification').checked = c.enableNotification !== false;
        _emailConfigModal.show();
    }

    function saveConfig() {
        var dto = {
            enableNotification: document.getElementById('chkEmailEnableNotification').checked,
            smtpHost: document.getElementById('txtSmtpHost').value.trim(),
            smtpPort: parseInt(document.getElementById('txtSmtpPort').value) || 587,
            useSsl: document.getElementById('chkSmtpSsl').checked,
            useStartTls: document.getElementById('chkSmtpStartTls').checked,
            username: document.getElementById('txtSmtpUsername').value.trim(),
            password: document.getElementById('txtSmtpPassword').value,
            fromAddress: document.getElementById('txtFromAddress').value.trim(),
            fromDisplayName: document.getElementById('txtFromDisplayName').value.trim(),
            ratePerMinute: parseInt(document.getElementById('txtEmailRatePerMinute').value) || 10,
            testSendThrottleSeconds: parseInt(document.getElementById('txtEmailTestThrottle').value) || 10
        };
        fetch('/api/email-config', {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(dto)
        })
        .then(function (r) { return r.json().then(function (b) { return { ok: r.ok, body: b }; }); })
        .then(function (res) {
            if (res.ok && res.body.success) {
                _emailConfigModal.hide();
                location.reload();
            } else {
                alert(res.body.message || t('alarm.alert.save_failed'));
            }
        })
        .catch(function (e) { alert(t('alarm.alert.save_failed') + ': ' + e.message); });
    }

    function showAddGroupModal() {
        document.getElementById('emailGroupModalTitle').textContent = t('alarm.email_modal.title_add_group');
        document.getElementById('emailGroupEditId').value = '';
        document.getElementById('txtEmailGroupName').value = '';
        document.getElementById('txtEmailGroupLabel').value = '';
        document.getElementById('selEmailMaxSeverity').value = '3';
        document.getElementById('selEmailLanguage').value = 'zh-TW';
        document.getElementById('chkEmailGroupEnabled').checked = true;
        document.getElementById('txtEmailGroupRemarks').value = '';
        _currentEmailGroupRecipients = [];
        renderEmailRecipientsInModal();
        _emailGroupModal.show();
    }

    function editGroup(id) {
        var g = _emailGroups.find(function (x) { return x.id === id; });
        if (!g) return;
        document.getElementById('emailGroupModalTitle').textContent = t('alarm.email_modal.title_edit_group');
        document.getElementById('emailGroupEditId').value = g.id;
        document.getElementById('txtEmailGroupName').value = g.name;
        document.getElementById('txtEmailGroupLabel').value = g.label;
        document.getElementById('selEmailMaxSeverity').value = String(g.maxSeverity);
        document.getElementById('selEmailLanguage').value = g.language || 'zh-TW';
        document.getElementById('chkEmailGroupEnabled').checked = g.isEnabled;
        document.getElementById('txtEmailGroupRemarks').value = g.remarks || '';
        _currentEmailGroupRecipients = (g.recipients || []).slice();
        renderEmailRecipientsInModal();
        _emailGroupModal.show();
    }

    function renderEmailRecipientsInModal() {
        var section = document.getElementById('emailRecipientsSection');
        var hint = document.getElementById('emailRecipientsHint');
        if (hint) hint.style.display = _currentEmailGroupRecipients.length ? 'none' : '';

        var existing = section.querySelector('table');
        if (existing) existing.remove();
        var existingBtn = section.querySelector('.btn-add-recipient');
        if (existingBtn) existingBtn.remove();

        if (_currentEmailGroupRecipients.length > 0) {
            var html = '<table class="table table-sm"><thead><tr>'
                + '<th>' + t('alarm.col.enabled') + '</th>'
                + '<th>Email</th>'
                + '<th>' + t('alarm.field.recipient_name') + '</th>'
                + '<th style="width:160px">' + t('alarm.col.actions') + '</th>'
                + '</tr></thead><tbody>';
            _currentEmailGroupRecipients.forEach(function (r) {
                html += '<tr>'
                    + '<td>' + (r.isEnabled ? '<i class="fas fa-check text-success"></i>' : '<i class="fas fa-times text-muted"></i>') + '</td>'
                    + '<td>' + escHtml(r.emailAddress) + '</td>'
                    + '<td>' + escHtml(r.displayName || '') + '</td>'
                    + '<td>'
                    + (r.id ? '<button class="btn btn-sm btn-outline-info me-1" onclick="window._alarmEmail.testRecipient(' + r.id + ', this)" title="' + t('alarm.button.test_send') + '"><i class="fas fa-paper-plane"></i></button>' : '')
                    + '<button class="btn btn-sm btn-outline-danger" onclick="window._alarmEmail.removeRecipient(' + (r.id || 0) + ')"><i class="fas fa-trash"></i></button>'
                    + '</td></tr>';
            });
            html += '</tbody></table>';
            section.insertAdjacentHTML('beforeend', html);
        }

        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'btn btn-sm btn-outline-secondary btn-add-recipient';
        btn.innerHTML = '<i class="fas fa-plus me-1"></i>' + t('alarm.button.add_recipient');
        btn.onclick = function () { showAddRecipientModal(); };
        section.appendChild(btn);
    }

    function showAddRecipientModal() {
        var editId = document.getElementById('emailGroupEditId').value;
        if (!editId) {
            alert(t('alarm.alert.save_group_first'));
            return;
        }
        document.getElementById('emailRecipientModalTitle').textContent = t('alarm.email_modal.title_add_recipient');
        document.getElementById('emailRecipientEditId').value = '';
        document.getElementById('emailRecipientGroupId').value = editId;
        document.getElementById('txtRecipientEmail').value = '';
        document.getElementById('txtRecipientName').value = '';
        document.getElementById('chkRecipientEnabled').checked = true;
        _emailRecipientModal.show();
    }

    function saveRecipient() {
        var email = document.getElementById('txtRecipientEmail').value.trim();
        var groupId = parseInt(document.getElementById('emailRecipientGroupId').value);
        if (!email) { alert(t('alarm.alert.input_email')); return; }
        var dto = {
            id: parseInt(document.getElementById('emailRecipientEditId').value) || null,
            groupId: groupId,
            emailAddress: email,
            displayName: document.getElementById('txtRecipientName').value.trim() || null,
            isEnabled: document.getElementById('chkRecipientEnabled').checked
        };
        fetch('/api/email-recipients', {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(dto)
        })
        .then(function (r) { return r.json().then(function (b) { return { ok: r.ok, body: b }; }); })
        .then(function (res) {
            if (res.ok && res.body.success) {
                _emailRecipientModal.hide();
                location.reload();  // 重新載入以刷新 _emailGroups
            } else {
                alert(res.body.message || t('alarm.alert.save_failed'));
            }
        })
        .catch(function (e) { alert(t('alarm.alert.save_failed') + ': ' + e.message); });
    }

    function removeRecipient(recipientId) {
        if (!recipientId) return;
        if (!confirm(t('alarm.confirm.delete_recipient'))) return;
        fetch('/api/email-recipients/' + recipientId, { method: 'DELETE' })
        .then(function (r) { return r.json(); })
        .then(function (res) {
            if (res.success) {
                location.reload();
            } else {
                alert(res.message || t('alarm.alert.delete_failed'));
            }
        });
    }

    function saveGroup() {
        var name = document.getElementById('txtEmailGroupName').value.trim();
        var label = document.getElementById('txtEmailGroupLabel').value.trim();
        if (!name) { alert(t('alarm.alert.input_group_name')); return; }
        if (!label) { alert(t('alarm.alert.input_label')); return; }

        var editId = document.getElementById('emailGroupEditId').value;
        var dto = {
            id: editId ? parseInt(editId) : null,
            name: name,
            label: label,
            maxSeverity: parseInt(document.getElementById('selEmailMaxSeverity').value),
            language: document.getElementById('selEmailLanguage').value,
            isEnabled: document.getElementById('chkEmailGroupEnabled').checked,
            remarks: document.getElementById('txtEmailGroupRemarks').value.trim() || null
        };

        fetch('/api/email-groups', {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(dto)
        })
        .then(function (r) { return r.json().then(function (b) { return { ok: r.ok, body: b }; }); })
        .then(function (res) {
            if (res.ok && res.body.success) {
                _emailGroupModal.hide();
                location.reload();
            } else {
                alert(res.body.message || t('alarm.alert.save_failed'));
            }
        })
        .catch(function (e) { alert(t('alarm.alert.save_failed') + ': ' + e.message); });
    }

    function deleteGroup(id) {
        if (!confirm(t('alarm.confirm.delete_email_group'))) return;
        fetch('/api/email-groups/' + id, { method: 'DELETE' })
        .then(function (r) { return r.json(); })
        .then(function (res) {
            if (res.success) location.reload();
            else alert(res.message || t('alarm.alert.delete_failed'));
        });
    }

    function toggleGroupEnabled(id, isEnabled) {
        fetch('/api/email-groups/' + id + '/toggle', {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ isEnabled: isEnabled })
        }).then(function (r) { return r.json(); })
          .then(function (res) {
            if (res.success) {
                var g = _emailGroups.find(function (x) { return x.id === id; });
                if (g) g.isEnabled = isEnabled;
                renderEmailTable();
            }
          });
    }

    function showRulesModal(groupId) {
        var g = _emailGroups.find(function (x) { return x.id === groupId; });
        if (!g) return;
        document.getElementById('emailRulesGroupId').value = groupId;
        document.getElementById('emailRulesModalTitle').textContent =
            t('alarm.email_modal.title_rules') + ' - ' + g.label;
        var sel = (g.alarmRuleIds || []).slice();
        var list = document.getElementById('emailRulesList');
        if (!_alarmRulesForRouting || _alarmRulesForRouting.length === 0) {
            list.innerHTML = '<div class="text-muted p-3">' + t('alarm.empty.no_rules') + '</div>';
        } else {
            var html = '';
            _alarmRulesForRouting.forEach(function (r) {
                var checked = sel.indexOf(r.id) >= 0 ? ' checked' : '';
                html += '<div class="form-check">'
                     + '<input class="form-check-input email-rule-cb" type="checkbox" value="' + r.id + '" id="ruleCb_' + r.id + '"' + checked + '>'
                     + '<label class="form-check-label" for="ruleCb_' + r.id + '">'
                     + escHtml(r.pointName) + ' <small class="text-muted">(' + escHtml(r.sid) + ')</small></label></div>';
            });
            list.innerHTML = html;
        }
        _emailRulesModal.show();
    }

    function saveRules() {
        var groupId = parseInt(document.getElementById('emailRulesGroupId').value);
        var ids = [];
        document.querySelectorAll('.email-rule-cb:checked').forEach(function (cb) {
            ids.push(parseInt(cb.value));
        });
        fetch('/api/email-groups/' + groupId + '/rules', {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(ids)
        }).then(function (r) { return r.json(); })
          .then(function (res) {
            if (res.success) {
                _emailRulesModal.hide();
                location.reload();
            } else {
                alert(res.message || t('alarm.alert.save_failed'));
            }
          });
    }

    function testRecipient(id, btn) {
        if (btn) {
            btn.disabled = true;
            btn.dataset.origHtml = btn.innerHTML;
            btn.innerHTML = '<i class="fas fa-spinner fa-spin"></i>';
        }
        fetch('/api/email-recipients/' + id + '/test', { method: 'POST' })
        .then(function (r) {
            return r.json().then(function (b) { return { status: r.status, body: b }; });
        })
        .then(function (res) {
            if (res.status === 200 && res.body.success) {
                showToastSafe(t('alarm.toast.test_sent'), 'success');
            } else if (res.status === 429) {
                alert(res.body.message || t('alarm.alert.retry_later'));
            } else {
                alert(t('alarm.alert.send_failed_with_msg', { message: (res.body.message || res.status) }));
            }
        })
        .catch(function (e) { alert(t('alarm.alert.send_failed') + ': ' + e.message); })
        .finally(function () {
            if (btn) {
                btn.disabled = false;
                if (btn.dataset.origHtml) btn.innerHTML = btn.dataset.origHtml;
            }
        });
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

    window._alarmEmail = {
        showConfigModal: showConfigModal,
        saveConfig: saveConfig,
        showAddGroupModal: showAddGroupModal,
        editGroup: editGroup,
        saveGroup: saveGroup,
        deleteGroup: deleteGroup,
        toggleGroupEnabled: toggleGroupEnabled,
        showRulesModal: showRulesModal,
        saveRules: saveRules,
        saveRecipient: saveRecipient,
        removeRecipient: removeRecipient,
        testRecipient: testRecipient
    };
})();
