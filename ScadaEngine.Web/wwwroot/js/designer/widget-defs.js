// ============================================================
// Designer — Widget 定義 + Builder HTML
// ============================================================
// 內容：WIDGET_DEFS 大物件、9 種 widget 的 buildXxxHtml、
// renderTextWidget、表格儲存格輔助函式。
// 依賴：state.js（escHtml / hasAlarmRule / isDarkColor）
// ============================================================

// ============================================================
// Widget 定義
// ============================================================
// 注意：defaultProps 內的 szTitle/szLabel/szText 等預設值會在 createWidget 時
// 透過 widget-core.js 的 getWidgetDefaultProps() 以當前 culture 解析（plan 決策 1）
const WIDGET_DEFS = {
    table: {
        szLabel: '表格',
        szIcon: 'fas fa-table',
        nDefaultW: 360,
        nDefaultH: 220,
        nMinW: 120, nMinH: 60,
        defaultProps: {
            szTitle: '__i18n__:designer.default.table_title',
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
            szTitle:     '__i18n__:designer.default.gauge_title',
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
            szTitle:      '__i18n__:designer.default.text_title',
            szText:       '__i18n__:designer.default.text_content',
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
            szTitle:     '__i18n__:designer.default.controlBtn_title',
            szCid:       '',
            szPointName: '',
            szBtnLabel:  '__i18n__:designer.default.controlBtn_label',
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
            szTitle:     '__i18n__:designer.default.realtimeValue_title',
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
            szTitle:        '__i18n__:designer.default.diPoint_title',
            szSid:          '',
            szPointName:    '',
            // 排程綁定（互斥於 szSid，任一時刻僅一個有值；plan 決策 2）
            nScheduleId:    null,
            szScheduleName: '',
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
            szTitle:            '__i18n__:designer.default.aoPoint_title',
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
            szMenuManualLabel:  '__i18n__:designer.default.aoPoint_menu_manual',
            szMenuAutoLabel:    '__i18n__:designer.default.aoPoint_menu_auto',
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
            szTitle:          '__i18n__:designer.default.doPoint_title',
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
            szMenuOnLabel:    '__i18n__:designer.default.doPoint_menu_on',
            szMenuOffLabel:   '__i18n__:designer.default.doPoint_menu_off',
            szMenuAutoLabel:  '__i18n__:designer.default.doPoint_menu_auto',
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
            szTitle:          '__i18n__:designer.default.pump_title',
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

// Resolve `__i18n__:key` placeholders in default props using current culture.
// Called by widget-core.js when materializing widget defaults on create.
function getWidgetDefaultProps(szType) {
    const def = WIDGET_DEFS[szType];
    if (!def || !def.defaultProps) return {};
    const out = {};
    for (const k in def.defaultProps) {
        const v = def.defaultProps[k];
        if (typeof v === 'string' && v.startsWith('__i18n__:')) {
            out[k] = t(v.slice('__i18n__:'.length));
        } else {
            out[k] = v;
        }
    }
    return out;
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
        <button class="text-del-btn" onclick="deleteWidget('${el.id}')" title="${escHtml(t('designer.widget.delete_text_tooltip'))}">✕</button>
        <div class="resize-knob" title="${escHtml(t('designer.widget.resize_tooltip'))}"></div>
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
// Note: TABLE_SAMPLE 為內部 sample 資料常數，未實際渲染至 UI，保留繁中即可。
const TABLE_SAMPLE = [
    ['溫度感測器', '85.3°C', '正常', '2026-03-05'],
    ['壓力感測器', '2.40 bar', '正常', '2026-03-05'],
    ['流量計',     '12.7 L/s', '警告', '2026-03-05'],
    ['液位感測器', '67.2%',   '正常', '2026-03-05'],
    ['電流感測器', '32.1 A',  '正常', '2026-03-05'],
    ['電壓感測器', '220 V',   '正常', '2026-03-05'],
];

// 表頭預設值（建立 table widget 時的初始 header cell text）— 依當前 culture 取值，
// 寫入 DB 後即為 user input
function getDefaultColHeaders() {
    return [
        t('designer.table.header.name'),
        t('designer.table.header.value'),
        t('designer.table.header.status'),
        t('designer.table.header.timestamp')
    ];
}

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
        : `<div class="ctrl-btn-cid-label" style="color:#dc3545;"><i class="fas fa-unlink me-1"></i>${escHtml(t('designer.widget.unbound_cid'))}</div>`;
    return `
        <div class="ctrl-btn-body">
            <button class="ctrl-btn-main" style="background:${props.szBtnColor || '#198754'};" disabled>
                <i class="fas ${props.szBtnIcon || 'fa-hand-pointer'}" style="font-size:12px;"></i>
                ${escHtml(props.szBtnLabel || t('designer.default.controlBtn_label'))}
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
        : `<div style="font-size:10px;color:#dc3545;margin-top:2px;"><i class="fas fa-unlink me-1"></i>${escHtml(t('designer.widget.unbound_sid'))}</div>`;
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
    const bSchedule = props.nScheduleId != null;
    let szBindLabel;
    if (bSchedule) {
        // Designer 預覽：排程模式靜態顯示 OFF + 排程名標籤（plan 決策 4）
        szBindLabel = `<div style="font-size:10px;color:#0d6efd;margin-top:2px;"><i class="fas fa-calendar-alt me-1"></i>${escHtml(props.szScheduleName || '')}</div>`;
    } else if (props.szPointName) {
        szBindLabel = '';
    } else {
        szBindLabel = `<div style="font-size:10px;color:#dc3545;margin-top:2px;"><i class="fas fa-unlink me-1"></i>${escHtml(t('designer.widget.unbound_sid'))}</div>`;
    }
    const szBg = (props.szBgColor && props.szBgColor !== 'transparent') ? props.szBgColor : 'transparent';
    const szMode = props.szDisplayMode || 'indicator';

    // 警報標記（排程模式無 SID 警報概念，僅 SID 模式顯示）
    const szAlarmBadge = (!bSchedule && hasAlarmRule(props.szSid))
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
            ${szBindLabel}
        </div>`;
}

// ============================================================
// AO 點位 HTML（Designer 預覽）
// ============================================================
function buildAoPointHtml(props) {
    const szCidLabel = props.szPointName
        ? ''
        : `<div class="ao-point-cid-label" style="color:#dc3545;"><i class="fas fa-unlink me-1"></i>${escHtml(t('designer.widget.unbound_cid'))}</div>`;
    const szBlock = props.szBlockColor
                  || (props.szBgColor && props.szBgColor !== 'transparent' ? props.szBgColor : null)
                  || '#0d6efd';
    const nFs = props.nFontSize || 16;
    const szName = props.szDisplayName || props.szTitle || t('designer.default.aoPoint_title');

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
        : `<div class="do-point-cid-label" style="color:#dc3545;"><i class="fas fa-unlink me-1"></i>${escHtml(t('designer.widget.unbound_cid'))}</div>`;
    const szBlock = props.szBlockColor
                  || (props.szBgColor && props.szBgColor !== 'transparent' ? props.szBgColor : null)
                  || '#0d6efd';
    const nFs = props.nFontSize || 16;
    const szName = props.szDisplayName || props.szTitle || t('designer.default.doPoint_title');

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
