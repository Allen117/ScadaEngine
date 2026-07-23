// ============================================================
// Designer — Point Picker（兩步驟：先選設備，再選點位）
// ============================================================
// 內容：picker 狀態、設備 / 計算點 / DB 點切換、設備清單、點位篩選、
// confirmPointPick 統一分派到 createXxxWithPoint，以及屬性面板「重選」
// rerouteXxxPoint 系列函式。
// 依賴：state.js / widget-defs.js / prop-panel.js（createXxxWithPoint
// + nSelectedCellRow / nSelectedCellCol） / widget-core.js（renderWidget
// 等）。靠 global hoisting 解 prop-panel ↔ picker 循環依賴。
// ============================================================

let arrAllDevices      = null;   // 快取設備清單
let arrAllPoints       = null;   // 快取所有點位
let arrAllSchedules    = null;   // 快取排程清單（DI widget 綁排程用）
let nPickedDevId       = -1;     // 已選設備的 DB Id（用於 SID 範圍比對）
let nPickedModbusId    = null;   // 已選子項目的 ModbusID（精確過濾）
let szPickedSid        = null;   // 已選點位 SID
let nPickedScheduleId  = null;   // 已選排程 ID
let szPickedScheduleName = '';   // 已選排程名稱
let szPickerSourceMode = 'point';// picker 來源模式：'point' | 'schedule'（僅 DI widget 可切換）
let pendingGaugeX      = 0;
let pendingGaugeY      = 0;
let szPickerWidgetType = 'gauge'; // 記錄是哪種 widget 開啟選擇器
let _pointPickerModal  = null;
let nPickedCalcGroup   = null;   // 計算點位群組篩選（null=全部, ''=未分組, 'GroupName'=指定群組）
let szPickedDbGroup    = null;   // DB 來源 Coordinator 群組篩選（null=全部, 'CoordinatorName'=指定群組）

const CALC_DEVICE_ID    = -999; // 計算點位的虛擬設備 ID
const DB_DEVICE_ID      = -998; // DB 來源點位的虛擬設備 ID
const CIRCUIT_DEVICE_ID = -997; // 能源迴路的虛擬設備 ID（plan 2026-07-23 迴路指標）

// ---- 能源迴路資料源（plan 2026-07-23）----
let arrAllCircuits    = null;   // 快取迴路清單（含虛擬節點）
let _circuitById      = null;   // id → circuit
let _circuitBySid     = null;   // sid → { circuit, szRole }（五 SID 欄全建索引：kwh/v/a/kw/pf）
let nPickedCircuitId  = null;   // 已選迴路 Id（與 szPickedSid 互斥）

// 水泵綁定欄位（多 SID/CID）
let _pumpPickerSlot    = '';   // 目前正在選擇的 pump 綁定欄位 key（如 'szSidRun'、'szCidStartStop'）
let _pumpPickerNameKey = '';   // 對應的名稱 key（如 'szRunName'、'szStartStopName'）

// 管路綁定模式（'di' | 'analog'，二擇一互斥；使用者正在綁哪一種）
let _pipePickerMode    = '';

// SID 格式: {coordinatorId*65536 + modbusId*256 + 1}-S{N}
// 某設備所屬 SID 的數字前綴落在 [Id*65536, (Id+1)*65536-1] 範圍內
function getSidNumericPrefix(szSid) {
    const m = szSid.match(/^(\d+)-S\d+$/);
    return m ? parseInt(m[1], 10) : -1;
}

function isCalcPoint(szSid) {
    return szSid && szSid.startsWith('CALC-');
}

function isDbPoint(szSid) {
    return !!(szSid && /^DB\d+-S\d+$/.test(szSid));
}

function isPointOfDevice(szSid, nDevId) {
    if (nDevId === CALC_DEVICE_ID) return isCalcPoint(szSid);
    if (nDevId === DB_DEVICE_ID)   return isDbPoint(szSid);
    if (isCalcPoint(szSid)) return false;
    if (isDbPoint(szSid))   return false;
    const nPfx = getSidNumericPrefix(szSid);
    return nPfx >= nDevId * 65536 && nPfx < (nDevId + 1) * 65536;
}

async function openPointPicker(x, y, szWidgetType) {
    pendingGaugeX      = x;
    pendingGaugeY      = y;
    szPickerWidgetType = szWidgetType || 'gauge';
    szPickedSid        = null;
    nPickedDevId       = -1;
    nPickedCircuitId   = null;
    nPickedScheduleId  = null;
    szPickedScheduleName = '';
    szPickerSourceMode = 'point';
    _updateSourceToggleVisibility();
    await _ensurePickerData();
    _showPickerStep1();
}

// 切換來源 toggle 顯示與否（僅 DI widget 顯示）
function _updateSourceToggleVisibility() {
    const toggle = document.getElementById('ppSourceToggle');
    if (!toggle) return;
    const bShow = szPickerWidgetType === 'diPoint';
    toggle.style.display = bShow ? '' : 'none';
    // 重置 toggle 視覺狀態為「點位」
    const btnP = document.getElementById('ppBtnPoint');
    const btnS = document.getElementById('ppBtnSchedule');
    if (btnP && btnS) {
        if (szPickerSourceMode === 'schedule') {
            btnP.classList.remove('active'); btnS.classList.add('active');
        } else {
            btnS.classList.remove('active'); btnP.classList.add('active');
        }
    }
}

// 切換 picker 來源類型（'point' | 'schedule'），僅 DI widget 開啟時可用
function switchPickerSource(szMode) {
    if (szPickerWidgetType !== 'diPoint') return;
    szPickerSourceMode = szMode;
    const btnP = document.getElementById('ppBtnPoint');
    const btnS = document.getElementById('ppBtnSchedule');
    if (szMode === 'schedule') {
        if (btnP) btnP.classList.remove('active');
        if (btnS) btnS.classList.add('active');
        document.getElementById('ppStep0').style.display = 'none';
        document.getElementById('ppStep1').style.display = 'none';
        document.getElementById('ppStep2').style.display = 'none';
        document.getElementById('ppStep3').style.display = '';
        document.getElementById('ppModalTitle').textContent = t('designer.picker.title.schedule');
        szPickedSid = null;
        nPickedScheduleId = null;
        szPickedScheduleName = '';
        document.getElementById('btnConfirmPoint').disabled = true;
        _renderScheduleList();
    } else {
        if (btnS) btnS.classList.remove('active');
        if (btnP) btnP.classList.add('active');
        document.getElementById('ppStep3').style.display = 'none';
        nPickedScheduleId = null;
        szPickedScheduleName = '';
        _showPickerStep0();
    }
}

async function _ensureSchedules() {
    if (arrAllSchedules) return;
    const r = await fetch('/api/schedules');
    if (!r.ok) throw new Error('HTTP ' + r.status);
    arrAllSchedules = await r.json();
}

async function _renderScheduleList() {
    const container = document.getElementById('scheduleListContainer');
    container.innerHTML = '<div style="color:#888;font-size:12px;text-align:center;padding:20px;">' + escHtml(t('designer.picker.loading')) + '</div>';
    try {
        await _ensureSchedules();
    } catch (e) {
        container.innerHTML = '<div style="color:#dc3545;font-size:12px;text-align:center;padding:20px;">' + escHtml(t('designer.picker.schedule_load_error')) + '</div>';
        return;
    }
    const enabled = (arrAllSchedules || []).filter(s => s.isEnabled);
    if (enabled.length === 0) {
        container.innerHTML = '<div style="color:#888;font-size:12px;text-align:center;padding:20px;">' +
            '<i class="fas fa-calendar-times" style="font-size:24px;display:block;margin-bottom:8px;"></i>' +
            escHtml(t('designer.picker.no_schedules')) + '</div>';
        return;
    }
    const TYPE_LABELS = [
        t('designer.picker.schedule.weekly'),
        t('designer.picker.schedule.n_weekly'),
        t('designer.picker.schedule.monthly'),
        t('designer.picker.schedule.n_monthly')
    ];
    container.innerHTML = enabled.map(s => {
        const szTypeLabel = TYPE_LABELS[s.nRecurrenceType] || '';
        const szTime = escHtml(s.szStartTime || '') + ' - ' + escHtml(s.szEndTime || '');
        return `
        <div class="point-list-item" data-schedule-id="${s.nId}"
             onclick="selectScheduleItem(this,${s.nId},'${escHtml(s.szName)}')">
            <i class="fas fa-calendar-alt" style="font-size:14px;color:#0d6efd;flex-shrink:0;"></i>
            <div style="flex:1;min-width:0;">
                <div class="point-name">${escHtml(s.szName)}</div>
                <div class="point-sid">${escHtml(szTypeLabel)} ・ ${szTime}</div>
            </div>
        </div>`;
    }).join('');
}

function selectScheduleItem(el, nId, szName) {
    document.querySelectorAll('#scheduleListContainer .point-list-item')
            .forEach(i => i.classList.remove('selected'));
    el.classList.add('selected');
    nPickedScheduleId = nId;
    szPickedScheduleName = szName;
    document.getElementById('btnConfirmPoint').disabled = false;
}

async function _ensurePickerData() {
    // 迴路清單並行預載（失敗不阻擋點位選取 — 僅迴路分頁與 SID 反查功能失效）
    const pCircuits = _ensureCircuitData().catch(() => {});
    if (arrAllDevices && arrAllPoints) { await pCircuits; return; }
    try {
        const [rDev, rPt] = await Promise.all([
            fetch('/Designer/Devices'),
            fetch('/Designer/Points')
        ]);
        if (!rDev.ok) throw new Error('Devices HTTP ' + rDev.status);
        if (!rPt.ok)  throw new Error('Points HTTP '  + rPt.status);
        arrAllDevices = await rDev.json();
        arrAllPoints  = await rPt.json();
        _enrichPointsWithDeviceLabel();
    } catch (err) {
        alert(t('designer.picker.load_error') + '\n' + err.message);
        throw err;
    }
    await pCircuits;
}

// ---- 能源迴路資料載入（plan 2026-07-23）----
// 建 id → circuit 與 sid → { circuit, szRole } 反查 map；
// 五個 SID 欄（kWh 累積讀值 + V/A/kW/PF 電表資訊）全建索引，供表頭驅動整列帶入反查。
async function _ensureCircuitData() {
    if (arrAllCircuits) return;
    const r = await fetch('/Designer/api/circuits');
    if (!r.ok) throw new Error('Circuits HTTP ' + r.status);
    const circuits = await r.json();
    const byId  = {};
    const bySid = {};
    const SID_ROLE_FIELDS = [
        ['sid',            'kwh'],
        ['voltageSid',     'v'],
        ['currentSid',     'a'],
        ['powerSid',       'kw'],
        ['powerFactorSid', 'pf']
    ];
    circuits.forEach(c => {
        byId[c.id] = c;
        SID_ROLE_FIELDS.forEach(([szField, szRole]) => {
            const szSid = c[szField];
            if (!szSid) return;
            // 同 SID 被多迴路引用時以先建立者為準（依 API 排序 = ParentId, SortOrder）
            if (!bySid[szSid]) bySid[szSid] = { circuit: c, szRole: szRole };
        });
    });
    arrAllCircuits = circuits;
    _circuitById   = byId;
    _circuitBySid  = bySid;
}

// 為每個點位計算所屬設備/子設備名稱，作為顯示前綴
function _enrichPointsWithDeviceLabel() {
    if (!arrAllDevices || !arrAllPoints) return;
    arrAllPoints.forEach(p => {
        if (isDbPoint(p.szSid)) {
            p.szDeviceLabel = p.szGroupName || t('designer.picker.db_source_default');
            return;
        }
        if (isCalcPoint(p.szSid)) {
            p.szDeviceLabel = p.szGroupName || t('designer.picker.source.calc');
            return;
        }
        const nPfx = getSidNumericPrefix(p.szSid);
        let szLabel = '';
        for (const d of arrAllDevices) {
            if (!isPointOfDevice(p.szSid, d.nId)) continue;
            const modbusIds   = (d.szModbusID || '').split(',').map(s => s.trim()).filter(Boolean);
            const deviceNames = (d.szDeviceName || '').split(',').map(s => s.trim());
            if (modbusIds.length > 1) {
                // 多子 ID：找出所屬的子設備名稱
                for (let j = 0; j < modbusIds.length; j++) {
                    const mid  = parseInt(modbusIds[j], 10);
                    const base = d.nId * 65536 + mid * 256;
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
        p.szDeviceLabel = szLabel;
    });
}

function _showPickerStep0() {
    document.getElementById('ppStep0').style.display = '';
    document.getElementById('ppStep1').style.display = 'none';
    document.getElementById('ppStep2').style.display = 'none';
    const step3 = document.getElementById('ppStep3');
    if (step3) step3.style.display = 'none';
    // 迴路來源分頁：僅 AI 點位與表格資料 cell 可綁迴路指標
    const srcCircuit = document.getElementById('ppSrcCircuit');
    if (srcCircuit) {
        const bShowCircuit = szPickerWidgetType === 'realtimeValue' || szPickerWidgetType === 'tableCell';
        srcCircuit.style.display = bShowCircuit ? '' : 'none';
    }
    document.getElementById('ppModalTitle').textContent = t('designer.picker.title.source');
    document.getElementById('btnConfirmPoint').disabled = true;
    renderDeviceList();
    if (!_pointPickerModal) {
        _pointPickerModal = new bootstrap.Modal(document.getElementById('pointPickerModal'));
    }
    _pointPickerModal.show();
}

function _showPickerStep1() {
    _showPickerStep0();
}

function showDeviceStep() {
    nPickedDevId     = -1;
    nPickedModbusId  = null;
    nPickedCalcGroup = null;
    szPickedDbGroup  = null;
    szPickedSid      = null;
    document.getElementById('ppStep0').style.display = 'none';
    document.getElementById('ppStep1').style.display = '';
    document.getElementById('ppStep2').style.display = 'none';
    document.getElementById('ppModalTitle').textContent = t('designer.picker.title.device');
    document.getElementById('btnConfirmPoint').disabled = true;
    renderDeviceList();
}

function showCalcPointStep() {
    nPickedDevId = CALC_DEVICE_ID;
    nPickedModbusId = null;
    szPickedSid = null;
    nPickedCalcGroup = null;
    szPickedDbGroup  = null;

    // 取得計算點位的群組清單
    const calcGroups = _getCalcGroups();
    if (calcGroups.length > 0) {
        // 有群組 — 顯示群組清單（Step 1）
        document.getElementById('ppStep0').style.display = 'none';
        document.getElementById('ppStep1').style.display = '';
        document.getElementById('ppStep2').style.display = 'none';
        document.getElementById('ppModalTitle').textContent = t('designer.picker.title.calc_group');
        _renderCalcGroupList(calcGroups);
    } else {
        // 無群組 — 直接顯示全部計算點位
        _showCalcPointsFlat();
    }
}

function _getCalcGroups() {
    if (!arrAllPoints) return [];
    const groups = {};
    arrAllPoints.forEach(p => {
        if (!isCalcPoint(p.szSid)) return;
        const g = p.szGroupName || '';
        if (!groups[g]) groups[g] = 0;
        groups[g]++;
    });
    return Object.keys(groups).filter(g => g !== '').sort();
}

function _renderCalcGroupList(groups) {
    const container = document.getElementById('deviceListContainer');
    const hasUngrouped = (arrAllPoints || []).some(p => isCalcPoint(p.szSid) && !p.szGroupName);
    let html = groups.map(g => {
        const nPts = (arrAllPoints || []).filter(p => isCalcPoint(p.szSid) && p.szGroupName === g).length;
        return `
        <div class="point-list-item" onclick="selectCalcGroup('${escHtml(g)}')">
            <i class="fas fa-layer-group" style="font-size:14px;color:#ffc107;flex-shrink:0;"></i>
            <div style="flex:1;min-width:0;">
                <div class="point-name">${escHtml(g)}</div>
                <div class="point-sid">${escHtml(t('designer.picker.points_count', { count: nPts }))}</div>
            </div>
            <i class="fas fa-chevron-right" style="color:#555;font-size:11px;"></i>
        </div>`;
    }).join('');
    if (hasUngrouped) {
        const nPts = (arrAllPoints || []).filter(p => isCalcPoint(p.szSid) && !p.szGroupName).length;
        html += `
        <div class="point-list-item" onclick="selectCalcGroup('')">
            <i class="fas fa-inbox" style="font-size:14px;color:#6c757d;flex-shrink:0;"></i>
            <div style="flex:1;min-width:0;">
                <div class="point-name">${escHtml(t('designer.picker.ungrouped'))}</div>
                <div class="point-sid">${escHtml(t('designer.picker.points_count', { count: nPts }))}</div>
            </div>
            <i class="fas fa-chevron-right" style="color:#555;font-size:11px;"></i>
        </div>`;
    }
    container.innerHTML = html;
}

function selectCalcGroup(szGroup) {
    nPickedDevId = CALC_DEVICE_ID;
    nPickedModbusId = null;
    nPickedCalcGroup = szGroup;
    szPickedSid = null;
    document.getElementById('ppDeviceName').textContent = szGroup || t('designer.picker.ungrouped');
    document.getElementById('ppDeviceIcon').className = 'fas fa-layer-group me-1';
    document.getElementById('ppStep0').style.display = 'none';
    document.getElementById('ppStep1').style.display = 'none';
    document.getElementById('ppStep2').style.display = '';
    document.getElementById('ppModalTitle').textContent = t('designer.picker.title.calc_point');
    document.getElementById('ppPointSearch').value = '';
    document.getElementById('btnConfirmPoint').disabled = true;
    _renderFilteredPoints('');
}

function _showCalcPointsFlat() {
    document.getElementById('ppStep0').style.display = 'none';
    document.getElementById('ppStep1').style.display = 'none';
    document.getElementById('ppStep2').style.display = '';
    document.getElementById('ppModalTitle').textContent = t('designer.picker.title.calc_point');
    document.getElementById('ppDeviceName').textContent = t('designer.picker.source.calc');
    document.getElementById('ppDeviceIcon').className = 'fas fa-calculator me-1';
    document.getElementById('ppPointSearch').value = '';
    document.getElementById('btnConfirmPoint').disabled = true;
    _renderFilteredPoints('');
}

// ---- DB 來源點位 ----
function showDbPointStep() {
    nPickedDevId     = DB_DEVICE_ID;
    nPickedModbusId  = null;
    szPickedSid      = null;
    szPickedDbGroup  = null;
    nPickedCalcGroup = null;

    const dbGroups = _getDbGroups();
    document.getElementById('ppStep0').style.display = 'none';
    document.getElementById('ppStep1').style.display = '';
    document.getElementById('ppStep2').style.display = 'none';
    document.getElementById('ppModalTitle').textContent = t('designer.picker.title.db_source');
    _renderDbGroupList(dbGroups);
}

function _getDbGroups() {
    if (!arrAllPoints) return [];
    const groups = {};
    arrAllPoints.forEach(p => {
        if (!isDbPoint(p.szSid)) return;
        const g = p.szGroupName || '';
        if (!groups[g]) groups[g] = 0;
        groups[g]++;
    });
    return Object.keys(groups).sort();
}

function _renderDbGroupList(groups) {
    const container = document.getElementById('deviceListContainer');
    if (!groups || groups.length === 0) {
        container.innerHTML = '<div style="color:#888;font-size:12px;text-align:center;padding:20px;">' +
            '<i class="fas fa-database" style="font-size:24px;display:block;margin-bottom:8px;"></i>' + escHtml(t('designer.picker.no_db_points')) + '</div>';
        return;
    }
    container.innerHTML = groups.map(g => {
        const nPts = (arrAllPoints || []).filter(p => isDbPoint(p.szSid) && (p.szGroupName || '') === g).length;
        const szDisplay = g || t('designer.picker.db_source_default');
        return `
        <div class="point-list-item" onclick="selectDbGroup('${escHtml(g)}')">
            <i class="fas fa-database" style="font-size:14px;color:#6ec1a3;flex-shrink:0;"></i>
            <div style="flex:1;min-width:0;">
                <div class="point-name">${escHtml(szDisplay)}</div>
                <div class="point-sid">${escHtml(t('designer.picker.points_count', { count: nPts }))}</div>
            </div>
            <i class="fas fa-chevron-right" style="color:#555;font-size:11px;"></i>
        </div>`;
    }).join('');
}

function selectDbGroup(szGroup) {
    nPickedDevId    = DB_DEVICE_ID;
    nPickedModbusId = null;
    szPickedDbGroup = szGroup;
    szPickedSid     = null;
    document.getElementById('ppDeviceName').textContent = szGroup || t('designer.picker.db_source_default');
    document.getElementById('ppDeviceIcon').className = 'fas fa-database me-1';
    document.getElementById('ppStep0').style.display = 'none';
    document.getElementById('ppStep1').style.display = 'none';
    document.getElementById('ppStep2').style.display = '';
    document.getElementById('ppModalTitle').textContent = t('designer.picker.title.db_point');
    document.getElementById('ppPointSearch').value = '';
    document.getElementById('btnConfirmPoint').disabled = true;
    _renderFilteredPoints('');
}

// ---- 能源迴路來源（plan 2026-07-23 迴路指標）----
// 樹狀列出全部迴路（含虛擬節點），列表即終點 — 點選迴路直接可確認，無 Step2。
function showCircuitStep(nHighlightId) {
    nPickedDevId     = CIRCUIT_DEVICE_ID;
    nPickedModbusId  = null;
    szPickedSid      = null;
    nPickedCircuitId = null;
    nPickedCalcGroup = null;
    szPickedDbGroup  = null;

    document.getElementById('ppStep0').style.display = 'none';
    document.getElementById('ppStep1').style.display = '';
    document.getElementById('ppStep2').style.display = 'none';
    document.getElementById('ppModalTitle').textContent = t('designer.picker.title.circuit');
    document.getElementById('btnConfirmPoint').disabled = true;
    _renderCircuitTree(nHighlightId);

    if (nHighlightId != null && _circuitById && _circuitById[nHighlightId]) {
        nPickedCircuitId = nHighlightId;
        document.getElementById('btnConfirmPoint').disabled = false;
        setTimeout(() => {
            const item = document.querySelector('#deviceListContainer .point-list-item[data-circuit-id="' + nHighlightId + '"]');
            if (item) item.scrollIntoView({ block: 'center', behavior: 'smooth' });
        }, 50);
    }
}

function _renderCircuitTree(nHighlightId) {
    const container = document.getElementById('deviceListContainer');
    if (!arrAllCircuits || arrAllCircuits.length === 0) {
        container.innerHTML = '<div style="color:#888;font-size:12px;text-align:center;padding:20px;">' +
            '<i class="fas fa-sitemap" style="font-size:24px;display:block;margin-bottom:8px;"></i>' +
            escHtml(t('designer.picker.no_circuits')) + '</div>';
        return;
    }
    // 平坦清單組樹：依 parentId 分組後深度優先展開，縮排呈現層級
    const byParent = {};
    arrAllCircuits.forEach(c => {
        const k = c.parentId == null ? 'root' : String(c.parentId);
        (byParent[k] = byParent[k] || []).push(c);
    });
    Object.values(byParent).forEach(arr => arr.sort((x, y) => (x.sortOrder - y.sortOrder) || (x.id - y.id)));

    let szHtml = '';
    function walk(szKey, nDepth) {
        (byParent[szKey] || []).forEach(c => {
            const bVirtual  = !c.sid;
            const szIcon    = bVirtual ? 'fa-sitemap' : 'fa-bolt';
            const szIconClr = bVirtual ? '#69db7c' : '#ffd43b';
            const szTag     = bVirtual ? `<span class="point-unit">${escHtml(t('designer.picker.circuit_virtual'))}</span>` : '';
            const szSel     = (nHighlightId != null && c.id === nHighlightId) ? ' selected' : '';
            szHtml += `
        <div class="point-list-item${szSel}" data-circuit-id="${c.id}" style="padding-left:${12 + nDepth * 18}px;"
             onclick="selectCircuitItem(this,${c.id})">
            <i class="fas ${szIcon}" style="font-size:13px;color:${szIconClr};flex-shrink:0;"></i>
            <div style="flex:1;min-width:0;">
                <div class="point-name">${escHtml(c.name)}</div>
            </div>
            ${szTag}
        </div>`;
            walk(String(c.id), nDepth + 1);
        });
    }
    walk('root', 0);
    container.innerHTML = szHtml;
}

function selectCircuitItem(el, nId) {
    document.querySelectorAll('#deviceListContainer .point-list-item')
            .forEach(i => i.classList.remove('selected'));
    el.classList.add('selected');
    nPickedCircuitId = nId;
    szPickedSid      = null;
    document.getElementById('btnConfirmPoint').disabled = false;
}

function goBackToStep0() {
    document.getElementById('ppStep0').style.display = '';
    document.getElementById('ppStep1').style.display = 'none';
    document.getElementById('ppStep2').style.display = 'none';
    document.getElementById('ppModalTitle').textContent = t('designer.picker.title.source');
    document.getElementById('btnConfirmPoint').disabled = true;
    szPickedSid = null;
    nPickedCircuitId = null;
}

// 從 widget 取得已綁定的 SID（不同 widget 用 szSid 或 szCid）
function _getBoundSidFromWidget(el) {
    if (!el || !el.widgetProps) return '';
    return el.widgetProps.szSid || el.widgetProps.szCid || '';
}

// 若有已綁定 SID，直接跳到 Step2 並高亮該點位；否則回落 Step1
function _showPickerForBoundSid(szBoundSid) {
    if (!szBoundSid || !arrAllDevices || !arrAllPoints) {
        _showPickerStep1();
        return;
    }
    const point = arrAllPoints.find(p => p.szSid === szBoundSid);
    if (!point) { _showPickerStep1(); return; }

    // 找出所屬設備
    let foundDevice = null;
    let foundModbusId = null;
    let szDevLabel = '';

    if (isCalcPoint(szBoundSid)) {
        // 計算點位 — 使用虛擬設備 ID
        nPickedDevId    = CALC_DEVICE_ID;
        nPickedModbusId = null;
        szPickedSid     = szBoundSid;
        nPickedCalcGroup = point.szGroupName || null;
        szDevLabel      = point.szGroupName || t('designer.picker.source.calc');
    } else if (isDbPoint(szBoundSid)) {
        nPickedDevId    = DB_DEVICE_ID;
        nPickedModbusId = null;
        szPickedSid     = szBoundSid;
        szPickedDbGroup = point.szGroupName || null;
        szDevLabel      = point.szGroupName || t('designer.picker.db_source_default');
    } else {
        for (const d of arrAllDevices) {
            if (!isPointOfDevice(szBoundSid, d.nId)) continue;
            foundDevice = d;
            const modbusIds   = (d.szModbusID || '').split(',').map(s => s.trim()).filter(Boolean);
            const deviceNames = (d.szDeviceName || '').split(',').map(s => s.trim());
            if (modbusIds.length > 1) {
                const nPfx = getSidNumericPrefix(szBoundSid);
                for (let j = 0; j < modbusIds.length; j++) {
                    const mid  = parseInt(modbusIds[j], 10);
                    const base = d.nId * 65536 + mid * 256;
                    if (nPfx >= base && nPfx < base + 256) {
                        foundModbusId = mid;
                        szDevLabel = (j < deviceNames.length && deviceNames[j]) ? deviceNames[j] : d.szName;
                        break;
                    }
                }
            } else {
                szDevLabel = d.szName;
            }
            break;
        }
        if (!foundDevice) { _showPickerStep1(); return; }

        // 設定 picker 狀態，直接跳 Step2
        nPickedDevId    = foundDevice.nId;
        nPickedModbusId = foundModbusId;
        szPickedSid     = szBoundSid;
    }

    document.getElementById('ppDeviceName').textContent = szDevLabel;
    document.getElementById('ppDeviceIcon').className = isCalcPoint(szBoundSid) ? 'fas fa-calculator me-1' : (isDbPoint(szBoundSid) ? 'fas fa-database me-1' : 'fas fa-server me-1');
    document.getElementById('ppStep0').style.display = 'none';
    document.getElementById('ppStep1').style.display = 'none';
    document.getElementById('ppStep2').style.display = '';
    document.getElementById('ppModalTitle').textContent = t(isCalcPoint(szBoundSid) ? 'designer.picker.title.calc_point' : 'designer.picker.title.point');
    document.getElementById('ppPointSearch').value = '';
    document.getElementById('btnConfirmPoint').disabled = false;
    renderDeviceList();
    _renderFilteredPoints('');

    // 高亮已綁定點位並捲動至可視範圍
    setTimeout(() => {
        const item = document.querySelector('#pointListContainer .point-list-item[data-sid="' + szBoundSid + '"]');
        if (item) {
            item.classList.add('selected');
            item.scrollIntoView({ block: 'center', behavior: 'smooth' });
        }
    }, 50);

    if (!_pointPickerModal) {
        _pointPickerModal = new bootstrap.Modal(document.getElementById('pointPickerModal'));
    }
    _pointPickerModal.show();
}

function renderDeviceList() {
    const container = document.getElementById('deviceListContainer');
    if (!arrAllDevices || arrAllDevices.length === 0) {
        container.innerHTML = '<div style="color:#888;font-size:12px;text-align:center;padding:20px;">' +
            '<i class="fas fa-plug" style="font-size:24px;display:block;margin-bottom:8px;"></i>' + escHtml(t('designer.picker.no_devices')) + '</div>';
        return;
    }
    container.innerHTML = arrAllDevices.map(d => {
        const nPts = (arrAllPoints || []).filter(p => isPointOfDevice(p.szSid, d.nId)).length;
        const modbusIds = (d.szModbusID || '').split(',').map(s => s.trim()).filter(Boolean);
        const deviceNames = (d.szDeviceName || '').split(',').map(s => s.trim());

        if (modbusIds.length > 1) {
            // 多 ModbusID：可展開的群組
            const subHtml = modbusIds.map((mid, j) => {
                const subName = j < deviceNames.length ? deviceNames[j] : '';
                const label = subName || mid;
                return `
                <div class="point-list-item" style="padding-left:28px;background:#1e1e1e;"
                     onclick="selectDeviceItem(this,${d.nId},'${escHtml(label)}',${mid})">
                    <i class="fas fa-microchip" style="font-size:12px;color:#6ea8fe;flex-shrink:0;"></i>
                    <div style="flex:1;min-width:0;">
                        <div class="point-name">${escHtml(label)}</div>
                    </div>
                    <i class="fas fa-chevron-right" style="color:#555;font-size:11px;"></i>
                </div>`;
            }).join('');

            return `
            <div class="point-list-item" style="cursor:pointer;"
                 onclick="toggleDeviceSub(this)">
                <i class="fas fa-server" style="font-size:14px;color:#7ecfff;flex-shrink:0;"></i>
                <div style="flex:1;min-width:0;">
                    <div class="point-name">${escHtml(d.szName)}</div>
                    <div class="point-sid">${escHtml(t('designer.picker.points_count', { count: nPts }))}</div>
                </div>
                <i class="fas fa-chevron-down toggle-dev-icon" style="color:#555;font-size:11px;transition:transform .2s;"></i>
            </div>
            <div class="dev-sub-menu" style="display:none;">${subHtml}</div>`;
        }

        return `
        <div class="point-list-item" onclick="selectDeviceItem(this,${d.nId},'${escHtml(d.szName)}')">
            <i class="fas fa-server" style="font-size:14px;color:#7ecfff;flex-shrink:0;"></i>
            <div style="flex:1;min-width:0;">
                <div class="point-name">${escHtml(d.szName)}</div>
                <div class="point-sid">${escHtml(t('designer.picker.points_count', { count: nPts }))}</div>
            </div>
            <i class="fas fa-chevron-right" style="color:#555;font-size:11px;"></i>
        </div>`;
    }).join('');
}

function toggleDeviceSub(el) {
    const subMenu = el.nextElementSibling;
    const icon = el.querySelector('.toggle-dev-icon');
    if (subMenu.style.display === 'none') {
        subMenu.style.display = '';
        icon.style.transform = 'rotate(180deg)';
    } else {
        subMenu.style.display = 'none';
        icon.style.transform = '';
    }
}

function selectDeviceItem(el, nDevId, szLabel, nModbusId) {
    nPickedDevId = nDevId;
    nPickedModbusId = nModbusId != null ? nModbusId : null;
    document.getElementById('ppDeviceName').textContent = szLabel || String(nDevId);
    document.getElementById('ppDeviceIcon').className = 'fas fa-server me-1';
    document.getElementById('ppStep0').style.display = 'none';
    document.getElementById('ppStep1').style.display = 'none';
    document.getElementById('ppStep2').style.display = '';
    document.getElementById('ppModalTitle').textContent = t('designer.picker.title.point');
    document.getElementById('ppPointSearch').value = '';
    szPickedSid = null;
    document.getElementById('btnConfirmPoint').disabled = true;
    _renderFilteredPoints('');
}

function goBackToDevices() {
    if (nPickedDevId === CALC_DEVICE_ID) {
        if (nPickedCalcGroup != null) {
            // 從計算點位列表返回群組列表
            nPickedCalcGroup = null;
            showCalcPointStep();
        } else {
            goBackToStep0();
        }
    } else if (nPickedDevId === DB_DEVICE_ID) {
        // DB 來源：返回 Coordinator 清單
        szPickedDbGroup = null;
        showDbPointStep();
    } else {
        document.getElementById('ppStep0').style.display = 'none';
        document.getElementById('ppStep1').style.display = '';
        document.getElementById('ppStep2').style.display = 'none';
        document.getElementById('ppModalTitle').textContent = t('designer.picker.title.device');
        document.getElementById('btnConfirmPoint').disabled = true;
        szPickedSid = null;
    }
}

function filterPointList(szKeyword) {
    _renderFilteredPoints(szKeyword);
    szPickedSid = null;
    document.getElementById('btnConfirmPoint').disabled = true;
}

function _renderFilteredPoints(szKeyword) {
    const szQ = szKeyword.trim().toLowerCase();
    const filtered = (arrAllPoints || []).filter(p => {
        if (nPickedDevId === CALC_DEVICE_ID && nPickedCalcGroup != null) {
            // 計算點位群組篩選
            if (!isCalcPoint(p.szSid)) return false;
            if ((p.szGroupName || '') !== nPickedCalcGroup) return false;
        } else if (nPickedDevId === DB_DEVICE_ID) {
            // DB 來源點位：依 Coordinator 群組篩選（null=全部 DB 點位）
            if (!isDbPoint(p.szSid)) return false;
            if (szPickedDbGroup != null && (p.szGroupName || '') !== szPickedDbGroup) return false;
        } else if (nPickedModbusId != null) {
            const nPfx = getSidNumericPrefix(p.szSid);
            const base = nPickedDevId * 65536 + nPickedModbusId * 256;
            if (nPfx < base || nPfx >= base + 256) return false;
        } else {
            if (!isPointOfDevice(p.szSid, nPickedDevId)) return false;
        }
        return !szQ || p.szName.toLowerCase().includes(szQ);
    });
    const container = document.getElementById('pointListContainer');
    if (filtered.length === 0) {
        container.innerHTML = '<div style="color:#888;font-size:12px;text-align:center;padding:20px;">' +
            '<i class="fas fa-inbox" style="font-size:24px;display:block;margin-bottom:8px;"></i>' + escHtml(t('designer.picker.no_matching_points')) + '</div>';
        return;
    }
    container.innerHTML = filtered.map(p => {
        const szPrefix = p.szDeviceLabel ? `<span style="color:#6ea8fe;font-size:11px;">${escHtml(p.szDeviceLabel)}</span><span style="color:#555;margin:0 4px;">/</span>` : '';
        return `
        <div class="point-list-item" data-sid="${escHtml(p.szSid)}"
             onclick="selectPointItem(this,'${escHtml(p.szSid)}')">
            <i class="fas fa-circle" style="font-size:6px;color:#6c9;flex-shrink:0;"></i>
            <div style="flex:1;min-width:0;">
                <div class="point-name">${szPrefix}${escHtml(p.szName)}</div>
            </div>
            <span class="point-unit">${escHtml(p.szUnit || '')}</span>
        </div>`;
    }).join('');
}

function selectPointItem(el, szSid) {
    document.querySelectorAll('#pointListContainer .point-list-item')
            .forEach(i => i.classList.remove('selected'));
    el.classList.add('selected');
    szPickedSid = szSid;
    document.getElementById('btnConfirmPoint').disabled = false;
}

function confirmPointPick() {
    // 排程綁定分支（僅 DI widget 在 'schedule' 模式時觸發）
    if (szPickerWidgetType === 'diPoint' && szPickerSourceMode === 'schedule') {
        if (nPickedScheduleId == null) return;
        _pointPickerModal.hide();
        if (pendingGaugeX === -1) {
            // 重選模式：更新目前選取的 DI widget
            if (!selectedEl || selectedEl.dataset.type !== 'diPoint') return;
            selectedEl.widgetProps.nScheduleId    = nPickedScheduleId;
            selectedEl.widgetProps.szScheduleName = szPickedScheduleName;
            // 互斥清空：綁排程時清掉 SID 綁定（plan 決策 2）
            selectedEl.widgetProps.szSid       = '';
            selectedEl.widgetProps.szPointName = '';
            selectedEl.widgetProps.szTitle     = szPickedScheduleName;
            renderWidget(selectedEl);
            renderPropPanel(selectedEl);
        } else {
            createDiPointWithSchedule(nPickedScheduleId, szPickedScheduleName, pendingGaugeX, pendingGaugeY);
        }
        return;
    }
    // 迴路綁定分支（AI 點位 / 表格資料 cell 於「迴路」分頁選取；plan 2026-07-23）
    if (nPickedCircuitId != null && (szPickerWidgetType === 'realtimeValue' || szPickerWidgetType === 'tableCell')) {
        const circuit = _circuitById ? _circuitById[nPickedCircuitId] : null;
        if (!circuit) return;
        _pointPickerModal.hide();
        if (szPickerWidgetType === 'tableCell') {
            if (!selectedEl || nSelectedCellRow < 0) return;
            const props = selectedEl.widgetProps;
            initArrCells(props);
            const cell = props.arrCells[nSelectedCellRow]?.[nSelectedCellCol];
            if (cell) _bindCellToCircuitMetric(cell, circuit, cell.szMetric || 'day_kwh');
            renderWidget(selectedEl);
            const sel = selectedEl.querySelector(`.w-table [data-row="${nSelectedCellRow}"][data-col="${nSelectedCellCol}"]`);
            if (sel) sel.classList.add('selected-cell');
            renderTableCellPropPanel(selectedEl, nSelectedCellRow, nSelectedCellCol);
            // 表頭驅動整列帶入（待 picker 動畫收完再彈 confirm/toast）
            const widgetEl   = selectedEl;
            const nRowIdx    = nSelectedCellRow;
            const nPickedCol = nSelectedCellCol;
            setTimeout(() => { _tryCircuitRowAutoFill(widgetEl, nRowIdx, nPickedCol, circuit, true); }, 300);
        } else if (pendingGaugeX === -1) {
            // 重選模式：更新目前選取的 AI 點位 widget
            if (!selectedEl || selectedEl.dataset.type !== 'realtimeValue') return;
            _bindRtValueToCircuitMetric(selectedEl.widgetProps, circuit);
            renderWidget(selectedEl);
            renderPropPanel(selectedEl);
        } else {
            createRealtimeValueWithCircuit(circuit, pendingGaugeX, pendingGaugeY);
        }
        return;
    }

    if (!szPickedSid || !arrAllPoints) return;
    const point = arrAllPoints.find(p => p.szSid === szPickedSid);
    if (!point) return;
    _pointPickerModal.hide();
    const szFullName = point.szDeviceLabel ? point.szDeviceLabel + ' / ' + point.szName : point.szName;

    if (szPickerWidgetType === 'tableCell') {
        // 表格儲存格綁定點位
        if (!selectedEl || nSelectedCellRow < 0) return;
        const props = selectedEl.widgetProps;
        initArrCells(props);
        const cell = props.arrCells[nSelectedCellRow]?.[nSelectedCellCol];
        if (cell) {
            cell.szSid = point.szSid;
            cell.szPointName = szFullName;
            // 互斥清空：改綁一般點位時清掉迴路指標鍵
            _clearCellCircuitKeys(cell);
            // 表格 DI Cell 綁定 SID 時，繼承同 SID 已存在的 ON/OFF 標籤
            if (cell.szPointType === 'DI') {
                const diLabelsCell = _findDiLabelsForSid(point.szSid);
                if (diLabelsCell) {
                    cell.szOnLabel  = diLabelsCell.szOnLabel;
                    cell.szOffLabel = diLabelsCell.szOffLabel;
                }
            }
        }
        renderWidget(selectedEl);
        // 重新高亮並顯示 cell 屬性面板
        const sel = selectedEl.querySelector(`.w-table [data-row="${nSelectedCellRow}"][data-col="${nSelectedCellCol}"]`);
        if (sel) sel.classList.add('selected-cell');
        renderTableCellPropPanel(selectedEl, nSelectedCellRow, nSelectedCellCol);
        // 自動帶入 — 待 picker 動畫收完再彈：
        // SID 反查命中迴路 → 表頭驅動整列帶入（plan 2026-07-23）；
        // 未命中或表頭全不匹配 → 回落既有列範本（plan 2026-06-01-designer-row-template）
        {
            const widgetEl   = selectedEl;
            const nRowIdx    = nSelectedCellRow;
            const nPickedCol = nSelectedCellCol;
            setTimeout(() => {
                const hit = _circuitBySid ? _circuitBySid[point.szSid] : null;
                const bHandled = hit ? _tryCircuitRowAutoFill(widgetEl, nRowIdx, nPickedCol, hit.circuit, false, point) : false;
                if (!bHandled && window._rowTemplate && typeof window._rowTemplate.tryAutoFill === 'function') {
                    window._rowTemplate.tryAutoFill(widgetEl, nRowIdx, nPickedCol, point);
                }
            }, 300);
        }
    } else if (pendingGaugeX === -1) {
        // 重選模式：更新目前選取的 widget
        if (!selectedEl) return;
        const szType = selectedEl.dataset.type;
        if (szType === 'gauge') {
            const fNewMin = point.fMin ?? 0;
            const fNewMax = point.fMax ?? 100;
            selectedEl.widgetProps.szSid       = point.szSid;
            selectedEl.widgetProps.szPointName = szFullName;
            selectedEl.widgetProps.szTitle     = szFullName;
            selectedEl.widgetProps.szUnit      = point.szUnit || '';
            selectedEl.widgetProps.fMin        = fNewMin;
            selectedEl.widgetProps.fMax        = fNewMax;
            selectedEl.widgetProps.fValue      = (fNewMin + fNewMax) / 2;
        } else if (szType === 'controlBtn') {
            selectedEl.widgetProps.szCid       = point.szSid;
            selectedEl.widgetProps.szPointName = szFullName;
            selectedEl.widgetProps.szTitle     = szFullName;
        } else if (szType === 'realtimeValue') {
            selectedEl.widgetProps.szSid       = point.szSid;
            selectedEl.widgetProps.szPointName = szFullName;
            selectedEl.widgetProps.szTitle     = szFullName;
            selectedEl.widgetProps.szUnit      = point.szUnit || '';
            // 互斥清空：改綁一般點位時清掉迴路指標鍵
            _clearRtValueCircuitKeys(selectedEl.widgetProps);
            // 選到迴路 kWh 點位且累積(meter)模式溢位上限空白 → 自動帶 MaxKwh
            _tryFillMaxKwhFromCircuit(selectedEl.widgetProps);
        } else if (szType === 'diPoint') {
            selectedEl.widgetProps.szSid       = point.szSid;
            selectedEl.widgetProps.szPointName = szFullName;
            selectedEl.widgetProps.szTitle     = szFullName;
            // 互斥清空：綁 SID 時清掉排程綁定（plan 決策 2）
            selectedEl.widgetProps.nScheduleId    = null;
            selectedEl.widgetProps.szScheduleName = '';
            // 繼承同 SID 已存在的 DI ON/OFF 標籤
            const diLabelsRe = _findDiLabelsForSid(point.szSid);
            if (diLabelsRe) {
                selectedEl.widgetProps.szOnLabel  = diLabelsRe.szOnLabel;
                selectedEl.widgetProps.szOffLabel = diLabelsRe.szOffLabel;
            }
        } else if (szType === 'aoPoint') {
            selectedEl.widgetProps.szCid       = point.szSid;
            selectedEl.widgetProps.szPointName = szFullName;
            selectedEl.widgetProps.szTitle     = szFullName;
            selectedEl.widgetProps.szUnit      = point.szUnit || '';
            selectedEl.widgetProps.fMin        = point.fMin ?? 0;
            selectedEl.widgetProps.fMax        = point.fMax ?? 100;
        } else if (szType === 'doPoint') {
            selectedEl.widgetProps.szCid       = point.szSid;
            selectedEl.widgetProps.szPointName = szFullName;
            selectedEl.widgetProps.szTitle     = szFullName;
        } else if (szType === 'pump' || szType === 'coolingTower' || szType === 'ahuFan' || szType === 'chiller') {
            // 馬達型設備多綁定欄位（pump 與三種新設備共用同一 slot 機制）
            if (_pumpPickerSlot) {
                selectedEl.widgetProps[_pumpPickerSlot]    = point.szSid;
                selectedEl.widgetProps[_pumpPickerNameKey] = szFullName;
                if (_pumpPickerSlot === 'szSidFreq') {
                    selectedEl.widgetProps.nFreqMax = point.fMax ?? 60;
                }
                if (_pumpPickerSlot === 'szCidFreqSet') {
                    selectedEl.widgetProps.nFreqSetMin = point.fMin ?? 0;
                    selectedEl.widgetProps.nFreqSetMax = point.fMax ?? 60;
                }
                if (_pumpPickerSlot === 'szSidLoad') {
                    selectedEl.widgetProps.nLoadMax = point.fMax ?? 100;
                }
            }
        } else if (szType === 'pipe') {
            const props = selectedEl.widgetProps;
            const szNewMode = (_pipePickerMode === 'analog') ? 'analog' : 'di';
            // 互斥確認（plan 決策 6）：已綁另一種模式且有值時，先確認再清除
            if (props.szBindMode && props.szBindMode !== szNewMode && props.szSid) {
                const szMsg = szNewMode === 'analog'
                    ? t('designer.pipe.confirm_switch_to_analog')
                    : t('designer.pipe.confirm_switch_to_di');
                if (!confirm(szMsg)) return;   // 取消 → 完全不改（原綁定保留）
            }
            const bWasAnalog = props.szBindMode === 'analog';
            props.szBindMode  = szNewMode;
            props.szSid       = point.szSid;
            props.szPointName = szFullName;
            props.szTitle     = szFullName;
            if (szNewMode === 'analog' && !bWasAnalog) {
                // 首次進入類比模式：以點位中間值作為預設閾值（可再調）
                const fMin = point.fMin ?? 0;
                const fMax = point.fMax ?? 100;
                props.fThreshold = Math.round((fMin + fMax) / 2 * 100) / 100;
            }
        }
        renderWidget(selectedEl);
        renderPropPanel(selectedEl);
    } else {
        if (szPickerWidgetType === 'controlBtn') {
            createControlBtnWithPoint(point, pendingGaugeX, pendingGaugeY);
        } else if (szPickerWidgetType === 'realtimeValue') {
            createRealtimeValueWithPoint(point, pendingGaugeX, pendingGaugeY);
        } else if (szPickerWidgetType === 'diPoint') {
            createDiPointWithPoint(point, pendingGaugeX, pendingGaugeY);
        } else if (szPickerWidgetType === 'aoPoint') {
            createAoPointWithPoint(point, pendingGaugeX, pendingGaugeY);
        } else if (szPickerWidgetType === 'doPoint') {
            createDoPointWithPoint(point, pendingGaugeX, pendingGaugeY);
        } else {
            createGaugeWithPoint(point, pendingGaugeX, pendingGaugeY);
        }
    }
}

// 從屬性面板「重選」呼叫（gauge）
async function rerouteGaugePoint() {
    if (!selectedEl || selectedEl.dataset.type !== 'gauge') return;
    pendingGaugeX      = -1;
    pendingGaugeY      = -1;
    szPickerWidgetType = 'gauge';
    szPickedSid        = null;
    nPickedDevId       = -1;
    try {
        await _ensurePickerData();
        _showPickerForBoundSid(_getBoundSidFromWidget(selectedEl));
    } catch (_) { /* 已在 _ensurePickerData 顯示 alert */ }
}

// 從屬性面板「重選」呼叫（controlBtn）
async function rerouteControlBtnPoint() {
    if (!selectedEl || selectedEl.dataset.type !== 'controlBtn') return;
    pendingGaugeX      = -1;
    pendingGaugeY      = -1;
    szPickerWidgetType = 'controlBtn';
    szPickedSid        = null;
    nPickedDevId       = -1;
    try {
        await _ensurePickerData();
        _showPickerForBoundSid(_getBoundSidFromWidget(selectedEl));
    } catch (_) { /* 已在 _ensurePickerData 顯示 alert */ }
}

// 從屬性面板「重選」呼叫（realtimeValue）
// 已綁迴路 → 直接開迴路分頁並高亮；否則走 SID 定位
async function rerouteRealtimeValuePoint() {
    if (!selectedEl || selectedEl.dataset.type !== 'realtimeValue') return;
    pendingGaugeX      = -1;
    pendingGaugeY      = -1;
    szPickerWidgetType = 'realtimeValue';
    szPickedSid        = null;
    nPickedDevId       = -1;
    nPickedCircuitId   = null;
    try {
        await _ensurePickerData();
        const props = selectedEl.widgetProps || {};
        if (props.nCircuitId != null) {
            _showPickerForBoundCircuit(props.nCircuitId);
        } else {
            _showPickerForBoundSid(_getBoundSidFromWidget(selectedEl));
        }
    } catch (_) { /* 已在 _ensurePickerData 顯示 alert */ }
}

// 開啟 picker 並停在迴路分頁（高亮已綁定迴路；迴路已刪除時仍顯示清單）
function _showPickerForBoundCircuit(nCircuitId) {
    if (!_pointPickerModal) {
        _pointPickerModal = new bootstrap.Modal(document.getElementById('pointPickerModal'));
    }
    _pointPickerModal.show();
    document.getElementById('ppStep0').style.display = 'none';
    const step3 = document.getElementById('ppStep3');
    if (step3) step3.style.display = 'none';
    showCircuitStep(nCircuitId);
}

// 從屬性面板「重選」呼叫（diPoint）
// DI widget 同時支援 SID 與排程綁定，須依當前綁定類型決定預設停留分頁
async function rerouteDiPointPoint() {
    if (!selectedEl || selectedEl.dataset.type !== 'diPoint') return;
    pendingGaugeX      = -1;
    pendingGaugeY      = -1;
    szPickerWidgetType = 'diPoint';
    szPickedSid        = null;
    nPickedDevId       = -1;
    nPickedScheduleId  = null;
    szPickedScheduleName = '';
    const props = selectedEl.widgetProps || {};
    const bScheduleBound = props.nScheduleId != null;
    szPickerSourceMode = bScheduleBound ? 'schedule' : 'point';
    _updateSourceToggleVisibility();
    try {
        await _ensurePickerData();
        if (bScheduleBound) {
            // 直接開排程分頁並嘗試高亮已綁定排程
            if (!_pointPickerModal) {
                _pointPickerModal = new bootstrap.Modal(document.getElementById('pointPickerModal'));
            }
            _pointPickerModal.show();
            // 手動切換到排程分頁（不經 switchPickerSource，因要 await 完成 list render 後再高亮）
            const btnP = document.getElementById('ppBtnPoint');
            const btnS = document.getElementById('ppBtnSchedule');
            if (btnP) btnP.classList.remove('active');
            if (btnS) btnS.classList.add('active');
            document.getElementById('ppStep0').style.display = 'none';
            document.getElementById('ppStep1').style.display = 'none';
            document.getElementById('ppStep2').style.display = 'none';
            document.getElementById('ppStep3').style.display = '';
            document.getElementById('ppModalTitle').textContent = t('designer.picker.title.schedule');
            szPickedSid = null;
            nPickedScheduleId = null;
            szPickedScheduleName = '';
            document.getElementById('btnConfirmPoint').disabled = true;
            await _renderScheduleList();
            const item = document.querySelector('#scheduleListContainer .point-list-item[data-schedule-id="' + props.nScheduleId + '"]');
            if (item) {
                item.classList.add('selected');
                nPickedScheduleId    = props.nScheduleId;
                szPickedScheduleName = props.szScheduleName || '';
                document.getElementById('btnConfirmPoint').disabled = false;
                item.scrollIntoView({ block: 'center', behavior: 'smooth' });
            }
        } else {
            _showPickerForBoundSid(_getBoundSidFromWidget(selectedEl));
        }
    } catch (_) { /* 已在 _ensurePickerData 顯示 alert */ }
}

// 從屬性面板「重選」呼叫（aoPoint）
async function rerouteAoPointPoint() {
    if (!selectedEl || selectedEl.dataset.type !== 'aoPoint') return;
    pendingGaugeX      = -1;
    pendingGaugeY      = -1;
    szPickerWidgetType = 'aoPoint';
    szPickedSid        = null;
    nPickedDevId       = -1;
    try {
        await _ensurePickerData();
        _showPickerForBoundSid(_getBoundSidFromWidget(selectedEl));
    } catch (_) { /* 已在 _ensurePickerData 顯示 alert */ }
}

// 從屬性面板「重選」呼叫（doPoint）
async function rerouteDoPointPoint() {
    if (!selectedEl || selectedEl.dataset.type !== 'doPoint') return;
    pendingGaugeX      = -1;
    pendingGaugeY      = -1;
    szPickerWidgetType = 'doPoint';
    szPickedSid        = null;
    nPickedDevId       = -1;
    try {
        await _ensurePickerData();
        _showPickerForBoundSid(_getBoundSidFromWidget(selectedEl));
    } catch (_) { /* 已在 _ensurePickerData 顯示 alert */ }
}

// 從屬性面板「重選」呼叫（pump 多綁定欄位）
async function reroutePumpBinding(szSlotKey, szNameKey) {
    if (!selectedEl || selectedEl.dataset.type !== 'pump') return;
    _pumpPickerSlot    = szSlotKey;
    _pumpPickerNameKey = szNameKey;
    pendingGaugeX      = -1;
    pendingGaugeY      = -1;
    szPickerWidgetType = 'pump';
    szPickedSid        = null;
    nPickedDevId       = -1;
    try {
        await _ensurePickerData();
        // 嘗試定位到已綁定的 SID
        const szBoundSid = selectedEl.widgetProps[szSlotKey] || '';
        _showPickerForBoundSid(szBoundSid);
    } catch (_) { /* 已在 _ensurePickerData 顯示 alert */ }
}

// 從屬性面板「重選」呼叫（冷卻水塔 / 空調箱風扇 / 冰機 多綁定欄位）
async function rerouteMotorBinding(szSlotKey, szNameKey) {
    if (!selectedEl) return;
    const szType = selectedEl.dataset.type;
    if (szType !== 'coolingTower' && szType !== 'ahuFan' && szType !== 'chiller') return;
    _pumpPickerSlot    = szSlotKey;
    _pumpPickerNameKey = szNameKey;
    pendingGaugeX      = -1;
    pendingGaugeY      = -1;
    szPickerWidgetType = szType;
    szPickedSid        = null;
    nPickedDevId       = -1;
    try {
        await _ensurePickerData();
        const szBoundSid = selectedEl.widgetProps[szSlotKey] || '';
        _showPickerForBoundSid(szBoundSid);
    } catch (_) { /* 已在 _ensurePickerData 顯示 alert */ }
}

// 從屬性面板「綁定/重選」呼叫（pipe — DI 或 類比二擇一互斥）
async function reroutePipeBinding(szMode) {
    if (!selectedEl || selectedEl.dataset.type !== 'pipe') return;
    _pipePickerMode    = (szMode === 'analog') ? 'analog' : 'di';
    pendingGaugeX      = -1;
    pendingGaugeY      = -1;
    szPickerWidgetType = 'pipe';
    szPickedSid        = null;
    nPickedDevId       = -1;
    try {
        await _ensurePickerData();
        // 目前已綁同一模式 → 定位到該 SID；否則從頭選
        const szBoundSid = (selectedEl.widgetProps.szBindMode === _pipePickerMode)
            ? (selectedEl.widgetProps.szSid || '') : '';
        _showPickerForBoundSid(szBoundSid);
    } catch (_) { /* 已在 _ensurePickerData 顯示 alert */ }
}

// 開啟點位選擇器（表格儲存格）
async function openCellPointPicker(nRow, nCol) {
    nSelectedCellRow = nRow;
    nSelectedCellCol = nCol;
    szPickerWidgetType = 'tableCell';
    pendingGaugeX = -1;
    pendingGaugeY = -1;
    nPickedCircuitId = null;
    await _ensurePickerData();
    // 已綁迴路指標 → 停在迴路分頁並高亮；已綁 SID → 直接跳到該點位
    let szBoundSid = '';
    let nBoundCircuitId = null;
    if (selectedEl && selectedEl.widgetProps) {
        const cell = selectedEl.widgetProps.arrCells?.[nRow]?.[nCol];
        if (cell && cell.szSid) szBoundSid = cell.szSid;
        if (cell && cell.nCircuitId != null) nBoundCircuitId = cell.nCircuitId;
    }
    if (nBoundCircuitId != null) {
        _showPickerForBoundCircuit(nBoundCircuitId);
    } else {
        _showPickerForBoundSid(szBoundSid);
    }
}

// ============================================================
// 能源迴路指標 — 綁定 helper + 表頭驅動整列自動帶入
// （plan 2026-07-23-designer-table-ems-accumulation-autofill）
// ============================================================

// 迴路指標合法值（day_kwh=本日度數(曆日) / month_kwh=本月度數(曆月) /
// period_kwh=本月電度(期別) / period_cost=本月電費(期別)）
const CIRCUIT_METRICS = ['day_kwh', 'month_kwh', 'period_kwh', 'period_cost'];

// 表格 cell 綁定迴路指標（與 szSid 互斥；plan 決策 3：新鍵最小化）
function _bindCellToCircuitMetric(cell, circuit, szMetric) {
    cell.nCircuitId    = circuit.id;
    cell.szCircuitName = circuit.name;   // 快照，執行期以 id 為準
    cell.szMetric      = CIRCUIT_METRICS.includes(szMetric) ? szMetric : 'day_kwh';
    cell.szSid         = '';
    cell.szPointName   = '';
}

// 清除表格 cell 的迴路指標鍵（解除綁定 / 改綁一般點位時呼叫；全 delete 保持 JSON 無殘留）
function _clearCellCircuitKeys(cell) {
    delete cell.nCircuitId;
    delete cell.szCircuitName;
    delete cell.szMetric;
}

// realtimeValue widget 綁定迴路指標（與 szSid / SID 型累積鍵互斥）
function _bindRtValueToCircuitMetric(props, circuit) {
    props.nCircuitId    = circuit.id;
    props.szCircuitName = circuit.name;
    if (!CIRCUIT_METRICS.includes(props.szMetric)) props.szMetric = 'day_kwh';
    props.szTitle       = circuit.name;
    props.szPointName   = '';
    props.szSid         = '';
    props.szUnit        = '';
    // SID 型累積鍵與迴路指標互斥，全 delete
    delete props.szValueMode;
    delete props.szAccKind;
    delete props.dMaxValue;
    delete props.szAccUnit;
}

// 清除 realtimeValue 的迴路指標鍵（改綁一般點位時呼叫）
function _clearRtValueCircuitKeys(props) {
    delete props.nCircuitId;
    delete props.szCircuitName;
    delete props.szMetric;
}

// ── realtimeValue MaxKwh 自動帶入 ──
// 條件：SID 型累積 meter 模式、溢位上限空白、綁定 SID 恰為某迴路的 kWh 累積讀值 SID
function _tryFillMaxKwhFromCircuit(props) {
    if (props.dMaxValue != null) return false;
    if (props.szValueMode !== 'day' && props.szValueMode !== 'month') return false;
    if ((props.szAccKind || 'meter') !== 'meter') return false;
    const hit = (props.szSid && _circuitBySid) ? _circuitBySid[props.szSid] : null;
    if (hit && hit.szRole === 'kwh' && hit.circuit.maxKwh != null) {
        props.dMaxValue = hit.circuit.maxKwh;
        return true;
    }
    return false;
}

// 供 prop-panel.js 切換顯示模式時呼叫（迴路資料未載入時先載）
async function tryFillCircuitMaxKwhAsync(props, fnDone) {
    try { await _ensureCircuitData(); } catch (_) { return; }
    if (_tryFillMaxKwhFromCircuit(props) && typeof fnDone === 'function') fnDone();
}

// ============================================================
// 表頭驅動整列自動帶入（plan 決策 2）
// 表格慣例：col 0 = 列名（迴路/設備名）、row 0 = 欄名。
// 綁定命中迴路後，同列空白 cell 依表頭別名帶入：
//   V/A/kW/PF/kWh → 自動綁對應即時點位（toast 摘要）
//   四指標欄     → confirm 一次詢問後綁為迴路指標 cell
// ============================================================

// 表頭別名表 — 集中一處，現場要加詞只改這裡。
// 比對規則：正規化（trim / 小寫 / 全形→半形 / 去尾端括號單位）後「完全相符」。
// key 前五項為即時值角色（對應迴路五 SID 欄），後四項為迴路指標。
const CIRCUIT_HEADER_ALIASES = {
    v:  ['v', '電壓', '電壓v', 'voltage', 'volt'],
    a:  ['a', '電流', '電流a', 'current', 'amp', 'amps'],
    kw: ['kw', '功率', '即時功率', '瞬時功率', '實功', 'power'],
    pf: ['pf', '功因', '功率因數', 'power factor', 'cosφ', 'cosphi'],
    kwh: ['kwh', '電度', '度數', '累積電度', '電表讀值', '累計度數', 'energy'],
    day_kwh:    ['本日度數', '本日用電', '今日度數', '今日用電', '當日度數', '當日用電', '當日累積', '日用電量',
                 '本日kwh', '今日kwh', '當日kwh', '日kwh', '本日用電量', 'today kwh', 'daily kwh', 'day kwh'],
    month_kwh:  ['本月度數', '本月用電', '當月度數', '當月用電', '當月累積', '月用電量',
                 '本月kwh', '當月kwh', '月kwh', '本月用電量', 'month kwh', 'monthly kwh'],
    period_kwh: ['本月電度', '本期電度', '本期度數', '本期用電', '本期kwh', 'period kwh', 'billing kwh'],
    period_cost: ['本月電費', '本期電費', '當月電費', '電費', '電費金額', 'cost', 'period cost', 'electricity cost']
};

// 即時值角色 → 迴路 SID 欄位名
const CIRCUIT_ROLE_SID_FIELD = { kwh: 'sid', v: 'voltageSid', a: 'currentSid', kw: 'powerSid', pf: 'powerFactorSid' };

// 表頭正規化：trim → 全形轉半形 → 去除尾端括號後綴（如「電壓(V)」「本月電費（元）」）→ 壓縮空白 → 小寫
function _normalizeHeaderText(szText) {
    if (!szText) return '';
    let s = String(szText).trim();
    // 全形 → 半形（含全形空白）
    s = s.replace(/[！-～]/g, ch => String.fromCharCode(ch.charCodeAt(0) - 0xfee0))
         .replace(/　/g, ' ');
    // 去尾端括號後綴（可多層，如「電費(元)(NTD)」）
    let prev;
    do { prev = s; s = s.replace(/\s*\([^()]*\)\s*$/, ''); } while (s !== prev);
    return s.replace(/\s+/g, ' ').trim().toLowerCase();
}

// 正規化表頭 → 命中的角色 key（v/a/kw/pf/kwh/day_kwh/...）或 null
function _matchHeaderRole(szHeaderText) {
    const szNorm = _normalizeHeaderText(szHeaderText);
    if (!szNorm) return null;
    for (const szRole of Object.keys(CIRCUIT_HEADER_ALIASES)) {
        if (CIRCUIT_HEADER_ALIASES[szRole].includes(szNorm)) return szRole;
    }
    return null;
}

// 依 SID 找點位顯示名（arrAllPoints 查無時回落「迴路名 角色」）
function _circuitPointDisplayName(szSid, circuit, szRole) {
    const p = (arrAllPoints || []).find(pt => pt.szSid === szSid);
    if (p) return p.szDeviceLabel ? p.szDeviceLabel + ' / ' + p.szName : p.szName;
    return circuit.name + ' ' + szRole.toUpperCase();
}

// 迴路角色 → 列範本角色 key（取 window._roleAliases 較豐富的點位名別名）
const CIRCUIT_ROLE_TO_TEMPLATE_ROLE = { v: 'V', a: 'A', kw: 'KW', pf: 'PF', kwh: 'KWH' };

// ── 同設備點位優先（v8，回應「KWH 建議到迴路設定的模擬點」）──
// 在「剛綁定點位」的同一設備（szDeviceLabel 相同）下，用點位名稱比對找對應角色的點位。
// 名稱比對：正規化後直接命中別名，或前綴式命名（PM-1-KWH / PM1_KWH / PM1 KWH）取最後分段命中。
function _findSameDevicePointForRole(pickedPoint, szRole) {
    if (!pickedPoint || !pickedPoint.szDeviceLabel || !arrAllPoints) return null;
    const szTplRole = CIRCUIT_ROLE_TO_TEMPLATE_ROLE[szRole];
    const aliases = ((window._roleAliases && szTplRole && window._roleAliases[szTplRole]) || [])
        .concat(CIRCUIT_HEADER_ALIASES[szRole] || []);
    for (const p of arrAllPoints) {
        if (p.szDeviceLabel !== pickedPoint.szDeviceLabel) continue;
        if (p.szSid === pickedPoint.szSid) continue;   // 排除剛綁定的點位本身
        const szNorm = _normalizeHeaderText(p.szName || '');
        if (!szNorm) continue;
        if (aliases.includes(szNorm)) return p;
        const nIdx = Math.max(szNorm.lastIndexOf('-'), szNorm.lastIndexOf('_'), szNorm.lastIndexOf(' '));
        if (nIdx > 0 && aliases.includes(szNorm.substring(nIdx + 1))) return p;
    }
    return null;
}

// ── 建立帶入建議清單（純函式，node 可測）──
// 掃描表頭（col 1 起、跳過使用者剛綁的欄），對命中別名的欄產生一筆 item：
//   szKind='realtime' — 迴路對應 SID 即時值（迴路未設定該 SID → bMissing）
//   szKind='metric'   — 迴路指標
//   bOccupied         — 該 cell 已綁定（顯示但不可勾選，不覆蓋）
// 回傳 { arrItems, bAnyHeaderHit, nMetricHits }
// pickedPoint（可為 null）＝使用者剛綁定的點位；即時值欄的建議來源順序：
//   1) 同設備名稱比對點位（_findSameDevicePointForRole）— 列＝設備的心智模型優先
//   2) 迴路五 SID 欄（虛擬迴路/迴路綁定時的唯一來源）
function _buildCircuitFillItems(props, nRowIdx, nPickedCol, circuit, pickedPoint) {
    const headers = props.arrCells[0] || [];
    const row     = props.arrCells[nRowIdx] || [];
    const arrItems = [];
    let bAnyHeaderHit = false;
    let nMetricHits = 0;

    for (let ci = 1; ci < row.length; ci++) {
        if (ci === nPickedCol) continue;
        const szHeader = headers[ci] ? (headers[ci].szText || '') : '';
        const szRole = _matchHeaderRole(szHeader);
        if (!szRole) continue;
        bAnyHeaderHit = true;
        const cell = row[ci];
        const bOccupied = !cell || !!cell.szSid || cell.nCircuitId != null;

        if (CIRCUIT_ROLE_SID_FIELD[szRole]) {
            let szSid = '';
            let szPointName = '';
            const pSame = _findSameDevicePointForRole(pickedPoint, szRole);
            if (pSame) {
                szSid = pSame.szSid;
                szPointName = pSame.szDeviceLabel ? pSame.szDeviceLabel + ' / ' + pSame.szName : pSame.szName;
            } else {
                szSid = circuit[CIRCUIT_ROLE_SID_FIELD[szRole]] || '';
                if (szSid) szPointName = _circuitPointDisplayName(szSid, circuit, szRole);
            }
            arrItems.push({
                nCol: ci, szHeader: szHeader, szKind: 'realtime', szRole: szRole,
                szSid: szSid, szPointName: szPointName,
                bMissing: !szSid, bOccupied: bOccupied
            });
        } else {
            nMetricHits++;
            arrItems.push({
                nCol: ci, szHeader: szHeader, szKind: 'metric', szMetric: szRole,
                bMissing: false, bOccupied: bOccupied
            });
        }
    }
    return { arrItems: arrItems, bAnyHeaderHit: bAnyHeaderHit, nMetricHits: nMetricHits };
}

// ── 套用勾選項目（純套用，node 可測）── 回傳 { nRealtime, nMetric }
function _applyCircuitFillItems(props, nRowIdx, circuit, arrSelectedItems) {
    const row = props.arrCells[nRowIdx] || [];
    let nRealtime = 0, nMetric = 0;
    arrSelectedItems.forEach(item => {
        const cell = row[item.nCol];
        if (!cell || item.bMissing || item.bOccupied) return;
        if (cell.szSid || cell.nCircuitId != null) return;   // 再驗一次不覆蓋
        if (item.szKind === 'realtime') {
            cell.szSid       = item.szSid;
            cell.szPointName = item.szPointName;
            cell.szPointType = 'AI';
            nRealtime++;
        } else if (item.szKind === 'metric') {
            _bindCellToCircuitMetric(cell, circuit, item.szMetric);
            nMetric++;
        }
    });
    return { nRealtime: nRealtime, nMetric: nMetric };
}

// ── 帶入確認 modal（欄名 → 帶入內容清單，勾選客製後才套用）──
let _cmetricFillModalInstance = null;
let _cmetricFillCtx = null;   // { widgetEl, nRowIdx, nPickedCol, circuit, arrItems }

function _showCircuitFillModal(ctx) {
    const body   = document.getElementById('cmetricFillBody');
    const footer = document.getElementById('cmetricFillFooter');
    const modalEl = document.getElementById('cmetricFillModal');
    if (!body || !footer || !modalEl) {
        // modal 不存在（防呆）：直接套用全部可帶入項
        _doCircuitFill(ctx, ctx.arrItems.filter(i => !i.bMissing && !i.bOccupied));
        return;
    }
    _cmetricFillCtx = ctx;

    const szRows = ctx.arrItems.map((item, idx) => {
        const bSelectable = !item.bMissing && !item.bOccupied;
        let szContent, szTag, szTagClass;
        if (item.szKind === 'metric') {
            szContent  = escHtml(t('designer.metric.' + item.szMetric));
            szTag      = t('designer.cmetric.fill_tag_metric');
            szTagClass = 'rt-found';
        } else if (item.bMissing) {
            szContent  = '<span class="rt-na">&#x2014;</span>';
            szTag      = t('designer.cmetric.fill_tag_missing');
            szTagClass = 'rt-missing';
        } else {
            szContent  = escHtml(item.szPointName);
            szTag      = t('designer.cmetric.fill_tag_realtime');
            szTagClass = 'rt-found';
        }
        if (item.bOccupied) {
            szTag      = t('designer.cmetric.fill_tag_bound');
            szTagClass = 'rt-missing';
        }
        const szCb = bSelectable
            ? `<input type="checkbox" class="cmf-cb" data-idx="${idx}" checked
                      style="width:auto;cursor:pointer;flex-shrink:0;margin:0;">`
            : '<span style="width:13px;flex-shrink:0;"></span>';
        return `<label class="rt-preview-row${bSelectable ? '' : ' rt-row-missing'}" style="cursor:${bSelectable ? 'pointer' : 'default'};margin-bottom:0;">
            ${szCb}
            <span class="rt-role">${escHtml(item.szHeader)}</span>
            <span class="rt-arrow">&#x2192;</span>
            <span class="rt-point">${szContent}</span>
            <span class="rt-tag ${szTagClass}">${escHtml(szTag)}</span>
        </label>`;
    }).join('');

    // 列名（col 0）與迴路名不一致時提醒 — 綁到別的迴路的點位時最容易在這裡發現
    const szRowLabel = (ctx.szRowLabel || '').trim();
    const szMismatchWarn = (szRowLabel && szRowLabel !== (ctx.circuit.name || '').trim())
        ? '<div style="color:#ffc107;font-size:11px;margin-bottom:8px;"><i class="fas fa-exclamation-triangle me-1"></i>' +
          escHtml(t('designer.cmetric.fill_row_mismatch', { row: szRowLabel, name: ctx.circuit.name })) + '</div>'
        : '';

    body.innerHTML =
        '<div class="rt-detected">' + escHtml(t('designer.cmetric.fill_detected', { name: ctx.circuit.name })) + '</div>' +
        szMismatchWarn +
        '<div class="rt-preview-title">' + escHtml(t('designer.cmetric.fill_preview_title')) + '</div>' +
        '<div class="rt-preview-list">' + szRows + '</div>';

    footer.innerHTML =
        '<button type="button" class="btn btn-sm btn-outline-secondary" id="btnCmfSkip">' +
            '<i class="fas fa-times me-1"></i>' + escHtml(t('designer.cmetric.fill_btn_skip')) +
        '</button>' +
        '<button type="button" class="btn btn-sm btn-success" id="btnCmfApply">' +
            '<i class="fas fa-magic me-1"></i>' + escHtml(t('designer.cmetric.fill_btn_apply')) +
        '</button>';

    if (!_cmetricFillModalInstance) {
        _cmetricFillModalInstance = new bootstrap.Modal(modalEl);
    }
    document.getElementById('btnCmfSkip').onclick = () => _cmetricFillModalInstance.hide();
    document.getElementById('btnCmfApply').onclick = () => {
        const checkedIdx = Array.from(body.querySelectorAll('.cmf-cb:checked')).map(cb => parseInt(cb.dataset.idx, 10));
        const arrSelected = _cmetricFillCtx.arrItems.filter((_, idx) => checkedIdx.includes(idx));
        _cmetricFillModalInstance.hide();
        _doCircuitFill(_cmetricFillCtx, arrSelected);
    };
    _cmetricFillModalInstance.show();
}

// ── 套用 + 重繪 + toast（modal 確認後）──
function _doCircuitFill(ctx, arrSelectedItems) {
    const { nRealtime, nMetric } = _applyCircuitFillItems(
        ctx.widgetEl.widgetProps, ctx.nRowIdx, ctx.circuit, arrSelectedItems);
    if (nRealtime + nMetric === 0) return;
    if (typeof renderWidget === 'function') renderWidget(ctx.widgetEl);
    const sel = ctx.widgetEl.querySelector(
        '.w-table [data-row="' + ctx.nRowIdx + '"][data-col="' + ctx.nPickedCol + '"]');
    if (sel) sel.classList.add('selected-cell');
    if (typeof renderTableCellPropPanel === 'function') {
        renderTableCellPropPanel(ctx.widgetEl, ctx.nRowIdx, ctx.nPickedCol);
    }
    if (typeof showDesignToast === 'function') {
        const parts = [];
        if (nRealtime > 0) parts.push(t('designer.cmetric.autofill_toast', { count: nRealtime }));
        if (nMetric > 0) parts.push(t('designer.cmetric.autofill_toast_metrics', { count: nMetric }));
        showDesignToast('info', '<i class="fas fa-magic me-1"></i>' + escHtml(parts.join(' ')));
    }
}

// ── 主流程：綁定當下觸發（綁迴路指標 cell 或 SID 反查命中迴路的即時值 cell）──
// 即時值欄與指標欄一律走同一個確認清單 modal（勾選客製後才帶入，不再默默帶）。
// 回傳 true = 本流程已接手；false = 呼叫端回落既有列範本流程。
// 回落規則（v8：任一表頭命中即接手 — 即時值欄也依表頭帶，不需有指標欄）：
//   - 表頭「完全無命中」且綁一般點位 → false（走列範本固定樣板，原行為保留）
//   - 綁迴路（bFromCircuitBinding=true）→ 一律接手（列範本需要點位名比對，迴路綁定無從回落）
function _tryCircuitRowAutoFill(widgetEl, nRowIdx, nPickedCol, circuit, bFromCircuitBinding, pickedPoint) {
    try {
        if (!widgetEl || !circuit || nRowIdx == null || nRowIdx < 1) return false;
        const props = widgetEl.widgetProps;
        if (typeof initArrCells === 'function') initArrCells(props);
        if (!props.arrCells[nRowIdx]) return false;

        const { arrItems, bAnyHeaderHit } =
            _buildCircuitFillItems(props, nRowIdx, nPickedCol, circuit, pickedPoint || null);

        if (!bAnyHeaderHit) return !!bFromCircuitBinding;               // 表頭全無命中 → 點位綁定回落列範本

        const bHasSelectable = arrItems.some(i => !i.bMissing && !i.bOccupied);
        if (!bHasSelectable) return true;                               // 命中但全被綁定/未設定 → 無事可做

        _showCircuitFillModal({
            widgetEl: widgetEl, nRowIdx: nRowIdx, nPickedCol: nPickedCol,
            circuit: circuit, arrItems: arrItems,
            szRowLabel: (props.arrCells[nRowIdx][0] && props.arrCells[nRowIdx][0].szText) || ''
        });
        return true;
    } catch (err) {
        console.warn('[circuit-autofill] error:', err);
        return false;
    }
}
