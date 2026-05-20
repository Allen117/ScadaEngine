(function () {
    var data = window._condCtrlData || {};
    var allPoints   = data.allPoints   || [];
    var allCoords   = data.allCoords   || [];
    var allDbCoords = data.allDbCoords || [];
    var dbRules     = data.dbRules     || [];

    // ── SID 類型判斷 ───────────────────────────────────────────────
    function isCalcSid(sid) { return !!sid && sid.indexOf('CALC-') === 0; }
    function isDbSid(sid)   { return !!sid && /^DB\d+-S\d+$/.test(sid); }
    function getDbCoordIdForSid(sid) {
        var m = sid && sid.match(/^DB(\d+)-S\d+$/);
        return m ? parseInt(m[1]) : 0;
    }

    // 設備下拉選單value三類："CALC" / "DB:{n}" / Modbus coord id (數字字串) / "0" 表全部
    function parseDeviceValue(v) {
        if (v === 'CALC') return { kind: 'calc' };
        if (typeof v === 'string' && v.indexOf('DB:') === 0) {
            var n = parseInt(v.substring(3));
            return isNaN(n) ? { kind: 'all' } : { kind: 'db', id: n };
        }
        var nId = parseInt(v) || 0;
        return nId > 0 ? { kind: 'modbus', id: nId } : { kind: 'all' };
    }

    var rules        = [];
    var ruleSeq      = 0;
    var editingRuleId = null;

    // operator \u7b26\u865f \u2194 tinyint \u5c0d\u7167
    var opToInt = { '>': 0, '<': 1, '>=': 2, '<=': 3, '==': 4, '!=': 5 };
    var intToOp = { 0: '>', 1: '<', 2: '>=', 3: '<=', 4: '==', 5: '!=' };

    var conditionCoordinator   = document.getElementById('conditionCoordinator');
    var controlCoordinator     = document.getElementById('controlCoordinator');
    var conditionPoint         = document.getElementById('conditionPoint');
    var conditionOperator      = document.getElementById('conditionOperator');
    var conditionValue         = document.getElementById('conditionValue');
    var controlPoint           = document.getElementById('controlPoint');
    var controlValue           = document.getElementById('controlValue');
    var remarksValue           = document.getElementById('remarksValue');
    var btnAddRule              = document.getElementById('btnAddRule');
    var btnClearForm            = document.getElementById('btnClearForm');
    var btnSaveEdit             = document.getElementById('btnSaveEdit');
    var btnCancelEdit           = document.getElementById('btnCancelEdit');
    var btnClearAll             = document.getElementById('btnClearAll');
    var btnSaveToDb             = document.getElementById('btnSaveToDb');
    var saveAlert               = document.getElementById('saveAlert');
    var ruleTableBody           = document.getElementById('ruleTableBody');
    var ruleTableWrapper        = document.getElementById('ruleTableWrapper');
    var emptyRuleMsg            = document.getElementById('emptyRuleMsg');
    var ruleCountBadge          = document.getElementById('ruleCount');
    var formAlert               = document.getElementById('formAlert');
    var formAlertMsg            = document.getElementById('formAlertMsg');
    var conditionSubDeviceCol   = document.getElementById('conditionSubDeviceCol');
    var conditionSubDevice      = document.getElementById('conditionSubDevice');
    var controlSubDeviceCol     = document.getElementById('controlSubDeviceCol');
    var controlSubDevice        = document.getElementById('controlSubDevice');
    var conditionPointCol       = document.getElementById('conditionPointCol');
    var controlPointCol         = document.getElementById('controlPointCol');

    // \u2500\u2500 \u5f9e SID \u53cd\u63a8\u6240\u5c6c Coordinator ID \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500
    function findCoordForSid(sid) {
        var hyphen = sid.indexOf('-');
        if (hyphen < 0) return 0;
        var num = parseInt(sid.substring(0, hyphen));
        if (isNaN(num)) return 0;
        var coord = allCoords.find(function (c) { return num >= c.id * 65536 && num < (c.id + 1) * 65536; });
        return coord ? coord.id : 0;
    }

    // 從 SID 反推「設備下拉值」（Modbus / DB:N / CALC / '0'=未知）
    function findDeviceValueForSid(sid) {
        if (isCalcSid(sid)) return 'CALC';
        if (isDbSid(sid))   return 'DB:' + getDbCoordIdForSid(sid);
        var nDbId = findCoordForSid(sid);
        return nDbId > 0 ? String(nDbId) : '0';
    }

    // \u2500\u2500 \u591aID\u8a2d\u5099\u5224\u65b7\u8207\u5b50\u8a2d\u5099\u8655\u7406 \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500
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

    function findSubModbusIdForSid(sid) {
        var hyphen = sid.indexOf('-');
        if (hyphen < 0) return null;
        var num = parseInt(sid.substring(0, hyphen));
        if (isNaN(num)) return null;
        return Math.floor(((num - 1) % 65536) / 256);
    }

    function getSubDeviceNameForSid(sid) {
        if (isCalcSid(sid)) return '計算點位';
        if (isDbSid(sid)) {
            var dbId = getDbCoordIdForSid(sid);
            var dbCoord = allDbCoords.find(function (c) { return c.id === dbId; });
            return dbCoord ? dbCoord.name : 'DB 來源';
        }
        var hyphen = sid.indexOf('-');
        if (hyphen < 0) return null;
        var num = parseInt(sid.substring(0, hyphen));
        if (isNaN(num)) return null;
        for (var ci = 0; ci < allCoords.length; ci++) {
            var coord = allCoords[ci];
            if (num >= coord.id * 65536 && num < (coord.id + 1) * 65536) {
                if (!isMultiIdCoord(coord)) return null;
                var subs = getSubDevices(coord);
                for (var si = 0; si < subs.length; si++) {
                    var sub = subs[si];
                    var rangeBase = coord.id * 65536 + sub.modbusId * 256;
                    if (num >= rangeBase && num < rangeBase + 256) {
                        return sub.name;
                    }
                }
                return null;
            }
        }
        return null;
    }

    function filterPointDropdownBySubDevice(selectEl, nDbId, nSubModbusId) {
        var currentVal = selectEl.value;
        while (selectEl.options.length > 1) selectEl.remove(1);
        var rangeBase = nDbId * 65536 + nSubModbusId * 256;
        var rangeEnd = rangeBase + 256;
        var pts = allPoints.filter(function (p) {
            var h = p.sid.indexOf('-');
            if (h < 0) return false;
            var num = parseInt(p.sid.substring(0, h));
            return !isNaN(num) && num >= rangeBase && num < rangeEnd;
        });
        pts.forEach(function (p) {
            var opt = new Option(p.name, p.sid);
            opt.dataset.name = p.name;
            opt.dataset.unit = p.unit;
            selectEl.add(opt);
        });
        selectEl.value = pts.some(function (p) { return p.sid === currentVal; }) ? currentVal : '';
    }

    function setupCoordinatorForSid(sid, coordSelectEl, subDeviceColEl, subDeviceSelectEl, pointSelectEl, pointColEl) {
        var deviceVal = findDeviceValueForSid(sid);
        coordSelectEl.value = deviceVal;
        var dv = parseDeviceValue(deviceVal);

        // CALC / DB 來源都沒有子設備概念
        if (dv.kind === 'calc' || dv.kind === 'db') {
            subDeviceColEl.classList.add('d-none');
            pointColEl.classList.remove('col-md-2');
            pointColEl.classList.add('col-md-4');
            filterPointDropdownByDevice(pointSelectEl, dv);
            pointSelectEl.value = sid;
            return;
        }

        var coord = allCoords.find(function (c) { return c.id === dv.id; });
        if (coord && isMultiIdCoord(coord)) {
            subDeviceColEl.classList.remove('d-none');
            pointColEl.classList.remove('col-md-4');
            pointColEl.classList.add('col-md-2');
            while (subDeviceSelectEl.options.length > 1) subDeviceSelectEl.remove(1);
            getSubDevices(coord).forEach(function (s) {
                subDeviceSelectEl.add(new Option(s.name, s.modbusId));
            });
            var subId = findSubModbusIdForSid(sid);
            if (subId !== null) {
                subDeviceSelectEl.value = subId;
                filterPointDropdownBySubDevice(pointSelectEl, dv.id, subId);
            }
        } else {
            subDeviceColEl.classList.add('d-none');
            pointColEl.classList.remove('col-md-2');
            pointColEl.classList.add('col-md-4');
            filterPointDropdownByDevice(pointSelectEl, dv);
        }
        pointSelectEl.value = sid;
    }

    // \u2500\u2500 \u8f09\u5165\u898f\u5247\u81f3\u8868\u55ae \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500
    function loadRule(nId) {
        if (editingRuleId !== null) return;
        var r = rules.find(function (x) { return x.id === nId; });
        if (!r) return;

        setupCoordinatorForSid(r.conditionPointSid, conditionCoordinator, conditionSubDeviceCol, conditionSubDevice, conditionPoint, conditionPointCol);
        conditionOperator.value = r.operator;
        conditionValue.value    = r.conditionValue;

        setupCoordinatorForSid(r.controlPointSid, controlCoordinator, controlSubDeviceCol, controlSubDevice, controlPoint, controlPointCol);
        controlValue.value = r.controlValue;

        remarksValue.value = r.remarks || '';
        hideFormAlert();

        document.querySelector('.card.shadow-sm.mb-3')
            .scrollIntoView({ behavior: 'smooth', block: 'start' });
    }

    // \u2500\u2500 \u9032\u5165 / \u96e2\u958b\u7de8\u8f2f\u6a21\u5f0f \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500
    function enterEditMode(nId) {
        var prev = editingRuleId;
        editingRuleId = null;
        loadRule(nId);
        editingRuleId = nId;

        btnAddRule.disabled   = true;
        btnClearForm.disabled = true;
        btnSaveEdit.classList.remove('d-none');
        btnCancelEdit.classList.remove('d-none');

        document.querySelectorAll('#ruleTableBody tr[data-ruleid]').forEach(function (tr) {
            tr.classList.toggle('table-warning', parseInt(tr.dataset.ruleid) === nId);
        });
    }

    function exitEditMode() {
        editingRuleId = null;
        btnAddRule.disabled   = false;
        btnClearForm.disabled = false;
        btnSaveEdit.classList.add('d-none');
        btnCancelEdit.classList.add('d-none');
        document.querySelectorAll('#ruleTableBody tr[data-ruleid]').forEach(function (tr) {
            tr.classList.remove('table-warning');
        });
        clearForm();
    }

    function saveEdit() {
        var condSid = conditionPoint.value;
        var op      = conditionOperator.value;
        var condVal = conditionValue.value.trim();
        var ctrlSid = controlPoint.value;
        var ctrlVal = controlValue.value.trim();

        if (!condSid) { showFormAlert('\u8acb\u9078\u64c7\u689d\u4ef6\u9ede\u4f4d'); return; }
        if (!condVal) { showFormAlert('\u8acb\u8f38\u5165\u689d\u4ef6\u6578\u503c'); return; }
        if (!ctrlSid) { showFormAlert('\u8acb\u9078\u64c7\u63a7\u5236\u9ede\u4f4d'); return; }
        if (!ctrlVal) { showFormAlert('\u8acb\u8f38\u5165\u63a7\u5236\u503c');   return; }
        if (condSid === ctrlSid) { showFormAlert('\u689d\u4ef6\u9ede\u4f4d\u8207\u63a7\u5236\u9ede\u4f4d\u4e0d\u80fd\u76f8\u540c'); return; }

        var condOpt  = conditionPoint.options[conditionPoint.selectedIndex];
        var ctrlOpt  = controlPoint.options[controlPoint.selectedIndex];
        var ruleIdx  = rules.findIndex(function (r) { return r.id === editingRuleId; });
        if (ruleIdx < 0) { exitEditMode(); return; }

        rules[ruleIdx] = Object.assign({}, rules[ruleIdx], {
            conditionPointSid:  condSid,
            conditionPointName: condOpt.dataset.name || condSid,
            operator:           op,
            conditionValue:     parseFloat(condVal),
            controlPointSid:    ctrlSid,
            controlPointName:   ctrlOpt.dataset.name || ctrlSid,
            controlValue:       parseFloat(ctrlVal),
            remarks:            remarksValue.value.trim().substring(0, 50)
        });

        renderRules();
        exitEditMode();
    }

    // \u2500\u2500 \u8a2d\u5099\u7be9\u9078 \u2192 \u91cd\u65b0\u586b\u5145\u9ede\u4f4d\u4e0b\u62c9\u9078\u55ae \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500
    function filterPointDropdown(selectEl, nDbId) {
        // 維持向後相容：純 Modbus coord id（0 = 全部 Modbus + 計算 + DB）
        if (nDbId <= 0) {
            filterPointDropdownByDevice(selectEl, { kind: 'all' });
            return;
        }
        filterPointDropdownByDevice(selectEl, { kind: 'modbus', id: nDbId });
    }

    // 依「設備下拉值」抽出對應點位塞回下拉
    function filterPointDropdownByDevice(selectEl, dv) {
        var currentVal = selectEl.value;
        while (selectEl.options.length > 1) selectEl.remove(1);

        var pts;
        if (dv.kind === 'calc') {
            pts = allPoints.filter(function (p) { return isCalcSid(p.sid); });
        } else if (dv.kind === 'db') {
            pts = allPoints.filter(function (p) { return isDbSid(p.sid) && getDbCoordIdForSid(p.sid) === dv.id; });
        } else if (dv.kind === 'modbus') {
            pts = allPoints.filter(function (p) {
                if (isCalcSid(p.sid) || isDbSid(p.sid)) return false;
                var hyphen = p.sid.indexOf('-');
                if (hyphen < 0) return false;
                var num = parseInt(p.sid.substring(0, hyphen));
                if (isNaN(num)) return false;
                return num >= dv.id * 65536 && num < (dv.id + 1) * 65536;
            });
        } else {
            pts = allPoints;
        }

        pts.forEach(function (p) {
            var opt = new Option(p.name, p.sid);
            opt.dataset.name = p.name;
            opt.dataset.unit = p.unit;
            selectEl.add(opt);
        });

        selectEl.value = pts.some(function (p) { return p.sid === currentVal; }) ? currentVal : '';
    }

    // \u2500\u2500 \u8868\u683c\u6e32\u67d3 \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500
    function renderRules() {
        ruleCountBadge.textContent = rules.length;

        if (rules.length === 0) {
            emptyRuleMsg.classList.remove('d-none');
            ruleTableWrapper.classList.add('d-none');
            btnClearAll.classList.add('d-none');
            btnSaveToDb.classList.remove('d-none');
            return;
        }

        emptyRuleMsg.classList.add('d-none');
        ruleTableWrapper.classList.remove('d-none');
        btnClearAll.classList.remove('d-none');
        btnSaveToDb.classList.remove('d-none');

        var opDisplay = { '>': '>', '<': '\u003c', '>=': '>\u003d', '<=': '\u2264', '==': '=', '!=': '\u2260' };

        ruleTableBody.innerHTML = rules.map(function (r, idx) {
            var condSub = getSubDeviceNameForSid(r.conditionPointSid);
            var ctrlSub = getSubDeviceNameForSid(r.controlPointSid);
            var condPrefix = condSub ? '<small class="text-muted">' + escHtml(condSub) + ' \u203a</small> ' : '';
            var ctrlPrefix = ctrlSub ? '<small class="text-muted">' + escHtml(ctrlSub) + ' \u203a</small> ' : '';
            return '' +
            '<tr data-ruleid="' + r.id + '" class="' + (r.id === editingRuleId ? 'table-warning' : '') + '"' +
            '    style="cursor:pointer" onclick="window._condCtrl.loadRule(' + r.id + ')" title="\u9ede\u64ca\u8f09\u5165\u81f3\u8868\u55ae">' +
            '    <td class="text-center text-muted">' + (idx + 1) + '</td>' +
            '    <td>' +
            '        ' + condPrefix + '<span class="fw-semibold">' + escHtml(r.conditionPointName) + '</span>' +
            '    </td>' +
            '    <td class="text-center">' +
            '        <span class="operator-badge text-primary">' + escHtml(opDisplay[r.operator] || r.operator) + '</span>' +
            '    </td>' +
            '    <td class="text-center fw-bold">' + escHtml(String(r.conditionValue)) + '</td>' +
            '    <td>' +
            '        ' + ctrlPrefix + '<span class="fw-semibold">' + escHtml(r.controlPointName) + '</span>' +
            '    </td>' +
            '    <td class="text-center fw-bold text-success">' + escHtml(String(r.controlValue)) + '</td>' +
            '    <td><small class="text-muted">' + escHtml(r.remarks || '') + '</small></td>' +
            '    <td class="text-center">' +
            '        <button class="btn btn-outline-primary btn-sm py-0 px-1 me-1" onclick="event.stopPropagation(); window._condCtrl.enterEditMode(' + r.id + ')" title="\u7de8\u8f2f">' +
            '            <i class="fas fa-edit"></i>' +
            '        </button>' +
            '        <button class="btn btn-outline-danger btn-sm py-0 px-1" onclick="event.stopPropagation(); window._condCtrl.deleteRule(' + r.id + ')" title="\u522a\u9664">' +
            '            <i class="fas fa-trash-alt"></i>' +
            '        </button>' +
            '    </td>' +
            '</tr>';
        }).join('');
    }

    // \u2500\u2500 \u65b0\u589e\u898f\u5247 \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500
    function addRule() {
        var condSid = conditionPoint.value;
        var op      = conditionOperator.value;
        var condVal = conditionValue.value.trim();
        var ctrlSid = controlPoint.value;
        var ctrlVal = controlValue.value.trim();

        if (!condSid) { showFormAlert('\u8acb\u9078\u64c7\u689d\u4ef6\u9ede\u4f4d'); return; }
        if (!condVal) { showFormAlert('\u8acb\u8f38\u5165\u689d\u4ef6\u6578\u503c'); return; }
        if (!ctrlSid) { showFormAlert('\u8acb\u9078\u64c7\u63a7\u5236\u9ede\u4f4d'); return; }
        if (!ctrlVal) { showFormAlert('\u8acb\u8f38\u5165\u63a7\u5236\u503c');   return; }
        if (condSid === ctrlSid) { showFormAlert('\u689d\u4ef6\u9ede\u4f4d\u8207\u63a7\u5236\u9ede\u4f4d\u4e0d\u80fd\u76f8\u540c'); return; }

        hideFormAlert();

        var condOpt = conditionPoint.options[conditionPoint.selectedIndex];
        var ctrlOpt = controlPoint.options[controlPoint.selectedIndex];

        rules.push({
            id:                 ++ruleSeq,
            conditionPointSid:  condSid,
            conditionPointName: condOpt.dataset.name || condSid,
            operator:           op,
            conditionValue:     parseFloat(condVal),
            controlPointSid:    ctrlSid,
            controlPointName:   ctrlOpt.dataset.name || ctrlSid,
            controlValue:       parseFloat(ctrlVal),
            remarks:            remarksValue.value.trim().substring(0, 50)
        });

        renderRules();
        clearForm();
    }

    function deleteRule(nId) {
        rules = rules.filter(function (r) { return r.id !== nId; });
        renderRules();
    }

    function clearForm() {
        conditionCoordinator.value = '0';
        controlCoordinator.value   = '0';
        conditionSubDeviceCol.classList.add('d-none');
        controlSubDeviceCol.classList.add('d-none');
        conditionPointCol.classList.remove('col-md-2');
        conditionPointCol.classList.add('col-md-4');
        controlPointCol.classList.remove('col-md-2');
        controlPointCol.classList.add('col-md-4');
        filterPointDropdown(conditionPoint, 0);
        filterPointDropdown(controlPoint,   0);
        conditionPoint.value    = '';
        controlPoint.value      = '';
        conditionOperator.value = '>';
        conditionValue.value    = '';
        controlValue.value      = '';
        remarksValue.value      = '';
        hideFormAlert();
    }

    function showFormAlert(msg) {
        formAlertMsg.textContent = msg;
        formAlert.classList.remove('d-none');
    }

    function hideFormAlert() {
        formAlert.classList.add('d-none');
    }

    function escHtml(str) {
        return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

    // \u2500\u2500 \u5132\u5b58\u898f\u5247\u81f3\u8cc7\u6599\u5eab \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500
    async function saveRulesToDb() {
        btnSaveToDb.disabled = true;
        btnSaveToDb.innerHTML = '<i class="fas fa-spinner fa-spin me-1"></i>\u5132\u5b58\u4e2d\u2026';

        var payload = rules.map(function (r) {
            return {
                ConditionPointSID: r.conditionPointSid,
                Operator:          opToInt[r.operator] || 0,
                ConditionValue:    r.conditionValue,
                ControlPointSID:   r.controlPointSid,
                ControlValue:      r.controlValue,
                Remarks:           r.remarks || null
            };
        });

        try {
            var resp = await fetch('/ConditionCtrl/SaveRules', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });
            var result = await resp.json();
            if (resp.ok && result.success) {
                showSaveAlert(result.message, 'success');
            } else {
                showSaveAlert(result.message || '\u5132\u5b58\u5931\u6557', 'danger');
            }
        } catch (e) {
            showSaveAlert('\u7db2\u8def\u932f\u8aa4\uff0c\u8acb\u7a0d\u5f8c\u518d\u8a66', 'danger');
        } finally {
            btnSaveToDb.disabled = false;
            btnSaveToDb.innerHTML = '<i class="fas fa-database me-1"></i>\u5132\u5b58\u898f\u5247\u81f3\u8cc7\u6599\u5eab';
        }
    }

    function showSaveAlert(msg, type) {
        saveAlert.className = 'alert alert-' + type + ' py-1 px-3 mb-0 small';
        saveAlert.textContent = msg;
        saveAlert.classList.remove('d-none');
        setTimeout(function () { saveAlert.classList.add('d-none'); }, 4000);
    }

    // \u2500\u2500 \u4e8b\u4ef6\u7d81\u5b9a \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500
    document.addEventListener('DOMContentLoaded', function () {

        // \u5f9e\u8cc7\u6599\u5eab\u8f09\u5165\u5df2\u5b58\u5728\u898f\u5247
        dbRules.forEach(function (d) {
            var condPt = allPoints.find(function (p) { return p.sid === d.conditionPointSid; });
            var ctrlPt = allPoints.find(function (p) { return p.sid === d.controlPointSid; });
            rules.push({
                id:                 ++ruleSeq,
                conditionPointSid:  d.conditionPointSid,
                conditionPointName: condPt ? condPt.name : d.conditionPointSid,
                operator:           d.operatorSymbol,
                conditionValue:     d.conditionValue,
                controlPointSid:    d.controlPointSid,
                controlPointName:   ctrlPt ? ctrlPt.name : d.controlPointSid,
                controlValue:       d.controlValue,
                remarks:            d.remarks || ''
            });
        });
        if (rules.length > 0) renderRules();

        function bindCoordinatorChange(coordSelectEl, subDeviceColEl, subDeviceSelectEl, pointSelectEl, pointColEl) {
            coordSelectEl.addEventListener('change', function () {
                var dv = parseDeviceValue(this.value);

                if (dv.kind === 'modbus') {
                    var coord = allCoords.find(function (c) { return c.id === dv.id; });
                    if (coord && isMultiIdCoord(coord)) {
                        subDeviceColEl.classList.remove('d-none');
                        pointColEl.classList.remove('col-md-4');
                        pointColEl.classList.add('col-md-2');
                        while (subDeviceSelectEl.options.length > 1) subDeviceSelectEl.remove(1);
                        getSubDevices(coord).forEach(function (s) { subDeviceSelectEl.add(new Option(s.name, s.modbusId)); });
                        subDeviceSelectEl.value = '';
                        while (pointSelectEl.options.length > 1) pointSelectEl.remove(1);
                        pointSelectEl.value = '';
                        return;
                    }
                }

                // CALC / DB / 單ID Modbus / 全部 → 直接刷新點位下拉
                subDeviceColEl.classList.add('d-none');
                pointColEl.classList.remove('col-md-2');
                pointColEl.classList.add('col-md-4');
                filterPointDropdownByDevice(pointSelectEl, dv);
            });
        }

        bindCoordinatorChange(conditionCoordinator, conditionSubDeviceCol, conditionSubDevice, conditionPoint, conditionPointCol);
        bindCoordinatorChange(controlCoordinator,   controlSubDeviceCol,   controlSubDevice,   controlPoint,   controlPointCol);

        conditionSubDevice.addEventListener('change', function () {
            var nDbId = parseInt(conditionCoordinator.value) || 0;
            var nSubId = parseInt(this.value);
            if (!isNaN(nSubId)) {
                filterPointDropdownBySubDevice(conditionPoint, nDbId, nSubId);
            } else {
                while (conditionPoint.options.length > 1) conditionPoint.remove(1);
                conditionPoint.value = '';
            }
        });

        controlSubDevice.addEventListener('change', function () {
            var nDbId = parseInt(controlCoordinator.value) || 0;
            var nSubId = parseInt(this.value);
            if (!isNaN(nSubId)) {
                filterPointDropdownBySubDevice(controlPoint, nDbId, nSubId);
            } else {
                while (controlPoint.options.length > 1) controlPoint.remove(1);
                controlPoint.value = '';
            }
        });

        btnAddRule.addEventListener('click', addRule);
        btnClearForm.addEventListener('click', clearForm);
        btnSaveEdit.addEventListener('click', saveEdit);
        btnCancelEdit.addEventListener('click', exitEditMode);
        btnSaveToDb.addEventListener('click', saveRulesToDb);

        btnClearAll.addEventListener('click', function () {
            if (confirm('\u78ba\u5b9a\u8981\u6e05\u9664\u6240\u6709\u898f\u5247\u55ce\uff1f')) {
                rules = [];
                renderRules();
            }
        });

        [conditionValue, controlValue].forEach(function (el) {
            el.addEventListener('keydown', function (e) { if (e.key === 'Enter') addRule(); });
        });
    });

    // \u2500\u2500 \u5c0d\u5916\u4ecb\u9762\uff08\u4f9b HTML onclick \u547c\u53eb\uff09\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500
    window._condCtrl = {
        loadRule:      loadRule,
        deleteRule:    deleteRule,
        enterEditMode: enterEditMode
    };
})();
