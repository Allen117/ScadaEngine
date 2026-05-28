// LogicFlow 對齊 / 等距分布工具列
// 8 顆按鈕：6 對齊（靠左/水平置中/靠右/靠上/垂直置中/靠下）+ 2 分布（水平/垂直）
// 與 Designer 的 align.js 邏輯一致，差別：操作 S.canvasNodes[].x/y（model）後同步 DOM left/top + renderEdges
// 載入順序：state → canvas → align → tree → picker → index（在 canvas 之後 / index 之前）
(function () {
    const S = window.__lfNS;
    const GRID = 20;

    // 取得選取的節點 + 對應 DOM 元素（用 model 的 x/y 計算 bounding box，width/height 從 DOM 取）
    function getSelected() {
        const canvas = document.getElementById('diagramCanvas');
        if (!canvas) return [];
        const arr = [];
        for (const nid of S.selectedNodeIds) {
            const node = S.canvasNodes.find(n => n.id === nid);
            const el = canvas.querySelector(`.flow-node[data-node-id="${nid}"]`);
            if (node && el && el.offsetWidth > 0) {
                arr.push({ node, el, w: el.offsetWidth, h: el.offsetHeight });
            }
        }
        return arr;
    }

    function getBox(items) {
        let minL = Infinity, maxR = -Infinity, minT = Infinity, maxB = -Infinity;
        for (const it of items) {
            const l = it.node.x, t = it.node.y;
            const r = l + it.w, b = t + it.h;
            if (l < minL) minL = l;
            if (r > maxR) maxR = r;
            if (t < minT) minT = t;
            if (b > maxB) maxB = b;
        }
        return { minL, maxR, minT, maxB };
    }

    function snap(v) { return Math.max(0, Math.round(v / GRID) * GRID); }

    function commit() {
        S.updateCanvasSize && S.updateCanvasSize();
        S.renderEdges && S.renderEdges();
    }

    // fn(it, box) 回傳 { x, y }；x 或 y 為 null 表示「不改該軸」
    function applyAlign(fn) {
        const items = getSelected();
        if (items.length < 2) return;
        const box = getBox(items);
        for (const it of items) {
            const nxt = fn(it, box);
            if (nxt.x != null) {
                const nx = snap(nxt.x);
                it.node.x = nx;
                it.el.style.left = nx + 'px';
            }
            if (nxt.y != null) {
                const ny = snap(nxt.y);
                it.node.y = ny;
                it.el.style.top = ny + 'px';
            }
        }
        commit();
    }

    function alignLeft()     { applyAlign((it, b) => ({ x: b.minL, y: null })); }
    function alignRight()    { applyAlign((it, b) => ({ x: b.maxR - it.w, y: null })); }
    function alignTop()      { applyAlign((it, b) => ({ x: null, y: b.minT })); }
    function alignBottom()   { applyAlign((it, b) => ({ x: null, y: b.maxB - it.h })); }
    function alignCenterH()  { applyAlign((it, b) => ({ x: (b.minL + b.maxR) / 2 - it.w / 2, y: null })); }
    function alignCenterV()  { applyAlign((it, b) => ({ x: null, y: (b.minT + b.maxB) / 2 - it.h / 2 })); }

    function distributeHorizontally() {
        const items = getSelected();
        if (items.length < 3) return;
        items.sort((a, b) => a.node.x - b.node.x);
        const first = items[0], last = items[items.length - 1];
        const firstLeft = first.node.x;
        const lastRight = last.node.x + last.w;
        let sumMidW = 0;
        for (let i = 1; i < items.length - 1; i++) sumMidW += items[i].w;
        const totalGap = lastRight - firstLeft - first.w - sumMidW - last.w;
        const gap = totalGap / (items.length - 1);

        let cursor = firstLeft + first.w + gap;
        for (let i = 1; i < items.length - 1; i++) {
            const it = items[i];
            const nx = snap(cursor);
            it.node.x = nx;
            it.el.style.left = nx + 'px';
            cursor += it.w + gap;
        }
        commit();
    }

    function distributeVertically() {
        const items = getSelected();
        if (items.length < 3) return;
        items.sort((a, b) => a.node.y - b.node.y);
        const first = items[0], last = items[items.length - 1];
        const firstTop = first.node.y;
        const lastBottom = last.node.y + last.h;
        let sumMidH = 0;
        for (let i = 1; i < items.length - 1; i++) sumMidH += items[i].h;
        const totalGap = lastBottom - firstTop - first.h - sumMidH - last.h;
        const gap = totalGap / (items.length - 1);

        let cursor = firstTop + first.h + gap;
        for (let i = 1; i < items.length - 1; i++) {
            const it = items[i];
            const ny = snap(cursor);
            it.node.y = ny;
            it.el.style.top = ny + 'px';
            cursor += it.h + gap;
        }
        commit();
    }

    function updateAlignToolbarState() {
        const alignGrp = document.getElementById('alignToolGroup');
        const distGrp = document.getElementById('distToolGroup');
        const n = S.selectedNodeIds ? S.selectedNodeIds.size : 0;
        if (alignGrp) alignGrp.style.display = (n >= 2) ? 'inline-flex' : 'none';
        if (distGrp)  distGrp.style.display  = (n >= 3) ? 'inline-flex' : 'none';
    }

    S.alignLeft = alignLeft;
    S.alignRight = alignRight;
    S.alignTop = alignTop;
    S.alignBottom = alignBottom;
    S.alignCenterH = alignCenterH;
    S.alignCenterV = alignCenterV;
    S.distributeHorizontally = distributeHorizontally;
    S.distributeVertically = distributeVertically;
    S.updateAlignToolbarState = updateAlignToolbarState;
})();
