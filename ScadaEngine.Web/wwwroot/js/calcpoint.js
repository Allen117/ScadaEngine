(function () {
    'use strict';

    function t(key, args) { return (window.i18n && window.i18n.t) ? window.i18n.t(key, args) : key; }

    var _calcPoints = window._calcPointData || [];
    var _editMode = false;
    var _deleteSID = '';
    var _calcPointModal = null;
    var _deleteModal = null;
    var _pickerModal = null;

    // ── 點位選擇器狀態 ──
    var _pickerDevices = null;   // 設備清單
    var _pickerPoints = null;    // 點位清單
    var _pickerTargetRow = null; // 目前開啟選擇器的 <tr>
    var _pickerDevId = -1;
    var _pickerModbusId = null;
    var _pickerCalcGroup = null;
    var _pickerSelectedSid = null;
    var CALC_DEV_ID = -999;

    function init() {
        _calcPointModal = new bootstrap.Modal(document.getElementById('calcPointModal'));
        _deleteModal = new bootstrap.Modal(document.getElementById('deleteModal'));

        var formulaInput = document.getElementById('inputFormula');
        if (formulaInput) new bootstrap.Tooltip(formulaInput);

        if (window.i18n && window.i18n.ready) {
            window.i18n.ready(renderTable);
        } else {
            renderTable();
        }
    }

    // ══════════════════════════════════════
    //  資料表格
    // ══════════════════════════════════════

    function renderTable() {
        var tbody = document.getElementById('calcPointTableBody');
        var emptyState = document.getElementById('emptyState');
        var countLabel = document.getElementById('calcPointCount');

        if (_calcPoints.length === 0) {
            tbody.innerHTML = '';
            emptyState.style.display = '';
            countLabel.textContent = t('calcpoint.count.total', { n: 0 });
            return;
        }

        emptyState.style.display = 'none';
        countLabel.textContent = t('calcpoint.count.total', { n: _calcPoints.length });

        var szEnabled = t('calcpoint.status.enabled');
        var szDisabled = t('calcpoint.status.disabled');
        var szEdit = t('calcpoint.btn.edit');
        var szDelete = t('calcpoint.btn.delete');

        var html = '';
        for (var i = 0; i < _calcPoints.length; i++) {
            var p = _calcPoints[i];
            var statusBadge = p.isEnabled
                ? '<span class="badge bg-success">' + escapeHtml(szEnabled) + '</span>'
                : '<span class="badge bg-secondary">' + escapeHtml(szDisabled) + '</span>';

            html += '<tr>' +
                '<td>' + escapeHtml(p.szName) + '</td>' +
                '<td class="font-monospace small text-truncate" style="max-width:250px;" title="' + escapeHtml(p.szFormula) + '">' + escapeHtml(p.szFormula) + '</td>' +
                '<td>' + escapeHtml(p.szUnit || '') + '</td>' +
                '<td>' + escapeHtml(p.szGroupName || '') + '</td>' +
                '<td class="text-center">' + statusBadge + '</td>' +
                '<td class="small">' + escapeHtml(p.szCreatedAt || '') + '</td>' +
                '<td class="text-center">' +
                '<button class="btn btn-outline-primary btn-sm me-1" onclick="window._calcPoint.openEditModal(\'' + escapeHtml(p.szSID) + '\')">' +
                '<i class="fas fa-edit me-1"></i>' + escapeHtml(szEdit) + '</button>' +
                '<button class="btn btn-outline-danger btn-sm" onclick="window._calcPoint.openDeleteModal(\'' + escapeHtml(p.szSID) + '\')">' +
                '<i class="fas fa-trash-alt me-1"></i>' + escapeHtml(szDelete) + '</button>' +
                '</td></tr>';
        }
        tbody.innerHTML = html;
    }

    // ══════════════════════════════════════
    //  變數列操作
    // ══════════════════════════════════════

    function addVariableRow(varName, sid, pointName) {
        var tbody = document.getElementById('variableTableBody');
        var tr = document.createElement('tr');
        var szDisplayName = pointName || '';
        if (!szDisplayName && sid) {
            szDisplayName = _findPointName(sid);
        }

        var szUnset = t('calcpoint.pk.unset');
        var szSelect = t('calcpoint.btn.select');
        var szVarPlaceholder = t('calcpoint.placeholder.var_name');

        tr.innerHTML =
            '<td><input type="text" class="form-control form-control-sm var-name" value="' + escapeHtml(varName || '') + '" placeholder="' + escapeHtml(szVarPlaceholder) + '"></td>' +
            '<td>' +
            '  <div class="d-flex align-items-center gap-2">' +
            '    <input type="hidden" class="var-sid" value="' + escapeHtml(sid || '') + '">' +
            '    <span class="var-point-label flex-grow-1 small' + (szDisplayName ? '' : ' text-muted') + '">' +
                    (szDisplayName ? escapeHtml(szDisplayName) : escapeHtml(szUnset)) +
            '    </span>' +
            '    <button class="btn btn-outline-primary btn-sm flex-shrink-0" onclick="window._calcPoint.openVarPicker(this)">' +
            '      <i class="fas fa-search me-1"></i>' + escapeHtml(szSelect) +
            '    </button>' +
            '  </div>' +
            '</td>' +
            '<td class="text-center"><button class="btn btn-outline-danger btn-sm" onclick="this.closest(\'tr\').remove()">' +
            '<i class="fas fa-times"></i></button></td>';
        tbody.appendChild(tr);
    }

    function _findPointName(sid) {
        if (!_pickerPoints || !sid) return sid;
        for (var i = 0; i < _pickerPoints.length; i++) {
            if (_pickerPoints[i].szSid === sid) {
                var p = _pickerPoints[i];
                return p._deviceLabel ? p._deviceLabel + ' / ' + p.szName : p.szName;
            }
        }
        return sid;
    }

    function collectFormData() {
        var inputMappings = {};
        var rows = document.querySelectorAll('#variableTableBody tr');
        for (var i = 0; i < rows.length; i++) {
            var name = rows[i].querySelector('.var-name').value.trim();
            var sid = rows[i].querySelector('.var-sid').value;
            if (name && sid) {
                inputMappings[name] = sid;
            }
        }

        return {
            name: document.getElementById('inputName').value.trim(),
            unit: document.getElementById('inputUnit').value.trim(),
            groupName: document.getElementById('inputGroupName').value.trim(),
            formula: document.getElementById('inputFormula').value.trim(),
            inputMappings: JSON.stringify(inputMappings)
        };
    }

    // ══════════════════════════════════════
    //  新增 / 編輯 Modal
    // ══════════════════════════════════════

    function openCreateModal() {
        _editMode = false;
        document.getElementById('calcPointModalTitle').textContent = t('calcpoint.modal.add_title');
        document.getElementById('editSID').value = '';
        document.getElementById('inputName').value = '';
        document.getElementById('inputUnit').value = '';
        document.getElementById('inputGroupName').value = '';
        document.getElementById('inputFormula').value = '';
        document.getElementById('variableTableBody').innerHTML = '';
        document.getElementById('previewResult').textContent = '';
        addVariableRow('', '');
        _calcPointModal.show();
    }

    function openEditModal(sid) {
        var point = null;
        for (var i = 0; i < _calcPoints.length; i++) {
            if (_calcPoints[i].szSID === sid) { point = _calcPoints[i]; break; }
        }
        if (!point) return;

        _ensurePickerData(function () {
            _fillEditModal(point);
        });
    }

    function _fillEditModal(point) {
        _editMode = true;
        document.getElementById('calcPointModalTitle').textContent = t('calcpoint.modal.edit_title') + ' - ' + point.szSID;
        document.getElementById('editSID').value = point.szSID;
        document.getElementById('inputName').value = point.szName;
        document.getElementById('inputUnit').value = point.szUnit || '';
        document.getElementById('inputGroupName').value = point.szGroupName || '';
        document.getElementById('inputFormula').value = point.szFormula;
        document.getElementById('previewResult').textContent = '';

        document.getElementById('variableTableBody').innerHTML = '';
        try {
            var mappings = JSON.parse(point.szInputMappings);
            var keys = Object.keys(mappings);
            for (var j = 0; j < keys.length; j++) {
                addVariableRow(keys[j], mappings[keys[j]]);
            }
        } catch (e) {
            addVariableRow('', '');
        }

        _calcPointModal.show();
    }

    // ══════════════════════════════════════
    //  儲存 / 刪除
    // ══════════════════════════════════════

    function saveCalcPoint() {
        var data = collectFormData();

        if (!data.name) { alert(t('calcpoint.msg.enter_name')); return; }
        if (!data.formula) { alert(t('calcpoint.msg.enter_formula')); return; }

        var mappings = JSON.parse(data.inputMappings);
        if (Object.keys(mappings).length === 0) { alert(t('calcpoint.msg.need_variable')); return; }

        var url, body;
        if (_editMode) {
            url = '/CalcPoint/Update';
            body = {
                SID: document.getElementById('editSID').value,
                Name: data.name,
                Unit: data.unit,
                GroupName: data.groupName,
                Formula: data.formula,
                InputMappings: data.inputMappings,
                IsEnabled: true
            };
        } else {
            url = '/CalcPoint/Create';
            body = {
                Name: data.name,
                Unit: data.unit,
                GroupName: data.groupName,
                Formula: data.formula,
                InputMappings: data.inputMappings
            };
        }

        fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        })
            .then(function (res) { return res.json(); })
            .then(function (result) {
                if (result.success) {
                    _calcPointModal.hide();
                    location.reload();
                } else {
                    alert(result.message || t('calcpoint.msg.operation_failed'));
                }
            })
            .catch(function (err) {
                console.error(err);
                alert(t('calcpoint.msg.network_error'));
            });
    }

    function openDeleteModal(sid) {
        _deleteSID = sid;
        var msgEl = document.getElementById('deleteMessage');
        if (msgEl) {
            msgEl.innerHTML = t('calcpoint.modal.delete_message', { sid: escapeHtml(sid) });
        }
        _deleteModal.show();
    }

    function confirmDelete() {
        fetch('/CalcPoint/Delete', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ SID: _deleteSID })
        })
            .then(function (res) { return res.json(); })
            .then(function (result) {
                if (result.success) {
                    _deleteModal.hide();
                    location.reload();
                } else {
                    alert(result.message || t('calcpoint.msg.delete_failed_default'));
                }
            })
            .catch(function (err) {
                console.error(err);
                alert(t('calcpoint.msg.network_error'));
            });
    }

    // ══════════════════════════════════════
    //  公式預覽
    // ══════════════════════════════════════

    function previewFormula() {
        var data = collectFormData();
        if (!data.formula) { alert(t('calcpoint.msg.enter_formula_first')); return; }

        var resultSpan = document.getElementById('previewResult');
        resultSpan.textContent = t('calcpoint.msg.calculating');
        resultSpan.className = 'fw-semibold text-muted';

        fetch('/CalcPoint/Preview', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ Formula: data.formula, InputMappings: data.inputMappings })
        })
            .then(function (res) { return res.json(); })
            .then(function (result) {
                if (result.success) {
                    var unit = document.getElementById('inputUnit').value.trim();
                    resultSpan.textContent = '→ ' + result.result.toFixed(2) + (unit ? ' ' + unit : '');
                    resultSpan.className = 'fw-semibold text-success';
                } else {
                    resultSpan.textContent = '✖ ' + result.message;
                    resultSpan.className = 'fw-semibold text-danger';
                }
            })
            .catch(function () {
                resultSpan.textContent = t('calcpoint.msg.network_error');
                resultSpan.className = 'fw-semibold text-danger';
            });
    }

    // ══════════════════════════════════════
    //  點位選擇器（Picker）
    // ══════════════════════════════════════

    function openVarPicker(btnEl) {
        _pickerTargetRow = btnEl.closest('tr');
        _pickerSelectedSid = null;
        _pickerDevId = -1;
        _pickerModbusId = null;
        _pickerCalcGroup = null;
        _ensurePickerData(function () {
            _pkShowStep0();
        });
    }

    function _ensurePickerData(cb) {
        if (_pickerDevices && _pickerPoints) { cb(); return; }
        Promise.all([
            fetch('/Designer/Devices').then(function (r) { return r.json(); }),
            fetch('/Designer/Points').then(function (r) { return r.json(); })
        ]).then(function (results) {
            _pickerDevices = results[0];
            _pickerPoints = results[1];
            _enrichPointLabels();
            cb();
        }).catch(function (err) {
            alert(t('calcpoint.pk.load_failed') + '\n' + err.message);
        });
    }

    function _enrichPointLabels() {
        if (!_pickerDevices || !_pickerPoints) return;
        var szCalcDefaultLabel = t('calcpoint.pk.calc_points_label');
        _pickerPoints.forEach(function (p) {
            if (_isCalcPoint(p.szSid)) {
                p._deviceLabel = p.szGroupName || szCalcDefaultLabel;
                return;
            }
            var nPfx = _getSidPrefix(p.szSid);
            var szLabel = '';
            for (var i = 0; i < _pickerDevices.length; i++) {
                var d = _pickerDevices[i];
                if (!_isPointOfDevice(p.szSid, d.nId)) continue;
                var modbusIds = (d.szModbusID || '').split(',').map(function (s) { return s.trim(); }).filter(Boolean);
                var deviceNames = (d.szDeviceName || '').split(',').map(function (s) { return s.trim(); });
                if (modbusIds.length > 1) {
                    for (var j = 0; j < modbusIds.length; j++) {
                        var mid = parseInt(modbusIds[j], 10);
                        var base = d.nId * 65536 + mid * 256;
                        if (nPfx >= base && nPfx < base + 256) {
                            szLabel = (j < deviceNames.length && deviceNames[j]) ? deviceNames[j] : d.szName;
                            break;
                        }
                    }
                } else {
                    szLabel = d.szName;
                }
                break;
            }
            p._deviceLabel = szLabel;
        });
    }

    function _isCalcPoint(sid) { return sid && sid.indexOf('CALC-') === 0; }
    function _getSidPrefix(sid) {
        var m = sid.match(/^(\d+)-S\d+$/);
        return m ? parseInt(m[1], 10) : -1;
    }
    function _isPointOfDevice(sid, nDevId) {
        if (nDevId === CALC_DEV_ID) return _isCalcPoint(sid);
        if (_isCalcPoint(sid)) return false;
        var nPfx = _getSidPrefix(sid);
        return nPfx >= nDevId * 65536 && nPfx < (nDevId + 1) * 65536;
    }

    // ── Picker 步驟導航 ──

    function _pkShowStep0() {
        document.getElementById('cpPkStep0').style.display = '';
        document.getElementById('cpPkStep1').style.display = 'none';
        document.getElementById('cpPkStep2').style.display = 'none';
        document.getElementById('cpPkTitle').textContent = t('calcpoint.pk.title_source');
        document.getElementById('cpPkBtnConfirm').disabled = true;

        if (!_pickerModal) {
            _pickerModal = new bootstrap.Modal(document.getElementById('cpPointPickerModal'));
        }
        _pickerModal.show();
    }

    function _pkShowDeviceStep() {
        document.getElementById('cpPkStep0').style.display = 'none';
        document.getElementById('cpPkStep1').style.display = '';
        document.getElementById('cpPkStep2').style.display = 'none';
        document.getElementById('cpPkTitle').textContent = t('calcpoint.pk.title_select_device');
        _pkRenderDeviceList();
    }

    function _pkShowCalcStep() {
        _pickerDevId = CALC_DEV_ID;
        _pickerModbusId = null;
        _pickerSelectedSid = null;
        _pickerCalcGroup = null;

        var groups = _pkGetCalcGroups();
        if (groups.length > 0) {
            document.getElementById('cpPkStep0').style.display = 'none';
            document.getElementById('cpPkStep1').style.display = '';
            document.getElementById('cpPkStep2').style.display = 'none';
            document.getElementById('cpPkTitle').textContent = t('calcpoint.pk.title_select_calc_group');
            _pkRenderCalcGroupList(groups);
        } else {
            _pkShowCalcFlat();
        }
    }

    function _pkGetCalcGroups() {
        if (!_pickerPoints) return [];
        var groups = {};
        _pickerPoints.forEach(function (p) {
            if (!_isCalcPoint(p.szSid)) return;
            var g = p.szGroupName || '';
            if (!groups[g]) groups[g] = 0;
            groups[g]++;
        });
        return Object.keys(groups).filter(function (g) { return g !== ''; }).sort();
    }

    function _pkRenderCalcGroupList(groups) {
        var container = document.getElementById('cpPkDeviceList');
        var hasUngrouped = (_pickerPoints || []).some(function (p) { return _isCalcPoint(p.szSid) && !p.szGroupName; });

        var html = '';
        for (var i = 0; i < groups.length; i++) {
            var g = groups[i];
            var nPts = (_pickerPoints || []).filter(function (p) { return _isCalcPoint(p.szSid) && p.szGroupName === g; }).length;
            html += '<div class="cpPk-list-item" onclick="window._calcPoint.pkSelectCalcGroup(\'' + escapeHtml(g) + '\')">' +
                '<i class="fas fa-layer-group" style="color:#ffc107;flex-shrink:0;"></i>' +
                '<div style="flex:1;min-width:0;">' +
                '<div class="cpPk-item-name">' + escapeHtml(g) + '</div>' +
                '<div class="cpPk-item-sub">' + escapeHtml(t('calcpoint.pk.points_count', { n: nPts })) + '</div>' +
                '</div>' +
                '<i class="fas fa-chevron-right" style="color:#999;font-size:11px;"></i></div>';
        }
        if (hasUngrouped) {
            var nU = (_pickerPoints || []).filter(function (p) { return _isCalcPoint(p.szSid) && !p.szGroupName; }).length;
            html += '<div class="cpPk-list-item" onclick="window._calcPoint.pkSelectCalcGroup(\'\')">' +
                '<i class="fas fa-inbox" style="color:#6c757d;flex-shrink:0;"></i>' +
                '<div style="flex:1;min-width:0;">' +
                '<div class="cpPk-item-name">' + escapeHtml(t('calcpoint.pk.ungrouped')) + '</div>' +
                '<div class="cpPk-item-sub">' + escapeHtml(t('calcpoint.pk.points_count', { n: nU })) + '</div>' +
                '</div>' +
                '<i class="fas fa-chevron-right" style="color:#999;font-size:11px;"></i></div>';
        }
        container.innerHTML = html;
    }

    function pkSelectCalcGroup(g) {
        _pickerCalcGroup = g;
        _pickerSelectedSid = null;
        document.getElementById('cpPkDevName').textContent = g || t('calcpoint.pk.ungrouped');
        document.getElementById('cpPkDevIcon').className = 'fas fa-layer-group me-1';
        _pkShowPointList(t('calcpoint.pk.title_select_calc_point'));
    }

    function _pkShowCalcFlat() {
        _pickerCalcGroup = null;
        document.getElementById('cpPkDevName').textContent = t('calcpoint.pk.calc_points_label');
        document.getElementById('cpPkDevIcon').className = 'fas fa-calculator me-1';
        _pkShowPointList(t('calcpoint.pk.title_select_calc_point'));
    }

    // ── 設備清單 ──

    function _pkRenderDeviceList() {
        var container = document.getElementById('cpPkDeviceList');
        if (!_pickerDevices || _pickerDevices.length === 0) {
            container.innerHTML = '<div class="text-center text-muted py-4"><i class="fas fa-plug fa-2x d-block mb-2"></i>' + escapeHtml(t('calcpoint.pk.no_devices')) + '</div>';
            return;
        }

        var html = '';
        for (var i = 0; i < _pickerDevices.length; i++) {
            var d = _pickerDevices[i];
            var nPts = (_pickerPoints || []).filter(function (p) { return _isPointOfDevice(p.szSid, d.nId); }).length;
            var modbusIds = (d.szModbusID || '').split(',').map(function (s) { return s.trim(); }).filter(Boolean);
            var deviceNames = (d.szDeviceName || '').split(',').map(function (s) { return s.trim(); });

            if (modbusIds.length > 1) {
                var subHtml = '';
                for (var j = 0; j < modbusIds.length; j++) {
                    var mid = modbusIds[j];
                    var subName = j < deviceNames.length ? deviceNames[j] : '';
                    var label = subName || mid;
                    subHtml += '<div class="cpPk-list-item cpPk-sub-item" onclick="window._calcPoint.pkSelectDevice(' + d.nId + ',\'' + escapeHtml(label) + '\',' + mid + ')">' +
                        '<i class="fas fa-microchip" style="color:#0d6efd;flex-shrink:0;font-size:12px;"></i>' +
                        '<div style="flex:1;min-width:0;"><div class="cpPk-item-name">' + escapeHtml(label) + '</div></div>' +
                        '<i class="fas fa-chevron-right" style="color:#999;font-size:11px;"></i></div>';
                }
                html += '<div class="cpPk-list-item" onclick="window._calcPoint.pkToggleSub(this)" style="cursor:pointer;">' +
                    '<i class="fas fa-server" style="color:#0d6efd;flex-shrink:0;"></i>' +
                    '<div style="flex:1;min-width:0;">' +
                    '<div class="cpPk-item-name">' + escapeHtml(d.szName) + '</div>' +
                    '<div class="cpPk-item-sub">' + escapeHtml(t('calcpoint.pk.points_count', { n: nPts })) + '</div>' +
                    '</div>' +
                    '<i class="fas fa-chevron-down cpPk-toggle-icon" style="color:#999;font-size:11px;transition:transform .2s;"></i></div>' +
                    '<div class="cpPk-sub-menu" style="display:none;">' + subHtml + '</div>';
            } else {
                html += '<div class="cpPk-list-item" onclick="window._calcPoint.pkSelectDevice(' + d.nId + ',\'' + escapeHtml(d.szName) + '\')">' +
                    '<i class="fas fa-server" style="color:#0d6efd;flex-shrink:0;"></i>' +
                    '<div style="flex:1;min-width:0;">' +
                    '<div class="cpPk-item-name">' + escapeHtml(d.szName) + '</div>' +
                    '<div class="cpPk-item-sub">' + escapeHtml(t('calcpoint.pk.points_count', { n: nPts })) + '</div>' +
                    '</div>' +
                    '<i class="fas fa-chevron-right" style="color:#999;font-size:11px;"></i></div>';
            }
        }
        container.innerHTML = html;
    }

    function pkToggleSub(el) {
        var sub = el.nextElementSibling;
        var icon = el.querySelector('.cpPk-toggle-icon');
        if (sub.style.display === 'none') {
            sub.style.display = '';
            icon.style.transform = 'rotate(180deg)';
        } else {
            sub.style.display = 'none';
            icon.style.transform = '';
        }
    }

    function pkSelectDevice(nDevId, szLabel, nModbusId) {
        _pickerDevId = nDevId;
        _pickerModbusId = nModbusId != null ? nModbusId : null;
        _pickerSelectedSid = null;
        document.getElementById('cpPkDevName').textContent = szLabel || String(nDevId);
        document.getElementById('cpPkDevIcon').className = 'fas fa-server me-1';
        _pkShowPointList(t('calcpoint.pk.title_select_point'));
    }

    // ── 點位清單 ──

    function _pkShowPointList(title) {
        document.getElementById('cpPkStep0').style.display = 'none';
        document.getElementById('cpPkStep1').style.display = 'none';
        document.getElementById('cpPkStep2').style.display = '';
        document.getElementById('cpPkTitle').textContent = title;
        document.getElementById('cpPkSearch').value = '';
        document.getElementById('cpPkBtnConfirm').disabled = true;
        _pkRenderPoints('');
    }

    function _pkRenderPoints(keyword) {
        var szQ = keyword.trim().toLowerCase();
        var filtered = (_pickerPoints || []).filter(function (p) {
            if (_pickerDevId === CALC_DEV_ID) {
                if (!_isCalcPoint(p.szSid)) return false;
                if (_pickerCalcGroup != null) {
                    if ((p.szGroupName || '') !== _pickerCalcGroup) return false;
                }
            } else if (_pickerModbusId != null) {
                var nPfx = _getSidPrefix(p.szSid);
                var base = _pickerDevId * 65536 + _pickerModbusId * 256;
                if (nPfx < base || nPfx >= base + 256) return false;
            } else {
                if (!_isPointOfDevice(p.szSid, _pickerDevId)) return false;
            }
            return !szQ || p.szName.toLowerCase().indexOf(szQ) >= 0;
        });

        var container = document.getElementById('cpPkPointList');
        if (filtered.length === 0) {
            container.innerHTML = '<div class="text-center text-muted py-4"><i class="fas fa-inbox fa-2x d-block mb-2"></i>' + escapeHtml(t('calcpoint.pk.no_points')) + '</div>';
            return;
        }

        var html = '';
        for (var i = 0; i < filtered.length; i++) {
            var p = filtered[i];
            var prefix = p._deviceLabel ? '<span style="color:#0d6efd;font-size:11px;">' + escapeHtml(p._deviceLabel) + '</span><span style="color:#aaa;margin:0 4px;">/</span>' : '';
            html += '<div class="cpPk-list-item" data-sid="' + escapeHtml(p.szSid) + '" onclick="window._calcPoint.pkSelectPoint(this,\'' + escapeHtml(p.szSid) + '\')">' +
                '<i class="fas fa-circle" style="font-size:6px;color:#198754;flex-shrink:0;"></i>' +
                '<div style="flex:1;min-width:0;">' +
                '<div class="cpPk-item-name">' + prefix + escapeHtml(p.szName) + '</div>' +
                '</div>' +
                '<span class="cpPk-item-unit">' + escapeHtml(p.szUnit || '') + '</span></div>';
        }
        container.innerHTML = html;
    }

    function pkSelectPoint(el, sid) {
        var items = document.querySelectorAll('#cpPkPointList .cpPk-list-item');
        for (var i = 0; i < items.length; i++) items[i].classList.remove('selected');
        el.classList.add('selected');
        _pickerSelectedSid = sid;
        document.getElementById('cpPkBtnConfirm').disabled = false;
    }

    function pkFilterPoints(keyword) {
        _pkRenderPoints(keyword);
        _pickerSelectedSid = null;
        document.getElementById('cpPkBtnConfirm').disabled = true;
    }

    // ── Picker 確認 / 返回 ──

    function pkConfirm() {
        if (!_pickerSelectedSid || !_pickerTargetRow) return;
        var point = null;
        for (var i = 0; i < _pickerPoints.length; i++) {
            if (_pickerPoints[i].szSid === _pickerSelectedSid) { point = _pickerPoints[i]; break; }
        }
        if (!point) return;

        var szFullName = point._deviceLabel ? point._deviceLabel + ' / ' + point.szName : point.szName;

        // 更新目標列
        var hiddenInput = _pickerTargetRow.querySelector('.var-sid');
        var labelSpan = _pickerTargetRow.querySelector('.var-point-label');
        hiddenInput.value = point.szSid;
        labelSpan.textContent = szFullName;
        labelSpan.classList.remove('text-muted');

        _pickerModal.hide();
        _pickerTargetRow = null;
    }

    function pkGoBack() {
        if (_pickerDevId === CALC_DEV_ID) {
            if (_pickerCalcGroup != null) {
                _pickerCalcGroup = null;
                _pkShowCalcStep();
            } else {
                _pkShowStep0();
            }
        } else {
            _pkShowDeviceStep();
        }
    }

    function pkBackToStep0() {
        _pkShowStep0();
    }

    // ── 工具函式 ──

    function escapeHtml(str) {
        if (!str) return '';
        return String(str).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    // ── 公開 API ──
    window._calcPoint = {
        openCreateModal: openCreateModal,
        openEditModal: openEditModal,
        openDeleteModal: openDeleteModal,
        confirmDelete: confirmDelete,
        saveCalcPoint: saveCalcPoint,
        addVariableRow: function () { addVariableRow('', ''); },
        previewFormula: previewFormula,
        openVarPicker: openVarPicker,
        pkShowDeviceStep: _pkShowDeviceStep,
        pkShowCalcStep: _pkShowCalcStep,
        pkSelectDevice: pkSelectDevice,
        pkSelectCalcGroup: pkSelectCalcGroup,
        pkSelectPoint: pkSelectPoint,
        pkFilterPoints: pkFilterPoints,
        pkConfirm: pkConfirm,
        pkGoBack: pkGoBack,
        pkBackToStep0: pkBackToStep0,
        pkToggleSub: pkToggleSub
    };

    // DOM Ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
