// ============================================================
// scadapage/widget-pipe.js — 管路流動元件 HTML（圖形共用 common/pipe-svg.js）
// ============================================================
// 純搬移自 scadapage.js（plan 2026-08-06-scadapage-js-split）。
// 不包 IIFE、global scope，與 js/designer/ 拆分同模式；
// 共享狀態集中於 state.js。原始 4-space 縮排刻意保留，
// 以保證 template literal 內容 byte-level 不變（純搬移原則）。
// ============================================================

    // ── 管路流動元件 HTML（純顯示，無控制；圖形共用 common/pipe-svg.js）──
    // szState: 'flow' 流動 | 'stop' 靜止 | 'bad' 斷線；szValueText 選填（類比值顯示於 tooltip）
    // nW/nH：widget 外框尺寸（SVG viewBox 與舊格式直管推導用）
    function buildPipeViewHtml(props, szState, szValueText, nW, nH) {
        var szTitle = props.szTitle || '';
        var szStateText = szState === 'bad' ? '斷線' : szState === 'flow' ? '流動' : '靜止';
        var arrTip = [];
        if (szTitle) arrTip.push(escViewHtml(szTitle));
        arrTip.push(szStateText);
        if (szValueText) arrTip.push(escViewHtml(szValueText));
        var szTooltip = '<div class="scada-hover-label" style="display:none;position:absolute;bottom:100%;' +
            'margin-bottom:4px;left:50%;transform:translateX(-50%);white-space:nowrap;' +
            'background:rgba(33,37,41,.85);color:#fff;font-size:11px;padding:3px 10px;border-radius:4px;' +
            'pointer-events:none;z-index:10;">' + arrTip.join(' — ') + '</div>';

        return PipeSvg.build({ props: props, szState: szState, nW: nW, nH: nH, szHoverHtml: szTooltip });
    }
