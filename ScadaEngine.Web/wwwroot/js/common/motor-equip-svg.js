// ============================================================
// 共用：馬達型設備 SVG 產生器（冰機 / 冷卻水塔 / 空調箱風扇）
// ============================================================
// Designer 預覽（widget-defs.js）與 ScadaPage 執行期（scadapage.js）共用
// 同一份圖形，避免兩處各畫一次而走樣（plan 決策 3-A）。
//
// 對外：window.MotorEquip.build(opts) → 回傳 widget 內層 HTML 字串
//   opts = {
//     szType:        'chiller' | 'coolingTower' | 'ahuFan',
//     props:         widget props（顏色 / 綁定 key / nFreqMax / nLoadMax）,
//     szState:       'stop' | 'run' | 'fault',
//     szPrimaryVal:  主數值（水塔/風扇=頻率、冰機=負載%），'' 表無資料,
//     szModeVal:     手自動狀態值（1/true=自動），''=未綁,
//     bInteractive:  true=執行期（頻率條可拖曳），false=Designer 預覽,
//     szBadgesHtml:  注入的 M 角標 HTML（Designer 傳 ''）,
//     szOverlayHtml: 注入的覆蓋層 HTML（冰機設定溫度，其餘傳 ''）,
//     szHoverHtml:   注入的 hover 標籤 HTML（Designer 傳 ''）
//   }
//
// 頻率條沿用 pump 既有 class（pump-gauge-fill / pump-gauge-text /
// pump-gauge-handle），故 scadapage.js 的頻率拖曳與更新邏輯可直接共用。
// ============================================================
(function () {
    'use strict';

    // 主數值條幾何（與 pump 對齊，讓拖曳邏輯共用）
    var BAR_X = 115, BAR_W = 10, BAR_TOP = 22, BAR_H = 68;

    function _contrast(szBg) {
        if (!szBg || szBg === 'transparent') return '#333';
        var m = szBg.match(/^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i);
        if (!m) return '#333';
        var lum = (parseInt(m[1], 16) * 299 + parseInt(m[2], 16) * 587 + parseInt(m[3], 16) * 114) / 1000;
        return lum > 128 ? '#333' : '#f0f0f0';
    }

    // 旋轉葉輪群組的 transform-origin + 動畫（僅執行期 run 狀態；Designer 恆不轉）
    function _spinAttr(cx, cy, bSpin) {
        return bSpin
            ? ' style="transform-origin:' + cx + 'px ' + cy + 'px; animation: pump-spin 1.5s linear infinite;"'
            : '';
    }

    // 以單一葉片 path 依角度陣列旋轉，組成整組風扇葉片
    function _blades(szPath, cx, cy, aAngles) {
        var s = '<g fill="rgba(255,255,255,.4)">';
        for (var i = 0; i < aAngles.length; i++) {
            s += '<path d="' + szPath + '" transform="rotate(' + aAngles[i] + ',' + cx + ',' + cy + ')"/>';
        }
        return s + '</g>';
    }

    // ── 各設備本體 SVG（不含主數值條 / 角標 / 覆蓋層）──
    function _bodySvg(szType, szBody, szCircle, bSpin) {
        if (szType === 'coolingTower') {
            return '' +
                '<rect x="30" y="84" width="60" height="8" rx="2" fill="#4a4a4a"/>' +
                '<rect x="30" y="90" width="60" height="3" rx="1" fill="#333"/>' +
                '<rect x="36" y="80" width="5" height="6" fill="#3a3a3a"/>' +
                '<rect x="79" y="80" width="5" height="6" fill="#3a3a3a"/>' +
                '<path d="M 36,82 L 84,82 L 74,46 L 46,46 Z" fill="' + szBody + '" stroke="#3a3a3a" stroke-width="2" stroke-linejoin="round"/>' +
                '<rect x="46" y="47" width="28" height="3" fill="rgba(255,255,255,.12)"/>' +
                '<line x1="46" y1="58" x2="74" y2="58" stroke="rgba(0,0,0,.18)" stroke-width="1.5"/>' +
                '<line x1="43" y1="66" x2="77" y2="66" stroke="rgba(0,0,0,.18)" stroke-width="1.5"/>' +
                '<line x1="40" y1="74" x2="80" y2="74" stroke="rgba(0,0,0,.18)" stroke-width="1.5"/>' +
                '<rect x="44" y="40" width="32" height="7" rx="2" fill="#5a5a5a" stroke="#3a3a3a" stroke-width="1"/>' +
                '<circle cx="60" cy="30" r="15" fill="rgba(0,0,0,.2)" stroke="#3a3a3a" stroke-width="1"/>' +
                '<g' + _spinAttr(60, 30, bSpin) + '>' +
                    '<circle cx="60" cy="30" r="12.5" fill="' + szCircle + '" stroke="#333" stroke-width="1.5"/>' +
                    _blades('M60,30 L60,19.5 C63,21 63,26 60,29 Z', 60, 30, [0, 90, 180, 270]) +
                    '<circle cx="60" cy="30" r="3.2" fill="#3a3a3a" stroke="#2a2a2a" stroke-width="1"/>' +
                    '<circle cx="58.6" cy="28.6" r="1.1" fill="rgba(255,255,255,.25)"/>' +
                '</g>';
        }
        if (szType === 'ahuFan') {
            return '' +
                '<rect x="34" y="86" width="8" height="6" rx="1" fill="#4a4a4a"/>' +
                '<rect x="78" y="86" width="8" height="6" rx="1" fill="#4a4a4a"/>' +
                '<rect x="24" y="22" width="72" height="64" rx="5" fill="' + szBody + '" stroke="#3a3a3a" stroke-width="2"/>' +
                '<rect x="27" y="25" width="66" height="4" rx="2" fill="rgba(255,255,255,.12)"/>' +
                '<rect x="18" y="44" width="6" height="24" rx="1" fill="#5a5a5a" stroke="#3a3a3a" stroke-width="1"/>' +
                '<rect x="96" y="44" width="6" height="24" rx="1" fill="#5a5a5a" stroke="#3a3a3a" stroke-width="1"/>' +
                '<circle cx="60" cy="54" r="23" fill="rgba(0,0,0,.18)" stroke="#3a3a3a" stroke-width="1"/>' +
                '<g' + _spinAttr(60, 54, bSpin) + '>' +
                    '<circle cx="60" cy="54" r="19" fill="' + szCircle + '" stroke="#333" stroke-width="1.5"/>' +
                    _blades('M60,54 L60,37 C64,40 64,47 60,51 Z', 60, 54, [0, 60, 120, 180, 240, 300]) +
                    '<circle cx="60" cy="54" r="4" fill="#3a3a3a" stroke="#2a2a2a" stroke-width="1"/>' +
                    '<circle cx="58" cy="52" r="1.3" fill="rgba(255,255,255,.25)"/>' +
                '</g>';
        }
        // chiller（無旋轉，狀態以核心面板顏色表示；右側冰水進出管）
        return '' +
            '<rect x="22" y="84" width="76" height="7" rx="2" fill="#4a4a4a"/>' +
            '<rect x="22" y="90" width="76" height="3" rx="1" fill="#333"/>' +
            '<rect x="34" y="22" width="18" height="14" rx="4" fill="#5a5a5a" stroke="#3a3a3a" stroke-width="1.5"/>' +
            '<rect x="68" y="22" width="18" height="14" rx="4" fill="#5a5a5a" stroke="#3a3a3a" stroke-width="1.5"/>' +
            '<circle cx="43" cy="29" r="2" fill="rgba(255,255,255,.25)"/>' +
            '<circle cx="77" cy="29" r="2" fill="rgba(255,255,255,.25)"/>' +
            '<rect x="22" y="34" width="76" height="52" rx="4" fill="' + szBody + '" stroke="#3a3a3a" stroke-width="2"/>' +
            '<rect x="25" y="37" width="70" height="4" rx="2" fill="rgba(255,255,255,.12)"/>' +
            '<rect x="98" y="46" width="9" height="7" rx="1" fill="#6ca9d6" stroke="#3a3a3a" stroke-width="1"/>' +
            '<rect x="98" y="66" width="9" height="7" rx="1" fill="#cf8a8a" stroke="#3a3a3a" stroke-width="1"/>' +
            '<rect x="36" y="50" width="46" height="22" rx="4" fill="' + szCircle + '" stroke="#333" stroke-width="1.5"/>' +
            '<circle cx="45" cy="61" r="3.5" fill="rgba(255,255,255,.9)"/>' +
            '<rect x="53" y="56" width="22" height="3" rx="1.5" fill="rgba(255,255,255,.45)"/>' +
            '<rect x="53" y="63" width="15" height="3" rx="1.5" fill="rgba(255,255,255,.3)"/>';
    }

    // ── 主數值條（水塔/風扇=頻率、冰機=負載%）──
    // 沿用 pump-gauge-* class 讓執行期拖曳/更新共用；load 條不含 handle（唯讀）
    function _barSvg(szKind, nMax, szVal, szState, szFaultColor, szContrast, bDraggable) {
        var szUnit    = szKind === 'load' ? '%' : 'Hz';
        var nDecimals = szKind === 'load' ? 0 : 1;
        var szBaseColor = szKind === 'load' ? '#20c997' : '#17a2b8';
        var fVal   = (szVal !== undefined && szVal !== '' && szVal !== '--') ? parseFloat(szVal) : 0;
        var fRatio = Math.max(0, Math.min(1, fVal / (nMax || 1)));
        var nFillH = Math.round(BAR_H * fRatio);
        var nFillY = BAR_TOP + BAR_H - nFillH;
        var szFill = szState === 'fault' ? szFaultColor : szBaseColor;
        var szText = (szVal !== undefined && szVal !== '' && szVal !== '--')
            ? parseFloat(szVal).toFixed(nDecimals) + ' ' + szUnit : '';

        var s = '' +
            '<rect x="' + BAR_X + '" y="' + BAR_TOP + '" width="' + BAR_W + '" height="' + BAR_H + '" rx="3" fill="#333" stroke="#555" stroke-width="1"/>' +
            '<rect class="pump-gauge-fill" x="' + BAR_X + '" y="' + nFillY + '" width="' + BAR_W + '" height="' + nFillH + '" rx="3" fill="' + szFill + '"/>' +
            '<text x="120" y="' + (BAR_TOP - 4) + '" text-anchor="middle" font-size="8" fill="' + szContrast + '">' + nMax + '</text>' +
            '<text x="120" y="' + (BAR_TOP + BAR_H + 10) + '" text-anchor="middle" font-size="8" fill="' + szContrast + '">0</text>' +
            (szText ? '<text class="pump-gauge-text" x="130" y="' + (nFillY + nFillH / 2 + 3) + '" text-anchor="start" font-size="9" fill="' + szContrast + '" font-weight="600">' + szText + '</text>' : '');
        if (bDraggable) {
            s += '<rect class="pump-gauge-handle" x="108" y="' + (BAR_TOP - 4) + '" width="24" height="' + (BAR_H + 8) + '" fill="transparent" style="cursor:ns-resize;" />';
        }
        return s;
    }

    // 本體顏色：綁手自動時依模式著色（Designer 預設自動色），否則中性灰
    function _bodyColor(props, szModeVal) {
        var szManual = props.szManualColor || '#ffc107';
        var szAuto   = props.szAutoColor   || '#0d6efd';
        if (props.szSidMode && szModeVal !== undefined && szModeVal !== '') {
            var bAuto = (szModeVal == 1 || szModeVal === '1' || szModeVal === true || szModeVal === 'true');
            return bAuto ? szAuto : szManual;
        }
        // Designer 預覽：綁了 mode 就顯示自動色，否則中性灰
        return props.szSidMode ? szAuto : '#555';
    }

    function build(opts) {
        var szType = opts.szType;
        var props  = opts.props || {};
        var szState = opts.szState || 'stop';

        var szRunColor   = props.szRunColor   || '#28a745';
        var szStopColor  = props.szStopColor  || '#6c757d';
        var szFaultColor = props.szFaultColor || '#dc3545';
        var szCircle = szState === 'fault' ? szFaultColor
                     : szState === 'run'   ? szRunColor
                     : szStopColor;
        var szBody = _bodyColor(props, opts.szModeVal);
        var szBg   = (props.szBgColor && props.szBgColor !== 'transparent') ? props.szBgColor : 'transparent';
        var szContrast = _contrast(szBg);
        var bSpin  = opts.bInteractive && szState === 'run' && szType !== 'chiller';

        // 主數值條：水塔/風扇綁 szSidFreq、冰機綁 szSidLoad 時才顯示
        var bBar = false, szBarHtml = '';
        if (szType === 'chiller') {
            bBar = !!props.szSidLoad;
            if (bBar) szBarHtml = _barSvg('load', parseFloat(props.nLoadMax) || 100, opts.szPrimaryVal, szState, szFaultColor, szContrast, false);
        } else {
            bBar = !!props.szSidFreq;
            if (bBar) szBarHtml = _barSvg('freq', parseFloat(props.nFreqMax) || 60, opts.szPrimaryVal, szState, szFaultColor, szContrast, !!opts.bInteractive);
        }

        var szViewBox = bBar ? '0 0 170 110' : '0 0 120 110';

        return '<div style="position:relative;width:100%;height:100%;background:' + szBg + ';border-radius:4px;">' +
            '<svg viewBox="' + szViewBox + '" xmlns="http://www.w3.org/2000/svg" style="width:100%;height:100%;display:block;">' +
                _bodySvg(szType, szBody, szCircle, bSpin) +
                szBarHtml +
            '</svg>' +
            (opts.szBadgesHtml || '') +
            (opts.szOverlayHtml || '') +
            (opts.szHoverHtml || '') +
            '</div>';
    }

    window.MotorEquip = { build: build, BAR_TOP: BAR_TOP, BAR_H: BAR_H };
})();
