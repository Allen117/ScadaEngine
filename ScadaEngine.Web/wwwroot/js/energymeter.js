// 電表/迴路設定頁邏輯（IIFE 封裝，對外掛在 window._em）
(function () {
    'use strict';

    let g_nodes = [];           // 平坦清單
    let g_sidOptions = [];      // kWh 點位下拉選項
    let g_sidKwOptions = [];    // kW 點位下拉選項（需量計算用）
    let g_selectedId = null;
    let g_modal = null;

    // ============ 初始化 ============
    document.addEventListener('DOMContentLoaded', async () => {
        g_modal = new bootstrap.Modal(document.getElementById('circuitModal'));
        document.getElementsByName('emType').forEach(r => r.addEventListener('change', updateMeterFieldsVisibility));
        const maxKwhEl = document.getElementById('emMaxKwh');
        maxKwhEl.addEventListener('input', () => { maxKwhEl.value = formatThousand(maxKwhEl.value); });
        document.getElementById('emDevice').addEventListener('change', onDeviceChange);
        document.getElementById('emDemandEnabled').addEventListener('change', onDemandEnabledChange);
        await Promise.all([loadTree(), loadSidOptions(), loadSidKwOptions()]);
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

    async function loadSidKwOptions() {
        try {
            const res = await fetch('/EnergyMeter/api/sids-kw');
            g_sidKwOptions = await res.json();
        } catch (err) {
            console.error('[EnergyMeter] 載入 kW SID 清單失敗', err);
        }
    }

    function renderDemandSidOptions(selectedSid) {
        const sel = document.getElementById('emDemandSid');
        const devices = [...new Set(g_sidKwOptions.map(o => o.deviceName || '未指定'))].sort();
        sel.innerHTML = '<option value="">—— 請選擇功率點位 ——</option>' +
            devices.map(dev => {
                const opts = g_sidKwOptions.filter(o => (o.deviceName || '未指定') === dev);
                return `<optgroup label="${escapeHtml(dev)}">${
                    opts.map(o => `<option value="${escapeHtml(o.sid)}"${o.sid === selectedSid ? ' selected' : ''}>${escapeHtml(o.name)}</option>`).join('')
                }</optgroup>`;
            }).join('');
    }

    function onDemandEnabledChange() {
        document.getElementById('emDemandSidRow').style.display =
            document.getElementById('emDemandEnabled').checked ? '' : 'none';
    }

    function renderDeviceOptions() {
        const devices = [...new Set(g_sidOptions.map(o => o.deviceName || '未指定'))].sort();
        const sel = document.getElementById('emDevice');
        sel.innerHTML = '<option value="">-- 請選擇設備 --</option>' +
            devices.map(d => `<option value="${escapeHtml(d)}">${escapeHtml(d)}</option>`).join('');
    }

    function renderSidOptionsForDevice(szDeviceName) {
        const sel = document.getElementById('emSid');
        if (!szDeviceName) {
            sel.innerHTML = '<option value="">-- 請先選擇設備 --</option>';
            sel.disabled = true;
            return;
        }
        const list = g_sidOptions.filter(o => (o.deviceName || '未指定') === szDeviceName);
        sel.innerHTML = '<option value="">-- 請選擇點位 --</option>' +
            list.map(o => `<option value="${escapeHtml(o.sid)}">${escapeHtml(o.name)}</option>`).join('');
        sel.disabled = false;
    }

    function onDeviceChange() {
        const szDev = document.getElementById('emDevice').value;
        renderSidOptionsForDevice(szDev);
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

        return `<div class="tree-item">
            <div class="tree-node ${g_selectedId === node.id ? 'active' : ''}" data-id="${node.id}">
                <i class="fas fa-caret-down tree-toggle ${hasChildren ? '' : 'invisible'}"></i>
                <i class="fas ${isMeter ? 'fa-bolt is-meter' : 'fa-folder'} tree-icon"></i>
                ${signBadge}
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
            ? (sidOpt.deviceName ? `${sidOpt.deviceName} - ${sidOpt.name}` : sidOpt.name)
            : (isMeter ? '⚠ 找不到對應的點位' : '');
        const demandOpt = isMeter && node.demandSid ? g_sidKwOptions.find(o => o.sid === node.demandSid) : null;
        const demandLabel = demandOpt
            ? (demandOpt.deviceName ? `${demandOpt.deviceName} - ${demandOpt.name}` : demandOpt.name)
            : (node.demandSid || '');

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
            ${node.demandSid ? `
            <div class="em-detail-row">
                <div class="em-detail-label">需量計算點位</div>
                <div class="em-detail-value">${escapeHtml(demandLabel)}</div>
            </div>` : ''}` : ''}
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

    // ============ Modal 開啟 ============
    function openCreateModal(parentId) {
        document.getElementById('modalTitle').textContent = parentId == null ? '新增點表/迴路' : '新增子節點';
        document.getElementById('emId').value = '';
        document.getElementById('emParentId').value = parentId == null ? '' : parentId;
        document.getElementById('emName').value = '';
        document.getElementById('emDevice').value = '';
        renderSidOptionsForDevice('');
        document.getElementById('emMaxKwh').value = formatThousand(1000000000);
        document.getElementById('emDemandEnabled').checked = false;
        renderDemandSidOptions('');
        document.getElementById('emDemandSidRow').style.display = 'none';
        document.getElementById('emDesc').value = '';
        document.getElementById('emTypeVirtual').checked = true;
        document.getElementById('emSignPos').checked = true;
        updateMeterFieldsVisibility();
        updateSignRowVisibility(parentId);
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
        const szDev = opt ? (opt.deviceName || '未指定') : '';
        document.getElementById('emDevice').value = szDev;
        renderSidOptionsForDevice(szDev);
        document.getElementById('emSid').value = node.sid || '';
        document.getElementById('emMaxKwh').value = node.maxKwh == null ? '' : formatThousand(Math.trunc(node.maxKwh));
        const hasDemand = !!node.demandSid;
        document.getElementById('emDemandEnabled').checked = hasDemand;
        renderDemandSidOptions(node.demandSid || '');
        document.getElementById('emDemandSidRow').style.display = hasDemand ? '' : 'none';
        document.getElementById('emDesc').value = node.description || '';
        if (node.sid) document.getElementById('emTypeMeter').checked = true;
        else document.getElementById('emTypeVirtual').checked = true;
        if (node.sign === -1) document.getElementById('emSignNeg').checked = true;
        else document.getElementById('emSignPos').checked = true;
        updateMeterFieldsVisibility();
        updateSignRowVisibility(node.parentId);
        g_modal.show();
    }

    function updateMeterFieldsVisibility() {
        const isMeter = document.getElementById('emTypeMeter').checked;
        document.getElementById('emMeterFields').style.display = isMeter ? '' : 'none';
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
        const isDemand = isMeter && document.getElementById('emDemandEnabled').checked;
        const szDemandSid = isDemand ? document.getElementById('emDemandSid').value : null;
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
            demandSid: szDemandSid || null,
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

    // ============ 工具 ============
    function escapeHtml(s) {
        if (s == null) return '';
        return String(s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;')
            .replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#039;');
    }

    // 對外介面
    window._em = { openCreateModal, openEditModal, saveCircuit, deleteNode };
})();
