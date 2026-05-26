// ============================================================
// Designer — 多選元件對齊 / 等距分布
// ============================================================
// 內容：6 種對齊（靠左 / 靠右 / 靠上 / 靠下 / 水平置中 / 垂直置中）+
// 2 種等距分布（水平 / 垂直），含工具列按鈕顯隱切換。
// 依賴：state.js（canvas / snapGrid / selectedWidgetIds / selectedEl /
// syncPosInputs）。本檔須在 widget-core.js 之後、index.js 之前載入
// （updateAlignToolbarState 會被 widget-core 的 updateWidgetSelectionVisual
// 呼叫）。
// ============================================================

// ============================================================
// 共用 helpers
// ============================================================

// 收集目前選取且仍存在於畫布的 widget 元素
function getSelectedWidgetEls() {
    const arr = [];
    selectedWidgetIds.forEach(szId => {
        const el = document.getElementById(szId);
        if (el) arr.push(el);
    });
    return arr;
}

// 計算選取群組的 bounding box（left/right/top/bottom + 元素陣列）
function getSelectionBoundingBox(els) {
    let minL = Infinity, maxR = -Infinity, minT = Infinity, maxB = -Infinity;
    for (const el of els) {
        const l = parseInt(el.style.left) || 0;
        const t = parseInt(el.style.top)  || 0;
        const r = l + el.offsetWidth;
        const b = t + el.offsetHeight;
        if (l < minL) minL = l;
        if (r > maxR) maxR = r;
        if (t < minT) minT = t;
        if (b > maxB) maxB = b;
    }
    return { minL, maxR, minT, maxB };
}

// 將 x 限制於 canvas 範圍內（[0, canvas.W - el.W]）
function clampX(x, nW) {
    return Math.max(0, Math.min(canvas.offsetWidth - nW, x));
}
// 將 y 限制於 canvas 範圍內
function clampY(y, nH) {
    return Math.max(0, Math.min(canvas.offsetHeight - nH, y));
}

// 套用對齊：對每個 el 計算新 (x, y) → snap → clamp → 寫回 style
// fnSetter(el, box) 回傳 { x, y }；x 或 y 可為 null 表示「不改該軸」
function applyAlign(fnSetter) {
    const els = getSelectedWidgetEls();
    if (els.length < 2) return;
    const box = getSelectionBoundingBox(els);
    for (const el of els) {
        const cur = {
            x: parseInt(el.style.left) || 0,
            y: parseInt(el.style.top)  || 0
        };
        const nxt = fnSetter(el, box);
        const newX = nxt.x === null ? cur.x : clampX(snapGrid(nxt.x), el.offsetWidth);
        const newY = nxt.y === null ? cur.y : clampY(snapGrid(nxt.y), el.offsetHeight);
        el.style.left = newX + 'px';
        el.style.top  = newY + 'px';
    }
    // 若有單選項目（即多選中最後 focus 的），同步屬性面板 X/Y
    if (selectedEl) {
        syncPosInputs(parseInt(selectedEl.style.left), parseInt(selectedEl.style.top));
    }
}

// ============================================================
// 6 種對齊
// ============================================================

function alignLeft() {
    applyAlign((el, b) => ({ x: b.minL, y: null }));
}

function alignRight() {
    applyAlign((el, b) => ({ x: b.maxR - el.offsetWidth, y: null }));
}

function alignTop() {
    applyAlign((el, b) => ({ x: null, y: b.minT }));
}

function alignBottom() {
    applyAlign((el, b) => ({ x: null, y: b.maxB - el.offsetHeight }));
}

// 水平置中：各 el 同一 X 中心（垂直線通過群組水平中心）
function alignCenterH() {
    applyAlign((el, b) => {
        const cx = (b.minL + b.maxR) / 2;
        return { x: cx - el.offsetWidth / 2, y: null };
    });
}

// 垂直置中：各 el 同一 Y 中心（水平線通過群組垂直中心）
function alignCenterV() {
    applyAlign((el, b) => {
        const cy = (b.minT + b.maxB) / 2;
        return { x: null, y: cy - el.offsetHeight / 2 };
    });
}

// ============================================================
// 2 種等距分布（PowerPoint / Visio 慣例：等間距）
// ============================================================

function distributeHorizontally() {
    const els = getSelectedWidgetEls();
    if (els.length < 3) return;

    // 依 left 排序
    els.sort((a, b) => (parseInt(a.style.left) || 0) - (parseInt(b.style.left) || 0));

    const first = els[0];
    const last  = els[els.length - 1];
    const firstLeft = parseInt(first.style.left) || 0;
    const lastLeft  = parseInt(last.style.left)  || 0;
    const lastRight = lastLeft + last.offsetWidth;

    let sumMiddleW = 0;
    for (let i = 1; i < els.length - 1; i++) sumMiddleW += els[i].offsetWidth;

    // gap = 總空白 / (n-1)；空白 = lastRight - firstLeft - (first.W + 中間 W + last.W)
    const totalGap = lastRight - firstLeft - first.offsetWidth - sumMiddleW - last.offsetWidth;
    const gap = totalGap / (els.length - 1);

    let cursor = firstLeft + first.offsetWidth + gap;
    for (let i = 1; i < els.length - 1; i++) {
        const el = els[i];
        const newX = clampX(snapGrid(cursor), el.offsetWidth);
        el.style.left = newX + 'px';
        cursor += el.offsetWidth + gap;
    }

    if (selectedEl) {
        syncPosInputs(parseInt(selectedEl.style.left), parseInt(selectedEl.style.top));
    }
}

function distributeVertically() {
    const els = getSelectedWidgetEls();
    if (els.length < 3) return;

    els.sort((a, b) => (parseInt(a.style.top) || 0) - (parseInt(b.style.top) || 0));

    const first = els[0];
    const last  = els[els.length - 1];
    const firstTop  = parseInt(first.style.top) || 0;
    const lastTop   = parseInt(last.style.top)  || 0;
    const lastBottom = lastTop + last.offsetHeight;

    let sumMiddleH = 0;
    for (let i = 1; i < els.length - 1; i++) sumMiddleH += els[i].offsetHeight;

    const totalGap = lastBottom - firstTop - first.offsetHeight - sumMiddleH - last.offsetHeight;
    const gap = totalGap / (els.length - 1);

    let cursor = firstTop + first.offsetHeight + gap;
    for (let i = 1; i < els.length - 1; i++) {
        const el = els[i];
        const newY = clampY(snapGrid(cursor), el.offsetHeight);
        el.style.top = newY + 'px';
        cursor += el.offsetHeight + gap;
    }

    if (selectedEl) {
        syncPosInputs(parseInt(selectedEl.style.left), parseInt(selectedEl.style.top));
    }
}

// ============================================================
// 工具列按鈕顯隱（由 widget-core.updateWidgetSelectionVisual 呼叫）
// ============================================================
function updateAlignToolbarState() {
    const alignGrp = document.getElementById('alignToolGroup');
    const distGrp  = document.getElementById('distToolGroup');
    const n = selectedWidgetIds.size;
    if (alignGrp) alignGrp.style.display = (n >= 2) ? 'flex' : 'none';
    if (distGrp)  distGrp.style.display  = (n >= 3) ? 'flex' : 'none';
}
