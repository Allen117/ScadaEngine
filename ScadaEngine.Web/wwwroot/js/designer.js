// ============================================================
// 元件庫收合
// ============================================================
function toggleComponentPanel() {
    const panel = document.getElementById('componentPanel');
    const tab   = document.getElementById('compExpandTab');
    const isCollapsed = panel.classList.toggle('collapsed');
    tab.style.display = isCollapsed ? 'flex' : 'none';
}

// ============================================================
// 狀態
// ============================================================
let nWidgetCounter = 0;
let selectedEl = null;
let selectedWidgetIds = new Set();   // 多選元件 Id 集合

// 移動
let isMoving = false;
let moveStartMouse = { x: 0, y: 0 };    // 群組拖曳起始滑鼠座標
let moveOrigPositions = {};               // 群組拖曳原始位置 { widgetId: {x,y} }

// 縮放
let isResizing = false;
let resizingEl = null;
let resizeStart = { mx: 0, my: 0, w: 0, h: 0 };

// 目前背景圖追蹤（供頁面切換時儲存）
let szCurrentBgDataUrl  = null;
let szCurrentBgFileName = null;

// ============================================================
// 頁面樹資料結構
// ============================================================
let nPageIdCounter = 1;
let szCurrentPageId = 'p1';

let arrPageTree = [{
    szId:           'p1',
    szName:         '主頁面',
    szIcon:         'fa-home',
    arrChildren:    [],
    szBgDataUrl:    null,
    szBgFileName:   null,
    nCanvasW:       1200,
    nCanvasH:       800,
    arrWidgetState: []
}];

// ── 警報規則快取（僅用於鈴鐺圖示顯示）──
let _setAlarmSids = new Set();
async function loadAlarmRuleSids() {
    try {
        const resp = await fetch('/api/alarm-rules');
        if (resp.ok) {
            const arr = await resp.json();
            _setAlarmSids = new Set(arr.filter(r => r.isEnabled).map(r => r.szSID));
        }
    } catch (ex) { console.warn('載入警報規則失敗:', ex); }
}
function hasAlarmRule(szSid) { return szSid && _setAlarmSids.has(szSid); }

const canvas = document.getElementById('canvas');

// ============================================================
// Widget 定義
// ============================================================
const WIDGET_DEFS = {
    table: {
        szLabel: '表格',
        szIcon: 'fas fa-table',
        nDefaultW: 360,
        nDefaultH: 220,
        nMinW: 120, nMinH: 60,
        defaultProps: {
            szTitle: '數據表格',
            nRows: 5,
            nCols: 3,
            szHeaderColor: '#343a40',
            arrCells: null,
            arrColDecimals: null
        }
    },
    gauge: {
        szLabel: '儀錶板',
        szIcon: 'fas fa-tachometer-alt',
        nDefaultW: 220,
        nDefaultH: 175,
        nMinW: 100, nMinH: 80,
        defaultProps: {
            szTitle:     '溫度',
            szSid:       '',
            szPointName: '',
            fValue:  50.0,
            fMin:    0.0,
            fMax:    100.0,
            szUnit:  '°C',
            szColor: '#0d6efd',
            szBgColor: 'transparent',
            szHighColor: '#dc3545',
            szLowColor:  '#fd7e14'
        }
    },
    text: {
        szLabel: '文字',
        szIcon: 'fas fa-font',
        nDefaultW: 160,
        nDefaultH: 60,
        nMinW: 40, nMinH: 20,
        defaultProps: {
            szTitle:      '文字標籤',
            szText:       '文字內容',
            szFontFamily: 'inherit',
            szFontColor:  '#212529',
            nFontSize:    18,
            szFontWeight: 'normal',
            isItalic:     false,
            szBgColor:    'transparent'
        }
    },
    controlBtn: {
        szLabel: '控制按鈕',
        szIcon: 'fas fa-toggle-on',
        nDefaultW: 100,
        nDefaultH: 38,
        nMinW: 60, nMinH: 20,
        defaultProps: {
            szTitle:     '控制',
            szCid:       '',
            szPointName: '',
            szBtnLabel:  '執行',
            szBtnIcon:   'fa-hand-pointer',
            fCtrlValue:  1,
            szBtnColor:  '#198754',
            szBgColor:   'transparent',
            nFontSize:   12
        }
    },
    realtimeValue: {
        szLabel: 'AI 點位',
        szIcon: 'fas fa-digital-tachograph',
        nDefaultW: 100,
        nDefaultH: 20,
        nMinW: 60, nMinH: 20,
        defaultProps: {
            szTitle:     'AI 點位',
            szSid:       '',
            szPointName: '',
            szUnit:      '',
            nFontSize:   12,
            szFontColor: '#212529',
            szBgColor:   'transparent',
            szHighColor: '#dc3545',
            szLowColor:  '#fd7e14'
        }
    },
    diPoint: {
        szLabel: 'DI 點位',
        szIcon: 'fas fa-circle',
        nDefaultW: 100,
        nDefaultH: 20,
        nMinW: 60, nMinH: 20,
        defaultProps: {
            szTitle:        'DI 點位',
            szSid:          '',
            szPointName:    '',
            szDisplayMode:  'indicator',   // 'indicator' = 燈號, 'text' = 文字
            szOnColor:      '#28a745',
            szOffColor:     '#6c757d',
            szOnLabel:      'ON',
            szOffLabel:     'OFF',
            nIndicatorSize: 14,
            nFontSize:      12,
            szFontColor:    '#212529',
            szBgColor:      'transparent',
            szAlarmColor:   '#dc3545'
        }
    },
    aoPoint: {
        szLabel: 'AO 點位',
        szIcon: 'fas fa-sliders-h',
        nDefaultW: 100,
        nDefaultH: 20,
        nMinW: 60, nMinH: 20,
        defaultProps: {
            szTitle:            'AO 點位',
            szDisplayName:      '',
            szCid:              '',
            szPointName:        '',
            szUnit:             '',
            fWriteValue:        0,
            fMin:               0,
            fMax:               100,
            fStep:              1,
            nDecimalPlaces:     2,
            nFontSize:          12,
            szFontColor:        '#ffffff',
            szMenuManualLabel:  '手動控制',
            szMenuAutoLabel:    '自動控制',
            szBgColor:          'transparent',
            szBlockColor:       '#0d6efd'
        }
    },
    doPoint: {
        szLabel: 'DO 點位',
        szIcon: 'fas fa-toggle-on',
        nDefaultW: 100,
        nDefaultH: 20,
        nMinW: 60, nMinH: 20,
        defaultProps: {
            szTitle:          'DO 點位',
            szDisplayName:    '',
            szCid:            '',
            szPointName:      '',
            szOnLabel:        'ON',
            szOffLabel:       'OFF',
            szOnColor:        '#28a745',
            szOffColor:       '#dc3545',
            nFontSize:        12,
            szFontColor:      '#ffffff',
            nOnValue:         1,
            nOffValue:        0,
            szMenuOnLabel:    '手動ON',
            szMenuOffLabel:   '手動OFF',
            szMenuAutoLabel:  '自動控制',
            szBgColor:        'transparent',
            szBlockColor:     '#0d6efd'
        }
    },
    pump: {
        szLabel: '水泵',
        szIcon: 'fas fa-fan',
        nDefaultW: 130,
        nDefaultH: 120,
        nMinW: 60, nMinH: 60,
        defaultProps: {
            szTitle:          '水泵',
            // SID 點位（唯讀監控）
            szSidRun:         '',   szRunName:       '',   // 運轉狀態
            szSidFault:       '',   szFaultName:     '',   // 故障狀態
            szSidMode:        '',   szModeName:      '',   // 手自動狀態
            szSidFreq:        '',   szFreqName:      '',   // 頻率
            // CID 點位（控制寫入）
            szCidStartStop:   '',   szStartStopName: '',   // 啟動停止
            szCidFreqSet:     '',   szFreqSetName:   '',   // 頻率設定
            nFreqSetMin:      0,
            nFreqSetMax:      60,
            // 外觀
            nFreqMax:         60,
            szManualColor:    '#ffc107',
            szAutoColor:      '#0d6efd',
            szRunColor:       '#28a745',
            szStopColor:      '#6c757d',
            szFaultColor:     '#dc3545',
            szOutletDir:      'right',
            szBgColor:        'transparent'
        }
    }
};

// ============================================================
// 計算 Designer 高度（動態適應 navbar）
// ============================================================
function adjustHeight() {
    const navbar = document.querySelector('.navbar');
    const navH = navbar ? navbar.offsetHeight : 56;
    document.getElementById('designerOuter').style.height = (window.innerHeight - navH) + 'px';
}
adjustHeight();
window.addEventListener('resize', adjustHeight);

// ============================================================
// 元件庫拖曳
// ============================================================
document.querySelectorAll('.widget-lib-item').forEach(item => {
    item.addEventListener('dragstart', e => {
        e.dataTransfer.setData('widgetType', item.dataset.widgetType);
        e.dataTransfer.effectAllowed = 'copy';
    });
});

canvas.addEventListener('dragover', e => {
    e.preventDefault();
    e.dataTransfer.dropEffect = 'copy';
    canvas.classList.add('drag-over');
});

canvas.addEventListener('dragleave', e => {
    if (e.target === canvas) canvas.classList.remove('drag-over');
});

canvas.addEventListener('drop', e => {
    e.preventDefault();
    canvas.classList.remove('drag-over');

    const szType = e.dataTransfer.getData('widgetType');
    if (!szType || !WIDGET_DEFS[szType]) return;

    const rect = canvas.getBoundingClientRect();
    const def = WIDGET_DEFS[szType];
    // 置中於滑鼠落點，並貼齊 20px 格
    let x = snapGrid(e.clientX - rect.left - def.nDefaultW / 2);
    let y = snapGrid(e.clientY - rect.top - 20);
    x = Math.max(0, x);
    y = Math.max(0, y);

    if (szType === 'gauge' || szType === 'controlBtn' || szType === 'realtimeValue' || szType === 'diPoint' || szType === 'aoPoint' || szType === 'doPoint') {
        // 儀錶板 / 控制按鈕 / AI點位 / DI點位 / AO點位 需先選擇綁定點位
        openPointPicker(x, y, szType);
    } else {
        createWidget(szType, x, y);
    }
});

// ============================================================
// 建立 Widget
// ============================================================
function createWidget(szType, x, y) {
    const def = WIDGET_DEFS[szType];
    const szId = 'w' + (++nWidgetCounter);

    const el = document.createElement('div');
    el.id = szId;
    el.className = 'canvas-widget';
    el.dataset.type = szType;
    el.style.left = x + 'px';
    el.style.top  = y + 'px';
    el.style.width  = def.nDefaultW + 'px';
    el.style.height = def.nDefaultH + 'px';
    el.widgetProps = { ...def.defaultProps };

    renderWidget(el);
    canvas.appendChild(el);
    selectWidget(el);
}

// ============================================================
// 渲染 Widget 內容
// ============================================================
function renderWidget(el) {
    const szType = el.dataset.type;

    // 文字 Widget 使用獨立渲染
    if (szType === 'text') { renderTextWidget(el); return; }

    const def    = WIDGET_DEFS[szType];
    const props  = el.widgetProps;

    const szContent = szType === 'table'          ? buildTableHtml(props)
                    : szType === 'controlBtn'     ? buildControlBtnHtml(props)
                    : szType === 'realtimeValue'  ? buildRealtimeValueHtml(props)
                    : szType === 'diPoint'        ? buildDiPointHtml(props)
                    : szType === 'aoPoint'        ? buildAoPointHtml(props)
                    : szType === 'doPoint'        ? buildDoPointHtml(props)
                    : szType === 'pump'           ? buildPumpHtml(props, 'stop')
                    : buildGaugeHtml(props);

    // hover tooltip（controlBtn / realtimeValue）
    const szPointLabel = props.szPointName || props.szCid || '';
    const bShowTooltip = (szType === 'controlBtn' || szType === 'realtimeValue' || szType === 'diPoint' || szType === 'aoPoint' || szType === 'doPoint') && szPointLabel;
    const szTooltipHtml = bShowTooltip
        ? `<div class="widget-hover-tooltip">${escHtml(szPointLabel)}</div>` : '';

    el.innerHTML = `
        <div class="widget-header">
            <i class="${def.szIcon} me-1" style="opacity:.7;font-size:10px;"></i>
            <span class="wh-title">${props.szTitle}</span>
            <button class="widget-del" onclick="deleteWidget('${el.id}')" title="刪除元件">✕</button>
        </div>
        <div class="widget-body">${szContent}</div>
        <div class="resize-knob" title="拖曳縮放"></div>
        ${szTooltipHtml}
    `;

    // 透明背景切換
    // AO/DO 點位永遠透明背景（方塊顏色由 szBlockColor 控制）
    // 控制按鈕使用專屬無外框模式
    if (szType === 'controlBtn') {
        el.classList.add('widget-ctrl-btn');
    } else if (szType === 'realtimeValue') {
        el.classList.add('widget-rv');
    } else if (szType === 'diPoint') {
        el.classList.add('widget-di');
    } else if (szType === 'aoPoint') {
        el.classList.add('widget-ao');
    } else if (szType === 'doPoint') {
        el.classList.add('widget-do');
    } else if ('szBgColor' in props) {
        const isBgTransparent = !props.szBgColor || props.szBgColor === 'transparent';
        el.classList.toggle('widget-transparent', isBgTransparent);
    }

    // 控制按鈕：整個 body 可拖移
    if (szType === 'controlBtn') {
        el.querySelector('.ctrl-btn-body').addEventListener('mousedown', e => {
            e.preventDefault();
            startMove(e, el);
        });
    }

    // AI / DI / AO / DO 點位：整個 body 可拖移（header 已隱藏）
    if (szType === 'realtimeValue' || szType === 'diPoint' || szType === 'aoPoint' || szType === 'doPoint') {
        el.querySelector('.widget-body').addEventListener('mousedown', e => {
            e.preventDefault();
            startMove(e, el);
        });
    }

    // 拖移（header）— controlBtn / realtimeValue / diPoint 已用 body 拖移
    if (szType !== 'controlBtn' && szType !== 'realtimeValue' && szType !== 'diPoint' && szType !== 'aoPoint' && szType !== 'doPoint') el.querySelector('.widget-header').addEventListener('mousedown', e => {
        if (e.target.classList.contains('widget-del')) return;
        e.preventDefault();
        startMove(e, el);
    });

    // 縮放（右下角）
    el.querySelector('.resize-knob').addEventListener('mousedown', e => {
        e.preventDefault();
        e.stopPropagation();
        startResize(e, el);
    });

    // 點選選取（支援 Ctrl 多選）
    el.addEventListener('mousedown', (ev) => onWidgetMouseDown(ev, el));

    // 表格儲存格事件（點擊/右鍵）
    if (szType === 'table') {
        el.querySelectorAll('.w-table td, .w-table th').forEach(td => {
            td.addEventListener('click', e => {
                e.stopPropagation();
                const nRow = parseInt(td.dataset.row);
                const nCol = parseInt(td.dataset.col);
                onTableCellClick(el, nRow, nCol);
            });
            td.addEventListener('contextmenu', e => {
                e.preventDefault();
                e.stopPropagation();
                const nRow = parseInt(td.dataset.row);
                const nCol = parseInt(td.dataset.col);
                onTableCellCtxMenu(e, el, nRow, nCol);
            });
        });
    }
}

// ============================================================
// 文字 Widget 渲染
// ============================================================
function renderTextWidget(el) {
    const props = el.widgetProps;
    const szFontStyle = props.isItalic ? 'italic' : 'normal';
    const szBg        = props.szBgColor || 'transparent';

    el.innerHTML = `
        <div class="text-widget-body" style="
            font-family:${props.szFontFamily};
            font-size:${props.nFontSize}px;
            color:${props.szFontColor};
            font-weight:${props.szFontWeight};
            font-style:${szFontStyle};
            background:${szBg};
        ">${escHtml(props.szText)}</div>
        <button class="text-del-btn" onclick="deleteWidget('${el.id}')" title="刪除文字">✕</button>
        <div class="resize-knob" title="拖曳縮放"></div>
    `;

    // 拖移（整個文字主體）
    el.querySelector('.text-widget-body').addEventListener('mousedown', e => {
        e.preventDefault();
        startMove(e, el);
    });

    // 縮放（右下角）
    el.querySelector('.resize-knob').addEventListener('mousedown', e => {
        e.preventDefault();
        e.stopPropagation();
        startResize(e, el);
    });

    // 點選選取（支援 Ctrl 多選）
    el.addEventListener('mousedown', (ev) => onWidgetMouseDown(ev, el));
}

// ============================================================
// 表格 HTML
// ============================================================
const TABLE_SAMPLE = [
    ['溫度感測器', '85.3°C', '正常', '2026-03-05'],
    ['壓力感測器', '2.40 bar', '正常', '2026-03-05'],
    ['流量計',     '12.7 L/s', '警告', '2026-03-05'],
    ['液位感測器', '67.2%',   '正常', '2026-03-05'],
    ['電流感測器', '32.1 A',  '正常', '2026-03-05'],
    ['電壓感測器', '220 V',   '正常', '2026-03-05'],
];

const COL_HEADERS = ['名稱', '數值', '狀態', '時間戳'];

// ── 儲存格預設值 ──
function _defaultHeaderCell(ci) {
    return { szText: '', szFontColor: '#fff', szFontWeight: '500', szAlign: 'left', nFontSize: 11 };
}
function _defaultDataCell(ri, ci) {
    return {
        szText: '', szFontColor: '#444', szFontWeight: 'normal', szAlign: 'left', nFontSize: 12,
        szSid: '', szPointName: '', szPointType: 'AI',
        szOnLabel: 'ON', szOffLabel: 'OFF',
        szHighColor: '#dc3545', szLowColor: '#fd7e14',
        szAlarmColor: '#dc3545'
    };
}

// ── 初始化 arrCells（新建或舊資料遷移）──
function initArrCells(props) {
    const nC = Math.max(1, props.nCols || 3);
    const nR = Math.max(1, props.nRows || 5);
    if (!props.arrCells) {
        props.arrCells = [];
        // row 0 = 標題列
        props.arrCells.push(Array.from({ length: nC }, (_, ci) => _defaultHeaderCell(ci)));
        // row 1..nR = 資料列
        for (let ri = 0; ri < nR; ri++) {
            props.arrCells.push(Array.from({ length: nC }, (_, ci) => _defaultDataCell(ri, ci)));
        }
    }
    if (!props.arrColDecimals) {
        props.arrColDecimals = Array.from({ length: nC }, () => null);
    }
}

// ── 當 nRows/nCols 改變時同步 arrCells 大小 ──
function syncArrCellsSize(props) {
    if (!props.arrCells) { initArrCells(props); return; }
    const nC = Math.max(1, props.nCols || 3);
    const nR = Math.max(1, props.nRows || 5);
    const nTotalRows = nR + 1; // +1 for header
    // 調整列數
    while (props.arrCells.length < nTotalRows) {
        const ri = props.arrCells.length - 1;
        props.arrCells.push(Array.from({ length: nC }, (_, ci) => _defaultDataCell(ri, ci)));
    }
    while (props.arrCells.length > nTotalRows) props.arrCells.pop();
    // 調整欄數
    props.arrCells.forEach((row, ri) => {
        while (row.length < nC) {
            const ci = row.length;
            row.push(ri === 0 ? _defaultHeaderCell(ci) : _defaultDataCell(ri - 1, ci));
        }
        while (row.length > nC) row.pop();
    });
    // 同步 arrColDecimals
    while (props.arrColDecimals.length < nC) props.arrColDecimals.push(null);
    while (props.arrColDecimals.length > nC) props.arrColDecimals.pop();
}

function buildTableHtml(props) {
    initArrCells(props);
    const nC = Math.max(1, props.nCols || 3);
    const nR = Math.max(1, props.nRows || 5);
    const arrCells = props.arrCells;

    // 標題列 (row 0)
    const szHeaders = arrCells[0].slice(0, nC).map((cell, ci) =>
        `<th data-row="0" data-col="${ci}" style="background:${props.szHeaderColor};
            color:${cell.szFontColor || '#fff'};font-weight:${cell.szFontWeight || '500'};
            font-size:${cell.nFontSize || 11}px;text-align:${cell.szAlign || 'left'};
            cursor:pointer;">${escHtml(cell.szText || '')}</th>`
    ).join('');

    // 資料列 (row 1..nR)
    const szRows = Array.from({ length: nR }, (_, ri) => {
        const rowIdx = ri + 1;
        if (rowIdx >= arrCells.length) return '';
        const row = arrCells[rowIdx];
        const szCells = row.slice(0, nC).map((cell, ci) => {
            const szSidAttr = cell.szSid ? `data-sid="${escHtml(cell.szSid)}"` : '';
            const szSidLabel = cell.szSid
                ? `<span style="font-size:9px;color:#0d6efd;opacity:.7;margin-left:4px;white-space:nowrap;"><i class="fas fa-link" style="font-size:8px;margin-right:2px;"></i>${escHtml(cell.szPointName || cell.szSid)}</span>` : '';
            return `<td data-row="${rowIdx}" data-col="${ci}" ${szSidAttr}
                style="color:${cell.szFontColor || '#444'};font-weight:${cell.szFontWeight || 'normal'};
                       font-size:${cell.nFontSize || 12}px;text-align:${cell.szAlign || 'left'};
                       cursor:pointer;position:relative;">${escHtml(cell.szText || '')}${szSidLabel}</td>`;
        }).join('');
        return `<tr>${szCells}</tr>`;
    }).join('');

    return `<table class="w-table">
        <thead><tr>${szHeaders}</tr></thead>
        <tbody>${szRows}</tbody>
    </table>`;
}

// ============================================================
// 儀錶板 SVG
// ============================================================
function buildGaugeHtml(props) {
    const cx = 100, cy = 110, r = 83;

    const fRaw = (props.fValue - props.fMin) / ((props.fMax - props.fMin) || 1);
    const fPct = Math.max(0.001, Math.min(0.999, fRaw));

    // 計算終點（SVG 座標，順時針 sweep=1）
    // 起始角 θ=180°（左端），終止角 = 180° + pct*180°
    const thetaEnd = (180 + fPct * 180) * Math.PI / 180;
    const ex = (cx + r * Math.cos(thetaEnd)).toFixed(2);
    const ey = (cy + r * Math.sin(thetaEnd)).toFixed(2);

    // 顏色閾值
    const fPctClamped = Math.max(0, Math.min(1, fRaw));
    const szColor = fPctClamped >= 0.85 ? '#dc3545'
                  : fPctClamped >= 0.65 ? '#fd7e14'
                  : props.szColor;

    // 背景弧（完整半圓，sweep=1）
    const szBg = `M ${cx-r} ${cy} A ${r} ${r} 0 0 1 ${cx+r} ${cy}`;
    // 數值弧
    const szVal = `M ${cx-r} ${cy} A ${r} ${r} 0 0 1 ${ex} ${ey}`;

    const szDisplayVal = Number(props.fValue).toFixed(1);
    const szGaugeBg = (props.szBgColor && props.szBgColor !== 'transparent') ? props.szBgColor : 'transparent';

    // 警報小鈴鐺
    const szAlarmBadge = hasAlarmRule(props.szSid)
        ? `<div style="position:absolute;top:2px;right:4px;font-size:9px;color:#dc3545;opacity:.7;z-index:1;">
               <i class="fas fa-bell"></i>
           </div>`
        : '';

    return `<div style="position:relative;width:100%;height:100%;background:${szGaugeBg};border-radius:4px;">
    ${szAlarmBadge}
    <svg viewBox="0 0 200 145" xmlns="http://www.w3.org/2000/svg"
                 style="width:100%;height:100%;display:block;">
        <!-- 背景弧 -->
        <path d="${szBg}" fill="none" stroke="#e9ecef" stroke-width="15" stroke-linecap="round"/>
        <!-- 數值弧 -->
        <path d="${szVal}" fill="none" stroke="${szColor}" stroke-width="15" stroke-linecap="round"/>
        <!-- 主數值 -->
        <text x="100" y="96" text-anchor="middle" font-size="26" font-weight="700"
              fill="#212529" font-family="'Segoe UI',sans-serif">${szDisplayVal}</text>
        <!-- 單位 -->
        <text x="100" y="113" text-anchor="middle" font-size="13" fill="#6c757d"
              font-family="'Segoe UI',sans-serif">${props.szUnit}</text>
        <!-- 最小值 -->
        <text x="${cx-r}" y="${cy+18}" text-anchor="middle" font-size="10" fill="#adb5bd">${props.fMin}</text>
        <!-- 最大值 -->
        <text x="${cx+r}" y="${cy+18}" text-anchor="middle" font-size="10" fill="#adb5bd">${props.fMax}</text>
        <!-- 標題 -->
        <text x="100" y="140" text-anchor="middle" font-size="11" fill="#868e96"
              font-family="'Segoe UI',sans-serif">${props.szTitle}</text>
    </svg></div>`;
}

// ============================================================
// 控制按鈕 HTML
// ============================================================
function buildControlBtnHtml(props) {
    const szCidLabel = props.szPointName
        ? ''
        : `<div class="ctrl-btn-cid-label" style="color:#dc3545;"><i class="fas fa-unlink me-1"></i>未綁定 CID</div>`;
    return `
        <div class="ctrl-btn-body">
            <button class="ctrl-btn-main" style="background:${props.szBtnColor || '#198754'};" disabled>
                <i class="fas ${props.szBtnIcon || 'fa-hand-pointer'}" style="font-size:12px;"></i>
                ${escHtml(props.szBtnLabel || '執行')}
            </button>
            ${szCidLabel}
        </div>`;
}

// ============================================================
// AI 點位 HTML（Designer 預覽）
// ============================================================
function buildRealtimeValueHtml(props) {
    const szSidLabel = props.szPointName
        ? ''
        : `<div style="font-size:10px;color:#dc3545;margin-top:2px;"><i class="fas fa-unlink me-1"></i>未綁定 SID</div>`;
    const szBg = (props.szBgColor && props.szBgColor !== 'transparent') ? props.szBgColor : 'transparent';

    // 警報小鈴鐺
    const szAlarmBadge = hasAlarmRule(props.szSid)
        ? `<div style="position:absolute;top:2px;right:4px;font-size:9px;color:#dc3545;opacity:.7;">
               <i class="fas fa-bell"></i>
           </div>`
        : '';

    let szBorder = '';
    if (szBg === 'transparent') {
        const szCanvasBg = document.getElementById('designCanvas')?.style.backgroundColor || '';
        szBorder = 'border:1px solid ' + (isDarkColor(szCanvasBg) ? '#fff' : '#000') + ';';
    }

    return `
        <div style="width:100%;height:100%;display:flex;flex-direction:column;position:relative;
                    align-items:center;justify-content:center;background:${szBg};border-radius:4px;${szBorder}">
            ${szAlarmBadge}
            <div style="font-size:${props.nFontSize || 28}px;font-weight:700;color:${props.szFontColor || '#212529'};
                        font-family:'Segoe UI',sans-serif;line-height:1.2;">
                --
                <span style="font-size:${Math.max(12, (props.nFontSize || 28) * 0.45)}px;font-weight:400;color:#6c757d;margin-left:4px;">${escHtml(props.szUnit || '')}</span>
            </div>
            ${szSidLabel}
        </div>`;
}

// ============================================================
// DI 點位 HTML（Designer 預覽）
// ============================================================
function buildDiPointHtml(props) {
    const szSidLabel = props.szPointName
        ? ''
        : `<div style="font-size:10px;color:#dc3545;margin-top:2px;"><i class="fas fa-unlink me-1"></i>未綁定 SID</div>`;
    const szBg = (props.szBgColor && props.szBgColor !== 'transparent') ? props.szBgColor : 'transparent';
    const szMode = props.szDisplayMode || 'indicator';

    // 警報標記
    const szAlarmBadge = hasAlarmRule(props.szSid)
        ? `<div style="position:absolute;top:2px;right:4px;font-size:9px;color:#dc3545;opacity:.7;">
               <i class="fas fa-bell"></i>
           </div>`
        : '';

    // Designer 預覽預設顯示 OFF 狀態
    let szContentHtml = '';
    if (szMode === 'text') {
        // 文字模式：只顯示 ON/OFF 文字，套用顏色
        const nFs = props.nFontSize || 24;
        const szColor = props.szOffColor || '#6c757d';
        const szLabel = props.szOffLabel || 'OFF';
        szContentHtml = `
            <span style="font-size:${nFs}px;font-weight:700;color:${szColor};
                         font-family:'Segoe UI',sans-serif;">${escHtml(szLabel)}</span>`;
    } else {
        // 燈號模式：圓形指示燈，自動適應容器大小
        const nSize = props.nIndicatorSize || 28;
        const szColor = props.szOffColor || '#6c757d';
        szContentHtml = `
            <span class="di-indicator" style="--di-size:${nSize}px;
                         background:${szColor};box-shadow:0 0 6px ${szColor};"></span>`;
    }

    let szBorder = '';
    if (szBg === 'transparent') {
        const szCanvasBg = document.getElementById('designCanvas')?.style.backgroundColor || '';
        szBorder = 'border:1px solid ' + (isDarkColor(szCanvasBg) ? '#fff' : '#000') + ';';
    }

    return `
        <div style="width:100%;height:100%;display:flex;flex-direction:column;position:relative;
                    align-items:center;justify-content:center;background:${szBg};border-radius:4px;gap:2px;${szBorder}">
            ${szAlarmBadge}
            ${szContentHtml}
            ${szSidLabel}
        </div>`;
}

// ============================================================
// AO 點位 HTML（Designer 預覽）
// ============================================================
function buildAoPointHtml(props) {
    const szCidLabel = props.szPointName
        ? ''
        : `<div class="ao-point-cid-label" style="color:#dc3545;"><i class="fas fa-unlink me-1"></i>未綁定 CID</div>`;
    const szBlock = props.szBlockColor
                  || (props.szBgColor && props.szBgColor !== 'transparent' ? props.szBgColor : null)
                  || '#0d6efd';
    const nFs = props.nFontSize || 16;
    const szName = props.szDisplayName || props.szTitle || 'AO 點位';

    return `
        <div class="ao-point-body">
            <div style="width:100%;height:100%;display:flex;align-items:center;justify-content:center;
                        font-size:${nFs}px;font-weight:600;color:${props.szFontColor || '#ffffff'};
                        text-align:center;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;
                        border-radius:6px;background:${szBlock};
                        box-shadow:0 2px 4px rgba(0,0,0,.15);">
                ${escHtml(szName)}
            </div>
            ${szCidLabel}
        </div>`;
}

// ============================================================
// DO 點位 HTML（Designer 預覽）
// ============================================================
function buildDoPointHtml(props) {
    const szCidLabel = props.szPointName
        ? ''
        : `<div class="do-point-cid-label" style="color:#dc3545;"><i class="fas fa-unlink me-1"></i>未綁定 CID</div>`;
    const szBlock = props.szBlockColor
                  || (props.szBgColor && props.szBgColor !== 'transparent' ? props.szBgColor : null)
                  || '#0d6efd';
    const nFs = props.nFontSize || 16;
    const szName = props.szDisplayName || props.szTitle || 'DO 點位';

    return `
        <div class="do-point-body">
            <div style="width:100%;height:100%;display:flex;align-items:center;justify-content:center;
                        font-size:${nFs}px;font-weight:600;color:${props.szFontColor || '#212529'};
                        text-align:center;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;
                        border-radius:6px;background:${szBlock};
                        box-shadow:0 2px 4px rgba(0,0,0,.15);">
                ${escHtml(szName)}
            </div>
            ${szCidLabel}
        </div>`;
}

// ============================================================
// 水泵 SVG HTML（蝸殼泵輪廓，出水口方向透過旋轉實現）
// ============================================================
function buildPumpHtml(props, szState) {
    // szState: 'stop' | 'run' | 'fault'
    const szRunColor   = props.szRunColor   || '#28a745';
    const szStopColor  = props.szStopColor  || '#6c757d';
    const szFaultColor = props.szFaultColor || '#dc3545';
    const szCircleColor = szState === 'fault' ? szFaultColor
                        : szState === 'run'   ? szRunColor
                        : szStopColor;
    // 泵體顏色：有綁定手自動SID時依模式著色（Designer預設顯示自動色），否則中性灰
    const szManualColor = props.szManualColor || '#ffc107';
    const szAutoColor   = props.szAutoColor   || '#0d6efd';
    const szBodyColor   = props.szSidMode ? szAutoColor : '#555';
    const szBg  = (props.szBgColor && props.szBgColor !== 'transparent') ? props.szBgColor : 'transparent';
    const szDir = props.szOutletDir || 'right';
    const szTransform = szDir === 'left'  ? 'translate(120,0) scale(-1,1)'
                      : szDir === 'up'    ? 'rotate(-90,60,50)'
                      : '';

    // 頻率 linear gauge（僅綁定 szSidFreq 時顯示）
    const bHasFreq = !!props.szSidFreq;
    const nFreqMax = props.nFreqMax || 60;
    const szViewBox = bHasFreq ? '0 0 170 110' : '0 0 120 110';
    const szContrastColor = (function() {
        const bg = szBg;
        if (!bg || bg === 'transparent') return '#333';
        const m = bg.match(/^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i);
        if (!m) return '#333';
        const lum = (parseInt(m[1],16)*299 + parseInt(m[2],16)*587 + parseInt(m[3],16)*114) / 1000;
        return lum > 128 ? '#333' : '#f0f0f0';
    })();
    let szGaugeHtml = '';
    if (bHasFreq) {
        const nBarTop = 22, nBarH = 68;
        const nFillH = Math.round(nBarH * 0.5);
        szGaugeHtml = `
            <rect x="115" y="${nBarTop}" width="10" height="${nBarH}" rx="3" fill="#333" stroke="#555" stroke-width="1"/>
            <rect x="115" y="${nBarTop + nBarH - nFillH}" width="10" height="${nFillH}" rx="3" fill="#17a2b8" opacity="0.6"/>
            <text x="120" y="${nBarTop - 4}" text-anchor="middle" font-size="8" fill="${szContrastColor}">${nFreqMax}</text>
            <text x="120" y="${nBarTop + nBarH + 10}" text-anchor="middle" font-size="8" fill="${szContrastColor}">0</text>
            <text x="130" y="${nBarTop + nBarH - nFillH + nFillH / 2 + 3}" text-anchor="start"
                  font-size="9" fill="${szContrastColor}" font-weight="600">-- Hz</text>`;
    }

    return `<div style="position:relative;width:100%;height:100%;background:${szBg};border-radius:4px;">
    <svg viewBox="${szViewBox}" xmlns="http://www.w3.org/2000/svg"
         style="width:100%;height:100%;display:block;">
        <!-- 底座 Base -->
        <rect x="38" y="86" width="44" height="6" rx="2" fill="#4a4a4a"/>
        <rect x="38" y="92" width="44" height="3" rx="1" fill="#333"/>
        <rect x="39" y="86" width="42" height="2" rx="1" fill="rgba(255,255,255,.12)"/>
        <!-- 支撐柱 -->
        <rect x="48" y="72" width="7" height="15" rx="1.5" fill="#4a4a4a" stroke="#3a3a3a" stroke-width=".5"/>
        <rect x="65" y="72" width="7" height="15" rx="1.5" fill="#4a4a4a" stroke="#3a3a3a" stroke-width=".5"/>
        <rect x="48" y="72" width="3.5" height="15" rx="1" fill="rgba(255,255,255,.08)"/>
        <rect x="65" y="72" width="3.5" height="15" rx="1" fill="rgba(255,255,255,.08)"/>
        <g transform="${szTransform}">
            <!-- 泵體 Volute shadow -->
            <path d="M 60,22 L 106,22 L 106,37 L 86,37 A 30,30 0 1,1 60,22 Z"
                  fill="rgba(0,0,0,.15)"/>
            <!-- 泵體 Volute body -->
            <path d="M 60,20 L 105,20 L 105,35 L 86,35 A 30,30 0 1,1 60,20 Z"
                  fill="${szBodyColor}" stroke="#3a3a3a" stroke-width="2" stroke-linejoin="round"/>
            <!-- 泵體 Top highlight -->
            <rect x="63" y="21" width="40" height="4" rx="1" fill="rgba(255,255,255,.15)"/>
            <!-- 出口法蘭 Outlet flange -->
            <rect x="103" y="18" width="4" height="19" rx="1" fill="${szBodyColor}" stroke="#3a3a3a" stroke-width="1"/>
            <line x1="104" y1="20" x2="106" y2="20" stroke="rgba(255,255,255,.2)" stroke-width=".8"/>
            <line x1="104" y1="35" x2="106" y2="35" stroke="rgba(0,0,0,.2)" stroke-width=".8"/>
            <!-- 葉片 Housing ring -->
            <circle cx="60" cy="50" r="14" fill="rgba(0,0,0,.2)" stroke="#3a3a3a" stroke-width="1"/>
            <!-- 葉片 Impeller disc -->
            <circle cx="60" cy="50" r="12" fill="${szCircleColor}" stroke="#333" stroke-width="1.5"/>
            <ellipse cx="57" cy="46" rx="6" ry="4" fill="rgba(255,255,255,.15)"/>
            <!-- 葉片 Blade 1 (up) -->
            <path d="M 58,48 C 56,43 57,39 60,38 C 63,39 64,43 62,48 Z" fill="rgba(255,255,255,.4)"/>
            <!-- 葉片 Blade 2 (lower-right) -->
            <path d="M 61.5,51 C 65,54 68.5,54.5 69,56 C 67,58 63,56 60.5,53 Z" fill="rgba(255,255,255,.4)"/>
            <!-- 葉片 Blade 3 (lower-left) -->
            <path d="M 58.5,51 C 55,54 51.5,54.5 51,56 C 53,58 57,56 59.5,53 Z" fill="rgba(255,255,255,.4)"/>
            <!-- 中心輪轂 Hub -->
            <circle cx="60" cy="50" r="3.5" fill="#3a3a3a" stroke="#2a2a2a" stroke-width="1"/>
            <circle cx="59" cy="49" r="1.2" fill="rgba(255,255,255,.25)"/>
        </g>${szGaugeHtml}
    </svg></div>`;
}

// ============================================================
// 選取 Widget
// ============================================================
function selectWidget(el) {
    if (selectedEl) selectedEl.classList.remove('selected');
    selectedEl = el;
    const panel = document.querySelector('.property-panel');
    if (el) {
        el.classList.add('selected');
        el.style.zIndex = ++nWidgetCounter + 10;
        renderPropPanel(el);
        panel.classList.remove('collapsed');
    } else {
        panel.classList.add('collapsed');
    }
}

// Widget mousedown 處理（支援 Ctrl 多選切換）
function onWidgetMouseDown(ev, el) {
    if (ev.ctrlKey) {
        if (selectedWidgetIds.has(el.id)) selectedWidgetIds.delete(el.id);
        else selectedWidgetIds.add(el.id);
        updateWidgetSelectionVisual();
        selectWidget(selectedWidgetIds.has(el.id) ? el : null);
        return;
    }
    if (!selectedWidgetIds.has(el.id)) { clearWidgetSelection(); selectedWidgetIds.add(el.id); updateWidgetSelectionVisual(); }
    selectWidget(el);
}

// 多選外觀更新（不做完整 re-render）
function updateWidgetSelectionVisual() {
    canvas.querySelectorAll('.canvas-widget').forEach(w => {
        w.classList.toggle('widget-multi-selected', selectedWidgetIds.has(w.id));
    });
}
function clearWidgetSelection() {
    selectedWidgetIds.clear();
    updateWidgetSelectionVisual();
}

// 複製選取元件 → localStorage（支援跨頁面貼上）
function copySelectedWidgets() {
    if (selectedWidgetIds.size === 0) return;
    const arr = [];
    selectedWidgetIds.forEach(szId => {
        const el = document.getElementById(szId);
        if (!el) return;
        arr.push({
            szType: el.dataset.type,
            nX: parseInt(el.style.left) || 0,
            nY: parseInt(el.style.top) || 0,
            nW: el.offsetWidth,
            nH: el.offsetHeight,
            props: JSON.parse(JSON.stringify(el.widgetProps))
        });
    });
    localStorage.setItem('_designer_clipboard', JSON.stringify(arr));
}

// 貼上元件：從 localStorage 讀取，置於畫布可視區域中央
function pasteWidgets() {
    const raw = localStorage.getItem('_designer_clipboard');
    if (!raw) return;
    let arr;
    try { arr = JSON.parse(raw); } catch { return; }
    if (!arr || arr.length === 0) return;

    const wrapper = canvas.closest('.canvas-wrapper');
    const vx = (wrapper ? wrapper.scrollLeft : 0) + (wrapper ? wrapper.clientWidth : canvas.offsetWidth) / 2;
    const vy = (wrapper ? wrapper.scrollTop : 0) + (wrapper ? wrapper.clientHeight : canvas.offsetHeight) / 2;
    const minX = Math.min(...arr.map(w => w.nX));
    const minY = Math.min(...arr.map(w => w.nY));
    const maxX = Math.max(...arr.map(w => w.nX + w.nW));
    const maxY = Math.max(...arr.map(w => w.nY + w.nH));
    const ox = Math.round((vx - (minX + maxX) / 2) / 5) * 5;
    const oy = Math.round((vy - (minY + maxY) / 2) / 5) * 5;

    clearWidgetSelection();
    selectWidget(null);
    for (const ws of arr) {
        const nws = { ...ws, nX: Math.max(0, ws.nX + ox), nY: Math.max(0, ws.nY + oy), props: JSON.parse(JSON.stringify(ws.props)) };
        restoreWidget(nws);
        const lastEl = canvas.querySelector('.canvas-widget:last-child');
        if (lastEl) selectedWidgetIds.add(lastEl.id);
    }
    updateWidgetSelectionVisual();
    const lastEl = canvas.querySelector('.canvas-widget:last-child');
    if (lastEl) selectWidget(lastEl);
}

// 刪除所有選取的元件
function deleteSelectedWidgets() {
    if (selectedWidgetIds.size === 0) return;
    selectedWidgetIds.forEach(szId => {
        const el = document.getElementById(szId);
        if (el) { if (selectedEl === el) selectWidget(null); el.remove(); }
    });
    selectedWidgetIds.clear();
}

// 點擊畫布空白處：關閉選單 / 取消選取 / 框選
canvas.addEventListener('mousedown', e => {
    if (e.target !== canvas) return;

    if (e.button !== 0) { selectWidget(null); clearWidgetSelection(); return; }

    // 開始框選
    const cRect = canvas.getBoundingClientRect();
    const sx = e.clientX - cRect.left;
    const sy = e.clientY - cRect.top;
    const selRect = document.createElement('div');
    selRect.className = 'designer-selection-rect';
    canvas.appendChild(selRect);
    let moved = false;

    function onMove(ev) {
        moved = true;
        const cx = ev.clientX - cRect.left;
        const cy = ev.clientY - cRect.top;
        selRect.style.left = Math.min(sx, cx) + 'px';
        selRect.style.top = Math.min(sy, cy) + 'px';
        selRect.style.width = Math.abs(cx - sx) + 'px';
        selRect.style.height = Math.abs(cy - sy) + 'px';
    }
    function onUp(ev) {
        document.removeEventListener('mousemove', onMove);
        document.removeEventListener('mouseup', onUp);
        selRect.remove();
        if (!moved) {
            if (!ev.ctrlKey) { selectWidget(null); clearWidgetSelection(); }
            return;
        }
        const cx = ev.clientX - cRect.left;
        const cy = ev.clientY - cRect.top;
        const rx = Math.min(sx, cx), ry = Math.min(sy, cy);
        const rw = Math.abs(cx - sx), rh = Math.abs(cy - sy);
        if (!ev.ctrlKey) clearWidgetSelection();
        canvas.querySelectorAll('.canvas-widget').forEach(w => {
            const wx = parseInt(w.style.left) || 0;
            const wy = parseInt(w.style.top) || 0;
            if (wx + w.offsetWidth > rx && wx < rx + rw && wy + w.offsetHeight > ry && wy < ry + rh) {
                selectedWidgetIds.add(w.id);
            }
        });
        updateWidgetSelectionVisual();
        if (selectedWidgetIds.size > 0) {
            const lastId = [...selectedWidgetIds].pop();
            selectWidget(document.getElementById(lastId));
        }
    }
    document.addEventListener('mousemove', onMove);
    document.addEventListener('mouseup', onUp);
});

// 畫布右鍵 → 新增文字選單
canvas.addEventListener('contextmenu', e => {
    if (e.target.closest('.canvas-widget')) return;  // 點到 widget 上不攔截
    e.preventDefault();
    const rect = canvas.getBoundingClientRect();
    const nX = Math.max(0, snapGrid(e.clientX - rect.left));
    const nY = Math.max(0, snapGrid(e.clientY - rect.top));
    showCanvasCtxMenu(e, nX, nY);
});

// ============================================================
// 屬性面板
// ============================================================
function renderPropPanel(el) {
    const szType = el.dataset.type;
    const props  = el.widgetProps;

    // 文字 Widget 使用獨立屬性面板
    if (szType === 'text') {
        document.getElementById('propBody').innerHTML = buildTextPropHtml(el, props);
        return;
    }

    // 共用欄位
    let szHtml = `
        <div class="prop-group">
            <label>標題</label>
            <input type="text" value="${escHtml(props.szTitle)}"
                   oninput="setProp('szTitle', this.value)">
        </div>
        <div class="prop-group">
            <label>X 位置</label>
            <input type="number" id="pX" value="${parseInt(el.style.left)}" step="20"
                   oninput="setPos('left', +this.value)">
        </div>
        <div class="prop-group">
            <label>Y 位置</label>
            <input type="number" id="pY" value="${parseInt(el.style.top)}" step="20"
                   oninput="setPos('top', +this.value)">
        </div>
        <div class="prop-group">
            <label>寬度</label>
            <input type="number" id="pW" value="${el.offsetWidth}" step="20"
                   oninput="setSize('width', +this.value)">
        </div>
        <div class="prop-group">
            <label>高度</label>
            <input type="number" id="pH" value="${el.offsetHeight}" step="20"
                   oninput="setSize('height', +this.value)">
        </div>
        <hr class="prop-divider">
    `;

    if (szType === 'table') {
        szHtml += `
        <div class="prop-group">
            <label>列數</label>
            <input type="number" value="${props.nRows}" min="1" max="20"
                   oninput="setProp('nRows', +this.value)">
        </div>
        <div class="prop-group">
            <label>欄數</label>
            <input type="number" value="${props.nCols}" min="1"
                   oninput="setProp('nCols', +this.value)">
        </div>
        <div class="prop-group">
            <label>標題列顏色</label>
            <input type="color" value="${props.szHeaderColor}"
                   oninput="setProp('szHeaderColor', this.value)">
        </div>`;
    } else if (szType === 'gauge') {
        const szSidLabel = props.szPointName
            ? `<span style="font-size:12px;color:#c8c8c8;">${escHtml(props.szPointName)}</span>`
            : `<span style="color:#888;font-size:11px;">（未綁定）</span>`;
        const isBgTransparent = !props.szBgColor || props.szBgColor === 'transparent';
        const szBgColorVal    = isBgTransparent ? '#ffffff' : props.szBgColor;
        szHtml += `
        <div class="prop-group">
            <label>綁定點位</label>
            <div style="display:flex;align-items:center;gap:6px;flex-wrap:wrap;">
                ${szSidLabel}
                <button class="btn btn-outline-warning btn-sm py-0 px-2" style="font-size:11px;"
                        onclick="rerouteGaugePoint()">
                    <i class="fas fa-exchange-alt me-1"></i>重選
                </button>
            </div>
        </div>
        <div class="prop-group">
            <label>當前值</label>
            <input type="number" value="${props.fValue}" step="0.1"
                   oninput="setProp('fValue', +this.value)">
        </div>
        <div class="prop-group">
            <label>最小值</label>
            <input type="number" value="${props.fMin}"
                   oninput="setProp('fMin', +this.value)">
        </div>
        <div class="prop-group">
            <label>最大值</label>
            <input type="number" value="${props.fMax}"
                   oninput="setProp('fMax', +this.value)">
        </div>
        <div class="prop-group">
            <label>單位（來自點位）</label>
            <input type="text" value="${escHtml(props.szUnit)}" readonly
                   style="background:#f0f0f0;color:#666;cursor:not-allowed;">
        </div>
        <div class="prop-group">
            <label>主色（正常範圍）</label>
            <input type="color" value="${props.szColor}"
                   oninput="setProp('szColor', this.value)">
        </div>
        <div class="prop-group">
            <label>上限顏色</label>
            <input type="color" value="${props.szHighColor || '#dc3545'}"
                   oninput="setProp('szHighColor', this.value)">
        </div>
        <div class="prop-group">
            <label>下限顏色</label>
            <input type="color" value="${props.szLowColor || '#fd7e14'}"
                   oninput="setProp('szLowColor', this.value)">
        </div>
        <div class="prop-group">
            <label>背景色</label>
            <label style="display:inline-flex;align-items:center;gap:3px;font-size:11px;color:#9d9d9d;
                          font-weight:normal;text-transform:none;letter-spacing:0;cursor:pointer;margin-top:4px;white-space:nowrap;">
                <input type="checkbox" style="cursor:pointer;width:auto;"
                       ${isBgTransparent ? 'checked' : ''}
                       onchange="onGaugeBgTransparentChange(this.checked)">
                透明背景
            </label>
            <input type="color" id="gaugeBgPicker" value="${szBgColorVal}"
                   style="width:100%;margin-top:4px;${isBgTransparent ? 'display:none;' : ''}"
                   oninput="setProp('szBgColor', this.value)">
        </div>
        `;
    } else if (szType === 'controlBtn') {
        const szCidLabel = props.szPointName
            ? `<span style="font-size:12px;color:#c8c8c8;">${escHtml(props.szPointName)}</span>`
            : `<span style="color:#888;font-size:11px;">（未綁定）</span>`;
        szHtml += `
        <div class="prop-group">
            <label>綁定 CID</label>
            <div style="display:flex;align-items:center;gap:6px;flex-wrap:wrap;">
                ${szCidLabel}
                <button class="btn btn-outline-success btn-sm py-0 px-2" style="font-size:11px;"
                        onclick="rerouteControlBtnPoint()">
                    <i class="fas fa-exchange-alt me-1"></i>重選
                </button>
            </div>
        </div>
        <div class="prop-group">
            <label>按鈕文字</label>
            <input type="text" value="${escHtml(props.szBtnLabel || '執行')}"
                   oninput="setProp('szBtnLabel', this.value)">
        </div>
        <div class="prop-group">
            <label>按鈕圖示</label>
            <select style="width:100%;padding:4px 6px;border:1px solid #555;border-radius:4px;
                           background:#2b2b2b;color:#e0e0e0;font-size:12px;"
                    onchange="setProp('szBtnIcon', this.value)">
                ${[
                    { v: 'fa-hand-pointer', t: '\u2709 手指' },
                    { v: 'fa-play',         t: '\u25B6 啟動' },
                    { v: 'fa-power-off',    t: '\u23FB 電源' },
                    { v: 'fa-paper-plane',  t: '\u2708 發送' },
                    { v: 'fa-bolt',         t: '\u26A1 閃電' }
                ].map(o => `<option value="${o.v}" ${(props.szBtnIcon || 'fa-hand-pointer') === o.v ? 'selected' : ''}>${o.t}</option>`).join('')}
            </select>
        </div>
        <div class="prop-group">
            <label>控制值</label>
            <input type="number" value="${props.fCtrlValue ?? 1}" step="any"
                   oninput="setProp('fCtrlValue', +this.value)">
        </div>
        <div class="prop-group">
            <label>按鈕顏色</label>
            <input type="color" value="${props.szBtnColor || '#198754'}"
                   oninput="setProp('szBtnColor', this.value)">
        </div>`;
    } else if (szType === 'realtimeValue') {
        const szSidLabel = props.szPointName
            ? `<span style="font-size:12px;color:#c8c8c8;">${escHtml(props.szPointName)}</span>`
            : `<span style="color:#888;font-size:11px;">（未綁定）</span>`;
        const isBgTransparent = !props.szBgColor || props.szBgColor === 'transparent';
        const szBgColorVal    = isBgTransparent ? '#ffffff' : props.szBgColor;
        szHtml += `
        <div class="prop-group">
            <label>綁定 SID</label>
            <div style="display:flex;align-items:center;gap:6px;flex-wrap:wrap;">
                ${szSidLabel}
                <button class="btn btn-outline-danger btn-sm py-0 px-2" style="font-size:11px;"
                        onclick="rerouteRealtimeValuePoint()">
                    <i class="fas fa-exchange-alt me-1"></i>重選
                </button>
            </div>
        </div>
        <div class="prop-group">
            <label>字體大小</label>
            <input type="number" value="${props.nFontSize || 28}" min="12" max="120" step="2"
                   oninput="setProp('nFontSize', +this.value)">
        </div>
        <div class="prop-group">
            <label>字色</label>
            <input type="color" value="${props.szFontColor || '#212529'}"
                   oninput="setProp('szFontColor', this.value)">
        </div>
        <div class="prop-group">
            <label>單位</label>
            <input type="text" value="${escHtml(props.szUnit || '')}"
                   oninput="setProp('szUnit', this.value)">
        </div>
        <div class="prop-group">
            <label>上限顏色</label>
            <input type="color" value="${props.szHighColor || '#dc3545'}"
                   oninput="setProp('szHighColor', this.value)">
        </div>
        <div class="prop-group">
            <label>下限顏色</label>
            <input type="color" value="${props.szLowColor || '#fd7e14'}"
                   oninput="setProp('szLowColor', this.value)">
        </div>
        <div class="prop-group">
            <label>背景色</label>
            <label style="display:inline-flex;align-items:center;gap:3px;font-size:11px;color:#9d9d9d;
                          font-weight:normal;text-transform:none;letter-spacing:0;cursor:pointer;margin-top:4px;white-space:nowrap;">
                <input type="checkbox" style="cursor:pointer;width:auto;"
                       ${isBgTransparent ? 'checked' : ''}
                       onchange="onRtValBgTransparentChange(this.checked)">
                透明背景
            </label>
            <input type="color" id="rtValBgPicker" value="${szBgColorVal}"
                   style="width:100%;margin-top:4px;${isBgTransparent ? 'display:none;' : ''}"
                   oninput="setProp('szBgColor', this.value)">
        </div>
        `;
    } else if (szType === 'diPoint') {
        const szSidLabel = props.szPointName
            ? `<span style="font-size:12px;color:#c8c8c8;">${escHtml(props.szPointName)}</span>`
            : `<span style="color:#888;font-size:11px;">（未綁定）</span>`;
        const isBgTransparent = !props.szBgColor || props.szBgColor === 'transparent';
        const szBgColorVal    = isBgTransparent ? '#ffffff' : props.szBgColor;
        const szMode = props.szDisplayMode || 'indicator';
        szHtml += `
        <div class="prop-group">
            <label>綁定 SID</label>
            <div style="display:flex;align-items:center;gap:6px;flex-wrap:wrap;">
                ${szSidLabel}
                <button class="btn btn-outline-success btn-sm py-0 px-2" style="font-size:11px;"
                        onclick="rerouteDiPointPoint()">
                    <i class="fas fa-exchange-alt me-1"></i>重選
                </button>
            </div>
        </div>
        <div class="prop-group">
            <label>顯示模式</label>
            <select onchange="setProp('szDisplayMode', this.value)"
                    style="width:100%;background:#3c3c3c;border:1px solid #555;color:#d4d4d4;
                           padding:4px 6px;font-size:12px;border-radius:3px;">
                <option value="indicator" ${szMode === 'indicator' ? 'selected' : ''}>燈號</option>
                <option value="text"      ${szMode === 'text'      ? 'selected' : ''}>文字</option>
            </select>
        </div>
        <hr class="prop-divider">
        ${szMode === 'indicator' ? `
        <div class="prop-group">
            <label>指示燈大小</label>
            <input type="number" value="${props.nIndicatorSize || 28}" min="12" max="80" step="2"
                   oninput="setProp('nIndicatorSize', +this.value)">
        </div>` : `
        <div class="prop-group">
            <label>字體大小</label>
            <input type="number" value="${props.nFontSize || 24}" min="10" max="120" step="1"
                   oninput="setProp('nFontSize', +this.value)">
        </div>`}
        <div class="prop-group">
            <label>ON 顏色</label>
            <input type="color" value="${props.szOnColor || '#28a745'}"
                   oninput="setProp('szOnColor', this.value)">
        </div>
        <div class="prop-group">
            <label>OFF 顏色</label>
            <input type="color" value="${props.szOffColor || '#6c757d'}"
                   oninput="setProp('szOffColor', this.value)">
        </div>
        <div class="prop-group">
            <label>ON 文字</label>
            <input type="text" value="${escHtml(props.szOnLabel || 'ON')}"
                   oninput="setProp('szOnLabel', this.value)">
        </div>
        <div class="prop-group">
            <label>OFF 文字</label>
            <input type="text" value="${escHtml(props.szOffLabel || 'OFF')}"
                   oninput="setProp('szOffLabel', this.value)">
        </div>
        <div class="prop-group">
            <label>${szMode === 'text' ? '\u8b66\u5831\u5b57\u8272' : '\u8b66\u5831\u984f\u8272'}</label>
            <input type="color" value="${props.szAlarmColor || '#dc3545'}"
                   oninput="setProp('szAlarmColor', this.value)">
        </div>
        <div class="prop-group">
            <label>背景色</label>
            <label style="display:inline-flex;align-items:center;gap:3px;font-size:11px;color:#9d9d9d;
                          font-weight:normal;text-transform:none;letter-spacing:0;cursor:pointer;margin-top:4px;white-space:nowrap;">
                <input type="checkbox" style="cursor:pointer;width:auto;"
                       ${isBgTransparent ? 'checked' : ''}
                       onchange="onDiPointBgTransparentChange(this.checked)">
                透明背景
            </label>
            <input type="color" id="diPointBgPicker" value="${szBgColorVal}"
                   style="width:100%;margin-top:4px;${isBgTransparent ? 'display:none;' : ''}"
                   oninput="setProp('szBgColor', this.value)">
        </div>
        `;
    } else if (szType === 'aoPoint') {
        const szCidLabel = props.szPointName
            ? `<span style="font-size:12px;color:#c8c8c8;">${escHtml(props.szPointName)}</span>`
            : `<span style="color:#888;font-size:11px;">（未綁定）</span>`;
        szHtml += `
        <div class="prop-group">
            <label>綁定 CID</label>
            <div style="display:flex;align-items:center;gap:6px;flex-wrap:wrap;">
                ${szCidLabel}
                <button class="btn btn-outline-info btn-sm py-0 px-2" style="font-size:11px;"
                        onclick="rerouteAoPointPoint()">
                    <i class="fas fa-exchange-alt me-1"></i>重選
                </button>
            </div>
        </div>
        <div class="prop-group">
            <label>顯示名稱</label>
            <input type="text" value="${escHtml(props.szDisplayName || props.szTitle || 'AO 點位')}"
                   oninput="setProp('szDisplayName', this.value)">
        </div>
        <hr class="prop-divider">
        <div class="prop-group">
            <label>預設寫入值</label>
            <input type="number" value="${props.fWriteValue ?? 0}" step="${props.fStep ?? 1}"
                   oninput="setProp('fWriteValue', +this.value)">
        </div>
        <div class="prop-group">
            <label>最小值</label>
            <input type="number" value="${props.fMin ?? 0}" step="any"
                   oninput="setProp('fMin', +this.value)">
        </div>
        <div class="prop-group">
            <label>最大值</label>
            <input type="number" value="${props.fMax ?? 100}" step="any"
                   oninput="setProp('fMax', +this.value)">
        </div>
        <div class="prop-group">
            <label>步進值</label>
            <input type="number" value="${props.fStep ?? 1}" min="0.001" step="any"
                   oninput="setProp('fStep', +this.value)">
        </div>
        <div class="prop-group">
            <label>小數點位數</label>
            <input type="number" value="${props.nDecimalPlaces ?? 2}" min="0" max="6" step="1"
                   oninput="setProp('nDecimalPlaces', +this.value)">
        </div>
        <div class="prop-group">
            <label>單位</label>
            <input type="text" value="${escHtml(props.szUnit || '')}"
                   oninput="setProp('szUnit', this.value)">
        </div>
        <hr class="prop-divider">
        <div class="prop-group">
            <label>「手動控制」選單文字</label>
            <input type="text" value="${escHtml(props.szMenuManualLabel ?? '')}"
                   placeholder="留空則不顯示"
                   oninput="setProp('szMenuManualLabel', this.value)">
        </div>
        <div class="prop-group">
            <label>「自動控制」選單文字</label>
            <input type="text" value="${escHtml(props.szMenuAutoLabel ?? '')}"
                   placeholder="留空則不顯示"
                   oninput="setProp('szMenuAutoLabel', this.value)">
        </div>
        <hr class="prop-divider">
        <div class="prop-group">
            <label>字體大小</label>
            <input type="number" value="${props.nFontSize || 16}" min="10" max="60" step="1"
                   oninput="setProp('nFontSize', +this.value)">
        </div>
        <div class="prop-group">
            <label>字色</label>
            <input type="color" value="${props.szFontColor || '#ffffff'}"
                   oninput="setProp('szFontColor', this.value)">
        </div>
        <div class="prop-group">
            <label>方塊顏色</label>
            <input type="color" value="${props.szBlockColor || '#0d6efd'}"
                   oninput="setProp('szBlockColor', this.value)">
        </div>`;
    } else if (szType === 'doPoint') {
        const szCidLabel = props.szPointName
            ? `<span style="font-size:12px;color:#c8c8c8;">${escHtml(props.szPointName)}</span>`
            : `<span style="color:#888;font-size:11px;">（未綁定）</span>`;
        szHtml += `
        <div class="prop-group">
            <label>綁定 CID</label>
            <div style="display:flex;align-items:center;gap:6px;flex-wrap:wrap;">
                ${szCidLabel}
                <button class="btn btn-outline-warning btn-sm py-0 px-2" style="font-size:11px;"
                        onclick="rerouteDoPointPoint()">
                    <i class="fas fa-exchange-alt me-1"></i>重選
                </button>
            </div>
        </div>
        <div class="prop-group">
            <label>顯示名稱</label>
            <input type="text" value="${escHtml(props.szDisplayName || props.szTitle || 'DO 點位')}"
                   oninput="setProp('szDisplayName', this.value)">
        </div>
        <hr class="prop-divider">
        <div class="prop-group">
            <label>ON 寫入值</label>
            <input type="number" value="${props.nOnValue ?? 1}" step="any"
                   oninput="setProp('nOnValue', +this.value)">
        </div>
        <div class="prop-group">
            <label>OFF 寫入值</label>
            <input type="number" value="${props.nOffValue ?? 0}" step="any"
                   oninput="setProp('nOffValue', +this.value)">
        </div>
        <hr class="prop-divider">
        <div class="prop-group">
            <label>「手動ON」選單文字</label>
            <input type="text" value="${escHtml(props.szMenuOnLabel ?? '')}"
                   placeholder="留空則不顯示"
                   oninput="setProp('szMenuOnLabel', this.value)">
        </div>
        <div class="prop-group">
            <label>「手動OFF」選單文字</label>
            <input type="text" value="${escHtml(props.szMenuOffLabel ?? '')}"
                   placeholder="留空則不顯示"
                   oninput="setProp('szMenuOffLabel', this.value)">
        </div>
        <div class="prop-group">
            <label>「自動控制」選單文字</label>
            <input type="text" value="${escHtml(props.szMenuAutoLabel ?? '')}"
                   placeholder="留空則不顯示"
                   oninput="setProp('szMenuAutoLabel', this.value)">
        </div>
        <hr class="prop-divider">
        <div class="prop-group">
            <label>字體大小</label>
            <input type="number" value="${props.nFontSize || 16}" min="10" max="60" step="1"
                   oninput="setProp('nFontSize', +this.value)">
        </div>
        <div class="prop-group">
            <label>字色</label>
            <input type="color" value="${props.szFontColor || '#212529'}"
                   oninput="setProp('szFontColor', this.value)">
        </div>
        <div class="prop-group">
            <label>方塊顏色</label>
            <input type="color" value="${props.szBlockColor || '#0d6efd'}"
                   oninput="setProp('szBlockColor', this.value)">
        </div>`;
    } else if (szType === 'pump') {
        const isBgTransparent = !props.szBgColor || props.szBgColor === 'transparent';
        const szBgColorVal    = isBgTransparent ? '#ffffff' : props.szBgColor;

        // 產生綁定欄位 HTML 的輔助函式
        function pumpBindRow(szLabel, szSidKey, szNameKey, szBtnClass) {
            const szName = props[szNameKey];
            const szDisp = szName
                ? `<span style="font-size:12px;color:#c8c8c8;">${escHtml(szName)}</span>`
                : `<span style="color:#888;font-size:11px;">（未綁定）</span>`;
            return `<div class="prop-group">
                <label>${szLabel}</label>
                <div style="display:flex;align-items:center;gap:6px;flex-wrap:wrap;">
                    ${szDisp}
                    <button class="btn ${szBtnClass} btn-sm py-0 px-2" style="font-size:11px;"
                            onclick="reroutePumpBinding('${szSidKey}','${szNameKey}')">
                        <i class="fas fa-exchange-alt me-1"></i>重選
                    </button>
                </div>
            </div>`;
        }

        szHtml += `
        <div style="font-size:11px;color:#aaa;margin-bottom:4px;letter-spacing:1px;">SID 監控點位</div>
        ${pumpBindRow('運轉狀態', 'szSidRun',   'szRunName',   'btn-outline-info')}
        ${pumpBindRow('故障狀態', 'szSidFault', 'szFaultName', 'btn-outline-danger')}
        ${pumpBindRow('手自動狀態', 'szSidMode', 'szModeName',  'btn-outline-warning')}
        ${pumpBindRow('頻率',     'szSidFreq',  'szFreqName',  'btn-outline-info')}
        <hr class="prop-divider">
        <div style="font-size:11px;color:#aaa;margin-bottom:4px;letter-spacing:1px;">CID 控制點位</div>
        ${pumpBindRow('啟動停止', 'szCidStartStop', 'szStartStopName', 'btn-outline-success')}
        ${pumpBindRow('頻率設定', 'szCidFreqSet',   'szFreqSetName',   'btn-outline-success')}
        <hr class="prop-divider">
        <div class="prop-group">
            <label>出水口方向</label>
            <select style="width:100%;padding:4px 6px;border:1px solid #555;border-radius:4px;
                           background:#2b2b2b;color:#e0e0e0;font-size:12px;"
                    onchange="setProp('szOutletDir', this.value)">
                <option value="right" ${(props.szOutletDir || 'right') === 'right' ? 'selected' : ''}>右</option>
                <option value="left"  ${props.szOutletDir === 'left'  ? 'selected' : ''}>左</option>
                <option value="up"    ${props.szOutletDir === 'up'    ? 'selected' : ''}>上</option>
            </select>
        </div>
        <div class="prop-group">
            <label>運轉顏色</label>
            <input type="color" value="${props.szRunColor || '#28a745'}"
                   oninput="setProp('szRunColor', this.value)">
        </div>
        <div class="prop-group">
            <label>停止顏色</label>
            <input type="color" value="${props.szStopColor || '#6c757d'}"
                   oninput="setProp('szStopColor', this.value)">
        </div>
        <div class="prop-group">
            <label>故障顏色</label>
            <input type="color" value="${props.szFaultColor || '#dc3545'}"
                   oninput="setProp('szFaultColor', this.value)">
        </div>
        <div class="prop-group">
            <label>手動顏色</label>
            <input type="color" value="${props.szManualColor || '#ffc107'}"
                   oninput="setProp('szManualColor', this.value)">
        </div>
        <div class="prop-group">
            <label>自動顏色</label>
            <input type="color" value="${props.szAutoColor || '#0d6efd'}"
                   oninput="setProp('szAutoColor', this.value)">
        </div>
        <div class="prop-group">
            <label>背景色</label>
            <label style="display:inline-flex;align-items:center;gap:3px;font-size:11px;color:#9d9d9d;
                          font-weight:normal;text-transform:none;letter-spacing:0;cursor:pointer;margin-top:4px;white-space:nowrap;">
                <input type="checkbox" style="cursor:pointer;width:auto;"
                       ${isBgTransparent ? 'checked' : ''}
                       onchange="onPumpBgTransparentChange(this.checked)">
                透明背景
            </label>
            <input type="color" id="pumpBgPicker" value="${szBgColorVal}"
                   style="width:100%;margin-top:4px;${isBgTransparent ? 'display:none;' : ''}"
                   oninput="setProp('szBgColor', this.value)">
        </div>`;
    }

    document.getElementById('propBody').innerHTML = szHtml;
}

// ============================================================
// 文字 Widget 屬性面板 HTML
// ============================================================
function buildTextPropHtml(el, props) {
    const szFontFamilyOpts = [
        { val: 'inherit',                             label: '預設' },
        { val: "'Microsoft JhengHei', sans-serif",    label: '微軟正黑體' },
        { val: "'Noto Sans TC', sans-serif",          label: 'Noto Sans TC' },
        { val: "'Arial', sans-serif",                 label: 'Arial' },
        { val: "'Times New Roman', serif",            label: 'Times New Roman' },
        { val: "'Courier New', monospace",            label: 'Courier New' },
    ].map(o => `<option value="${escHtml(o.val)}" ${props.szFontFamily === o.val ? 'selected' : ''}>${o.label}</option>`).join('');

    const isBgTransparent = props.szBgColor === 'transparent';
    const szBgColorVal    = isBgTransparent ? '#ffffff' : props.szBgColor;

    return `
        <div class="prop-group">
            <label>文字內容</label>
            <textarea rows="3" style="width:100%;background:#3c3c3c;border:1px solid #555;
                      color:#d4d4d4;padding:4px 6px;font-size:12px;border-radius:3px;
                      outline:none;resize:vertical;box-sizing:border-box;"
                      oninput="setProp('szText', this.value)">${escHtml(props.szText)}</textarea>
        </div>
        <div class="prop-group">
            <label>X 位置</label>
            <input type="number" id="pX" value="${parseInt(el.style.left)}" step="20"
                   oninput="setPos('left', +this.value)">
        </div>
        <div class="prop-group">
            <label>Y 位置</label>
            <input type="number" id="pY" value="${parseInt(el.style.top)}" step="20"
                   oninput="setPos('top', +this.value)">
        </div>
        <div class="prop-group">
            <label>寬度</label>
            <input type="number" id="pW" value="${el.offsetWidth}" step="20"
                   oninput="setSize('width', +this.value)">
        </div>
        <div class="prop-group">
            <label>高度</label>
            <input type="number" id="pH" value="${el.offsetHeight}" step="20"
                   oninput="setSize('height', +this.value)">
        </div>
        <hr class="prop-divider">
        <div class="prop-group">
            <label>字體大小</label>
            <input type="number" value="${props.nFontSize}" min="8" max="200" step="2"
                   oninput="setProp('nFontSize', +this.value)">
        </div>
        <div class="prop-group">
            <label>字色</label>
            <input type="color" value="${props.szFontColor}"
                   oninput="setProp('szFontColor', this.value)">
        </div>
        <div class="prop-group">
            <label>字體</label>
            <select oninput="setProp('szFontFamily', this.value)">${szFontFamilyOpts}</select>
        </div>
        <div class="prop-group">
            <label>粗體</label>
            <select oninput="setProp('szFontWeight', this.value)">
                <option value="normal" ${props.szFontWeight === 'normal' ? 'selected' : ''}>正常</option>
                <option value="bold"   ${props.szFontWeight === 'bold'   ? 'selected' : ''}>粗體</option>
            </select>
        </div>
        <div class="prop-group">
            <label>斜體</label>
            <select onchange="setProp('isItalic', this.value === 'true')">
                <option value="false" ${!props.isItalic ? 'selected' : ''}>否</option>
                <option value="true"  ${props.isItalic  ? 'selected' : ''}>是</option>
            </select>
        </div>
        <div class="prop-group">
            <label>背景色</label>
            <label style="display:inline-flex;align-items:center;gap:3px;font-size:11px;color:#9d9d9d;
                          font-weight:normal;text-transform:none;letter-spacing:0;cursor:pointer;margin-top:4px;white-space:nowrap;">
                <input type="checkbox" style="cursor:pointer;width:auto;"
                       ${isBgTransparent ? 'checked' : ''}
                       onchange="onTextBgTransparentChange(this.checked)">
                透明背景
            </label>
            <input type="color" id="txtBgColorPicker" value="${szBgColorVal}"
                   style="width:100%;margin-top:4px;${isBgTransparent ? 'display:none;' : ''}"
                   oninput="setProp('szBgColor', this.value)">
        </div>
    `;
}

function onTextBgTransparentChange(isChecked) {
    if (isChecked) {
        setProp('szBgColor', 'transparent');
        const picker = document.getElementById('txtBgColorPicker');
        if (picker) picker.style.display = 'none';
    } else {
        const picker = document.getElementById('txtBgColorPicker');
        const szColor = picker ? picker.value : '#ffffff';
        setProp('szBgColor', szColor);
        if (picker) picker.style.display = '';
    }
}

// 從單一 widget state (props) 中檢查是否有同 SID 的 DI 標籤
function _checkDiLabelsInProps(szType, props, szSid) {
    if (szType === 'diPoint' && props.szSid === szSid) {
        if (props.szOnLabel !== 'ON' || props.szOffLabel !== 'OFF') {
            return { szOnLabel: props.szOnLabel, szOffLabel: props.szOffLabel };
        }
    }
    if (szType === 'table' && props.arrCells) {
        for (const row of props.arrCells) {
            for (const cell of row) {
                if (cell && cell.szSid === szSid && cell.szPointType === 'DI') {
                    if (cell.szOnLabel !== 'ON' || cell.szOffLabel !== 'OFF') {
                        return { szOnLabel: cell.szOnLabel, szOffLabel: cell.szOffLabel };
                    }
                }
            }
        }
    }
    return null;
}

// 在所有頁面（當前畫布 + arrPageTree 其他頁面）中，找出同 SID 的 DI ON/OFF 標籤
function _findDiLabelsForSid(szSid) {
    if (!szSid) return null;
    // 1. 搜尋當前畫布 DOM
    for (const el of canvas.querySelectorAll('.canvas-widget')) {
        if (!el.widgetProps) continue;
        const found = _checkDiLabelsInProps(el.dataset.type, el.widgetProps, szSid);
        if (found) return found;
    }
    // 2. 搜尋其他頁面的 arrWidgetState（遞迴遍歷 arrPageTree）
    function searchTree(pages) {
        for (const page of pages) {
            if (page.szId !== szCurrentPageId && page.arrWidgetState) {
                for (const ws of page.arrWidgetState) {
                    const found = _checkDiLabelsInProps(ws.szType, ws.props || {}, szSid);
                    if (found) return found;
                }
            }
            if (page.arrChildren) {
                const found = searchTree(page.arrChildren);
                if (found) return found;
            }
        }
        return null;
    }
    return searchTree(arrPageTree);
}

// 同步 ON/OFF 標籤到所有頁面中綁定同一個 SID 的 DI widget 與 table cell
function _syncDiLabelsToSid(szSid, szOnLabel, szOffLabel, elExclude) {
    if (!szSid) return;
    // 1. 同步當前畫布 DOM
    for (const el of canvas.querySelectorAll('.canvas-widget')) {
        if (!el.widgetProps) continue;
        if (el === elExclude && !el.widgetProps.arrCells) continue;
        const p = el.widgetProps;
        if (el.dataset.type === 'diPoint' && p.szSid === szSid && el !== elExclude) {
            p.szOnLabel  = szOnLabel;
            p.szOffLabel = szOffLabel;
            renderWidget(el);
        }
        if (el.dataset.type === 'table' && p.arrCells) {
            let isChanged = false;
            for (const row of p.arrCells) {
                for (const cell of row) {
                    if (cell && cell.szSid === szSid && cell.szPointType === 'DI') {
                        cell.szOnLabel  = szOnLabel;
                        cell.szOffLabel = szOffLabel;
                        isChanged = true;
                    }
                }
            }
            if (isChanged) renderWidget(el);
        }
    }
    // 2. 同步其他頁面的 arrWidgetState
    function syncTree(pages) {
        for (const page of pages) {
            if (page.szId !== szCurrentPageId && page.arrWidgetState) {
                for (const ws of page.arrWidgetState) {
                    const props = ws.props;
                    if (!props) continue;
                    if (ws.szType === 'diPoint' && props.szSid === szSid) {
                        props.szOnLabel  = szOnLabel;
                        props.szOffLabel = szOffLabel;
                    }
                    if (ws.szType === 'table' && props.arrCells) {
                        for (const row of props.arrCells) {
                            for (const cell of row) {
                                if (cell && cell.szSid === szSid && cell.szPointType === 'DI') {
                                    cell.szOnLabel  = szOnLabel;
                                    cell.szOffLabel = szOffLabel;
                                }
                            }
                        }
                    }
                }
            }
            if (page.arrChildren) syncTree(page.arrChildren);
        }
    }
    syncTree(arrPageTree);
}

// AI 點位上限/下限顏色 → 同步所有同 SID 的 AI Widget（realtimeValue + table AI cell）
function _syncAiColorsToSid(szSid, szHighColor, szLowColor, elExclude) {
    if (!szSid) return;
    const arrSyncTypes = ['realtimeValue', 'gauge'];
    // 1. 同步當前畫布 DOM
    for (const el of canvas.querySelectorAll('.canvas-widget')) {
        if (!el.widgetProps) continue;
        const p = el.widgetProps;
        if (arrSyncTypes.includes(el.dataset.type) && p.szSid === szSid && el !== elExclude) {
            p.szHighColor = szHighColor;
            p.szLowColor  = szLowColor;
            renderWidget(el);
        }
        if (el.dataset.type === 'table' && p.arrCells) {
            let isChanged = false;
            for (const row of p.arrCells) {
                for (const cell of row) {
                    if (cell && cell.szSid === szSid && (cell.szPointType || 'AI') === 'AI') {
                        cell.szHighColor = szHighColor;
                        cell.szLowColor  = szLowColor;
                        isChanged = true;
                    }
                }
            }
            if (isChanged) renderWidget(el);
        }
    }
    // 2. 同步其他頁面的 arrWidgetState
    function syncTree(pages) {
        for (const page of pages) {
            if (page.szId !== szCurrentPageId && page.arrWidgetState) {
                for (const ws of page.arrWidgetState) {
                    const props = ws.props;
                    if (!props) continue;
                    if (arrSyncTypes.includes(ws.szType) && props.szSid === szSid) {
                        props.szHighColor = szHighColor;
                        props.szLowColor  = szLowColor;
                    }
                    if (ws.szType === 'table' && props.arrCells) {
                        for (const row of props.arrCells) {
                            for (const cell of row) {
                                if (cell && cell.szSid === szSid && (cell.szPointType || 'AI') === 'AI') {
                                    cell.szHighColor = szHighColor;
                                    cell.szLowColor  = szLowColor;
                                }
                            }
                        }
                    }
                }
            }
            if (page.arrChildren) syncTree(page.arrChildren);
        }
    }
    syncTree(arrPageTree);
}

// DI 點位警報顏色 → 同步所有同 SID 的 DI Widget（diPoint + table DI cell）
function _syncDiAlarmColorToSid(szSid, szAlarmColor, elExclude) {
    if (!szSid) return;
    // 1. 同步當前畫布 DOM
    for (const el of canvas.querySelectorAll('.canvas-widget')) {
        if (!el.widgetProps) continue;
        const p = el.widgetProps;
        if (el.dataset.type === 'diPoint' && p.szSid === szSid && el !== elExclude) {
            p.szAlarmColor = szAlarmColor;
            renderWidget(el);
        }
        if (el.dataset.type === 'table' && p.arrCells) {
            let isChanged = false;
            for (const row of p.arrCells) {
                for (const cell of row) {
                    if (cell && cell.szSid === szSid && cell.szPointType === 'DI') {
                        cell.szAlarmColor = szAlarmColor;
                        isChanged = true;
                    }
                }
            }
            if (isChanged) renderWidget(el);
        }
    }
    // 2. 同步其他頁面的 arrWidgetState
    function syncTree(pages) {
        for (const page of pages) {
            if (page.szId !== szCurrentPageId && page.arrWidgetState) {
                for (const ws of page.arrWidgetState) {
                    const props = ws.props;
                    if (!props) continue;
                    if (ws.szType === 'diPoint' && props.szSid === szSid) {
                        props.szAlarmColor = szAlarmColor;
                    }
                    if (ws.szType === 'table' && props.arrCells) {
                        for (const row of props.arrCells) {
                            for (const cell of row) {
                                if (cell && cell.szSid === szSid && cell.szPointType === 'DI') {
                                    cell.szAlarmColor = szAlarmColor;
                                }
                            }
                        }
                    }
                }
            }
            if (page.arrChildren) syncTree(page.arrChildren);
        }
    }
    syncTree(arrPageTree);
}

// 更新 prop → 重新渲染 widget 內容
function setProp(szKey, val) {
    if (!selectedEl) return;
    selectedEl.widgetProps[szKey] = val;
    // 當列數/欄數改變時同步 arrCells
    if (selectedEl.dataset.type === 'table' && (szKey === 'nRows' || szKey === 'nCols')) {
        syncArrCellsSize(selectedEl.widgetProps);
        nSelectedCellRow = -1;
        nSelectedCellCol = -1;
    }
    // DI ON/OFF 標籤修改時，同步到所有同 SID 的 DI Widget
    if (selectedEl.dataset.type === 'diPoint' && (szKey === 'szOnLabel' || szKey === 'szOffLabel')) {
        const p = selectedEl.widgetProps;
        _syncDiLabelsToSid(p.szSid, p.szOnLabel, p.szOffLabel, selectedEl);
    }
    // AI/Gauge 上限/下限顏色修改時，同步到所有同 SID 的 Widget
    if ((selectedEl.dataset.type === 'realtimeValue' || selectedEl.dataset.type === 'gauge') && (szKey === 'szHighColor' || szKey === 'szLowColor')) {
        const p = selectedEl.widgetProps;
        _syncAiColorsToSid(p.szSid, p.szHighColor, p.szLowColor, selectedEl);
    }
    // DI 警報顏色修改時，同步到所有同 SID 的 DI Widget
    if (selectedEl.dataset.type === 'diPoint' && szKey === 'szAlarmColor') {
        const p = selectedEl.widgetProps;
        _syncDiAlarmColorToSid(p.szSid, p.szAlarmColor, selectedEl);
    }
    renderWidget(selectedEl);
    // DI 顯示模式切換時重新渲染屬性面板（更新標籤文字）
    if (selectedEl.dataset.type === 'diPoint' && szKey === 'szDisplayMode') {
        renderPropPanel(selectedEl);
    }
}

function setPos(szSide, nVal) {
    if (!selectedEl) return;
    selectedEl.style[szSide] = Math.max(0, nVal) + 'px';
}

function setSize(szSide, nVal) {
    if (!selectedEl) return;
    const def = WIDGET_DEFS[selectedEl.dataset.type];
    const nMin = szSide === 'width' ? (def?.nMinW || 40) : (def?.nMinH || 30);
    selectedEl.style[szSide] = Math.max(nMin, nVal) + 'px';
}

// ============================================================
// 表格儲存格選取與屬性面板
// ============================================================
let nSelectedCellRow = -1;
let nSelectedCellCol = -1;

function onTableCellClick(widgetEl, nRow, nCol) {
    selectWidget(widgetEl);
    nSelectedCellRow = nRow;
    nSelectedCellCol = nCol;
    // 高亮選中 cell
    widgetEl.querySelectorAll('.w-table td, .w-table th').forEach(c => c.classList.remove('selected-cell'));
    const sel = widgetEl.querySelector(`.w-table [data-row="${nRow}"][data-col="${nCol}"]`);
    if (sel) sel.classList.add('selected-cell');
    renderTableCellPropPanel(widgetEl, nRow, nCol);
}

function renderTableCellPropPanel(el, nRow, nCol) {
    const props = el.widgetProps;
    initArrCells(props);
    if (nRow >= props.arrCells.length || nCol >= props.arrCells[nRow].length) return;
    const cell = props.arrCells[nRow][nCol];
    const isHeader = (nRow === 0);

    let szHtml = `
        <div style="font-size:11px;color:#0d6efd;margin-bottom:8px;cursor:pointer;"
             onclick="renderPropPanel(selectedEl)">
            <i class="fas fa-arrow-left me-1"></i>返回表格屬性
        </div>
        <div style="font-size:10px;color:#888;margin-bottom:6px;">
            ${isHeader ? '標題列' : '資料列'} [${nRow}, ${nCol}]
        </div>
        <hr class="prop-divider">
        <div class="prop-group">
            <label>文字內容</label>
            <input type="text" value="${escHtml(cell.szText || '')}"
                   oninput="setCellProp('szText', this.value)">
        </div>
        <div class="prop-group">
            <label>字體大小</label>
            <input type="number" value="${cell.nFontSize || 12}" min="8" max="24"
                   oninput="setCellProp('nFontSize', +this.value)">
        </div>
        <div class="prop-group">
            <label>文字顏色</label>
            <input type="color" value="${cell.szFontColor || (isHeader ? '#ffffff' : '#444444')}"
                   oninput="setCellProp('szFontColor', this.value)">
        </div>
        <div class="prop-group">
            <label>粗細</label>
            <select onchange="setCellProp('szFontWeight', this.value)">
                <option value="normal" ${(cell.szFontWeight||'normal')==='normal'?'selected':''}>一般</option>
                <option value="bold" ${cell.szFontWeight==='bold'?'selected':''}>粗體</option>
                <option value="500" ${cell.szFontWeight==='500'?'selected':''}>中粗</option>
            </select>
        </div>
        <div class="prop-group">
            <label>對齊</label>
            <select onchange="setCellProp('szAlign', this.value)">
                <option value="left" ${(cell.szAlign||'left')==='left'?'selected':''}>靠左</option>
                <option value="center" ${cell.szAlign==='center'?'selected':''}>置中</option>
                <option value="right" ${cell.szAlign==='right'?'selected':''}>靠右</option>
            </select>
        </div>`;

    if (!isHeader) {
        // 綁定點位
        const szSidLabel = cell.szSid
            ? `<span style="font-size:11px;color:#c8c8c8;">${escHtml(cell.szPointName || cell.szSid)}</span>`
            : `<span style="color:#888;font-size:11px;">（未綁定）</span>`;
        szHtml += `
        <hr class="prop-divider">
        <div class="prop-group">
            <label>綁定點位</label>
            <div style="display:flex;align-items:center;gap:6px;flex-wrap:wrap;">
                ${szSidLabel}
                <button class="btn btn-outline-warning btn-sm py-0 px-2" style="font-size:11px;"
                        onclick="openCellPointPicker(${nRow}, ${nCol})">
                    <i class="fas fa-exchange-alt me-1"></i>${cell.szSid ? '重選' : '綁定'}
                </button>
                ${cell.szSid ? `<button class="btn btn-outline-danger btn-sm py-0 px-2" style="font-size:11px;"
                        onclick="clearCellSid(${nRow}, ${nCol})">
                    <i class="fas fa-unlink me-1"></i>清除
                </button>` : ''}
            </div>
        </div>`;
        // 首欄以外才顯示點位屬性
        if (nCol > 0) {
            const szPT = cell.szPointType || 'AI';
            szHtml += `
            <div class="prop-group">
                <label>點位屬性</label>
                <select onchange="setCellProp('szPointType', this.value)">
                    <option value="AI" ${szPT === 'AI' ? 'selected' : ''}>AI 點位</option>
                    <option value="DI" ${szPT === 'DI' ? 'selected' : ''}>DI 點位</option>
                </select>
            </div>`;
            if (szPT === 'AI') {
                szHtml += `
                <div class="prop-group">
                    <label>小數位數（整欄）</label>
                    <input type="number" value="${props.arrColDecimals[nCol] ?? ''}" min="0" max="6"
                           placeholder="不限"
                           oninput="setColDecimals(${nCol}, this.value)">
                </div>
                <div class="prop-group">
                    <label>上限顏色</label>
                    <input type="color" value="${cell.szHighColor || '#dc3545'}"
                           oninput="setCellProp('szHighColor', this.value)">
                </div>
                <div class="prop-group">
                    <label>下限顏色</label>
                    <input type="color" value="${cell.szLowColor || '#fd7e14'}"
                           oninput="setCellProp('szLowColor', this.value)">
                </div>
                `;
            } else if (szPT === 'DI') {
                szHtml += `
                <div class="prop-group">
                    <label>ON 文字</label>
                    <input type="text" value="${escHtml(cell.szOnLabel || 'ON')}"
                           oninput="setCellProp('szOnLabel', this.value)">
                </div>
                <div class="prop-group">
                    <label>OFF 文字</label>
                    <input type="text" value="${escHtml(cell.szOffLabel || 'OFF')}"
                           oninput="setCellProp('szOffLabel', this.value)">
                </div>
                <div class="prop-group">
                    <label>\u8b66\u5831\u5b57\u8272</label>
                    <input type="color" value="${cell.szAlarmColor || '#dc3545'}"
                           oninput="setCellProp('szAlarmColor', this.value)">
                </div>
                `;
            }
        }
    }

    document.getElementById('propBody').innerHTML = szHtml;
}

function setCellProp(szKey, val) {
    if (!selectedEl || nSelectedCellRow < 0) return;
    const props = selectedEl.widgetProps;
    if (!props.arrCells || nSelectedCellRow >= props.arrCells.length) return;
    // 字體大小、對齊、粗細 → 整欄同步修改（標題列與資料列各自獨立）
    const arrColWideKeys = ['nFontSize', 'szAlign', 'szFontWeight', 'szPointType'];
    if (arrColWideKeys.includes(szKey)) {
        const nCol = nSelectedCellCol;
        const isHeader = nSelectedCellRow === 0;
        for (let ri = 0; ri < props.arrCells.length; ri++) {
            // 標題列只改標題列，資料列只改資料列
            if (isHeader ? ri === 0 : ri > 0) {
                if (props.arrCells[ri][nCol]) {
                    props.arrCells[ri][nCol][szKey] = val;
                }
            }
        }
    } else {
        props.arrCells[nSelectedCellRow][nSelectedCellCol][szKey] = val;
    }
    // 表格 DI Cell ON/OFF 標籤修改時，同步到所有同 SID 的 DI Widget
    if ((szKey === 'szOnLabel' || szKey === 'szOffLabel')) {
        const cell = props.arrCells[nSelectedCellRow]?.[nSelectedCellCol];
        if (cell && cell.szSid && cell.szPointType === 'DI') {
            _syncDiLabelsToSid(cell.szSid, cell.szOnLabel, cell.szOffLabel, null);
        }
    }
    // 表格 AI Cell 上限/下限顏色修改時，同步到所有同 SID 的 AI Widget
    if ((szKey === 'szHighColor' || szKey === 'szLowColor')) {
        const cell = props.arrCells[nSelectedCellRow]?.[nSelectedCellCol];
        if (cell && cell.szSid && (cell.szPointType || 'AI') === 'AI') {
            _syncAiColorsToSid(cell.szSid, cell.szHighColor, cell.szLowColor, null);
        }
    }
    // 表格 DI Cell 警報顏色修改時，同步到所有同 SID 的 DI Widget
    if (szKey === 'szAlarmColor') {
        const cell = props.arrCells[nSelectedCellRow]?.[nSelectedCellCol];
        if (cell && cell.szSid && cell.szPointType === 'DI') {
            _syncDiAlarmColorToSid(cell.szSid, cell.szAlarmColor, null);
        }
    }
    renderWidget(selectedEl);
    // 重新高亮
    const sel = selectedEl.querySelector(`.w-table [data-row="${nSelectedCellRow}"][data-col="${nSelectedCellCol}"]`);
    if (sel) sel.classList.add('selected-cell');
    // 切換點位屬性或警報開關時重新渲染屬性面板
    const arrReRenderKeys = ['szPointType'];
    if (arrReRenderKeys.includes(szKey)) {
        renderTableCellPropPanel(selectedEl, nSelectedCellRow, nSelectedCellCol);
    }
}

function setColDecimals(nCol, val) {
    if (!selectedEl) return;
    const props = selectedEl.widgetProps;
    initArrCells(props);
    props.arrColDecimals[nCol] = val === '' ? null : Math.max(0, Math.min(6, parseInt(val)));
    // 不需重新渲染，小數位數僅影響 ScadaPage 即時顯示
}

function clearCellSid(nRow, nCol) {
    if (!selectedEl) return;
    const cell = selectedEl.widgetProps.arrCells[nRow][nCol];
    cell.szSid = '';
    cell.szPointName = '';
    renderWidget(selectedEl);
    const sel = selectedEl.querySelector(`.w-table [data-row="${nRow}"][data-col="${nCol}"]`);
    if (sel) sel.classList.add('selected-cell');
    renderTableCellPropPanel(selectedEl, nRow, nCol);
}

// ============================================================
// 移動 Widget（拖移 header）
// ============================================================
function startMove(e, el) {
    if (e.ctrlKey) return; // Ctrl+click 由 onWidgetMouseDown 處理
    e.stopPropagation();

    // 確保被拖曳的元件在選取集合中
    if (!selectedWidgetIds.has(el.id)) {
        clearWidgetSelection();
        selectedWidgetIds.add(el.id);
        updateWidgetSelectionVisual();
    }
    selectWidget(el);

    isMoving = true;
    document.querySelector('.property-panel')?.classList.add('collapsed');
    moveStartMouse = { x: e.clientX, y: e.clientY };
    moveOrigPositions = {};
    selectedWidgetIds.forEach(szId => {
        const w = document.getElementById(szId);
        if (w) moveOrigPositions[szId] = { x: parseInt(w.style.left) || 0, y: parseInt(w.style.top) || 0 };
    });
    document.addEventListener('mousemove', onDocMouseMove);
    document.addEventListener('mouseup', onDocMouseUp);
}

// ============================================================
// 縮放 Widget（右下角拖把）
// ============================================================
function startResize(e, el) {
    isResizing  = true;
    resizingEl  = el;
    document.querySelector('.property-panel')?.classList.add('collapsed');
    resizeStart = {
        mx: e.clientX,
        my: e.clientY,
        w:  el.offsetWidth,
        h:  el.offsetHeight
    };
    document.addEventListener('mousemove', onDocMouseMove);
    document.addEventListener('mouseup', onDocMouseUp);
}

// ============================================================
// 全域 mousemove / mouseup
// ============================================================
function onDocMouseMove(e) {
    if (isMoving && selectedWidgetIds.size > 0) {
        const dx = e.clientX - moveStartMouse.x;
        const dy = e.clientY - moveStartMouse.y;
        for (const szId of selectedWidgetIds) {
            const w = document.getElementById(szId);
            const orig = moveOrigPositions[szId];
            if (!w || !orig) continue;
            let x = snapGrid(orig.x + dx);
            let y = snapGrid(orig.y + dy);
            x = Math.max(0, Math.min(canvas.offsetWidth - w.offsetWidth, x));
            y = Math.max(0, Math.min(canvas.offsetHeight - w.offsetHeight, y));
            w.style.left = x + 'px';
            w.style.top = y + 'px';
        }
        if (selectedEl) syncPosInputs(parseInt(selectedEl.style.left), parseInt(selectedEl.style.top));
    }

    if (isResizing && resizingEl) {
        const dx = e.clientX - resizeStart.mx;
        const dy = e.clientY - resizeStart.my;
        const def = WIDGET_DEFS[resizingEl.dataset.type];
        const nW = snapGrid(Math.max(def?.nMinW || 40, resizeStart.w + dx));
        const nH = snapGrid(Math.max(def?.nMinH || 30, resizeStart.h + dy));
        resizingEl.style.width  = nW + 'px';
        resizingEl.style.height = nH + 'px';
        syncSizeInputs(nW, nH);
    }
}

function onDocMouseUp() {
    isMoving   = false;
    isResizing = false;
    resizingEl = null;
    document.removeEventListener('mousemove', onDocMouseMove);
    document.removeEventListener('mouseup', onDocMouseUp);
    if (selectedEl) document.querySelector('.property-panel')?.classList.remove('collapsed');
}

// ============================================================
// 刪除 Widget
// ============================================================
function deleteWidget(szId) {
    const el = document.getElementById(szId);
    if (!el) return;
    if (selectedEl === el) selectWidget(null);
    el.remove();
}

// 鍵盤快捷鍵：Delete/Backspace 刪除、Ctrl+C/V/X/A 複製貼上
document.addEventListener('keydown', e => {
    const tag = document.activeElement.tagName;
    const isInput = tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT';

    if ((e.key === 'Delete' || e.key === 'Backspace') && !isInput) {
        if (selectedWidgetIds.size > 0) deleteSelectedWidgets();
        else if (selectedEl) deleteWidget(selectedEl.id);
    }
    if (e.key === 'Escape') {
        clearWidgetSelection();
        selectWidget(null);
    }
    if (!isInput && (e.ctrlKey || e.metaKey)) {
        if (e.key === 'a') { e.preventDefault(); canvas.querySelectorAll('.canvas-widget').forEach(w => selectedWidgetIds.add(w.id)); updateWidgetSelectionVisual(); }
        if (e.key === 'c' && selectedWidgetIds.size > 0) { e.preventDefault(); copySelectedWidgets(); }
        if (e.key === 'x' && selectedWidgetIds.size > 0) { e.preventDefault(); copySelectedWidgets(); deleteSelectedWidgets(); }
        if (e.key === 'v') { e.preventDefault(); pasteWidgets(); }
    }
});

// ============================================================
// 點位選擇器（兩步驟：先選設備，再選點位）
// ============================================================
let arrAllDevices      = null;   // 快取設備清單
let arrAllPoints       = null;   // 快取所有點位
let nPickedDevId       = -1;     // 已選設備的 DB Id（用於 SID 範圍比對）
let nPickedModbusId    = null;   // 已選子項目的 ModbusID（精確過濾）
let szPickedSid        = null;   // 已選點位 SID
let pendingGaugeX      = 0;
let pendingGaugeY      = 0;
let szPickerWidgetType = 'gauge'; // 記錄是哪種 widget 開啟選擇器
let _pointPickerModal  = null;
let nPickedCalcGroup   = null;   // 計算點位群組篩選（null=全部, ''=未分組, 'GroupName'=指定群組）
let szPickedDbGroup    = null;   // DB 來源 Coordinator 群組篩選（null=全部, 'CoordinatorName'=指定群組）

const CALC_DEVICE_ID = -999; // 計算點位的虛擬設備 ID
const DB_DEVICE_ID   = -998; // DB 來源點位的虛擬設備 ID

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
    await _ensurePickerData();
    _showPickerStep1();
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
        alert('無法載入設備/點位清單，請確認資料庫連線。\n' + err.message);
        throw err;
    }
}

// 為每個點位計算所屬設備/子設備名稱，作為顯示前綴
function _enrichPointsWithDeviceLabel() {
    if (!arrAllDevices || !arrAllPoints) return;
    arrAllPoints.forEach(p => {
        if (isDbPoint(p.szSid)) {
            p.szDeviceLabel = p.szGroupName || 'DB 來源';
            return;
        }
        if (isCalcPoint(p.szSid)) {
            p.szDeviceLabel = p.szGroupName || '\u8a08\u7b97\u9ede\u4f4d';
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
    document.getElementById('ppModalTitle').textContent = '\u9078\u64c7\u9ede\u4f4d\u4f86\u6e90';
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
    document.getElementById('ppStep0').style.display = 'none';
    document.getElementById('ppStep1').style.display = '';
    document.getElementById('ppStep2').style.display = 'none';
    document.getElementById('ppModalTitle').textContent = '\u9078\u64c7\u8a2d\u5099';
}

function showCalcPointStep() {
    nPickedDevId = CALC_DEVICE_ID;
    nPickedModbusId = null;
    szPickedSid = null;
    nPickedCalcGroup = null;

    // 取得計算點位的群組清單
    const calcGroups = _getCalcGroups();
    if (calcGroups.length > 0) {
        // 有群組 — 顯示群組清單（Step 1）
        document.getElementById('ppStep0').style.display = 'none';
        document.getElementById('ppStep1').style.display = '';
        document.getElementById('ppStep2').style.display = 'none';
        document.getElementById('ppModalTitle').textContent = '\u9078\u64c7\u8a08\u7b97\u9ede\u4f4d\u7fa4\u7d44';
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
                <div class="point-sid">${nPts} \u500b\u9ede\u4f4d</div>
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
                <div class="point-name">\u672a\u5206\u7d44</div>
                <div class="point-sid">${nPts} \u500b\u9ede\u4f4d</div>
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
    document.getElementById('ppDeviceName').textContent = szGroup || '\u672a\u5206\u7d44';
    document.getElementById('ppDeviceIcon').className = 'fas fa-layer-group me-1';
    document.getElementById('ppStep0').style.display = 'none';
    document.getElementById('ppStep1').style.display = 'none';
    document.getElementById('ppStep2').style.display = '';
    document.getElementById('ppModalTitle').textContent = '\u9078\u64c7\u8a08\u7b97\u9ede\u4f4d';
    document.getElementById('ppPointSearch').value = '';
    document.getElementById('btnConfirmPoint').disabled = true;
    _renderFilteredPoints('');
}

function _showCalcPointsFlat() {
    document.getElementById('ppStep0').style.display = 'none';
    document.getElementById('ppStep1').style.display = 'none';
    document.getElementById('ppStep2').style.display = '';
    document.getElementById('ppModalTitle').textContent = '\u9078\u64c7\u8a08\u7b97\u9ede\u4f4d';
    document.getElementById('ppDeviceName').textContent = '\u8a08\u7b97\u9ede\u4f4d';
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

    const dbGroups = _getDbGroups();
    document.getElementById('ppStep0').style.display = 'none';
    document.getElementById('ppStep1').style.display = '';
    document.getElementById('ppStep2').style.display = 'none';
    document.getElementById('ppModalTitle').textContent = '選擇 DB 來源';
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
            '<i class="fas fa-database" style="font-size:24px;display:block;margin-bottom:8px;"></i>尚無 DB 來源點位</div>';
        return;
    }
    container.innerHTML = groups.map(g => {
        const nPts = (arrAllPoints || []).filter(p => isDbPoint(p.szSid) && (p.szGroupName || '') === g).length;
        const szDisplay = g || 'DB 來源';
        return `
        <div class="point-list-item" onclick="selectDbGroup('${escHtml(g)}')">
            <i class="fas fa-database" style="font-size:14px;color:#6ec1a3;flex-shrink:0;"></i>
            <div style="flex:1;min-width:0;">
                <div class="point-name">${escHtml(szDisplay)}</div>
                <div class="point-sid">${nPts} 個點位</div>
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
    document.getElementById('ppDeviceName').textContent = szGroup || 'DB 來源';
    document.getElementById('ppDeviceIcon').className = 'fas fa-database me-1';
    document.getElementById('ppStep0').style.display = 'none';
    document.getElementById('ppStep1').style.display = 'none';
    document.getElementById('ppStep2').style.display = '';
    document.getElementById('ppModalTitle').textContent = '選擇 DB 來源點位';
    document.getElementById('ppPointSearch').value = '';
    document.getElementById('btnConfirmPoint').disabled = true;
    _renderFilteredPoints('');
}

function goBackToStep0() {
    document.getElementById('ppStep0').style.display = '';
    document.getElementById('ppStep1').style.display = 'none';
    document.getElementById('ppStep2').style.display = 'none';
    document.getElementById('ppModalTitle').textContent = '\u9078\u64c7\u9ede\u4f4d\u4f86\u6e90';
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
        szDevLabel      = point.szGroupName || '\u8a08\u7b97\u9ede\u4f4d';
    } else if (isDbPoint(szBoundSid)) {
        nPickedDevId    = DB_DEVICE_ID;
        nPickedModbusId = null;
        szPickedSid     = szBoundSid;
        szPickedDbGroup = point.szGroupName || null;
        szDevLabel      = point.szGroupName || 'DB 來源';
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
    document.getElementById('ppModalTitle').textContent = isCalcPoint(szBoundSid) ? '\u9078\u64c7\u8a08\u7b97\u9ede\u4f4d' : '\u9078\u64c7\u9ede\u4f4d';
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
            '<i class="fas fa-plug" style="font-size:24px;display:block;margin-bottom:8px;"></i>尚無設備</div>';
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
                    <div class="point-sid">${nPts} 個點位</div>
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
                <div class="point-sid">${nPts} 個點位</div>
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
    document.getElementById('ppModalTitle').textContent = '\u9078\u64c7\u9ede\u4f4d';
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
        document.getElementById('ppModalTitle').textContent = '\u9078\u64c7\u8a2d\u5099';
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
            '<i class="fas fa-inbox" style="font-size:24px;display:block;margin-bottom:8px;"></i>無符合點位</div>';
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
async function rerouteDiPointPoint() {
    if (!selectedEl || selectedEl.dataset.type !== 'diPoint') return;
    pendingGaugeX      = -1;
    pendingGaugeY      = -1;
    szPickerWidgetType = 'diPoint';
    szPickedSid        = null;
    nPickedDevId       = -1;
    try {
        await _ensurePickerData();
        _showPickerForBoundSid(_getBoundSidFromWidget(selectedEl));
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

function createGaugeWithPoint(point, x, y) {
    const def  = WIDGET_DEFS['gauge'];
    const szId = 'w' + (++nWidgetCounter);
    const szFullName = point.szDeviceLabel ? point.szDeviceLabel + ' / ' + point.szName : point.szName;

    const el = document.createElement('div');
    el.id           = szId;
    el.className    = 'canvas-widget';
    el.dataset.type = 'gauge';
    el.style.left   = x + 'px';
    el.style.top    = y + 'px';
    el.style.width  = def.nDefaultW + 'px';
    el.style.height = def.nDefaultH + 'px';
    const fNewMin = point.fMin ?? 0;
    const fNewMax = point.fMax ?? 100;
    el.widgetProps  = {
        ...def.defaultProps,
        szSid:       point.szSid,
        szPointName: szFullName,
        szTitle:     szFullName,
        szUnit:  point.szUnit || '',
        fMin:    fNewMin,
        fMax:    fNewMax,
        fValue:  (fNewMin + fNewMax) / 2
    };

    renderWidget(el);
    canvas.appendChild(el);
    selectWidget(el);
}

function createControlBtnWithPoint(point, x, y) {
    const def  = WIDGET_DEFS['controlBtn'];
    const szId = 'w' + (++nWidgetCounter);
    const szFullName = point.szDeviceLabel ? point.szDeviceLabel + ' / ' + point.szName : point.szName;

    const el = document.createElement('div');
    el.id           = szId;
    el.className    = 'canvas-widget';
    el.dataset.type = 'controlBtn';
    el.style.left   = x + 'px';
    el.style.top    = y + 'px';
    el.style.width  = def.nDefaultW + 'px';
    el.style.height = def.nDefaultH + 'px';
    el.widgetProps  = {
        ...def.defaultProps,
        szCid:       point.szSid,
        szPointName: szFullName,
        szTitle:     szFullName
    };

    renderWidget(el);
    canvas.appendChild(el);
    selectWidget(el);
}

function createRealtimeValueWithPoint(point, x, y) {
    const def  = WIDGET_DEFS['realtimeValue'];
    const szId = 'w' + (++nWidgetCounter);
    const szFullName = point.szDeviceLabel ? point.szDeviceLabel + ' / ' + point.szName : point.szName;

    const el = document.createElement('div');
    el.id           = szId;
    el.className    = 'canvas-widget';
    el.dataset.type = 'realtimeValue';
    el.style.left   = x + 'px';
    el.style.top    = y + 'px';
    el.style.width  = def.nDefaultW + 'px';
    el.style.height = def.nDefaultH + 'px';
    el.widgetProps  = {
        ...def.defaultProps,
        szSid:       point.szSid,
        szPointName: szFullName,
        szTitle:     szFullName,
        szUnit:      point.szUnit || ''
    };

    renderWidget(el);
    canvas.appendChild(el);
    selectWidget(el);
}

function createDiPointWithPoint(point, x, y) {
    const def  = WIDGET_DEFS['diPoint'];
    const szId = 'w' + (++nWidgetCounter);
    const szFullName = point.szDeviceLabel ? point.szDeviceLabel + ' / ' + point.szName : point.szName;

    const el = document.createElement('div');
    el.id           = szId;
    el.className    = 'canvas-widget';
    el.dataset.type = 'diPoint';
    el.style.left   = x + 'px';
    el.style.top    = y + 'px';
    el.style.width  = def.nDefaultW + 'px';
    el.style.height = def.nDefaultH + 'px';
    el.widgetProps  = {
        ...def.defaultProps,
        szSid:       point.szSid,
        szPointName: szFullName,
        szTitle:     szFullName
    };
    // 繼承同 SID 已存在的 DI ON/OFF 標籤
    const diLabels = _findDiLabelsForSid(point.szSid);
    if (diLabels) {
        el.widgetProps.szOnLabel  = diLabels.szOnLabel;
        el.widgetProps.szOffLabel = diLabels.szOffLabel;
    }

    renderWidget(el);
    canvas.appendChild(el);
    selectWidget(el);
}

function createAoPointWithPoint(point, x, y) {
    const def  = WIDGET_DEFS['aoPoint'];
    const szId = 'w' + (++nWidgetCounter);
    const szFullName = point.szDeviceLabel ? point.szDeviceLabel + ' / ' + point.szName : point.szName;

    const el = document.createElement('div');
    el.id           = szId;
    el.className    = 'canvas-widget';
    el.dataset.type = 'aoPoint';
    el.style.left   = x + 'px';
    el.style.top    = y + 'px';
    el.style.width  = def.nDefaultW + 'px';
    el.style.height = def.nDefaultH + 'px';
    el.widgetProps  = {
        ...def.defaultProps,
        szCid:         point.szSid,
        szPointName:   szFullName,
        szTitle:       szFullName,
        szDisplayName: szFullName,
        szUnit:        point.szUnit || '',
        fMin:          point.fMin ?? 0,
        fMax:          point.fMax ?? 100
    };

    renderWidget(el);
    canvas.appendChild(el);
    selectWidget(el);
}

function createDoPointWithPoint(point, x, y) {
    const def  = WIDGET_DEFS['doPoint'];
    const szId = 'w' + (++nWidgetCounter);
    const szFullName = point.szDeviceLabel ? point.szDeviceLabel + ' / ' + point.szName : point.szName;

    const el = document.createElement('div');
    el.id           = szId;
    el.className    = 'canvas-widget';
    el.dataset.type = 'doPoint';
    el.style.left   = x + 'px';
    el.style.top    = y + 'px';
    el.style.width  = def.nDefaultW + 'px';
    el.style.height = def.nDefaultH + 'px';
    el.widgetProps  = {
        ...def.defaultProps,
        szCid:         point.szSid,
        szPointName:   szFullName,
        szTitle:       szFullName,
        szDisplayName: szFullName
    };

    renderWidget(el);
    canvas.appendChild(el);
    selectWidget(el);
}

function onGaugeBgTransparentChange(isChecked) {
    if (isChecked) {
        setProp('szBgColor', 'transparent');
        const picker = document.getElementById('gaugeBgPicker');
        if (picker) picker.style.display = 'none';
    } else {
        const picker = document.getElementById('gaugeBgPicker');
        const szColor = picker ? picker.value : '#ffffff';
        setProp('szBgColor', szColor);
        if (picker) picker.style.display = '';
    }
}

function onCtrlBtnBgTransparentChange(isChecked) {
    if (isChecked) {
        setProp('szBgColor', 'transparent');
        const picker = document.getElementById('ctrlBtnBgPicker');
        if (picker) picker.style.display = 'none';
    } else {
        const picker = document.getElementById('ctrlBtnBgPicker');
        const szColor = picker ? picker.value : '#ffffff';
        setProp('szBgColor', szColor);
        if (picker) picker.style.display = '';
    }
}

function onRtValBgTransparentChange(isChecked) {
    if (isChecked) {
        setProp('szBgColor', 'transparent');
        const picker = document.getElementById('rtValBgPicker');
        if (picker) picker.style.display = 'none';
    } else {
        const picker = document.getElementById('rtValBgPicker');
        const szColor = picker ? picker.value : '#ffffff';
        setProp('szBgColor', szColor);
        if (picker) picker.style.display = '';
    }
}

function onDiPointBgTransparentChange(isChecked) {
    if (isChecked) {
        setProp('szBgColor', 'transparent');
        const picker = document.getElementById('diPointBgPicker');
        if (picker) picker.style.display = 'none';
    } else {
        const picker = document.getElementById('diPointBgPicker');
        const szColor = picker ? picker.value : '#ffffff';
        setProp('szBgColor', szColor);
        if (picker) picker.style.display = '';
    }
}

function onAoPointBgTransparentChange(isChecked) {
    if (isChecked) {
        setProp('szBgColor', 'transparent');
        const picker = document.getElementById('aoPointBgPicker');
        if (picker) picker.style.display = 'none';
    } else {
        const picker = document.getElementById('aoPointBgPicker');
        const szColor = picker ? picker.value : '#ffffff';
        setProp('szBgColor', szColor);
        if (picker) picker.style.display = '';
    }
}

function onDoPointBgTransparentChange(isChecked) {
    if (isChecked) {
        setProp('szBgColor', 'transparent');
        const picker = document.getElementById('doPointBgPicker');
        if (picker) picker.style.display = 'none';
    } else {
        const picker = document.getElementById('doPointBgPicker');
        const szColor = picker ? picker.value : '#ffffff';
        setProp('szBgColor', szColor);
        if (picker) picker.style.display = '';
    }
}

// ============================================================
// 水泵 — 多點位綁定 / 背景色
// ============================================================
let _pumpPickerSlot    = '';   // 目前正在選擇的 pump 綁定欄位 key（如 'szSidRun'、'szCidStartStop'）
let _pumpPickerNameKey = '';   // 對應的名稱 key（如 'szRunName'、'szStartStopName'）

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

function onPumpBgTransparentChange(isChecked) {
    if (isChecked) {
        setProp('szBgColor', 'transparent');
        const picker = document.getElementById('pumpBgPicker');
        if (picker) picker.style.display = 'none';
    } else {
        const picker = document.getElementById('pumpBgPicker');
        const szColor = picker ? picker.value : '#ffffff';
        setProp('szBgColor', szColor);
        if (picker) picker.style.display = '';
    }
}

// ============================================================
// 清除畫布
// ============================================================
function clearCanvas() {
    if (!canvas.hasChildNodes()) return;
    if (confirm('確定要清除畫布上所有元件？')) {
        canvas.innerHTML = '';
        selectWidget(null);
        nWidgetCounter = 0;
        // 同步清空目前頁面的 widget 狀態
        const page = findPage(szCurrentPageId);
        if (page) page.arrWidgetState = [];
    }
}

// ============================================================
// 匯入圖片
// ============================================================

// 開啟檔案選擇器
function triggerImportImage() {
    document.getElementById('imgFileInput').click();
}

// 使用者選好檔案後觸發
function onImageFileSelected(input) {
    const file = input.files[0];
    if (!file) return;

    // 重設 input 讓相同檔案可再次選取
    input.value = '';

    const reader = new FileReader();
    reader.onload = function (e) {
        applyCanvasBgImage(e.target.result, file.name);
    };
    reader.readAsDataURL(file);
}

// 將 dataUrl 套用為畫布背景，並依圖片原始尺寸調整畫布
function applyCanvasBgImage(szDataUrl, szFileName) {
    const img = new Image();
    img.onload = function () {
        const nImgW = img.naturalWidth;
        const nImgH = img.naturalHeight;

        // 畫布調整為圖片原始尺寸（最小 400×300）
        const nCanvasW = Math.max(400, nImgW);
        const nCanvasH = Math.max(300, nImgH);
        canvas.style.width  = nCanvasW + 'px';
        canvas.style.height = nCanvasH + 'px';

        // 背景：圖片鋪底，格線疊在最上層形成分割網格
        canvas.style.backgroundImage = [
            'linear-gradient(rgba(0,0,0,0.08) 1px, transparent 1px)',
            'linear-gradient(90deg, rgba(0,0,0,0.08) 1px, transparent 1px)',
            `url('${szDataUrl}')`
        ].join(', ');
        canvas.style.backgroundSize     = '10px 10px, 10px 10px, 100% 100%';
        canvas.style.backgroundPosition = '0 0, 0 0, 0 0';
        canvas.style.backgroundRepeat   = 'repeat, repeat, no-repeat';
        canvas.style.backgroundColor    = '#fff';

        // 追蹤目前背景（供頁面切換儲存用）
        szCurrentBgDataUrl  = szDataUrl;
        szCurrentBgFileName = szFileName;

        // 更新工具列徽章
        document.getElementById('bgImgName').textContent = szFileName;
        document.getElementById('bgImgSize').textContent =
            `${nImgW} × ${nImgH} px`;
        const badge = document.getElementById('bgImgBadge');
        badge.style.display = 'flex';
    };
    img.src = szDataUrl;
}

// 移除背景圖，恢復預設格線白底
function clearBgImage() {
    canvas.style.backgroundImage = [
        'linear-gradient(rgba(0,0,0,0.06) 1px, transparent 1px)',
        'linear-gradient(90deg, rgba(0,0,0,0.06) 1px, transparent 1px)'
    ].join(', ');
    canvas.style.backgroundSize     = '10px 10px';
    canvas.style.backgroundPosition = '0 0';
    canvas.style.backgroundRepeat   = 'repeat';
    canvas.style.backgroundColor    = '#ffffff';
    canvas.style.width  = '1200px';
    canvas.style.height = '800px';

    szCurrentBgDataUrl  = null;
    szCurrentBgFileName = null;
    document.getElementById('bgImgBadge').style.display = 'none';
    document.getElementById('bgImgName').textContent = '';
}

// ============================================================
// 畫布狀態列（hover 顯示點位資訊）
// ============================================================
function showCanvasStatus(szText) {
    const bar = document.getElementById('canvasStatusbar');
    if (!bar || !szText) return;
    bar.innerHTML = '<i class="fas fa-map-pin"></i> ' + escHtml(szText);
    bar.classList.add('visible');
}
function hideCanvasStatus() {
    const bar = document.getElementById('canvasStatusbar');
    if (bar) bar.classList.remove('visible');
}

// ============================================================
// 工具函式
// ============================================================
function snapGrid(n, nGrid = 10) {
    return Math.round(n / nGrid) * nGrid;
}

function escHtml(s) {
    return String(s)
        .replace(/&/g, '&amp;')
        .replace(/"/g, '&quot;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;');
}

// 判斷色碼是否為深色（亮度 < 128 視為深色），支援 hex 與 rgb() 格式
function isDarkColor(sz) {
    if (!sz || sz === 'transparent') return false;
    let r, g, b;
    const mHex = sz.match(/^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i);
    if (mHex) {
        r = parseInt(mHex[1], 16); g = parseInt(mHex[2], 16); b = parseInt(mHex[3], 16);
    } else {
        const mRgb = sz.match(/rgb\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)/);
        if (!mRgb) return false;
        r = +mRgb[1]; g = +mRgb[2]; b = +mRgb[3];
    }
    return (r * 299 + g * 587 + b * 114) / 1000 < 128;
}

function syncPosInputs(x, y) {
    const pX = document.getElementById('pX');
    const pY = document.getElementById('pY');
    if (pX) pX.value = x;
    if (pY) pY.value = y;
}

function syncSizeInputs(w, h) {
    const pw = document.getElementById('pW');
    const ph = document.getElementById('pH');
    if (pw) pw.value = w;
    if (ph) ph.value = h;
}

// ============================================================
// 頁面樹 — 渲染
// ============================================================
function renderPageTree() {
    const wrap = document.getElementById('pageTreeWrap');
    wrap.innerHTML = '';
    arrPageTree.forEach(p => renderPageNode(wrap, p, 0));

    // 空白區域：右鍵 → 新增根頁面
    const blankZone = document.createElement('div');
    blankZone.className = 'page-blank-zone';
    blankZone.addEventListener('contextmenu', e => {
        e.preventDefault();
        showCtxMenu(e, null);
    });
    wrap.appendChild(blankZone);
}

function renderPageNode(container, page, nDepth) {
    const isActive   = page.szId === szCurrentPageId;
    const hasChildren = page.arrChildren && page.arrChildren.length > 0;

    const el = document.createElement('div');
    el.className = 'page-node' + (isActive ? ' active' : '');
    el.dataset.id = page.szId;
    el.style.paddingLeft = (8 + nDepth * 14) + 'px';

    const szIconClass = page.szIcon || (hasChildren ? 'fa-folder-open' : 'fa-file-alt');
    el.innerHTML = `<i class="fas ${szIconClass} pn-icon"></i><span class="pn-name">${escHtml(page.szName)}</span>`;

    el.addEventListener('click',        ()  => selectPage(page.szId));
    el.addEventListener('contextmenu',  e  => { e.preventDefault(); e.stopPropagation(); showCtxMenu(e, page.szId); });

    container.appendChild(el);

    if (hasChildren) {
        page.arrChildren.forEach(child => renderPageNode(container, child, nDepth + 1));
    }
}

// ============================================================
// 頁面樹 — 切換（儲存舊 / 載入新）
// ============================================================
function selectPage(szId) {
    if (szId === szCurrentPageId) return;
    saveCurrentPageState();
    szCurrentPageId = szId;
    const page = findPage(szId);
    if (page) loadPageState(page);
    renderPageTree();
}

// 儲存前過濾掉警報相關屬性（警報由 AlarmSetting 頁面集中管理）
const _ALARM_KEYS = new Set([
    'isAlarmEnabled', 'isAlarmHigh', 'fAlarmHigh', 'fDeadbandHigh', 'szAlarmHighColor',
    'isAlarmLow', 'fAlarmLow', 'fDeadbandLow', 'szAlarmLowColor',
    'szAlarmTrigger', 'szAlarmColor'
]);
function stripAlarmProps(objProps) {
    const result = {};
    for (const [k, v] of Object.entries(objProps)) {
        if (_ALARM_KEYS.has(k)) continue;
        if (k === 'arrCells' && Array.isArray(v)) {
            result[k] = v.map(row =>
                Array.isArray(row)
                    ? row.map(cell => {
                        if (typeof cell !== 'object' || cell === null) return cell;
                        const stripped = {};
                        for (const [ck, cv] of Object.entries(cell)) {
                            if (!_ALARM_KEYS.has(ck)) stripped[ck] = cv;
                        }
                        return stripped;
                    })
                    : row
            );
        } else {
            result[k] = v;
        }
    }
    return result;
}

function saveCurrentPageState() {
    const page = findPage(szCurrentPageId);
    if (!page) return;
    page.szBgDataUrl    = szCurrentBgDataUrl;
    page.szBgFileName   = szCurrentBgFileName;
    page.nCanvasW       = parseInt(canvas.style.width)  || 1200;
    page.nCanvasH       = parseInt(canvas.style.height) || 800;
    page.arrWidgetState = [];
    canvas.querySelectorAll('.canvas-widget').forEach(el => {
        page.arrWidgetState.push({
            szType: el.dataset.type,
            nX:     parseInt(el.style.left) || 0,
            nY:     parseInt(el.style.top)  || 0,
            nW:     el.offsetWidth,
            nH:     el.offsetHeight,
            props:  stripAlarmProps(el.widgetProps)
        });
    });
}

function loadPageState(page) {
    canvas.innerHTML = '';
    selectWidget(null);
    nWidgetCounter = 0;

    canvas.style.width  = (page.nCanvasW || 1200) + 'px';
    canvas.style.height = (page.nCanvasH || 800)  + 'px';

    if (page.szBgDataUrl) {
        applyCanvasBgImage(page.szBgDataUrl, page.szBgFileName || '');
    } else {
        clearBgImage();
    }

    (page.arrWidgetState || []).forEach(ws => restoreWidget(ws));
}

function restoreWidget(ws) {
    const def = WIDGET_DEFS[ws.szType];
    if (!def) return;
    const szId = 'w' + (++nWidgetCounter);
    const el   = document.createElement('div');
    el.id           = szId;
    el.className    = 'canvas-widget';
    el.dataset.type = ws.szType;
    el.style.left   = ws.nX + 'px';
    el.style.top    = ws.nY + 'px';
    el.style.width  = ws.nW + 'px';
    el.style.height = ws.nH + 'px';
    el.widgetProps  = { ...ws.props };
    renderWidget(el);
    canvas.appendChild(el);
}

// ============================================================
// 頁面樹 — 新增 / 刪除
// ============================================================
function addPage(szParentId) {
    const szName = prompt('請輸入頁面名稱：', '新頁面');
    if (szName === null) return;

    const newPage = {
        szId:           'p' + (++nPageIdCounter),
        szName:         szName.trim() || '新頁面',
        szIcon:         'fa-file-alt',
        arrChildren:    [],
        szBgDataUrl:    null,
        szBgFileName:   null,
        nCanvasW:       1200,
        nCanvasH:       800,
        arrWidgetState: []
    };

    if (szParentId === null) {
        arrPageTree.push(newPage);
    } else {
        const parent = findPage(szParentId);
        if (parent) parent.arrChildren.push(newPage);
    }
    renderPageTree();
}

function deletePage(szId) {
    const nTotal = countPages(arrPageTree);
    if (nTotal <= 1) { alert('至少需要保留一個頁面。'); return; }

    const page = findPage(szId);
    const szMsg = (page && page.arrChildren.length > 0)
        ? `確定要刪除「${page.szName}」及其所有子頁面？`
        : `確定要刪除「${page?.szName}」？`;
    if (!confirm(szMsg)) return;

    // 若刪除目前頁面（或其祖先），先切換至其他頁面
    if (szCurrentPageId === szId || isDescendantOf(szId, szCurrentPageId)) {
        const szFallback = findFirstExcluding(szId, arrPageTree);
        if (szFallback) {
            saveCurrentPageState();
            szCurrentPageId = szFallback;
            const fb = findPage(szFallback);
            if (fb) loadPageState(fb);
        }
    }

    removeFromTree(szId, arrPageTree);
    renderPageTree();
}

// ============================================================
// 頁面樹 — 工具函式
// ============================================================
function findPage(szId, arr) {
    arr = arr || arrPageTree;
    for (const p of arr) {
        if (p.szId === szId) return p;
        const found = findPage(szId, p.arrChildren);
        if (found) return found;
    }
    return null;
}

function removeFromTree(szId, arr) {
    const idx = arr.findIndex(p => p.szId === szId);
    if (idx >= 0) { arr.splice(idx, 1); return true; }
    for (const p of arr) {
        if (removeFromTree(szId, p.arrChildren)) return true;
    }
    return false;
}

function countPages(arr) {
    return (arr || []).reduce((n, p) => n + 1 + countPages(p.arrChildren), 0);
}

// 判斷 szTargetId 是否在以 szAncestorId 為根的子樹內
function isDescendantOf(szAncestorId, szTargetId) {
    const ancestor = findPage(szAncestorId);
    if (!ancestor) return false;
    return !!findPage(szTargetId, ancestor.arrChildren);
}

// 找第一個不屬於 szExcludeId 子樹的頁面 id
function findFirstExcluding(szExcludeId, arr) {
    for (const p of arr) {
        if (p.szId !== szExcludeId && !isDescendantOf(szExcludeId, p.szId)) return p.szId;
        const found = findFirstExcluding(szExcludeId, p.arrChildren);
        if (found) return found;
    }
    return null;
}

// ============================================================
// 右鍵選單
// ============================================================
const ctxMenu    = document.getElementById('ctxMenu');
let arrCtxActions = [];

function showCtxMenu(e, szPageId) {
    const items = [];

    if (szPageId === null) {
        // 空白區右鍵
        items.push({ szIcon: 'fa-plus', szLabel: '新增頁面', fn: () => addPage(null) });
    } else {
        // 節點右鍵
        items.push({ szIcon: 'fa-pen',  szLabel: '編輯',      fn: () => editPage(szPageId) });
        items.push({ szIcon: 'fa-plus', szLabel: '新增子頁面', fn: () => addPage(szPageId) });
        if (countPages(arrPageTree) > 1) {
            items.push({ isDivider: true });
            items.push({ szIcon: 'fa-trash-alt', szLabel: '刪除', fn: () => deletePage(szPageId), isDanger: true });
        }
    }

    arrCtxActions = items.filter(i => !i.isDivider).map(i => i.fn);
    let nActionIdx = 0;

    ctxMenu.innerHTML = items.map(item => {
        if (item.isDivider) return `<div class="ctx-divider"></div>`;
        const szClass = 'ctx-item' + (item.isDanger ? ' ctx-danger' : '');
        const idx = nActionIdx++;
        return `<div class="${szClass}" data-idx="${idx}">
                    <i class="fas ${item.szIcon}" style="width:14px;opacity:.7;"></i>${item.szLabel}
                </div>`;
    }).join('');

    // 顯示後計算位置（避免超出視窗）
    ctxMenu.style.display = 'block';
    ctxMenu.style.left = '0px';
    ctxMenu.style.top  = '0px';
    const nMenuW = ctxMenu.offsetWidth;
    const nMenuH = ctxMenu.offsetHeight;
    let nLeft = e.clientX;
    let nTop  = e.clientY;
    if (nLeft + nMenuW > window.innerWidth)  nLeft = e.clientX - nMenuW;
    if (nTop  + nMenuH > window.innerHeight) nTop  = e.clientY - nMenuH;
    ctxMenu.style.left = nLeft + 'px';
    ctxMenu.style.top  = nTop  + 'px';
}

ctxMenu.addEventListener('click', e => {
    const item = e.target.closest('.ctx-item');
    if (!item) return;
    const fn = arrCtxActions[parseInt(item.dataset.idx)];
    if (fn) fn();
    hideCtxMenu();
});

function hideCtxMenu() { ctxMenu.style.display = 'none'; }

document.addEventListener('click',   hideCtxMenu);
document.addEventListener('keydown', e => { if (e.key === 'Escape') hideCtxMenu(); });

// 畫布右鍵選單（新增文字）
// 表格儲存格右鍵選單
function onTableCellCtxMenu(e, widgetEl, nRow, nCol) {
    selectWidget(widgetEl);
    nSelectedCellRow = nRow;
    nSelectedCellCol = nCol;
    const isHeader = (nRow === 0);
    const props = widgetEl.widgetProps;
    initArrCells(props);
    const cell = props.arrCells[nRow]?.[nCol];
    if (!cell) return;

    const items = [];
    items.push({ szIcon: 'fa-pen', szLabel: '編輯儲存格', fn: () => {
        onTableCellClick(widgetEl, nRow, nCol);
    }});
    if (!isHeader) {
        items.push({ szIcon: 'fa-link', szLabel: '綁定點位', fn: () => {
            openCellPointPicker(nRow, nCol);
        }});
        if (cell.szSid) {
            items.push({ szIcon: 'fa-unlink', szLabel: '清除綁定', fn: () => {
                clearCellSid(nRow, nCol);
            }});
        }
    }

    arrCtxActions = items.map(i => i.fn);
    ctxMenu.innerHTML = items.map((item, idx) => `
        <div class="ctx-item" data-idx="${idx}">
            <i class="fas ${item.szIcon}" style="width:14px;opacity:.7;"></i>${item.szLabel}
        </div>
    `).join('');
    ctxMenu.style.display = 'block';
    ctxMenu.style.left = '0px';
    ctxMenu.style.top  = '0px';
    const nMenuW = ctxMenu.offsetWidth;
    const nMenuH = ctxMenu.offsetHeight;
    let nLeft = e.clientX;
    let nTop  = e.clientY;
    if (nLeft + nMenuW > window.innerWidth)  nLeft = e.clientX - nMenuW;
    if (nTop  + nMenuH > window.innerHeight) nTop  = e.clientY - nMenuH;
    ctxMenu.style.left = nLeft + 'px';
    ctxMenu.style.top  = nTop  + 'px';
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

function showCanvasCtxMenu(e, nX, nY) {
    const items = [
        { szIcon: 'fa-font', szLabel: '新增文字', fn: () => createWidget('text', nX, nY) }
    ];
    arrCtxActions = items.map(i => i.fn);
    ctxMenu.innerHTML = items.map((item, idx) => `
        <div class="ctx-item" data-idx="${idx}">
            <i class="fas ${item.szIcon}" style="width:14px;opacity:.7;"></i>${item.szLabel}
        </div>
    `).join('');
    ctxMenu.style.display = 'block';
    ctxMenu.style.left = '0px';
    ctxMenu.style.top  = '0px';
    const nMenuW = ctxMenu.offsetWidth;
    const nMenuH = ctxMenu.offsetHeight;
    let nLeft = e.clientX;
    let nTop  = e.clientY;
    if (nLeft + nMenuW > window.innerWidth)  nLeft = e.clientX - nMenuW;
    if (nTop  + nMenuH > window.innerHeight) nTop  = e.clientY - nMenuH;
    ctxMenu.style.left = nLeft + 'px';
    ctxMenu.style.top  = nTop  + 'px';
}

// ============================================================
// 頁面編輯（名稱 + 圖示）
// ============================================================
const SCADA_ICONS = [
    { szClass: 'fa-home',               szLabel: '首頁'   },
    { szClass: 'fa-file-alt',           szLabel: '一般'   },
    { szClass: 'fa-folder-open',        szLabel: '群組'   },
    { szClass: 'fa-chart-line',         szLabel: '趨勢'   },
    { szClass: 'fa-tachometer-alt',     szLabel: '儀錶板' },
    { szClass: 'fa-thermometer-half',   szLabel: '溫度'   },
    { szClass: 'fa-bolt',               szLabel: '電力'   },
    { szClass: 'fa-water',              szLabel: '水流'   },
    { szClass: 'fa-wind',               szLabel: '空調'   },
    { szClass: 'fa-fire',               szLabel: '燃燒'   },
    { szClass: 'fa-snowflake',          szLabel: '冷凍'   },
    { szClass: 'fa-fan',                szLabel: '風機'   },
    { szClass: 'fa-pump-soap',          szLabel: '幫浦'   },
    { szClass: 'fa-cog',                szLabel: '設備'   },
    { szClass: 'fa-industry',           szLabel: '工廠'   },
    { szClass: 'fa-server',             szLabel: '伺服器' },
    { szClass: 'fa-network-wired',      szLabel: '網路'   },
    { szClass: 'fa-plug',               szLabel: '電源'   },
    { szClass: 'fa-sun',                szLabel: '太陽能' },
    { szClass: 'fa-map-marked-alt',     szLabel: '地圖'   },
    { szClass: 'fa-exclamation-triangle', szLabel: '警報' },
    { szClass: 'fa-bell',               szLabel: '通知'   },
    { szClass: 'fa-eye',                szLabel: '監控'   },
    { szClass: 'fa-database',           szLabel: '資料庫' },
    { szClass: 'fa-tools',              szLabel: '維護'   },
];

let szEditingPageId    = null;
let szEditingIconClass = null;
let _pageEditModal     = null;

function editPage(szId) {
    const page = findPage(szId);
    if (!page) return;
    szEditingPageId    = szId;
    szEditingIconClass = page.szIcon || 'fa-file-alt';
    document.getElementById('editPageNameInput').value = page.szName;
    renderIconPicker();
    if (!_pageEditModal) {
        _pageEditModal = new bootstrap.Modal(document.getElementById('pageEditModal'));
    }
    _pageEditModal.show();
}

function renderIconPicker() {
    const grid = document.getElementById('iconPickerGrid');
    grid.innerHTML = SCADA_ICONS.map(icon => `
        <div class="icon-pick-item${szEditingIconClass === icon.szClass ? ' selected' : ''}"
             title="${escHtml(icon.szLabel)}"
             onclick="selectPickedIcon(this,'${icon.szClass}')">
            <i class="fas ${icon.szClass}"></i>
            <span>${escHtml(icon.szLabel)}</span>
        </div>
    `).join('');
}

function selectPickedIcon(el, szClass) {
    document.querySelectorAll('#iconPickerGrid .icon-pick-item')
            .forEach(i => i.classList.remove('selected'));
    el.classList.add('selected');
    szEditingIconClass = szClass;
}

function confirmPageEdit() {
    const szName = document.getElementById('editPageNameInput').value.trim();
    if (!szName) {
        document.getElementById('editPageNameInput').focus();
        return;
    }
    const page = findPage(szEditingPageId);
    if (!page) return;
    page.szName = szName;
    page.szIcon = szEditingIconClass;
    _pageEditModal.hide();
    renderPageTree();
}

// ============================================================
// 初始化頁面樹，並嘗試還原已發布設計
// ============================================================
renderPageTree();
loadPublishedDesign();

// ============================================================
// 儲存設計至資料庫
// ============================================================
async function saveDesign() {
    // 1. 先把目前畫布狀態同步回 arrPageTree
    saveCurrentPageState();

    // 2. 將遞迴樹展平為陣列（含 szParentPageSid + nSortOrder）
    const arrPages = [];
    function flattenTree(arr, szParentSid) {
        arr.forEach((page, idx) => {
            arrPages.push({
                szPageSid:        page.szId,
                szParentPageSid:  szParentSid,
                nSortOrder:       idx,
                szPageName:       page.szName,
                szPageIcon:       page.szIcon  || null,
                nCanvasW:         page.nCanvasW || 1200,
                nCanvasH:         page.nCanvasH || 800,
                szBgFileName:     page.szBgFileName  || null,
                szBgDataUrl:      page.szBgDataUrl   || null,
                szWidgetStateJson: (page.arrWidgetState && page.arrWidgetState.length)
                                    ? JSON.stringify(page.arrWidgetState)
                                    : null
            });
            if (page.arrChildren && page.arrChildren.length)
                flattenTree(page.arrChildren, page.szId);
        });
    }
    flattenTree(arrPageTree, null);

    // 3. POST 至後端
    const btnSave = document.getElementById('btnSave');
    btnSave.disabled = true;
    btnSave.innerHTML = '<i class="fas fa-spinner fa-spin me-1"></i>儲存中…';

    try {
        const resp = await fetch('/Designer/Save', {
            method:  'POST',
            headers: { 'Content-Type': 'application/json' },
            body:    JSON.stringify({ szName: '未命名設計', pages: arrPages })
        });
        const result = await resp.json();
        if (result.success) {
            showDesignToast('success', '<i class="fas fa-check-circle me-1"></i>已成功儲存至資料庫');
        } else {
            showDesignToast('danger', '<i class="fas fa-exclamation-circle me-1"></i>儲存失敗：' + (result.error || '未知錯誤'));
        }
    } catch (err) {
        showDesignToast('danger', '<i class="fas fa-exclamation-circle me-1"></i>網路錯誤：' + err.message);
    } finally {
        btnSave.disabled = false;
        btnSave.innerHTML = '<i class="fas fa-save me-1"></i>儲存';
    }
}

// ============================================================
// 啟動時從資料庫還原已發布設計
// ============================================================
async function loadPublishedDesign() {
    try {
        const resp = await fetch('/Designer/Load');
        const result = await resp.json();
        if (!result.hasData || !result.pages || !result.pages.length) return;

        // 建立節點對照表
        const nodeMap = {};
        const sortMap = {};
        result.pages.forEach(p => {
            sortMap[p.szPageSid] = p.nSortOrder ?? 0;
            nodeMap[p.szPageSid] = {
                szId:           p.szPageSid,
                szName:         p.szPageName,
                szIcon:         p.szPageIcon  || null,
                arrChildren:    [],
                szBgDataUrl:    p.szBgDataUrl  || null,
                szBgFileName:   p.szBgFileName || null,
                nCanvasW:       p.nCanvasW     || 1200,
                nCanvasH:       p.nCanvasH     || 800,
                arrWidgetState: p.szWidgetStateJson
                                    ? JSON.parse(p.szWidgetStateJson)
                                    : []
            };
        });

        // 重建樹狀結構（根節點另行收集）
        const arrRoots = [];
        result.pages.forEach(p => {
            const node = nodeMap[p.szPageSid];
            if (p.szParentPageSid && nodeMap[p.szParentPageSid]) {
                nodeMap[p.szParentPageSid].arrChildren.push(node);
            } else {
                arrRoots.push(node);
            }
        });

        // 依 nSortOrder 排序各層子節點
        Object.values(nodeMap).forEach(node => {
            node.arrChildren.sort((a, b) => (sortMap[a.szId] || 0) - (sortMap[b.szId] || 0));
        });
        arrRoots.sort((a, b) => (sortMap[a.szId] || 0) - (sortMap[b.szId] || 0));

        // 更新全域計數器，避免新增頁面時 ID 衝突
        let nMax = 0;
        result.pages.forEach(p => {
            const m = p.szPageSid.match(/^p(\d+)/);
            if (m) nMax = Math.max(nMax, parseInt(m[1]));
        });
        nPageIdCounter = nMax;

        // 取代預設頁面樹並導航至第一根頁面
        arrPageTree.length = 0;
        arrRoots.forEach(r => arrPageTree.push(r));
        szCurrentPageId = arrPageTree[0].szId;
        renderPageTree();
        loadPageState(arrPageTree[0]);
        await loadAlarmRuleSids();
    } catch (err) {
        console.warn('載入已發布設計失敗：', err.message);
    }
}

function showDesignToast(szType, szHtml) {
    const wrap = document.createElement('div');
    wrap.style.cssText = 'position:fixed;bottom:24px;right:24px;z-index:9999;min-width:260px;';
    wrap.innerHTML = `
        <div class="alert alert-${szType} alert-dismissible fade show mb-0 shadow-sm" style="font-size:13px;">
            ${szHtml}
            <button type="button" class="btn-close btn-sm" data-bs-dismiss="alert"></button>
        </div>`;
    document.body.appendChild(wrap);
    setTimeout(() => { wrap.querySelector('.alert')?.classList.remove('show'); setTimeout(() => wrap.remove(), 300); }, 3500);
}
