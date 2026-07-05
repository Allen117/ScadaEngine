// 電表/迴路設定頁邏輯（IIFE 封裝，對外掛在 window._em）
(function () {
    'use strict';

    let g_nodes = [];           // 平坦清單
    let g_sidOptions = [];      // kWh 點位下拉選項
    let g_allPoints = [];       // 全點位清單（api/points）— 電表資訊比對 / 選點器 / 詳情顯示用
    let g_selectedId = null;
    let g_modal = null;
    let g_miModal = null;       // 電表資訊 Modal
    let g_ppModal = null;       // 兩步驟選點器 Modal

    // 電表資訊 4 SID 暫存（隨 saveCircuit 一次送出，不即時落 DB）
    const MI_ROLES = [
        { key: 'voltageSid',     role: 'V',  label: '電壓' },
        { key: 'currentSid',     role: 'A',  label: '電流' },
        { key: 'powerSid',       role: 'KW', label: '功率' },
        { key: 'powerFactorSid', role: 'PF', label: '功因' }
    ];
    let g_meterInfo = emptyMeterInfo();
    let g_miSuggestions = {};   // roleKey → 候選點位（開電表資訊 Modal 時計算）

    function emptyMeterInfo() {
        return { voltageSid: null, currentSid: null, powerSid: null, powerFactorSid: null };
    }

    // ============ 初始化 ============
    document.addEventListener('DOMContentLoaded', async () => {
        g_modal = new bootstrap.Modal(document.getElementById('circuitModal'));
        g_miModal = new bootstrap.Modal(document.getElementById('meterInfoModal'));
        g_ppModal = new bootstrap.Modal(document.getElementById('emPointPickerModal'));
        document.getElementsByName('emType').forEach(r => r.addEventListener('change', updateMeterFieldsVisibility));
        const maxKwhEl = document.getElementById('emMaxKwh');
        maxKwhEl.addEventListener('input', () => { maxKwhEl.value = formatThousand(maxKwhEl.value); });
        document.getElementById('emDevice').addEventListener('change', onDeviceChange);
        document.getElementById('emSubUnit').addEventListener('change', onSubUnitChange);
        document.getElementById('emMainMeter').addEventListener('change', updateMeterInfoRowVisibility);
        document.getElementById('ppSearch').addEventListener('input', renderPickerPointList);
        await Promise.all([loadTree(), loadSidOptions(), loadAllPoints()]);
    });

    function formatThousand(v) {
        if (v == null) return '';
        const digits = String(v).replace(/\D/g, '');
        if (!digits) return '';
        return digits.replace(/\B(?=(\d{3})+(?!\d))/g, ',');
    }
    function parseThousand(v) {
        if (v == null || String(v).trim() === '') return null;
        const digits = String(v).replace(/\D/g, '');
        return digits === '' ? null : parseInt(digits, 10);
    }

    async function loadTree() {
        try {
            const res = await fetch('/EnergyMeter/api/tree');
            g_nodes = await res.json();
            renderTree();
            if (g_selectedId) renderDetail(g_selectedId);
        } catch (err) {
            console.error('[EnergyMeter] 載入樹失敗', err);
            document.getElementById('treeContainer').innerHTML =
                '<div class="text-danger small">載入失敗：' + err.message + '</div>';
        }
    }

    async function loadSidOptions() {
        try {
            const res = await fetch('/EnergyMeter/api/sids');
            g_sidOptions = await res.json();
            renderDeviceOptions();
        } catch (err) {
            console.error('[EnergyMeter] 載入 SID 清單失敗', err);
        }
    }

    async function loadAllPoints() {
        try {
            const res = await fetch('/EnergyMeter/api/points');
            g_allPoints = await res.json();
        } catch (err) {
            console.error('[EnergyMeter] 載入全點位清單失敗', err);
        }
    }

    function pointBySid(szSid) {
        if (!szSid) return null;
        return g_allPoints.find(o => o.sid === szSid) || null;
    }

    function pointLabelOf(o) {
        return [o.coordName, o.deviceName, o.name].filter(s => s).join(' - ');
    }

    // 通訊設備下拉的 value 用「source|coordName」複合鍵，避免不同來源同名設備互撞
    function deviceKeyOf(o) {
        return o.source + '|' + (o.coordName || '未指定');
    }

    function renderDeviceOptions() {
        const groups = [
            { source: 'Modbus', label: 'Modbus 通訊設備' },
            { source: 'Calculated', label: '計算點位' },
            { source: 'DB', label: 'DB 來源' }
        ];
        const sel = document.getElementById('emDevice');
        let html = '<option value="">-- 請選擇設備 --</option>';
        groups.forEach(g => {
            const names = [...new Set(g_sidOptions.filter(o => o.source === g.source)
                .map(o => o.coordName || '未指定'))].sort();
            if (names.length === 0) return;
            html += `<optgroup label="${escapeHtml(g.label)}">` +
                names.map(n => `<option value="${escapeHtml(g.source + '|' + n)}">${escapeHtml(n)}</option>`).join('') +
                '</optgroup>';
        });
        sel.innerHTML = html;
    }

    function optionsForDeviceKey(szKey) {
        return g_sidOptions.filter(o => deviceKeyOf(o) === szKey);
    }

    // list = null 時顯示等待提示並 disable；有陣列時列出點位
    function renderSidOptions(list, szWaitLabel) {
        const sel = document.getElementById('emSid');
        if (!list) {
            sel.innerHTML = `<option value="">${szWaitLabel || '-- 請先選擇設備 --'}</option>`;
            sel.disabled = true;
            return;
        }
        sel.innerHTML = '<option value="">-- 請選擇點位 --</option>' +
            list.map(o => `<option value="${escapeHtml(o.sid)}">${escapeHtml(o.name)}</option>`).join('');
        sel.disabled = false;
    }

    // 依選定設備決定子單元層：有子單元 → 顯示並等待選擇；無 → 隱藏並直接列點位
    function renderSubUnitOptions(szKey) {
        const row = document.getElementById('emSubUnitRow');
        const sel = document.getElementById('emSubUnit');
        if (!szKey) {
            row.style.display = 'none';
            sel.innerHTML = '';
            renderSidOptions(null);
            return;
        }
        const list = optionsForDeviceKey(szKey);
        const subUnits = [...new Set(list.map(o => o.deviceName || ''))].filter(s => s !== '')
            .sort((a, b) => a.localeCompare(b, undefined, { numeric: true }));
        if (subUnits.length === 0) {
            row.style.display = 'none';
            sel.innerHTML = '';
            renderSidOptions(list);
            return;
        }
        row.style.display = '';
        sel.innerHTML = '<option value="">-- 請選擇子單元 --</option>' +
            subUnits.map(s => `<option value="${escapeHtml(s)}">${escapeHtml(s)}</option>`).join('');
        renderSidOptions(null, '-- 請先選擇子單元 --');
    }

    function onDeviceChange() {
        renderSubUnitOptions(document.getElementById('emDevice').value);
    }

    function onSubUnitChange() {
        const szKey = document.getElementById('emDevice').value;
        const szSub = document.getElementById('emSubUnit').value;
        if (!szSub) {
            renderSidOptions(null, '-- 請先選擇子單元 --');
            return;
        }
        renderSidOptions(optionsForDeviceKey(szKey).filter(o => (o.deviceName || '') === szSub));
    }

    // ============ 樹渲染 ============
    function renderTree() {
        const root = document.getElementById('treeContainer');
        const roots = g_nodes.filter(n => n.parentId == null)
            .sort((a, b) => a.sortOrder - b.sortOrder);
        if (roots.length === 0) {
            root.innerHTML = '<div class="text-center text-muted py-4">' +
                '<i class="fas fa-inbox fa-3x mb-2 d-block"></i><div>尚無迴路，請點右上角新增</div></div>';
            return;
        }
        root.innerHTML = roots.map(n => renderNode(n)).join('');
        bindNodeEvents();
    }

    function renderNode(node) {
        const children = g_nodes.filter(n => n.parentId === node.id)
            .sort((a, b) => a.sortOrder - b.sortOrder);
        const isMeter = !!node.sid;
        const hasChildren = children.length > 0;
        const signBadge = node.sign === -1
            ? '<span class="em-sign-neg" title="反向：從父迴路扣減">−</span>'
            : '';
        const mainStar = node.isMainMeter
            ? '<i class="fas fa-star em-main-star" title="主要電表"></i>'
            : '';

        return `<div class="tree-item">
            <div class="tree-node ${g_selectedId === node.id ? 'active' : ''}" data-id="${node.id}">
                <i class="fas fa-caret-down tree-toggle ${hasChildren ? '' : 'invisible'}"></i>
                <i class="fas ${isMeter ? 'fa-bolt is-meter' : 'fa-folder'} tree-icon"></i>
                ${signBadge}${mainStar}
                <span class="tree-name">${escapeHtml(node.name)}</span>
                <span class="tree-actions">
                    <button title="新增子節點" onclick="event.stopPropagation();window._em.openCreateModal(${node.id})">
                        <i class="fas fa-plus"></i>
                    </button>
                    <button title="編輯" onclick="event.stopPropagation();window._em.openEditModal(${node.id})">
                        <i class="fas fa-edit"></i>
                    </button>
                    <button class="del" title="刪除" onclick="event.stopPropagation();window._em.deleteNode(${node.id})">
                        <i class="fas fa-trash-alt"></i>
                    </button>
                </span>
            </div>
            ${hasChildren ? `<div class="tree-children">${children.map(renderNode).join('')}</div>` : ''}
        </div>`;
    }

    function bindNodeEvents() {
        document.querySelectorAll('#treeContainer .tree-node').forEach(el => {
            el.addEventListener('click', () => {
                const nId = parseInt(el.dataset.id, 10);
                g_selectedId = nId;
                document.querySelectorAll('#treeContainer .tree-node').forEach(n => n.classList.remove('active'));
                el.classList.add('active');
                renderDetail(nId);
            });
        });
    }

    // ============ 詳情渲染 ============
    function renderDetail(nId) {
        const node = g_nodes.find(n => n.id === nId);
        if (!node) return;
        const isMeter = !!node.sid;
        const sidOpt = isMeter ? g_sidOptions.find(o => o.sid === node.sid) : null;
        const sidLabel = sidOpt
            ? [sidOpt.coordName, sidOpt.deviceName, sidOpt.name].filter(s => s).join(' - ')
            : (isMeter ? '⚠ 找不到對應的點位' : '');

        document.getElementById('detailTitle').innerHTML =
            `<i class="fas ${isMeter ? 'fa-bolt text-warning' : 'fa-folder text-secondary'} me-1"></i>${escapeHtml(node.name)}`;

        const isRoot = node.parentId == null;
        const signLabel = node.sign === -1
            ? '<span class="em-badge-sign-neg"><i class="fas fa-minus me-1"></i>反向（從父扣減）</span>'
            : '<span class="em-badge-sign-pos"><i class="fas fa-plus me-1"></i>正向（加入父）</span>';

        const html = `
            <div class="em-detail-row">
                <div class="em-detail-label">類型</div>
                <div class="em-detail-value">
                    ${isMeter ? '<span class="em-badge-meter"><i class="fas fa-bolt me-1"></i>實體電表</span>'
                              : '<span class="em-badge-virtual"><i class="fas fa-folder me-1"></i>虛擬迴路</span>'}
                </div>
            </div>
            <div class="em-detail-row">
                <div class="em-detail-label">名稱</div>
                <div class="em-detail-value">${escapeHtml(node.name)}</div>
            </div>
            ${isRoot ? '' : `
            <div class="em-detail-row">
                <div class="em-detail-label">貢獻方向</div>
                <div class="em-detail-value">${signLabel}</div>
            </div>`}
            ${isMeter ? `
            <div class="em-detail-row">
                <div class="em-detail-label">點位名稱</div>
                <div class="em-detail-value">${escapeHtml(sidLabel)}</div>
            </div>
            <div class="em-detail-row">
                <div class="em-detail-label">MaxKwh</div>
                <div class="em-detail-value">${node.maxKwh != null ? formatThousand(Math.trunc(node.maxKwh)) : '<span class="text-muted">未設定</span>'}</div>
            </div>
            <div class="em-detail-row">
                <div class="em-detail-label">需量計算</div>
                <div class="em-detail-value">
                    ${node.isDemandEnabled
                        ? '<span class="badge bg-success"><i class="fas fa-tachometer-alt me-1"></i>已啟用</span>'
                        : '<span class="text-muted">未啟用</span>'}
                </div>
            </div>
            <div class="em-detail-row">
                <div class="em-detail-label">主要電表</div>
                <div class="em-detail-value">
                    ${node.isMainMeter
                        ? '<span class="em-badge-main"><i class="fas fa-star me-1"></i>主要電表</span>'
                        : '<span class="text-muted">否</span>'}
                </div>
            </div>
            ${node.isMainMeter ? renderMeterInfoDetailRows(node) : ''}` : ''}
            <div class="em-detail-row">
                <div class="em-detail-label">說明</div>
                <div class="em-detail-value">${node.description ? escapeHtml(node.description) : '<span class="text-muted">無</span>'}</div>
            </div>
            <div class="mt-3 d-flex gap-2">
                <button class="btn btn-sm btn-primary" onclick="window._em.openCreateModal(${node.id})">
                    <i class="fas fa-plus me-1"></i>新增子節點
                </button>
                <button class="btn btn-sm btn-outline-primary" onclick="window._em.openEditModal(${node.id})">
                    <i class="fas fa-edit me-1"></i>編輯
                </button>
                <button class="btn btn-sm btn-outline-danger" onclick="window._em.deleteNode(${node.id})">
                    <i class="fas fa-trash-alt me-1"></i>刪除
                </button>
            </div>`;
        document.getElementById('detailArea').innerHTML = html;
    }

    // 詳情區：主要電表的 電壓/電流/功率/功因 綁定列
    function renderMeterInfoDetailRows(node) {
        return MI_ROLES.map(r => {
            const szSid = node[r.key];
            const opt = pointBySid(szSid);
            const szVal = szSid
                ? (opt ? `${escapeHtml(pointLabelOf(opt))}${opt.unit ? ' <span class="text-muted small">(' + escapeHtml(opt.unit) + ')</span>' : ''}`
                       : '⚠ 找不到對應的點位（' + escapeHtml(szSid) + '）')
                : '<span class="text-muted">未設定</span>';
            return `<div class="em-detail-row">
                <div class="em-detail-label">${r.label}點位</div>
                <div class="em-detail-value">${szVal}</div>
            </div>`;
        }).join('');
    }

    // ============ Modal 開啟 ============
    function openCreateModal(parentId) {
        document.getElementById('modalTitle').textContent = parentId == null ? '新增點表/迴路' : '新增子節點';
        document.getElementById('emId').value = '';
        document.getElementById('emParentId').value = parentId == null ? '' : parentId;
        document.getElementById('emName').value = '';
        document.getElementById('emDevice').value = '';
        renderSubUnitOptions('');
        document.getElementById('emMaxKwh').value = formatThousand(1000000000);
        document.getElementById('emDemandEnabled').checked = false;
        document.getElementById('emMainMeter').checked = false;
        document.getElementById('emDesc').value = '';
        document.getElementById('emTypeVirtual').checked = true;
        document.getElementById('emSignPos').checked = true;
        g_meterInfo = emptyMeterInfo();
        updateMeterFieldsVisibility();
        updateSignRowVisibility(parentId);
        updateMeterInfoRowVisibility();
        g_modal.show();
    }

    function openEditModal(nId) {
        const node = g_nodes.find(n => n.id === nId);
        if (!node) return;
        document.getElementById('modalTitle').textContent = '編輯：' + node.name;
        document.getElementById('emId').value = node.id;
        document.getElementById('emParentId').value = node.parentId == null ? '' : node.parentId;
        document.getElementById('emName').value = node.name;
        const opt = node.sid ? g_sidOptions.find(o => o.sid === node.sid) : null;
        if (opt) {
            const szKey = deviceKeyOf(opt);
            document.getElementById('emDevice').value = szKey;
            renderSubUnitOptions(szKey);
            const szSub = opt.deviceName || '';
            if (szSub) {
                document.getElementById('emSubUnit').value = szSub;
                onSubUnitChange();
            }
            document.getElementById('emSid').value = node.sid;
        } else {
            document.getElementById('emDevice').value = '';
            renderSubUnitOptions('');
        }
        document.getElementById('emMaxKwh').value = node.maxKwh == null ? '' : formatThousand(Math.trunc(node.maxKwh));
        document.getElementById('emDemandEnabled').checked = !!node.isDemandEnabled;
        document.getElementById('emMainMeter').checked = !!node.isMainMeter;
        document.getElementById('emDesc').value = node.description || '';
        if (node.sid) document.getElementById('emTypeMeter').checked = true;
        else document.getElementById('emTypeVirtual').checked = true;
        if (node.sign === -1) document.getElementById('emSignNeg').checked = true;
        else document.getElementById('emSignPos').checked = true;
        g_meterInfo = {
            voltageSid: node.voltageSid || null,
            currentSid: node.currentSid || null,
            powerSid: node.powerSid || null,
            powerFactorSid: node.powerFactorSid || null
        };
        updateMeterFieldsVisibility();
        updateSignRowVisibility(node.parentId);
        updateMeterInfoRowVisibility();
        g_modal.show();
    }

    function updateMeterFieldsVisibility() {
        const isMeter = document.getElementById('emTypeMeter').checked;
        document.getElementById('emMeterFields').style.display = isMeter ? '' : 'none';
        updateMeterInfoRowVisibility();
    }

    // 「電表資訊設定」按鈕：實體電表 + 勾選主要電表才顯示
    function updateMeterInfoRowVisibility() {
        const isMeter = document.getElementById('emTypeMeter').checked;
        const isMain = document.getElementById('emMainMeter').checked;
        document.getElementById('emMeterInfoRow').style.display = (isMeter && isMain) ? '' : 'none';
        updateMeterInfoSummary();
    }

    function updateMeterInfoSummary() {
        const nBound = MI_ROLES.filter(r => g_meterInfo[r.key]).length;
        document.getElementById('emMeterInfoSummary').textContent =
            nBound === 0 ? '電壓/電流/功率/功因 點位綁定（尚未設定）'
                         : `電壓/電流/功率/功因 點位綁定（已綁定 ${nBound}/4）`;
    }

    function updateSignRowVisibility(parentId) {
        // 根節點（無父）不可選反向 — 隱藏整列並強制 +
        const signRow = document.getElementById('emSignRow');
        if (parentId == null) {
            signRow.style.display = 'none';
            document.getElementById('emSignPos').checked = true;
        } else {
            signRow.style.display = '';
        }
    }

    // ============ 儲存 ============
    async function saveCircuit() {
        const szId = document.getElementById('emId').value;
        const szParentId = document.getElementById('emParentId').value;
        const szName = document.getElementById('emName').value.trim();
        const isMeter = document.getElementById('emTypeMeter').checked;
        const szSid = isMeter ? document.getElementById('emSid').value : '';
        const nMaxKwh = isMeter ? parseThousand(document.getElementById('emMaxKwh').value) : null;
        const isDemandEnabled = isMeter && document.getElementById('emDemandEnabled').checked;
        const isMainMeter = isMeter && document.getElementById('emMainMeter').checked;
        const szDesc = document.getElementById('emDesc').value;
        const isRoot = szParentId === '';
        const nSign = isRoot ? 1 : (document.getElementById('emSignNeg').checked ? -1 : 1);

        if (!szName) { alert('請輸入名稱'); return; }
        if (isMeter && !szSid) { alert('實體電表必須選擇 SID'); return; }

        const dto = {
            name: szName,
            sid: szSid || null,
            maxKwh: nMaxKwh,
            sign: nSign,
            isDemandEnabled: isDemandEnabled,
            isMainMeter: isMainMeter,
            voltageSid: isMainMeter ? g_meterInfo.voltageSid : null,
            currentSid: isMainMeter ? g_meterInfo.currentSid : null,
            powerSid: isMainMeter ? g_meterInfo.powerSid : null,
            powerFactorSid: isMainMeter ? g_meterInfo.powerFactorSid : null,
            description: szDesc || null
        };

        try {
            let res;
            if (szId) {
                res = await fetch(`/EnergyMeter/api/tree/${szId}`, {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(dto)
                });
            } else {
                dto.parentId = szParentId === '' ? null : parseInt(szParentId, 10);
                res = await fetch('/EnergyMeter/api/tree', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(dto)
                });
            }
            if (!res.ok) {
                const err = await res.json().catch(() => ({}));
                throw new Error(err.message || res.statusText);
            }
            g_modal.hide();
            await loadTree();
        } catch (err) {
            alert('儲存失敗：' + err.message);
        }
    }

    // ============ 刪除 ============
    async function deleteNode(nId) {
        const node = g_nodes.find(n => n.id === nId);
        if (!node) return;
        if (!confirm(`確定要刪除「${node.name}」嗎？`)) return;

        try {
            let res = await fetch(`/EnergyMeter/api/tree/${nId}`, { method: 'DELETE' });
            if (res.status === 409) {
                if (!confirm('此迴路含有子節點，刪除會一併移除所有子孫，確定繼續？')) return;
                res = await fetch(`/EnergyMeter/api/tree/${nId}?force=true`, { method: 'DELETE' });
            }
            if (!res.ok) {
                const err = await res.json().catch(() => ({}));
                throw new Error(err.message || res.statusText);
            }
            if (g_selectedId === nId) g_selectedId = null;
            await loadTree();
            if (!g_selectedId) {
                document.getElementById('detailTitle').innerHTML = '<i class="fas fa-hand-pointer me-1"></i>請從左側選取迴路';
                document.getElementById('detailArea').innerHTML =
                    '<div class="text-center text-muted py-5"><i class="fas fa-sitemap fa-4x mb-3 d-block" style="opacity:.3"></i><p>選取左側的節點即可檢視/編輯詳細資訊</p></div>';
            }
        } catch (err) {
            alert('刪除失敗：' + err.message);
        }
    }

    // ============ 電表資訊設定 Modal（電壓/電流/功率/功因）============
    // Modal 疊層採循序切換：circuitModal → meterInfoModal → picker，不疊窗

    function openMeterInfoModal() {
        g_modal.hide();
        computeSuggestions();
        renderMeterInfoModal();
        g_miModal.show();
    }

    function closeMeterInfoModal() {
        g_miModal.hide();
        updateMeterInfoSummary();
        g_modal.show();
    }

    function renderMeterInfoModal() {
        renderSuggestBox();
        renderBindingList();
    }

    // ── 四列綁定渲染 ──
    function renderBindingList() {
        const box = document.getElementById('miBindingList');
        box.innerHTML = MI_ROLES.map(r => {
            const szSid = g_meterInfo[r.key];
            const opt = pointBySid(szSid);
            const szVal = szSid
                ? (opt ? `<span class="mi-point-name">${escapeHtml(pointLabelOf(opt))}</span>` +
                         (opt.unit ? ` <span class="mi-point-unit">${escapeHtml(opt.unit)}</span>` : '')
                       : `<span class="text-warning">⚠ ${escapeHtml(szSid)}（點位不存在）</span>`)
                : '<span class="text-muted">未設定</span>';
            return `<div class="mi-binding-row">
                <div class="mi-binding-label">${r.label}</div>
                <div class="mi-binding-value">${szVal}</div>
                <button type="button" class="btn btn-sm btn-outline-primary py-0" onclick="window._em.openPointPicker('${r.key}')">
                    <i class="fas fa-crosshairs me-1"></i>選擇
                </button>
            </div>`;
        }).join('');
    }

    // ── 自動比對（依度數點位名稱 + ROLE_ALIASES 別名表）──
    // 候選範圍 = 與度數點位相同 source+coordName+deviceName 的點位（排除度數點位本身）
    function computeSuggestions() {
        g_miSuggestions = {};
        const szKwhSid = document.getElementById('emSid').value;
        const kwhOpt = szKwhSid ? g_allPoints.find(o => o.sid === szKwhSid) : null;
        if (!kwhOpt || !window._roleAliases) return;

        const scope = g_allPoints.filter(o =>
            o.sid !== kwhOpt.sid &&
            o.source === kwhOpt.source &&
            o.coordName === kwhOpt.coordName &&
            o.deviceName === kwhOpt.deviceName);
        if (scope.length === 0) return;

        const szStem = kwhStemOf(kwhOpt.name);

        MI_ROLES.forEach(r => {
            let best = null, nBestScore = 0;
            scope.forEach(o => {
                const nScore = scorePointForRole(o, r, szStem);
                if (nScore > nBestScore) { nBestScore = nScore; best = o; } // 同分取清單序最小（先到先贏）
            });
            if (best) g_miSuggestions[r.key] = best;
        });
    }

    // 度數點名 stem = 去掉 KWH 組別名尾綴（含尾端分隔符）；無法去尾則空字串
    function kwhStemOf(szName) {
        if (!szName || !window._roleAliases || !window._roleAliases.KWH) return '';
        const szLower = szName.toLowerCase();
        const aliases = window._roleAliases.KWH.slice().sort((a, b) => b.length - a.length);
        for (const alias of aliases) {
            if (szLower.endsWith(alias) && szLower.length > alias.length) {
                return szName.substring(0, szName.length - alias.length).replace(/[-_\s]+$/, '');
            }
        }
        return '';
    }

    // 角色單位命中表：電壓 V、電流 A、功率 kW/W（排除 kWh/Wh — 用完整比對天然排除）、功因 PF/%/空白
    function unitHitsRole(szUnit, szRole) {
        const u = (szUnit || '').trim().toLowerCase();
        switch (szRole) {
            case 'V':  return u === 'v';
            case 'A':  return u === 'a';
            case 'KW': return u === 'kw' || u === 'w' || u === 'mw';
            case 'PF': return u === 'pf' || u === '%' || u === '';
            default:   return false;
        }
    }

    // 評分規則（plan 決策 3）：尾段命中 +3 / 整體命中 +3 / 包含（alias≥2 字）+2 / 單位命中 +2 / 含 stem +1
    function scorePointForRole(o, roleDef, szStem) {
        const aliases = (window._roleAliases && window._roleAliases[roleDef.role]) || [];
        const szNameLower = (o.name || '').toLowerCase();
        if (!szNameLower) return 0;
        let nScore = 0;

        const segs = szNameLower.split(/[-_\s]+/).filter(s => s);
        const szLastSeg = segs.length > 0 ? segs[segs.length - 1] : '';
        if (szLastSeg && aliases.includes(szLastSeg)) nScore += 3;          // 尾段命中
        if (aliases.includes(szNameLower)) nScore += 3;                      // 整體命中（點名就是角色）
        if (aliases.some(a => a.length >= 2 && szNameLower.includes(a))) nScore += 2; // 包含（防單字母誤中）
        if (unitHitsRole(o.unit, roleDef.role)) nScore += 2;                 // 單位命中
        if (szStem && szNameLower.includes(szStem.toLowerCase())) nScore += 1; // 含度數 stem（同設備多迴路時挑同名組）

        return nScore;
    }

    // ── 建議區塊渲染 ──
    function renderSuggestBox() {
        const box = document.getElementById('miSuggestBox');
        const pending = MI_ROLES.filter(r => g_miSuggestions[r.key] && g_meterInfo[r.key] !== g_miSuggestions[r.key].sid);
        if (pending.length === 0) {
            box.style.display = 'none';
            return;
        }
        box.style.display = '';
        document.getElementById('miSuggestList').innerHTML = pending.map(r => {
            const o = g_miSuggestions[r.key];
            return `<div class="mi-suggest-row">
                <span class="mi-binding-label">${r.label}</span>
                <span class="mi-suggest-arrow">&#x2192;</span>
                <span class="mi-point-name">${escapeHtml(pointLabelOf(o))}</span>
                ${o.unit ? `<span class="mi-point-unit">${escapeHtml(o.unit)}</span>` : ''}
                <button type="button" class="btn btn-sm btn-outline-success py-0 ms-auto" onclick="window._em.applySuggestion('${r.key}')">
                    <i class="fas fa-check me-1"></i>帶入
                </button>
            </div>`;
        }).join('');
    }

    function applySuggestion(szRoleKey) {
        const o = g_miSuggestions[szRoleKey];
        if (o) g_meterInfo[szRoleKey] = o.sid;
        renderMeterInfoModal();
    }

    function applyAllSuggestions() {
        MI_ROLES.forEach(r => {
            if (g_miSuggestions[r.key]) g_meterInfo[r.key] = g_miSuggestions[r.key].sid;
        });
        renderMeterInfoModal();
    }

    // ============ 兩步驟點位選擇器（設備 → 點位清單＋搜尋）============
    let g_pp = null; // { roleKey, deviceKey, subUnit, pickedSid }

    // 全點位版本的設備複合鍵（同 deviceKeyOf 規則，對 g_allPoints 使用）
    function ppDeviceKeyOf(o) {
        return o.source + '|' + (o.coordName || '未指定');
    }

    function openPointPicker(szRoleKey) {
        const roleDef = MI_ROLES.find(r => r.key === szRoleKey);
        g_pp = { roleKey: szRoleKey, deviceKey: null, subUnit: null, pickedSid: g_meterInfo[szRoleKey] || null };

        // 已綁定 → 預先定位到該點位的設備/子單元
        const bound = pointBySid(g_pp.pickedSid);
        if (bound) {
            g_pp.deviceKey = ppDeviceKeyOf(bound);
            g_pp.subUnit = bound.deviceName || null;
        }

        document.getElementById('ppTitle').textContent = `選擇${roleDef ? roleDef.label : ''}點位`;
        document.getElementById('ppSearch').value = '';
        g_miModal.hide();
        renderPickerDeviceList();
        renderPickerPointList();
        g_ppModal.show();
    }

    // bConfirm=true 時把選取寫回暫存；一律回到電表資訊 Modal
    function closePointPicker(bConfirm) {
        if (bConfirm && g_pp && g_pp.pickedSid) {
            g_meterInfo[g_pp.roleKey] = g_pp.pickedSid;
        }
        g_ppModal.hide();
        renderMeterInfoModal();
        g_miModal.show();
    }

    // 清除該列綁定並返回
    function clearPickedPoint() {
        if (g_pp) g_meterInfo[g_pp.roleKey] = null;
        g_ppModal.hide();
        renderMeterInfoModal();
        g_miModal.show();
    }

    // ── 步驟 1：設備清單（多子單元可展開）──
    function renderPickerDeviceList() {
        const box = document.getElementById('ppDeviceList');
        const groups = [
            { source: 'Modbus', label: 'Modbus 通訊設備' },
            { source: 'Calculated', label: '計算點位' },
            { source: 'DB', label: 'DB 來源' }
        ];
        let html = '';
        groups.forEach(g => {
            const names = [...new Set(g_allPoints.filter(o => o.source === g.source)
                .map(o => o.coordName || '未指定'))].sort();
            if (names.length === 0) return;
            html += `<div class="pp-group-label">${escapeHtml(g.label)}</div>`;
            names.forEach(n => {
                const szKey = g.source + '|' + n;
                const list = g_allPoints.filter(o => ppDeviceKeyOf(o) === szKey);
                const subUnits = [...new Set(list.map(o => o.deviceName || ''))].filter(s => s !== '')
                    .sort((a, b) => a.localeCompare(b, undefined, { numeric: true }));
                const isOpen = g_pp.deviceKey === szKey;
                html += `<div class="pp-device-item ${isOpen && !subUnits.length ? 'selected' : ''} ${isOpen ? 'open' : ''}"
                              data-key="${escapeHtml(szKey)}">
                    <i class="fas ${subUnits.length ? (isOpen ? 'fa-caret-down' : 'fa-caret-right') : 'fa-server'} me-1"></i>${escapeHtml(n)}
                </div>`;
                if (subUnits.length && isOpen) {
                    html += subUnits.map(s =>
                        `<div class="pp-subunit-item ${g_pp.subUnit === s ? 'selected' : ''}"
                              data-key="${escapeHtml(szKey)}" data-sub="${escapeHtml(s)}">
                            <i class="fas fa-microchip me-1"></i>${escapeHtml(s)}
                        </div>`).join('');
                }
            });
        });
        box.innerHTML = html || '<div class="text-muted small p-2">尚無點位資料</div>';

        box.querySelectorAll('.pp-device-item').forEach(el => {
            el.addEventListener('click', () => {
                const szKey = el.dataset.key;
                const hasSub = g_allPoints.some(o => ppDeviceKeyOf(o) === szKey && (o.deviceName || '') !== '');
                if (g_pp.deviceKey === szKey && hasSub) {
                    g_pp.deviceKey = null;   // 再點一次收合
                } else {
                    g_pp.deviceKey = szKey;
                }
                g_pp.subUnit = null;
                renderPickerDeviceList();
                renderPickerPointList();
            });
        });
        box.querySelectorAll('.pp-subunit-item').forEach(el => {
            el.addEventListener('click', () => {
                g_pp.deviceKey = el.dataset.key;
                g_pp.subUnit = el.dataset.sub;
                renderPickerDeviceList();
                renderPickerPointList();
            });
        });
    }

    // ── 步驟 2：點位清單（搜尋過濾 + 已綁高亮）──
    function renderPickerPointList() {
        if (!g_pp) return;
        const box = document.getElementById('ppPointList');
        const btnConfirm = document.getElementById('ppConfirmBtn');
        if (!g_pp.deviceKey) {
            box.innerHTML = '<div class="text-muted small p-2">請先選擇左側設備</div>';
            btnConfirm.disabled = !g_pp.pickedSid;
            return;
        }
        const hasSub = g_allPoints.some(o => ppDeviceKeyOf(o) === g_pp.deviceKey && (o.deviceName || '') !== '');
        if (hasSub && !g_pp.subUnit) {
            box.innerHTML = '<div class="text-muted small p-2">請先選擇子單元</div>';
            btnConfirm.disabled = !g_pp.pickedSid;
            return;
        }

        const szFilter = document.getElementById('ppSearch').value.trim().toLowerCase();
        let list = g_allPoints.filter(o => ppDeviceKeyOf(o) === g_pp.deviceKey &&
            (!hasSub || (o.deviceName || '') === g_pp.subUnit));
        if (szFilter) {
            list = list.filter(o =>
                (o.name || '').toLowerCase().includes(szFilter) ||
                (o.sid || '').toLowerCase().includes(szFilter));
        }

        if (list.length === 0) {
            box.innerHTML = '<div class="text-muted small p-2">無符合的點位</div>';
        } else {
            box.innerHTML = list.map(o =>
                `<div class="point-list-item ${g_pp.pickedSid === o.sid ? 'selected' : ''}" data-sid="${escapeHtml(o.sid)}">
                    <div>
                        <div class="point-name">${escapeHtml(o.name)}</div>
                        <div class="point-sid">${escapeHtml(o.sid)}</div>
                    </div>
                    ${o.unit ? `<span class="point-unit">${escapeHtml(o.unit)}</span>` : ''}
                </div>`).join('');
            box.querySelectorAll('.point-list-item').forEach(el => {
                el.addEventListener('click', () => {
                    g_pp.pickedSid = el.dataset.sid;
                    box.querySelectorAll('.point-list-item').forEach(n => n.classList.remove('selected'));
                    el.classList.add('selected');
                    btnConfirm.disabled = false;
                });
            });
            const sel = box.querySelector('.point-list-item.selected');
            if (sel) sel.scrollIntoView({ block: 'nearest' });
        }
        btnConfirm.disabled = !g_pp.pickedSid;
    }

    // ============ 工具 ============
    function escapeHtml(s) {
        if (s == null) return '';
        return String(s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;')
            .replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#039;');
    }

    // 對外介面
    window._em = {
        openCreateModal, openEditModal, saveCircuit, deleteNode,
        openMeterInfoModal, closeMeterInfoModal,
        applySuggestion, applyAllSuggestions,
        openPointPicker, closePointPicker, clearPickedPoint
    };
})();
