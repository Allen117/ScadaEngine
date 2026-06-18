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

const CALC_DEVICE_ID = -999; // 計算點位的虛擬設備 ID
const DB_DEVICE_ID   = -998; // DB 來源點位的虛擬設備 ID

// 水泵綁定欄位（多 SID/CID）
let _pumpPickerSlot    = '';   // 目前正在選擇的 pump 綁定欄位 key（如 'szSidRun'、'szCidStartStop'）
let _pumpPickerNameKey = '';   // 對應的名稱 key（如 'szRunName'、'szStartStopName'）

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
    if (arrAllDevices && arrAllPoints) return;
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

function goBackToStep0() {
    document.getElementById('ppStep0').style.display = '';
    document.getElementById('ppStep1').style.display = 'none';
    document.getElementById('ppStep2').style.display = 'none';
    document.getElementById('ppModalTitle').textContent = t('designer.picker.title.source');
    document.getElementById('btnConfirmPoint').disabled = true;
    szPickedSid = null;
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
        // 列範本（plan 2026-06-01-designer-row-template）— 待 picker 動畫收完再彈
        if (window._rowTemplate && typeof window._rowTemplate.tryAutoFill === 'function') {
            const widgetEl   = selectedEl;
            const nRowIdx    = nSelectedCellRow;
            const nPickedCol = nSelectedCellCol;
            setTimeout(() => {
                window._rowTemplate.tryAutoFill(widgetEl, nRowIdx, nPickedCol, point);
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
        } else if (szType === 'pump') {
            if (_pumpPickerSlot) {
                selectedEl.widgetProps[_pumpPickerSlot]  = point.szSid;
                selectedEl.widgetProps[_pumpPickerNameKey] = szFullName;
                if (_pumpPickerSlot === 'szSidFreq') {
                    selectedEl.widgetProps.nFreqMax = point.fMax ?? 60;
                }
                if (_pumpPickerSlot === 'szCidFreqSet') {
                    selectedEl.widgetProps.nFreqSetMin = point.fMin ?? 0;
                    selectedEl.widgetProps.nFreqSetMax = point.fMax ?? 60;
                }
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
async function rerouteRealtimeValuePoint() {
    if (!selectedEl || selectedEl.dataset.type !== 'realtimeValue') return;
    pendingGaugeX      = -1;
    pendingGaugeY      = -1;
    szPickerWidgetType = 'realtimeValue';
    szPickedSid        = null;
    nPickedDevId       = -1;
    try {
        await _ensurePickerData();
        _showPickerForBoundSid(_getBoundSidFromWidget(selectedEl));
    } catch (_) { /* 已在 _ensurePickerData 顯示 alert */ }
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

// 開啟點位選擇器（表格儲存格）
async function openCellPointPicker(nRow, nCol) {
    nSelectedCellRow = nRow;
    nSelectedCellCol = nCol;
    szPickerWidgetType = 'tableCell';
    pendingGaugeX = -1;
    pendingGaugeY = -1;
    await _ensurePickerData();
    // 若該儲存格已綁定 SID，直接跳到該點位
    let szBoundSid = '';
    if (selectedEl && selectedEl.widgetProps) {
        const cell = selectedEl.widgetProps.arrCells?.[nRow]?.[nCol];
        if (cell && cell.szSid) szBoundSid = cell.szSid;
    }
    _showPickerForBoundSid(szBoundSid);
}
