// ============================================================
// 共用：管路（正交折線）SVG 產生器
// ============================================================
// Designer 預覽（widget-defs.js）與 ScadaPage 執行期（scadapage.js）共用
// 同一份圖形，避免兩處各畫一次而走樣（同 motor-equip-svg.js 前例）。
//
// 資料模型（plan 2026-07-23 決策 1）：
//   props.arrPoints = [{x,y}, ...]（≥2 點，相對 widget 左上角 px）
//   不變量：任兩相鄰點共 x 或共 y（正交）。
//   舊格式（無 arrPoints）→ 由 szOrient + widget 寬高推導 2 節點直管，
//   推導結果不回寫（Designer 端由 widget-core 轉存新格式）。
//
// 渲染（決策 2）：同一條 path 疊三層 —
//   track：停止/斷線色實線（狀態底色）
//   flow ：流動色 stroke-dasharray 12 10 + stroke-dashoffset 動畫（僅 flow 狀態）
//   hit  ：加寬透明 stroke，唯一 pointer-events 命中區（hover / 拖移 / 右鍵）
//   stroke-linejoin/linecap: round → 轉角自然圓滑、流動連續過彎。
//
// 對外：window.PipeSvg
//   .SPEED_DUR                     流速檔 1..5 → 動畫時長
//   .padOf(props)                  節點 bounding box 外框留白（px）
//   .normPoints(props, nW, nH)     取節點陣列（含舊格式推導 fallback，回傳複本）
//   .build(opts)                   → widget 內層 HTML 字串
//     opts = { props, szState:'flow'|'stop'|'bad', nW, nH, szHoverHtml }
// ============================================================
(function () {
    'use strict';

    // 流速檔 1..5 → 動畫時長（值越大越快；沿用直管時期 PIPE_SPEED_DUR）
    var SPEED_DUR = { 1: '1.2s', 2: '0.9s', 3: '0.6s', 4: '0.4s', 5: '0.25s' };

    function _thk(props) {
        return Math.max(2, parseFloat(props.nThickness) || 8);
    }

    // widget 外框 = 節點 bbox + 每側 pad（round cap/join 外凸 thk/2，再留 2px）
    function padOf(props) {
        return Math.ceil(_thk(props) / 2) + 2;
    }

    // 取節點陣列（複本）。無有效 arrPoints → 依 szOrient + 寬高推導 2 節點直管，
    // 端點內縮 thk/2 使 round cap 與舊版 div 圓角端完全等視覺。
    function normPoints(props, nW, nH) {
        var arr = props.arrPoints;
        if (arr && arr.length >= 2) {
            var out = [];
            for (var i = 0; i < arr.length; i++) {
                out.push({ x: parseFloat(arr[i].x) || 0, y: parseFloat(arr[i].y) || 0 });
            }
            return out;
        }
        var w = parseFloat(nW), h = parseFloat(nH);
        if (!(w > 0)) w = 160;
        if (!(h > 0)) h = 24;
        var half = _thk(props) / 2;
        if (props.szOrient === 'v') {
            return [{ x: w / 2, y: half }, { x: w / 2, y: Math.max(half, h - half) }];
        }
        return [{ x: half, y: h / 2 }, { x: Math.max(half, w - half), y: h / 2 }];
    }

    function buildPathD(arrPts) {
        var d = '';
        for (var i = 0; i < arrPts.length; i++) {
            d += (i === 0 ? 'M' : 'L') + arrPts[i].x + ' ' + arrPts[i].y;
        }
        return d;
    }

    function build(opts) {
        var props   = opts.props || {};
        var szState = opts.szState || 'flow';
        var nThk    = _thk(props);
        var szFlow  = props.szFlowColor || '#0d6efd';
        var szStop  = props.szStopColor || '#adb5bd';
        var szBad   = props.szBadColor  || '#6c757d';
        var nSpeed  = Math.min(5, Math.max(1, parseInt(props.nSpeed) || 3));
        var szDur   = SPEED_DUR[nSpeed] || '0.6s';
        var bRev    = props.szDir === 'rev';
        var szBg    = (props.szBgColor && props.szBgColor !== 'transparent') ? props.szBgColor : 'transparent';

        var nW = parseFloat(opts.nW), nH = parseFloat(opts.nH);
        if (!(nW > 0)) nW = 160;
        if (!(nH > 0)) nH = 24;

        var arrPts = normPoints(props, nW, nH);
        var szD    = buildPathD(arrPts);

        // 管身底色：斷線→斷線色，其餘→停止色（流動時上面再疊 dash 動畫層）
        var szTrackColor = szState === 'bad' ? szBad : szStop;
        var szFlowPath = (szState === 'flow')
            ? '<path class="pipe-svg-flow' + (bRev ? ' rev' : '') + '" d="' + szD + '"' +
              ' stroke="' + szFlow + '" stroke-width="' + nThk + '"' +
              ' style="--flow-dur:' + szDur + ';"/>'
            : '';
        var nHitW = Math.max(nThk + 10, 16);

        return '<div class="pipe-widget pipe-poly" style="background:' + szBg + ';">' +
            '<svg class="pipe-svg" viewBox="0 0 ' + nW + ' ' + nH + '" width="100%" height="100%" preserveAspectRatio="none">' +
                '<path class="pipe-svg-track" d="' + szD + '" stroke="' + szTrackColor + '" stroke-width="' + nThk + '"/>' +
                szFlowPath +
                '<path class="pipe-svg-hit" d="' + szD + '" stroke-width="' + nHitW + '"/>' +
            '</svg>' +
            (opts.szHoverHtml || '') +
        '</div>';
    }

    window.PipeSvg = {
        SPEED_DUR:  SPEED_DUR,
        padOf:      padOf,
        normPoints: normPoints,
        build:      build
    };
})();
