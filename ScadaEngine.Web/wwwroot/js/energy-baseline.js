/* 能源基準（ISO 50001 基線 / EnPI / SEU）頁邏輯 */
(function () {
    'use strict';

    var g_models = [];
    var g_circuits = [];
    var g_points = [];
    var g_currentId = null;      // null = 新增中
    var g_current = null;        // 目前編輯中的模型（伺服器物件）
    var g_vars = [];             // 編輯中的 X 變數 [{varType, sourceSid, sourceCircuitId, label, unit}]
    var g_pkTarget = null;       // 'target' 或變數列 index
    var g_pkModal = null;
    var g_scatterChart = null;
    var g_enpiChart = null;
    var g_seuChart = null;
    var g_lastEnpi = null;       // 最後一次成功查詢的 EnPI 條件（匯出用）

    var MAX_VARS = 5;

    function t(key, args) { return (window.i18n && window.i18n.t) ? window.i18n.t(key, args) : key; }

    document.addEventListener('DOMContentLoaded', function () {
        var szToday = (window._enbInit && window._enbInit.today) || new Date().toISOString().slice(0, 10);
        setDefaultDates(szToday);
        g_pkModal = new bootstrap.Modal(document.getElementById('enbPointPickerModal'));
        if (window.i18n && window.i18n.ready) {
            window.i18n.ready(function () { loadAll(); });
        } else {
            loadAll();
        }
    });

    function setDefaultDates(szToday) {
        var dtToday = new Date(szToday + 'T00:00:00');
        var dtYearAgo = new Date(dtToday); dtYearAgo.setFullYear(dtYearAgo.getFullYear() - 1);
        var dtMonthAgo = new Date(dtToday); dtMonthAgo.setDate(dtMonthAgo.getDate() - 30);
        var szYearAgo = fmtDate(dtYearAgo), szMonthAgo = fmtDate(dtMonthAgo);
        document.getElementById('enbStart').value = szYearAgo;
        document.getElementById('enbEnd').value = szToday;
        document.getElementById('enpiStart').value = szMonthAgo;
        document.getElementById('enpiEnd').value = szToday;
        document.getElementById('seuStart').value = szYearAgo;
        document.getElementById('seuEnd').value = szToday;
    }

    function fmtDate(dt) {
        var m = String(dt.getMonth() + 1).padStart(2, '0');
        var d = String(dt.getDate()).padStart(2, '0');
        return dt.getFullYear() + '-' + m + '-' + d;
    }

    function loadAll() {
        Promise.all([
            fetch('/EnergyBaseline/api/circuits').then(function (r) { return r.json(); }),
            fetch('/EnergyBaseline/api/points').then(function (r) { return r.json(); })
        ]).then(function (results) {
            g_circuits = results[0];
            g_points = results[1];
            return loadModels();
        }).catch(function (err) {
            alert(t('enb.msg.load_failed') + '\n' + err.message);
        });
    }

    function loadModels() {
        return fetch('/EnergyBaseline/api/models')
            .then(function (r) { return r.json(); })
            .then(function (models) {
                g_models = models;
                renderModelList();
                renderEnpiBaselineSelect();
            });
    }

    // ============ 模型清單 ============

    function renderModelList() {
        var elList = document.getElementById('enbModelList');
        if (!g_models.length) {
            elList.innerHTML = '<div class="list-group-item text-muted small">' + escapeHtml(t('enb.model.empty')) + '</div>';
            return;
        }
        var html = '';
        g_models.forEach(function (m) {
            var szBadge = m.szStatus === 'frozen'
                ? '<span class="badge bg-success">' + escapeHtml(t('enb.model.status.frozen')) + '</span>'
                : '<span class="badge bg-secondary">' + escapeHtml(t('enb.model.status.draft')) + '</span>';
            var szGran = t(m.szGranularity === 'month' ? 'enb.model.granularity.month' : 'enb.model.granularity.day');
            html += '<div class="list-group-item enb-model-item' + (m.nId === g_currentId ? ' active' : '') + '" onclick="window._enb.selectModel(' + m.nId + ')">'
                + '<div class="d-flex justify-content-between align-items-center">'
                + '<strong>' + escapeHtml(m.szName) + '</strong>' + szBadge + '</div>'
                + '<div class="small text-muted">' + escapeHtml(m.szTargetLabel || '') + ' | ' + escapeHtml(szGran)
                + (m.dR2 != null ? ' | R² ' + m.dR2.toFixed(3) : '') + '</div>'
                + '</div>';
        });
        elList.innerHTML = html;
    }

    function selectModel(nId) {
        fetch('/EnergyBaseline/api/models/' + nId)
            .then(function (r) { return r.json(); })
            .then(function (m) {
                g_currentId = m.nId;
                g_current = m;
                fillEditor(m);
                renderModelList();
                renderStoredResult(m);
            });
    }

    function newModel() {
        g_currentId = null;
        g_current = null;
        g_vars = [{ varType: 'point', sourceSid: null, sourceCircuitId: null, label: '', unit: '' }];
        document.getElementById('enbEditorCard').style.display = '';
        document.getElementById('enbResultCard').style.display = 'none';
        document.getElementById('enbName').value = '';
        document.getElementById('enbDescription').value = '';
        document.getElementById('enbTargetTypeCircuit').checked = true;
        document.getElementById('enbGranularity').value = 'day';
        document.getElementById('enbModeCumulative').checked = true;
        document.getElementById('enbTargetPointDisplay').value = '';
        document.getElementById('enbTargetPointDisplay').dataset.sid = '';
        document.getElementById('enbTargetPointDisplay').dataset.unit = '';
        renderCircuitSelect(document.getElementById('enbTargetCircuit'), null);
        renderVarRows();
        setEditorState('draft');
        onTargetTypeChange();
        document.getElementById('enbStatusBadge').innerHTML = '';
        renderModelList();
    }

    function fillEditor(m) {
        document.getElementById('enbEditorCard').style.display = '';
        document.getElementById('enbName').value = m.szName || '';
        document.getElementById('enbDescription').value = m.szDescription || '';
        (m.szTargetType === 'point'
            ? document.getElementById('enbTargetTypePoint')
            : document.getElementById('enbTargetTypeCircuit')).checked = true;
        renderCircuitSelect(document.getElementById('enbTargetCircuit'), m.nTargetCircuitId);
        var elPt = document.getElementById('enbTargetPointDisplay');
        elPt.value = m.szTargetType === 'point' ? (m.szTargetLabel || m.szTargetSID || '') : '';
        elPt.dataset.sid = m.szTargetSID || '';
        elPt.dataset.unit = m.szTargetUnit || '';
        elPt.dataset.label = m.szTargetType === 'point' ? (m.szTargetLabel || '') : '';
        (m.szTargetMode === 'average'
            ? document.getElementById('enbModeAverage')
            : document.getElementById('enbModeCumulative')).checked = true;
        document.getElementById('enbGranularity').value = m.szGranularity || 'day';
        document.getElementById('enbStart').value = (m.dtBaselineStart || '').slice(0, 10);
        document.getElementById('enbEnd').value = (m.dtBaselineEnd || '').slice(0, 10);
        g_vars = (m.variables || []).map(function (v) {
            return { varType: v.szVarType, sourceSid: v.szSourceSID, sourceCircuitId: v.nSourceCircuitId, label: v.szLabel, unit: v.szUnit || '' };
        });
        if (!g_vars.length) g_vars = [{ varType: 'point', sourceSid: null, sourceCircuitId: null, label: '', unit: '' }];
        renderVarRows();
        setEditorState(m.szStatus);
        onTargetTypeChange();   // 最後呼叫 — Y 取樣方式 radio 的啟用狀態由此統一管理
        document.getElementById('enbStatusBadge').innerHTML = m.szStatus === 'frozen'
            ? '<span class="badge bg-success">' + escapeHtml(t('enb.model.status.frozen')) + '</span>'
            : '<span class="badge bg-secondary">' + escapeHtml(t('enb.model.status.draft')) + '</span>';
    }

    /* 凍結模型 → 表單唯讀、只留解除凍結/刪除 */
    function setEditorState(szStatus) {
        var isFrozen = szStatus === 'frozen';
        ['enbName', 'enbDescription', 'enbTargetCircuit', 'enbGranularity', 'enbStart', 'enbEnd'].forEach(function (id) {
            document.getElementById(id).disabled = isFrozen;
        });
        document.querySelectorAll('input[name="enbTargetType"]').forEach(function (el) { el.disabled = isFrozen; });
        document.getElementById('enbVarAddBtn').disabled = isFrozen;
        document.getElementById('enbSaveBtn').style.display = isFrozen ? 'none' : '';
        document.getElementById('enbRunBtn').style.display = isFrozen ? 'none' : '';
        document.getElementById('enbFreezeBtn').style.display = (!isFrozen && g_currentId != null) ? '' : 'none';
        document.getElementById('enbUnfreezeBtn').style.display = isFrozen ? '' : 'none';
        document.getElementById('enbDeleteBtn').style.display = g_currentId != null ? '' : 'none';
        document.querySelectorAll('#enbVarRows select, #enbVarRows input, #enbVarRows button').forEach(function (el) { el.disabled = isFrozen; });
    }

    // ============ Y 目標 ============

    function onTargetTypeChange() {
        var isPoint = document.getElementById('enbTargetTypePoint').checked;
        document.getElementById('enbTargetCircuit').style.display = isPoint ? 'none' : '';
        document.getElementById('enbTargetPointGroup').style.display = isPoint ? '' : 'none';
        // 迴路 Y 一律累計（kWh boundary 相減），鎖定選項
        document.querySelectorAll('input[name="enbTargetMode"]').forEach(function (el) {
            el.disabled = !isPoint || (g_current && g_current.szStatus === 'frozen');
        });
        if (!isPoint) document.getElementById('enbModeCumulative').checked = true;
    }

    function renderCircuitSelect(elSelect, nSelectedId) {
        var html = '<option value="">' + escapeHtml(t('enb.edit.select_placeholder')) + '</option>';
        g_circuits.forEach(function (c) {
            html += '<option value="' + c.id + '"' + (c.id === nSelectedId ? ' selected' : '') + '>'
                + escapeHtml(c.name) + '</option>';
        });
        elSelect.innerHTML = html;
    }

    // ============ X 變數列 ============

    function renderVarRows() {
        var elBody = document.getElementById('enbVarRows');
        var html = '';
        g_vars.forEach(function (v, i) {
            html += '<tr>'
                + '<td>' + (i + 1) + '</td>'
                + '<td><select class="form-select form-select-sm" onchange="window._enb.onVarTypeChange(' + i + ', this.value)">'
                + '<option value="point"' + (v.varType === 'point' ? ' selected' : '') + '>' + escapeHtml(t('enb.edit.var_type_point')) + '</option>'
                + '<option value="circuit"' + (v.varType === 'circuit' ? ' selected' : '') + '>' + escapeHtml(t('enb.edit.var_type_circuit')) + '</option>'
                + '</select></td>';
            if (v.varType === 'circuit') {
                html += '<td><select class="form-select form-select-sm" onchange="window._enb.onVarCircuitChange(' + i + ', this.value)" id="enbVarCircuit' + i + '"></select></td>';
            } else {
                html += '<td><div class="input-group input-group-sm">'
                    + '<input type="text" class="form-control" readonly value="' + escapeHtml(v.sourceSid || '') + '" placeholder="' + escapeHtml(t('enb.edit.select_placeholder')) + '" />'
                    + '<button class="btn btn-outline-secondary" type="button" onclick="window._enb.pickPoint(' + i + ')">' + escapeHtml(t('enb.edit.pick')) + '</button>'
                    + '</div></td>';
            }
            html += '<td><input type="text" class="form-control form-control-sm" value="' + escapeHtml(v.label || '') + '" maxlength="200" oninput="window._enb.onVarField(' + i + ', \'label\', this.value)" /></td>'
                + '<td><input type="text" class="form-control form-control-sm" value="' + escapeHtml(v.unit || '') + '" maxlength="50" oninput="window._enb.onVarField(' + i + ', \'unit\', this.value)" /></td>'
                + '<td><button class="btn btn-sm btn-outline-danger" onclick="window._enb.removeVariable(' + i + ')"><i class="fas fa-times"></i></button></td>'
                + '</tr>';
        });
        elBody.innerHTML = html;
        g_vars.forEach(function (v, i) {
            if (v.varType === 'circuit')
                renderCircuitSelect(document.getElementById('enbVarCircuit' + i), v.sourceCircuitId);
        });
    }

    function addVariable() {
        if (g_vars.length >= MAX_VARS) { alert(t('enb.msg.var_limit', { n: MAX_VARS })); return; }
        g_vars.push({ varType: 'point', sourceSid: null, sourceCircuitId: null, label: '', unit: '' });
        renderVarRows();
    }

    function removeVariable(i) {
        g_vars.splice(i, 1);
        renderVarRows();
    }

    function onVarTypeChange(i, szType) {
        g_vars[i].varType = szType;
        g_vars[i].sourceSid = null;
        g_vars[i].sourceCircuitId = null;
        renderVarRows();
    }

    function onVarCircuitChange(i, szValue) {
        var nId = szValue ? parseInt(szValue, 10) : null;
        g_vars[i].sourceCircuitId = nId;
        var circuit = g_circuits.find(function (c) { return c.id === nId; });
        if (circuit && !g_vars[i].label) {
            g_vars[i].label = circuit.name;
            g_vars[i].unit = 'kWh';
            renderVarRows();
        }
    }

    function onVarField(i, szField, szValue) {
        g_vars[i][szField] = szValue;
    }

    // ============ 點位選擇 Modal ============

    function pickPoint(target) {
        g_pkTarget = target;
        document.getElementById('enbPkSearch').value = '';
        pkFilter('');
        g_pkModal.show();
    }

    function pkFilter(szKeyword) {
        var szKey = (szKeyword || '').toLowerCase();
        var elList = document.getElementById('enbPkList');
        var html = '';
        var nShown = 0;
        for (var i = 0; i < g_points.length && nShown < 300; i++) {
            var p = g_points[i];
            var szText = (p.szSid + ' ' + p.szName + ' ' + (p.szUnit || '')).toLowerCase();
            if (szKey && szText.indexOf(szKey) < 0) continue;
            nShown++;
            html += '<div class="list-group-item list-group-item-action enb-pk-item" onclick="window._enb.pkSelect(' + i + ')">'
                + '<div class="d-flex justify-content-between align-items-center">'
                + '<div><strong>' + escapeHtml(p.szName) + '</strong>'
                + '<span class="text-muted small ms-2">' + escapeHtml(p.szSid) + (p.szUnit ? ' | ' + escapeHtml(p.szUnit) : '') + '</span></div>'
                + '<span class="badge bg-light text-dark border enb-pk-type-badge">' + escapeHtml(p.szType) + '</span>'
                + '</div></div>';
        }
        elList.innerHTML = html || '<div class="list-group-item text-muted small">' + escapeHtml(t('enb.pk.no_match')) + '</div>';
    }

    function pkSelect(nIndex) {
        var p = g_points[nIndex];
        if (g_pkTarget === 'target') {
            var elPt = document.getElementById('enbTargetPointDisplay');
            elPt.value = p.szName + ' (' + p.szSid + ')';
            elPt.dataset.sid = p.szSid;
            elPt.dataset.unit = p.szUnit || '';
            elPt.dataset.label = p.szName;
            // 依單位推斷取樣方式：kWh 類 → 累計；其餘 → 均值。使用者可覆寫。
            var isCumulative = /wh/i.test(p.szUnit || '');
            (isCumulative ? document.getElementById('enbModeCumulative')
                          : document.getElementById('enbModeAverage')).checked = true;
        } else {
            var v = g_vars[g_pkTarget];
            v.sourceSid = p.szSid;
            if (!v.label) v.label = p.szName;
            v.unit = p.szUnit || v.unit;
            renderVarRows();
        }
        g_pkModal.hide();
    }

    // ============ 儲存 / 回歸 / 凍結 / 刪除 ============

    function collectDto() {
        var isPoint = document.getElementById('enbTargetTypePoint').checked;
        var elPt = document.getElementById('enbTargetPointDisplay');
        var nCircuitId = document.getElementById('enbTargetCircuit').value;
        var circuit = g_circuits.find(function (c) { return c.id === parseInt(nCircuitId || '0', 10); });
        return {
            id: g_currentId,
            name: document.getElementById('enbName').value.trim(),
            targetType: isPoint ? 'point' : 'circuit',
            targetSid: isPoint ? (elPt.dataset.sid || null) : null,
            targetCircuitId: (!isPoint && nCircuitId) ? parseInt(nCircuitId, 10) : null,
            targetMode: document.getElementById('enbModeAverage').checked ? 'average' : 'cumulative',
            targetLabel: isPoint ? (elPt.dataset.label || elPt.value) : (circuit ? circuit.name : ''),
            targetUnit: isPoint ? (elPt.dataset.unit || null) : 'kWh',
            granularity: document.getElementById('enbGranularity').value,
            baselineStart: document.getElementById('enbStart').value,
            baselineEnd: document.getElementById('enbEnd').value,
            description: document.getElementById('enbDescription').value.trim() || null,
            variables: g_vars.map(function (v) {
                return { varType: v.varType, sourceSid: v.sourceSid, sourceCircuitId: v.sourceCircuitId, label: v.label, unit: v.unit || null };
            })
        };
    }

    function saveModel(isRun) {
        var dto = collectDto();
        if (!dto.name) { alert(t('enb.msg.need_name')); return; }
        if (!dto.baselineStart || !dto.baselineEnd) { alert(t('enb.msg.need_period')); return; }
        postJson('/EnergyBaseline/api/models', dto).then(function (res) {
            g_currentId = res.id;
            if (isRun) return runRegression(res.id);
            return loadModels().then(function () { selectModel(res.id); });
        }).catch(showError);
    }

    function runRegression(nId) {
        return postJson('/EnergyBaseline/api/models/' + nId + '/run', null).then(function (resp) {
            return loadModels().then(function () {
                selectModel(nId);
                renderRunResult(resp);
            });
        }).catch(showError);
    }

    function freezeModel() {
        if (g_currentId == null) return;
        if (!confirm(t('enb.msg.confirm_freeze'))) return;
        postJson('/EnergyBaseline/api/models/' + g_currentId + '/freeze', null)
            .then(function () { return loadModels(); })
            .then(function () { selectModel(g_currentId); })
            .catch(showError);
    }

    function unfreezeModel() {
        if (g_currentId == null) return;
        if (!confirm(t('enb.msg.confirm_unfreeze'))) return;
        postJson('/EnergyBaseline/api/models/' + g_currentId + '/unfreeze', null)
            .then(function () { return loadModels(); })
            .then(function () { selectModel(g_currentId); })
            .catch(showError);
    }

    function deleteModel() {
        if (g_currentId == null) return;
        if (!confirm(t('enb.msg.confirm_delete'))) return;
        fetch('/EnergyBaseline/api/models/' + g_currentId, { method: 'DELETE' })
            .then(function () {
                g_currentId = null;
                document.getElementById('enbEditorCard').style.display = 'none';
                document.getElementById('enbResultCard').style.display = 'none';
                return loadModels();
            })
            .catch(showError);
    }

    // ============ 回歸結果呈現 ============

    /* 從模型儲存值渲染（選取既有模型時 — 無散布圖資料） */
    function renderStoredResult(m) {
        if (m.dIntercept == null) {
            document.getElementById('enbResultCard').style.display = 'none';
            return;
        }
        var resp = {
            intercept: m.dIntercept, r2: m.dR2, adjR2: m.dAdjR2, cvRmse: m.dCvRmse,
            sampleCount: m.nSampleCount, droppedCount: null, incompleteCount: null,
            isSampleLow: m.nSampleCount != null && m.nSampleCount < (m.variables || []).length * 5,
            isR2Low: m.dR2 != null && m.dR2 < 0.5,
            variables: (m.variables || []).map(function (v) {
                return { label: v.szLabel, unit: v.szUnit, coefficient: v.dCoefficient, pValue: v.dPValue };
            }),
            scatter: []
        };
        renderRunResult(resp);
    }

    function renderRunResult(resp) {
        document.getElementById('enbResultCard').style.display = '';

        var szWarnings = '';
        if (resp.isSampleLow) szWarnings += '<div class="alert alert-warning py-2">' + escapeHtml(t('enb.result.warn_sample_low')) + '</div>';
        if (resp.isR2Low) szWarnings += '<div class="alert alert-warning py-2">' + escapeHtml(t('enb.result.warn_r2_low')) + '</div>';
        if (resp.droppedCount > 0) szWarnings += '<div class="alert alert-secondary py-2">' + escapeHtml(t('enb.result.dropped', { n: resp.droppedCount })) + '</div>';
        document.getElementById('enbResultWarnings').innerHTML = szWarnings;

        var stats = [
            { label: 'R²', value: resp.r2 != null ? resp.r2.toFixed(4) : '—', cls: resp.isR2Low ? 'enb-stat-bad' : 'enb-stat-good' },
            { label: t('enb.result.adj_r2'), value: resp.adjR2 != null ? resp.adjR2.toFixed(4) : '—', cls: '' },
            { label: 'CV(RMSE)', value: resp.cvRmse != null ? (resp.cvRmse * 100).toFixed(2) + '%' : '—', cls: '' },
            { label: t('enb.result.samples'), value: resp.sampleCount != null ? resp.sampleCount : '—', cls: resp.isSampleLow ? 'enb-stat-bad' : '' },
            { label: t('enb.result.intercept'), value: resp.intercept != null ? nf(resp.intercept, 4) : '—', cls: '' }
        ];
        document.getElementById('enbStatCards').innerHTML = stats.map(function (s) {
            return '<div class="col-6 col-md"><div class="enb-stat-card ' + s.cls + '">'
                + '<div class="enb-stat-label">' + escapeHtml(String(s.label)) + '</div>'
                + '<div class="enb-stat-value">' + escapeHtml(String(s.value)) + '</div></div></div>';
        }).join('');

        document.getElementById('enbCoefRows').innerHTML = resp.variables.map(function (v) {
            var szP = v.pValue == null ? '—' : (v.pValue < 0.001 ? '&lt;0.001' : v.pValue.toFixed(4));
            var szPCls = (v.pValue != null && v.pValue > 0.05) ? ' class="text-danger text-end"' : ' class="text-end"';
            return '<tr><td>' + escapeHtml(v.label) + (v.unit ? ' <span class="text-muted small">(' + escapeHtml(v.unit) + ')</span>' : '') + '</td>'
                + '<td class="text-end">' + (v.coefficient != null ? nf(v.coefficient, 4) : '—') + '</td>'
                + '<td' + szPCls + '>' + szP + '</td></tr>';
        }).join('');

        var szFormula = 'Y = ' + nf(resp.intercept, 4);
        resp.variables.forEach(function (v, i) {
            if (v.coefficient == null) return;
            szFormula += (v.coefficient >= 0 ? ' + ' : ' − ') + nf(Math.abs(v.coefficient), 4) + '·X' + (i + 1);
        });
        document.getElementById('enbFormula').textContent = szFormula;

        renderScatterChart(resp.scatter);
    }

    function renderScatterChart(scatter) {
        var elCanvas = document.getElementById('enbScatterChart');
        if (g_scatterChart) { g_scatterChart.destroy(); g_scatterChart = null; }
        if (!scatter || !scatter.length) {
            elCanvas.parentElement.style.display = 'none';
            return;
        }
        elCanvas.parentElement.style.display = '';
        var dMin = Infinity, dMax = -Infinity;
        scatter.forEach(function (s) {
            dMin = Math.min(dMin, s.actual, s.predicted);
            dMax = Math.max(dMax, s.actual, s.predicted);
        });
        g_scatterChart = new Chart(elCanvas, {
            type: 'scatter',
            data: {
                datasets: [
                    {
                        label: t('enb.result.scatter_series'),
                        data: scatter.map(function (s) { return { x: s.actual, y: s.predicted, label: s.label }; }),
                        backgroundColor: 'rgba(67, 160, 71, 0.65)'
                    },
                    {
                        type: 'line',
                        label: 'Y = X',
                        data: [{ x: dMin, y: dMin }, { x: dMax, y: dMax }],
                        borderColor: '#adb5bd', borderDash: [6, 4], borderWidth: 1.5,
                        pointRadius: 0, fill: false
                    }
                ]
            },
            options: {
                responsive: true, maintainAspectRatio: false,
                plugins: {
                    tooltip: {
                        callbacks: {
                            label: function (ctx) {
                                var raw = ctx.raw || {};
                                return (raw.label ? raw.label + ': ' : '') + t('enb.result.scatter_actual') + ' ' + nf(raw.x, 3)
                                    + ', ' + t('enb.result.scatter_predicted') + ' ' + nf(raw.y, 3);
                            }
                        }
                    }
                },
                scales: {
                    x: { title: { display: true, text: t('enb.result.scatter_actual') } },
                    y: { title: { display: true, text: t('enb.result.scatter_predicted') } }
                }
            }
        });
    }

    // ============ EnPI 報告 ============

    function renderEnpiBaselineSelect() {
        var elSelect = document.getElementById('enpiBaseline');
        var frozen = g_models.filter(function (m) { return m.szStatus === 'frozen'; });
        if (!frozen.length) {
            elSelect.innerHTML = '<option value="">' + escapeHtml(t('enb.enpi.no_frozen')) + '</option>';
            return;
        }
        elSelect.innerHTML = frozen.map(function (m) {
            return '<option value="' + m.nId + '">' + escapeHtml(m.szName) + ' (' + escapeHtml(m.szTargetLabel || '') + ')</option>';
        }).join('');
    }

    function collectEnpiQuery() {
        var szId = document.getElementById('enpiBaseline').value;
        if (!szId) { alert(t('enb.msg.select_baseline')); return null; }
        return {
            baselineId: parseInt(szId, 10),
            start: document.getElementById('enpiStart').value,
            end: document.getElementById('enpiEnd').value
        };
    }

    function queryEnpi() {
        var q = collectEnpiQuery();
        if (!q) return;
        postJson('/EnergyBaseline/api/enpi/query', q).then(function (r) {
            g_lastEnpi = q;
            renderEnpiResult(r);
        }).catch(showError);
    }

    function exportEnpi() {
        var q = g_lastEnpi || collectEnpiQuery();
        if (!q) return;
        fetch('/EnergyBaseline/api/enpi/export', {
            method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(q)
        }).then(function (res) {
            if (!res.ok) return res.json().then(function (e) { throw new Error(e.message || res.status); });
            var szName = 'EnPIReport.xlsx';
            var szDisp = res.headers.get('Content-Disposition') || '';
            var match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(szDisp);
            if (match) szName = decodeURIComponent(match[1]);
            return res.blob().then(function (blob) {
                var url = URL.createObjectURL(blob);
                var a = document.createElement('a');
                a.href = url; a.download = szName;
                document.body.appendChild(a); a.click(); a.remove();
                URL.revokeObjectURL(url);
            });
        }).catch(showError);
    }

    function renderEnpiResult(r) {
        document.getElementById('enpiResultArea').style.display = '';
        var szUnit = r.szTargetUnit || '';
        var cards = [
            { label: t('enb.enpi.total_actual'), value: nf(r.dTotalActual, 2) + ' ' + szUnit, cls: '' },
            { label: t('enb.enpi.total_predicted'), value: nf(r.dTotalPredicted, 2) + ' ' + szUnit, cls: '' },
            { label: t('enb.enpi.total_savings'), value: nf(r.dTotalSavings, 2) + ' ' + szUnit, cls: r.dTotalSavings >= 0 ? 'enb-stat-good' : 'enb-stat-bad' },
            { label: t('enb.enpi.overall'), value: r.dOverallEnpi != null ? r.dOverallEnpi.toFixed(4) : '—', cls: (r.dOverallEnpi != null && r.dOverallEnpi <= 1) ? 'enb-stat-good' : 'enb-stat-bad' }
        ];
        document.getElementById('enpiSummaryCards').innerHTML = cards.map(function (c) {
            return '<div class="col-6 col-md-3"><div class="enb-stat-card ' + c.cls + '">'
                + '<div class="enb-stat-label">' + escapeHtml(c.label) + '</div>'
                + '<div class="enb-stat-value">' + escapeHtml(String(c.value)) + '</div></div></div>';
        }).join('');

        var labels = r.buckets.map(function (b) { return b.szLabel; });
        if (g_enpiChart) { g_enpiChart.destroy(); g_enpiChart = null; }
        g_enpiChart = new Chart(document.getElementById('enpiChart'), {
            data: {
                labels: labels,
                datasets: [
                    {
                        type: 'bar', label: t('enb.enpi.chart_actual'),
                        data: r.buckets.map(function (b) { return b.dActual; }),
                        backgroundColor: 'rgba(67, 160, 71, 0.75)', yAxisID: 'y'
                    },
                    {
                        type: 'bar', label: t('enb.enpi.chart_predicted'),
                        data: r.buckets.map(function (b) { return b.dPredicted; }),
                        backgroundColor: 'rgba(120, 144, 156, 0.55)', yAxisID: 'y'
                    },
                    {
                        type: 'line', label: t('enb.enpi.chart_cum'),
                        data: r.buckets.map(function (b) { return b.dCumulativeSavings; }),
                        borderColor: '#ef6c00', backgroundColor: '#ef6c00',
                        yAxisID: 'y2', tension: 0.2, pointRadius: 2
                    }
                ]
            },
            options: {
                responsive: true, maintainAspectRatio: false,
                scales: {
                    y: { position: 'left', title: { display: true, text: szUnit } },
                    y2: { position: 'right', grid: { drawOnChartArea: false }, title: { display: true, text: t('enb.enpi.chart_cum') } }
                }
            }
        });

        document.getElementById('enpiRows').innerHTML = r.buckets.map(function (b) {
            if (b.isMissing) {
                return '<tr class="enb-missing-row"><td>' + escapeHtml(b.szLabel) + '</td>'
                    + '<td class="text-end" colspan="5">' + escapeHtml(t('enb.enpi.missing')) + '</td></tr>';
            }
            var szSavCls = (b.dSavings != null && b.dSavings < 0) ? ' text-danger' : '';
            return '<tr><td>' + escapeHtml(b.szLabel) + '</td>'
                + '<td class="text-end">' + nf(b.dActual, 3) + '</td>'
                + '<td class="text-end">' + nf(b.dPredicted, 3) + '</td>'
                + '<td class="text-end' + szSavCls + '">' + nf(b.dSavings, 3) + '</td>'
                + '<td class="text-end">' + nf(b.dCumulativeSavings, 3) + '</td>'
                + '<td class="text-end">' + (b.dEnpi != null ? b.dEnpi.toFixed(4) : '—') + '</td></tr>';
        }).join('');
    }

    // ============ SEU 鑑別 ============

    function querySeu() {
        var q = {
            start: document.getElementById('seuStart').value,
            end: document.getElementById('seuEnd').value,
            threshold: parseFloat(document.getElementById('seuThreshold').value) || 80
        };
        postJson('/EnergyBaseline/api/seu', q).then(renderSeuResult).catch(showError);
    }

    function renderSeuResult(r) {
        if (!r.hasSource) { alert(t('enb.seu.no_source')); return; }
        document.getElementById('seuResultArea').style.display = '';
        document.getElementById('seuChartTitle').textContent =
            t('enb.seu.title', { name: r.sourceName || t('enb.seu.all_roots'), total: nf(r.totalKwh, 0) });

        var labels = r.items.map(function (it) { return it.name; });
        if (g_seuChart) { g_seuChart.destroy(); g_seuChart = null; }
        g_seuChart = new Chart(document.getElementById('seuChart'), {
            data: {
                labels: labels,
                datasets: [
                    {
                        type: 'bar', label: 'kWh',
                        data: r.items.map(function (it) { return it.kwh; }),
                        backgroundColor: r.items.map(function (it) {
                            return it.isSeu ? 'rgba(46, 125, 50, 0.85)' : 'rgba(165, 214, 167, 0.7)';
                        }),
                        yAxisID: 'y'
                    },
                    {
                        type: 'line', label: t('enb.seu.chart_cum'),
                        data: r.items.map(function (it) { return it.cumPct; }),
                        borderColor: '#ef6c00', backgroundColor: '#ef6c00',
                        yAxisID: 'y2', tension: 0.1, pointRadius: 3
                    }
                ]
            },
            options: {
                responsive: true, maintainAspectRatio: false,
                scales: {
                    y: { position: 'left', title: { display: true, text: 'kWh' } },
                    y2: { position: 'right', min: 0, max: 100, grid: { drawOnChartArea: false }, ticks: { callback: function (v) { return v + '%'; } } }
                }
            }
        });

        document.getElementById('seuRows').innerHTML = r.items.map(function (it, i) {
            return '<tr' + (it.isSeu ? ' class="table-success"' : '') + '>'
                + '<td>' + (i + 1) + '</td>'
                + '<td>' + escapeHtml(it.name) + '</td>'
                + '<td class="text-end">' + nf(it.kwh, 2) + '</td>'
                + '<td class="text-end">' + it.pct.toFixed(2) + '%</td>'
                + '<td class="text-end">' + it.cumPct.toFixed(2) + '%</td>'
                + '<td class="text-center">' + (it.isSeu ? '<span class="badge bg-success">SEU</span>' : '') + '</td></tr>';
        }).join('');
    }

    // ============ 共用 ============

    function postJson(szUrl, body) {
        return fetch(szUrl, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: body == null ? null : JSON.stringify(body)
        }).then(function (res) {
            if (!res.ok) {
                return res.json().catch(function () { return {}; }).then(function (e) {
                    throw new Error(e.message || ('HTTP ' + res.status));
                });
            }
            return res.text().then(function (szText) { return szText ? JSON.parse(szText) : null; });
        });
    }

    function showError(err) { alert(err.message || err); }

    function nf(dValue, nDigits) {
        if (dValue == null || isNaN(dValue)) return '—';
        return Number(dValue).toLocaleString(undefined, { minimumFractionDigits: 0, maximumFractionDigits: nDigits });
    }

    function escapeHtml(szText) {
        if (szText == null) return '';
        return String(szText)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    window._enb = {
        newModel: newModel,
        selectModel: selectModel,
        saveModel: saveModel,
        freezeModel: freezeModel,
        unfreezeModel: unfreezeModel,
        deleteModel: deleteModel,
        onTargetTypeChange: onTargetTypeChange,
        addVariable: addVariable,
        removeVariable: removeVariable,
        onVarTypeChange: onVarTypeChange,
        onVarCircuitChange: onVarCircuitChange,
        onVarField: onVarField,
        pickPoint: pickPoint,
        pkFilter: pkFilter,
        pkSelect: pkSelect,
        queryEnpi: queryEnpi,
        exportEnpi: exportEnpi,
        querySeu: querySeu
    };
})();
