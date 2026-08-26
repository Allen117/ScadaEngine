// ============================================================
// scadapage/widget-table.js — Table Widget builder
// ============================================================
// 純搬移自 scadapage.js（plan 2026-08-06-scadapage-js-split）。
// 不包 IIFE、global scope，與 js/designer/ 拆分同模式；
// 共享狀態集中於 state.js。原始 4-space 縮排刻意保留，
// 以保證 template literal 內容 byte-level 不變（純搬移原則）。
// ============================================================

    // ── Table Widget ──
    function buildTableHtml(props) {
        var nC = Math.max(1, props.nCols || 3);
        var nR = Math.max(1, props.nRows || 5);
        var hdrColor = props.szHeaderColor || '#343a40';
        var arrColDec = props.arrColDecimals || [];
        // 表格大小：colgroup + per-row inline height（plan 2026-06-01）
        var nDefW = props.nDefaultColW || 80;
        var nDefH = props.nDefaultRowH || 20;
        var arrCW = props.arrColWidths || [];
        var arrRH = props.arrRowHeights || [];
        var bLocked = props.bTableSizeLocked === true;
        // 底色樣式（plan 2026-07-04）：未設定（null/undefined）= 沿用現行純白渲染
        var szBgOdd   = props.szBodyBgOdd   || '';
        var szBgEven  = props.szBodyBgEven  || '';
        var szBorderC = props.szBorderColor || '#f0f0f0';
        // 鎖定 → 用結構欄寬；未鎖定（舊檔）→ 用 table width:100% 等比例
        var szTableStyle = bLocked
            ? 'border-collapse:collapse;table-layout:fixed;'
            : 'width:100%;border-collapse:collapse;';
        var szColgroup = '';
        if (bLocked) {
            szColgroup = '<colgroup>';
            for (var ci2 = 0; ci2 < nC; ci2++) {
                var w = arrCW[ci2] != null ? +arrCW[ci2] : nDefW;
                szColgroup += '<col style="width:' + w + 'px">';
            }
            szColgroup += '</colgroup>';
        }

        if (props.arrCells && props.arrCells.length > 0) {
            var headerRow = props.arrCells[0] || [];
            var szHdr = headerRow.slice(0, nC).map(function (cell) {
                return '<th style="background:' + hdrColor + ';color:' + (cell.szFontColor || '#fff') + ';' +
                    'padding:4px 6px;font-size:' + (cell.nFontSize || 11) + 'px;' +
                    'font-weight:' + (cell.szFontWeight || '500') + ';' +
                    'text-align:' + (cell.szAlign || 'left') + ';">' + escViewHtml(cell.szText || '') + '</th>';
            }).join('');

            var szRows = Array.from({ length: nR }, function (_, ri) {
                var rowIdx = ri + 1;
                if (rowIdx >= props.arrCells.length) return '';
                var row = props.arrCells[rowIdx];
                var nRowH = arrRH[ri] != null ? +arrRH[ri] : nDefH;
                var szRowStyle = bLocked ? ' style="height:' + nRowH + 'px"' : '';
                var szRowBg = (ri % 2 === 1) ? szBgEven : szBgOdd;
                var szBgStyle = szRowBg ? 'background:' + szRowBg + ';' : '';
                var szCells = row.slice(0, nC).map(function (cell, ci) {
                    var nDec = arrColDec[ci];
                    var szPT = cell.szPointType || 'AI';
                    var szSidAttr = '';
                    if (cell.szSid) {
                        szSidAttr = 'data-sid="' + escViewHtml(cell.szSid) + '" data-point-type="' + szPT + '" data-decimals="' + (nDec !== undefined ? nDec : '') + '" data-orig-color="' + (cell.szFontColor || '#444') + '"';
                        if (szPT === 'DI') {
                            szSidAttr += ' data-sz-on-label="' + escViewHtml(cell.szOnLabel || 'ON') + '"';
                            szSidAttr += ' data-sz-off-label="' + escViewHtml(cell.szOffLabel || 'OFF') + '"';
                            szSidAttr += ' data-sz-alarm-color="' + escViewHtml(cell.szAlarmColor || '#dc3545') + '"';
                        }
                        if (szPT === 'AI') {
                            szSidAttr += ' data-sz-high-color="' + escViewHtml(cell.szHighColor || '#dc3545') + '"';
                            szSidAttr += ' data-sz-low-color="' + escViewHtml(cell.szLowColor || '#fd7e14') + '"';
                        }
                    } else if (cell.nCircuitId != null) {
                        // 迴路指標 cell（plan 2026-07-23）：不帶 data-sid → 1 秒 td[data-sid] 路徑不觸碰，
                        // 由 30 秒迴路指標輪詢依 class scada-cmetric-cell 更新；title = 迴路名＋指標名 hover 提示
                        var szCellMetric = cell.szMetric || 'day_kwh';
                        szSidAttr = 'class="scada-cmetric-cell" data-circuit-id="' + cell.nCircuitId + '"' +
                            ' data-metric="' + escViewHtml(szCellMetric) + '"' +
                            ' data-decimals="' + (nDec !== undefined && nDec !== null ? nDec : '') + '"' +
                            ' data-orig-color="' + (cell.szFontColor || '#444') + '"' +
                            ' title="' + escViewHtml(_cmetricTooltipTitle(cell.szCircuitName || '', szCellMetric, false)) + '"';
                    }
                    var szDisplay = (cell.szSid || cell.nCircuitId != null) ? '--' : escViewHtml(cell.szText || '');
                    return '<td ' + szSidAttr + ' style="padding:3px 6px;' +
                        'font-size:' + (cell.nFontSize || 12) + 'px;' +
                        'color:' + (cell.szFontColor || '#444') + ';' +
                        'font-weight:' + (cell.szFontWeight || 'normal') + ';' +
                        'text-align:' + (cell.szAlign || 'left') + ';' + szBgStyle +
                        'border-bottom:1px solid ' + szBorderC + ';">' + szDisplay + '</td>';
                }).join('');
                return '<tr' + szRowStyle + '>' + szCells + '</tr>';
            }).join('');

            var szHdrRowStyle = bLocked ? ' style="height:' + nDefH + 'px"' : '';
            return '<table style="' + szTableStyle + '">' + szColgroup + '<thead><tr' + szHdrRowStyle + '>' + szHdr + '</tr></thead><tbody>' + szRows + '</tbody></table>';
        }

        var HEADERS = ['\u540d\u7a31', '\u6578\u503c', '\u72c0\u614b', '\u6642\u9593\u6233'];
        var SAMPLE  = [
            ['\u6eab\u5ea6\u611f\u6e2c\u5668', '85.3\u00b0C',  '\u6b63\u5e38', '--'],
            ['\u58d3\u529b\u611f\u6e2c\u5668', '2.40 bar','\u6b63\u5e38', '--'],
            ['\u6d41\u91cf\u8a08',     '12.7 L/s','\u8b66\u544a', '--'],
            ['\u6db2\u4f4d\u611f\u6e2c\u5668', '67.2%',   '\u6b63\u5e38', '--'],
            ['\u96fb\u6d41\u611f\u6e2c\u5668', '32.1 A',  '\u6b63\u5e38', '--'],
        ];
        var szHdr2 = Array.from({ length: nC }, function (_, i) {
            return '<th style="background:' + hdrColor + ';color:#fff;padding:4px 6px;font-size:11px;">' + (HEADERS[i] || '\u6b04' + (i + 1)) + '</th>';
        }).join('');
        var szRows2 = Array.from({ length: nR }, function (_, ri) {
            var row = SAMPLE[ri % SAMPLE.length];
            return '<tr>' + Array.from({ length: nC }, function (_, ci) {
                return '<td style="padding:3px 6px;font-size:11px;border-bottom:1px solid #f0f0f0;">' + (row[ci] || '-') + '</td>';
            }).join('') + '</tr>';
        }).join('');
        return '<table style="width:100%;border-collapse:collapse;"><thead><tr>' + szHdr2 + '</tr></thead><tbody>' + szRows2 + '</tbody></table>';
    }
