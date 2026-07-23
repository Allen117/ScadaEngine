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
//     szModeVal:     手自動狀態值（1/true=自動），''=未綁
//                    （冰機語意=遠端/現場：1=遠端→面板灰、0=現場→面板深黃，不上機身）,
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
            // 等軸測方箱塔體（2026-07 依使用者參考圖重繪）：
            // 頂面菱形 上(58,14) 右(92,31) 下(58,48) 左(24,31)，箱高 46；
            // 葉輪畫在「壓扁前」座標再套 scale(1,0.5) 等軸壓扁，旋轉動畫沿用 _spinAttr。
            var _twRail = function (x1, y1, x2, y2) {   // 雙層黃色安全欄杆橫桿
                return '<line x1="' + x1 + '" y1="' + (y1 - 11) + '" x2="' + x2 + '" y2="' + (y2 - 11) + '" stroke="#e8a812" stroke-width="1.6"/>' +
                       '<line x1="' + x1 + '" y1="' + (y1 - 6.5) + '" x2="' + x2 + '" y2="' + (y2 - 6.5) + '" stroke="#e8a812" stroke-width="1.2"/>';
            };
            var _twPost = function (x, y) {             // 欄杆立柱
                return '<line x1="' + x + '" y1="' + y + '" x2="' + x + '" y2="' + (y - 11) + '" stroke="#e8a812" stroke-width="1.6" stroke-linecap="round"/>';
            };
            var szSvg = '' +
                '<ellipse cx="58" cy="95" rx="42" ry="7" fill="rgba(0,0,0,.16)"/>' +
                '<path d="M24,31 L58,48 L58,94 L24,77 Z" fill="' + szBody + '" stroke="#3a3a3a" stroke-width="1.5" stroke-linejoin="round"/>';
            for (var nX = 30; nX <= 54; nX += 6) {      // 左面浪板直紋
                szSvg += '<line x1="' + nX + '" y1="' + (31 + (nX - 24) / 2 + 1.5) + '" x2="' + nX + '" y2="' + (77 + (nX - 24) / 2 - 1.5) + '" stroke="rgba(0,0,0,.18)" stroke-width="1.5"/>';
            }
            szSvg += '<path d="M24,31 L58,48 L58,52 L24,35 Z" fill="rgba(255,255,255,.14)"/>' +
                '<path d="M58,48 L92,31 L92,77 L58,94 Z" fill="' + szBody + '" stroke="#3a3a3a" stroke-width="1.5" stroke-linejoin="round"/>' +
                '<path d="M58,48 L92,31 L92,77 L58,94 Z" fill="rgba(0,0,0,.28)"/>' +
                '<path d="M63,49.5 L87,37.5 L87,75.5 L63,87.5 Z" fill="rgba(10,18,34,.28)" stroke="rgba(0,0,0,.25)" stroke-width="1"/>' +
                '<line x1="63" y1="49.5" x2="87" y2="75.5" stroke="rgba(255,255,255,.28)" stroke-width="2"/>' +
                '<line x1="87" y1="37.5" x2="63" y2="87.5" stroke="rgba(255,255,255,.28)" stroke-width="2"/>' +
                '<path d="M58,14 L92,31 L58,48 L24,31 Z" fill="' + szBody + '" stroke="#3a3a3a" stroke-width="1.5" stroke-linejoin="round"/>' +
                '<path d="M58,14 L92,31 L58,48 L24,31 Z" fill="rgba(255,255,255,.3)"/>' +
                _twRail(24, 31, 58, 14) + _twRail(58, 14, 92, 31) +   // 欄杆後段（風扇後方）
                _twPost(24, 31) + _twPost(58, 14) + _twPost(92, 31) + _twPost(41, 22.5) + _twPost(75, 22.5) +
                '<rect x="39" y="15.5" width="6.5" height="8.5" rx="1" fill="#8f959c" stroke="#3a3a3a" stroke-width="1"/>' +
                '<ellipse cx="42.2" cy="15.5" rx="3.3" ry="1.5" fill="#b5bac0" stroke="#3a3a3a" stroke-width=".8"/>' +
                '<ellipse cx="58" cy="29" rx="21" ry="10.5" fill="rgba(0,0,0,.22)" stroke="#3a3a3a" stroke-width="1"/>' +
                '<ellipse cx="58" cy="29" rx="17.5" ry="8.75" fill="' + szCircle + '" stroke="#333" stroke-width="1.5"/>' +
                '<g transform="translate(58,29) scale(1,0.5) translate(-58,-29)">' +
                    '<g' + _spinAttr(58, 29, bSpin) + '>' +
                        _blades('M58,29 L58,14.5 C62.5,16.5 62.5,23 58,27 Z', 58, 29, [0, 60, 120, 180, 240, 300]) +
                        '<circle cx="58" cy="29" r="4.5" fill="#3a3a3a" stroke="#2a2a2a" stroke-width="1"/>' +
                        '<circle cx="56.3" cy="27.3" r="1.3" fill="rgba(255,255,255,.25)"/>' +
                    '</g>' +
                '</g>' +
                _twRail(24, 31, 58, 48) + _twRail(58, 48, 92, 31) +   // 欄杆前段（蓋在風扇上）
                _twPost(58, 48) + _twPost(41, 39.5) + _twPost(75, 39.5);
            return szSvg;
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
        // chiller — 水冷式雙筒身前視圖（2026-07 依現場照片重繪，Artifact 預覽 v4 定稿）：
        // 上筒=冷凝器、下筒=蒸發器；不畫接管（管路元件由使用者自由對接）。
        // 機身（雙筒）= 狀態色 szCircle；
        // 控制箱面板 = szBody（遠端灰 #61666b / 現場深黃，由 build() 依 szModeVal 決定），
        // 螢幕固定深色 HMI 不變色；頂部警示燈 = 狀態色。無旋轉件。
        return '' +
            // 底座
            '<rect x="10" y="90" width="100" height="4" rx="1.5" fill="#4a4a4a"/>' +
            '<rect x="10" y="93.2" width="100" height="2.3" rx="1" fill="#333"/>' +
            '<rect x="24" y="81" width="9" height="10" fill="#4a4a4a"/>' +
            '<rect x="86" y="81" width="9" height="10" fill="#4a4a4a"/>' +
            // 右側殼間連通管（殼後方）
            '<rect x="88" y="44" width="7" height="26" rx="2" fill="#6b7075" stroke="#3a3a3a" stroke-width="1"/>' +
            // 螺旋壓縮機（上筒後方）
            '<rect x="52" y="25" width="32" height="14" rx="7" fill="#565b60" stroke="#3a3a3a" stroke-width="1.2"/>' +
            '<rect x="57" y="27.5" width="22" height="3" rx="1.5" fill="rgba(255,255,255,.15)"/>' +
            // 下筒：蒸發器（冰水側）— 狀態色
            '<rect x="10" y="60" width="96" height="24" rx="12" fill="' + szCircle + '" stroke="#3a3a3a" stroke-width="1.5"/>' +
            '<rect x="18" y="62.5" width="80" height="4" rx="2" fill="rgba(255,255,255,.18)"/>' +
            '<rect x="18" y="76.5" width="80" height="4" rx="2" fill="rgba(0,0,0,.14)"/>' +
            '<rect x="14" y="59.2" width="3.2" height="25.6" rx="1" fill="rgba(0,0,0,.22)"/>' +
            '<rect x="98.8" y="59.2" width="3.2" height="25.6" rx="1" fill="rgba(0,0,0,.22)"/>' +
            '<line x1="52" y1="60.8" x2="52" y2="83.2" stroke="rgba(0,0,0,.15)" stroke-width="1"/>' +
            '<line x1="76" y1="60.8" x2="76" y2="83.2" stroke="rgba(0,0,0,.15)" stroke-width="1"/>' +
            // （2026-07-23 拿掉四支接管：管路元件由使用者自由對接，不預設接口位置）
            // 上筒：冷凝器（冷卻水側）— 狀態色
            '<rect x="26" y="36" width="80" height="21" rx="10.5" fill="' + szCircle + '" stroke="#3a3a3a" stroke-width="1.5"/>' +
            '<rect x="48" y="38.3" width="50" height="3.6" rx="1.8" fill="rgba(255,255,255,.18)"/>' +
            '<rect x="98.8" y="35.3" width="3.2" height="22.4" rx="1" fill="rgba(0,0,0,.22)"/>' +
            '<line x1="62" y1="36.8" x2="62" y2="56.2" stroke="rgba(0,0,0,.15)" stroke-width="1"/>' +
            '<line x1="82" y1="36.8" x2="82" y2="56.2" stroke="rgba(0,0,0,.15)" stroke-width="1"/>' +
            // 控制箱（最前景，左側；面板色 = szBody）
            '<rect x="14" y="18" width="32" height="42" rx="2" fill="' + szBody + '" stroke="#3a3a3a" stroke-width="1.5"/>' +
            '<rect x="15.5" y="19.5" width="29" height="2.5" rx="1" fill="rgba(255,255,255,.14)"/>' +
            '<line x1="37" y1="20" x2="37" y2="58" stroke="rgba(0,0,0,.28)" stroke-width="1"/>' +
            // 螢幕 — 固定深色 HMI，不隨狀態變色
            '<rect x="17" y="24" width="17" height="13" rx="1.5" fill="#243640" stroke="#333" stroke-width="1"/>' +
            '<rect x="19" y="26.5" width="9" height="1.6" rx=".8" fill="rgba(120,220,180,.8)"/>' +
            '<rect x="19" y="30" width="12" height="1.6" rx=".8" fill="rgba(255,255,255,.35)"/>' +
            '<rect x="19" y="33.2" width="7" height="1.6" rx=".8" fill="rgba(255,255,255,.25)"/>' +
            '<path d="M18,25 L23,25 L19.5,28 L18,28 Z" fill="rgba(255,255,255,.18)"/>' +
            // 指示燈（紅/綠/黃）
            '<circle cx="20" cy="43" r="1.8" fill="#e57373" stroke="#3a3a3a" stroke-width=".7"/>' +
            '<circle cx="25.5" cy="43" r="1.8" fill="#81c784" stroke="#3a3a3a" stroke-width=".7"/>' +
            '<circle cx="31" cy="43" r="1.8" fill="#ffd54f" stroke="#3a3a3a" stroke-width=".7"/>' +
            // 右門壓力錶 ×2（含指針）
            '<circle cx="41.5" cy="26" r="2.2" fill="#cfd4d8" stroke="#3a3a3a" stroke-width=".8"/>' +
            '<line x1="41.5" y1="26" x2="42.8" y2="24.8" stroke="#555" stroke-width=".6"/>' +
            '<circle cx="41.5" cy="33" r="2.2" fill="#cfd4d8" stroke="#3a3a3a" stroke-width=".8"/>' +
            '<line x1="41.5" y1="33" x2="40.4" y2="31.9" stroke="#555" stroke-width=".6"/>' +
            // 門把手＋警示貼紙
            '<rect x="34.2" y="39" width="1.5" height="6" rx=".7" fill="#2f2f2f"/>' +
            '<rect x="39.5" y="44" width="4.5" height="5.5" rx=".5" fill="#e8c33a" stroke="#3a3a3a" stroke-width=".6"/>' +
            // 頂部警示燈（狀態色）
            '<rect x="27.5" y="15" width="5" height="3.5" rx="1" fill="#4a4a4a"/>' +
            '<circle cx="30" cy="14.5" r="2.4" fill="' + szCircle + '" stroke="#333" stroke-width=".8"/>';
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

    // ── 冰機負載% 水平條（底部，唯讀；沿用 pump-gauge-* class 供輪詢局部更新）──
    // 條在左（x 10..76），右側留給設定溫度覆蓋層；% 文字置中在條內
    function _barSvgLoadH(nMax, szVal, szState, szFaultColor) {
        var X = 10, Y = 101, W = 66, H = 9;
        var fVal   = (szVal !== undefined && szVal !== '' && szVal !== '--') ? parseFloat(szVal) : 0;
        var fRatio = Math.max(0, Math.min(1, fVal / (nMax || 1)));
        var nFillW = Math.round(W * fRatio);
        var szFill = szState === 'fault' ? szFaultColor : '#20c997';
        var szText = (szVal !== undefined && szVal !== '' && szVal !== '--')
            ? parseFloat(szVal).toFixed(0) + ' %' : '';
        return '' +
            '<rect x="' + X + '" y="' + Y + '" width="' + W + '" height="' + H + '" rx="3" fill="#333" stroke="#555" stroke-width="1"/>' +
            '<rect class="pump-gauge-fill" x="' + X + '" y="' + Y + '" width="' + nFillW + '" height="' + H + '" rx="3" fill="' + szFill + '"/>' +
            '<text class="pump-gauge-text" x="' + (X + W / 2) + '" y="' + (Y + H - 2) + '" text-anchor="middle" font-size="7.5" fill="#fff" font-weight="700">' + szText + '</text>';
    }

    // 冰機控制箱面板色：綁手自動（遠端/現場）且值=現場（0/false）→ 深黃，否則面板灰
    // （機身雙筒專職狀態色，模式不上機身；1/true = 遠端）
    function _chillerCabinetColor(props, szModeVal) {
        var szLocal = props.szManualColor || '#c79100';
        if (props.szSidMode && szModeVal !== undefined && szModeVal !== '') {
            var bRemote = (szModeVal == 1 || szModeVal === '1' || szModeVal === true || szModeVal === 'true');
            return bRemote ? '#61666b' : szLocal;
        }
        return '#61666b';
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
        // 冰機：szBody = 控制箱面板色（遠端灰/現場深黃）；其餘設備 = 機身模式色
        var szBody = szType === 'chiller' ? _chillerCabinetColor(props, opts.szModeVal)
                                          : _bodyColor(props, opts.szModeVal);
        var szBg   = (props.szBgColor && props.szBgColor !== 'transparent') ? props.szBgColor : 'transparent';
        var szContrast = _contrast(szBg);
        var bSpin  = opts.bInteractive && szState === 'run' && szType !== 'chiller';

        // 主數值條：水塔/風扇綁 szSidFreq（右側直條）、冰機綁 szSidLoad（底部橫條）時才顯示
        var bBar = false, szBarHtml = '';
        if (szType === 'chiller') {
            bBar = !!props.szSidLoad;
            if (bBar) szBarHtml = _barSvgLoadH(parseFloat(props.nLoadMax) || 100, opts.szPrimaryVal, szState, szFaultColor);
        } else {
            bBar = !!props.szSidFreq;
            if (bBar) szBarHtml = _barSvg('freq', parseFloat(props.nFreqMax) || 60, opts.szPrimaryVal, szState, szFaultColor, szContrast, !!opts.bInteractive);
        }

        // 冰機無側邊直條，恆 120 寬；有負載橫條時往下加高
        var szViewBox = szType === 'chiller' ? (bBar ? '0 0 120 124' : '0 0 120 110')
                                             : (bBar ? '0 0 170 110' : '0 0 120 110');

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

    // CHILLER_BAR_W：冰機底部負載橫條的軌道寬（scadapage 輪詢局部更新 fill 寬度用）
    window.MotorEquip = { build: build, BAR_TOP: BAR_TOP, BAR_H: BAR_H, CHILLER_BAR_W: 66 };
})();
