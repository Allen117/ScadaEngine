// ============================================================
// 共用：自訂圖片/動畫（GIF）元件 HTML 產生器
// ============================================================
// Designer 預覽（widget-defs.js）與 ScadaPage 執行期（scadapage/widget-image.js）
// 共用同一份 HTML，避免兩處走樣（同 pipe-svg.js / motor-equip-svg.js 前例）。
//
// 設計（plan 2026-09-18）：
//   兩張圖 —— szAnimSrc（GIF，動）、szStillSrc（靜止圖，不動；決策 2 選項 a）。
//   GIF 天生 <img> 自動播放，切成靜止圖即「停」，無需影格控制。
//   狀態：
//     'run'  → 顯示 szAnimSrc（無則退 szStillSrc）
//     'stop' → 顯示 szStillSrc；若未提供靜止圖 → 退 szAnimSrc + 灰階(視覺提示已停，決策 2 fallback)
//     'bad'  → 顯示靜止圖(或動畫圖) + 灰階降透明(斷線)
//   兩張都沒 → 佔位提示（Designer 用；執行期同樣顯示，避免空白）。
//
// 對外：window.ImageWidget.build(props, szState) → HTML 字串
// ============================================================
(function () {
    function esc(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function build(props, szState, szNoImageHint) {
        props = props || {};
        var szAnim  = props.szAnimSrc  || '';
        var szStill = props.szStillSrc || '';
        var szFit   = props.szFit || 'contain';
        var szBg    = (props.szBgColor && props.szBgColor !== 'transparent') ? props.szBgColor : 'transparent';

        // 無任何圖 → 佔位提示
        if (!szAnim && !szStill) {
            return '<div class="image-widget-empty" style="width:100%;height:100%;display:flex;flex-direction:column;' +
                   'align-items:center;justify-content:center;gap:4px;color:#adb5bd;background:' + szBg + ';">' +
                   '<i class="fas fa-film" style="font-size:22px;opacity:.6;"></i>' +
                   '<span style="font-size:10px;">' + esc(szNoImageHint || '') + '</span>' +
                   '</div>';
        }

        var szSrc, szFilter = '';
        if (szState === 'run') {
            szSrc = szAnim || szStill;
        } else if (szState === 'bad') {
            szSrc = szStill || szAnim;
            szFilter = 'filter:grayscale(1) opacity(.4);';
        } else { // 'stop'
            if (szStill) {
                szSrc = szStill;
            } else {
                szSrc = szAnim;
                szFilter = 'filter:grayscale(1) opacity(.5);';   // 無靜止圖時的 fallback（決策 2）
            }
        }

        return '<div style="width:100%;height:100%;background:' + szBg + ';display:flex;' +
               'align-items:center;justify-content:center;overflow:hidden;">' +
               '<img src="' + esc(szSrc) + '" draggable="false" alt="" ' +
               'style="width:100%;height:100%;object-fit:' + szFit + ';' + szFilter +
               'pointer-events:none;user-select:none;">' +
               '</div>';
    }

    window.ImageWidget = { build: build };
})();
