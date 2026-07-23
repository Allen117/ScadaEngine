// ============================================================
// Designer — Widget 生命週期 / 拖移 / 縮放 / 選取 / 複製貼上
// ============================================================
// 內容：元件庫拖曳到畫布、createWidget / renderWidget、選取邏輯、
// 拖移 / resize、複製貼上刪除、鍵盤快捷鍵。
// 依賴：state.js / widget-defs.js（builder + WIDGET_DEFS）。
// 部分函式（如 deleteWidget）會被 widget-defs.js 內 renderTextWidget
// 的 onclick HTML 字串呼叫；onWidgetMouseDown / startMove / startResize
// 同樣被 widget-defs.js 引用。靠 global hoisting 解循環依賴 — 切勿改成
// const xxx = function() {} 形式。
// ============================================================

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
    // 透過 getWidgetDefaultProps() 解析 __i18n__ 預設值為當前 culture 字串
    el.widgetProps = getWidgetDefaultProps(szType);

    // table widget：新建即套用「由結構決定大小」（plan 決策 8）
    if (szType === 'table') {
        initArrCells(el.widgetProps);
        el.widgetProps.bTableSizeLocked = true;
        const sz = computeTableWidgetSize(el.widgetProps);
        el.style.width  = sz.nW + 'px';
        el.style.height = sz.nH + 'px';
    } else {
        el.style.width  = def.nDefaultW + 'px';
        el.style.height = def.nDefaultH + 'px';
    }

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

    // 折線管路：先確保 arrPoints（舊格式 h/v 轉新格式）並同步外框 = bbox + 留白
    if (szType === 'pipe') preparePipeWidget(el);

    const szContent = szType === 'table'          ? buildTableHtml(props)
                    : szType === 'controlBtn'     ? buildControlBtnHtml(props)
                    : szType === 'realtimeValue'  ? buildRealtimeValueHtml(props)
                    : szType === 'diPoint'        ? buildDiPointHtml(props)
                    : szType === 'aoPoint'        ? buildAoPointHtml(props)
                    : szType === 'doPoint'        ? buildDoPointHtml(props)
                    : szType === 'pump'           ? buildPumpHtml(props, 'stop')
                    : szType === 'pipe'           ? buildPipeHtml(props, 'flow', el)
                    : szType === 'coolingTower'   ? buildCoolingTowerHtml(props, 'stop')
                    : szType === 'ahuFan'         ? buildAhuFanHtml(props, 'stop')
                    : szType === 'chiller'        ? buildChillerHtml(props, 'stop')
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
            <button class="widget-del" onclick="deleteWidget('${el.id}')" title="${escHtml(t('designer.widget.delete_widget_tooltip'))}">✕</button>
        </div>
        <div class="widget-body">${szContent}</div>
        <div class="resize-knob" title="${escHtml(t('designer.widget.resize_tooltip'))}"></div>
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
    } else if (szType === 'pipe') {
        el.classList.add('widget-pipe');
    } else if ('szBgColor' in props) {
        const isBgTransparent = !props.szBgColor || props.szBgColor === 'transparent';
        el.classList.toggle('widget-transparent', isBgTransparent);
    }

    // table 鎖定尺寸時隱藏 resize knob（雙保險，搭配 startResize 短路）
    if (szType === 'table') {
        el.classList.toggle('widget-size-locked', !!props.bTableSizeLocked);
    }

    // 控制按鈕：整個 body 可拖移
    if (szType === 'controlBtn') {
        el.querySelector('.ctrl-btn-body').addEventListener('mousedown', e => {
            e.preventDefault();
            startMove(e, el);
        });
    }

    // AI / DI / AO / DO 點位 / 管路：整個 body 可拖移（header 已隱藏）
    if (szType === 'realtimeValue' || szType === 'diPoint' || szType === 'aoPoint' || szType === 'doPoint' || szType === 'pipe') {
        el.querySelector('.widget-body').addEventListener('mousedown', e => {
            e.preventDefault();
            startMove(e, el);
        });
    }

    // 拖移（header）— controlBtn / realtimeValue / diPoint / pipe 已用 body 拖移
    if (szType !== 'controlBtn' && szType !== 'realtimeValue' && szType !== 'diPoint' && szType !== 'aoPoint' && szType !== 'doPoint' && szType !== 'pipe') el.querySelector('.widget-header').addEventListener('mousedown', e => {
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

    // 折線管路：雙擊管身插入節點 + 節點手把（拖曳折彎 / 右鍵刪除）
    if (szType === 'pipe') {
        const hit = el.querySelector('.pipe-svg-hit');
        if (hit) hit.addEventListener('dblclick', e => onPipeBodyDblClick(e, el));
        renderPipeHandles(el);
    }

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
    if (typeof updateAlignToolbarState === 'function') updateAlignToolbarState();
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
    // table widget 大小由結構決定，禁止手動 resize（plan 決策 3）
    if (el && el.dataset.type === 'table' && el.widgetProps && el.widgetProps.bTableSizeLocked) return;
    // 折線管路大小由節點 bounding box 決定，禁止手動 resize（plan 2026-07-23 決策 4）
    if (el && el.dataset.type === 'pipe') return;
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
// 折線管路節點編輯（plan 2026-07-23）
// ============================================================
// 資料模型：props.arrPoints = [{x,y},...]（相對 widget 左上角 px），
// 不變量：任兩相鄰節點共 x 或共 y（正交）。widget 外框 = 節點 bbox + pad。
// 編輯以「畫布絕對座標」計算（widget 外框隨 bbox 平移，相對座標會變基準）。

// 舊格式（無 arrPoints）→ 依 szOrient + 目前寬高推導 2 節點，並同步外框。
// 於 renderWidget 進入時呼叫；Designer 重存即落新格式（執行期推導不回寫）。
function preparePipeWidget(el) {
    const props = el.widgetProps;
    if (!props.arrPoints || props.arrPoints.length < 2) {
        props.arrPoints = PipeSvg.normPoints(props,
            parseInt(el.style.width)  || el.offsetWidth,
            parseInt(el.style.height) || el.offsetHeight);
    }
    const nLeft = parseInt(el.style.left) || 0;
    const nTop  = parseInt(el.style.top)  || 0;
    _setPipeFrame(el, props.arrPoints.map(p => ({ x: nLeft + p.x, y: nTop + p.y })));
}

// 以畫布絕對座標節點重算外框（left/top/width/height = bbox + pad），
// 並把節點寫回相對座標。不觸發 renderWidget（由呼叫端決定）。
function _setPipeFrame(el, arrAbs) {
    const props = el.widgetProps;
    const pad = PipeSvg.padOf(props);
    let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
    for (const p of arrAbs) {
        if (p.x < minX) minX = p.x;
        if (p.y < minY) minY = p.y;
        if (p.x > maxX) maxX = p.x;
        if (p.y > maxY) maxY = p.y;
    }
    const nLeft = Math.round(minX - pad);
    const nTop  = Math.round(minY - pad);
    el.style.left   = nLeft + 'px';
    el.style.top    = nTop  + 'px';
    el.style.width  = Math.round(maxX - minX + pad * 2) + 'px';
    el.style.height = Math.round(maxY - minY + pad * 2) + 'px';
    props.arrPoints = arrAbs.map(p => ({ x: Math.round(p.x - nLeft), y: Math.round(p.y - nTop) }));
}

// 節點手把（選取時由 CSS 顯示；端點實心、中間節點空心）
function renderPipeHandles(el) {
    const arrPts = el.widgetProps.arrPoints || [];
    arrPts.forEach((p, i) => {
        const h = document.createElement('div');
        h.className   = 'pipe-node' + ((i === 0 || i === arrPts.length - 1) ? ' pipe-node-end' : '');
        h.style.left  = p.x + 'px';
        h.style.top   = p.y + 'px';
        h.title       = t('designer.pipe.node_tooltip');
        h.addEventListener('mousedown',   e => startPipeNodeDrag(e, el, i));
        h.addEventListener('contextmenu', e => { e.preventDefault(); e.stopPropagation(); deletePipeNode(el, i); });
        h.addEventListener('dblclick',    e => e.stopPropagation());
        el.appendChild(h);
    });
}

// 線段方向分類（'h' 水平 / 'v' 垂直；零長度或異常資料以較大位移軸判定）
function _pipeSegOrient(p1, p2) {
    return Math.abs(p2.x - p1.x) >= Math.abs(p2.y - p1.y) ? 'h' : 'v';
}

// ── 拖曳節點（正交修正：只動直接鄰點）──
// 鄰點修正規則：
//   鄰點是端點 → 沿共用線段原方向跟隨（原水平→繼承 y、原垂直→繼承 x）
//   鄰點是中間節點 → 修正在「不破壞其外側線段」的軸上：
//     外側線段原水平（鄰點與更外點共 y）→ 改鄰點 x（共用段轉垂直）
//     外側線段原垂直（共 x）→ 改鄰點 y（共用段轉水平）
// 如此任何案例（含連續共線節點）修正後全鏈仍正交，無需連鎖擴散。
let pipeDrag = null;

function startPipeNodeDrag(e, el, nIdx) {
    e.preventDefault();
    e.stopPropagation();
    if (!selectedWidgetIds.has(el.id)) {
        clearWidgetSelection();
        selectedWidgetIds.add(el.id);
        updateWidgetSelectionVisual();
    }
    selectWidget(el);
    const nLeft = parseInt(el.style.left) || 0;
    const nTop  = parseInt(el.style.top)  || 0;
    const arrAbs = el.widgetProps.arrPoints.map(p => ({ x: nLeft + p.x, y: nTop + p.y }));
    const arrOrient = [];
    for (let i = 0; i < arrAbs.length - 1; i++) arrOrient.push(_pipeSegOrient(arrAbs[i], arrAbs[i + 1]));
    pipeDrag = { el, nIdx, arrAbs, arrOrient };
    document.querySelector('.property-panel')?.classList.add('collapsed');
    document.addEventListener('mousemove', onPipeNodeMove);
    document.addEventListener('mouseup', endPipeNodeDrag);
}

function onPipeNodeMove(e) {
    if (!pipeDrag) return;
    const rect = canvas.getBoundingClientRect();
    let ax = snapGrid(e.clientX - rect.left);
    let ay = snapGrid(e.clientY - rect.top);
    ax = Math.max(0, Math.min(canvas.offsetWidth,  ax));
    ay = Math.max(0, Math.min(canvas.offsetHeight, ay));

    const { el, nIdx, arrAbs, arrOrient } = pipeDrag;
    const arrPts = arrAbs.map(p => ({ x: p.x, y: p.y }));
    arrPts[nIdx] = { x: ax, y: ay };

    // 前鄰點（共用線段 = arrOrient[nIdx-1]，其外側線段 = arrOrient[nIdx-2]）
    if (nIdx > 0) {
        const bEnd = (nIdx - 1 === 0);
        if (bEnd) {
            if (arrOrient[nIdx - 1] === 'h') arrPts[nIdx - 1].y = ay;
            else                             arrPts[nIdx - 1].x = ax;
        } else {
            if (arrOrient[nIdx - 2] === 'h') arrPts[nIdx - 1].x = ax;
            else                             arrPts[nIdx - 1].y = ay;
        }
    }
    // 後鄰點（共用線段 = arrOrient[nIdx]，其外側線段 = arrOrient[nIdx+1]）
    if (nIdx < arrPts.length - 1) {
        const bEnd = (nIdx + 1 === arrPts.length - 1);
        if (bEnd) {
            if (arrOrient[nIdx] === 'h') arrPts[nIdx + 1].y = ay;
            else                         arrPts[nIdx + 1].x = ax;
        } else {
            if (arrOrient[nIdx + 1] === 'h') arrPts[nIdx + 1].x = ax;
            else                             arrPts[nIdx + 1].y = ay;
        }
    }

    _setPipeFrame(el, arrPts);
    renderWidget(el);
    syncPosInputs(parseInt(el.style.left), parseInt(el.style.top));
}

function endPipeNodeDrag() {
    if (!pipeDrag) return;
    const el = pipeDrag.el;
    pipeDrag = null;
    document.removeEventListener('mousemove', onPipeNodeMove);
    document.removeEventListener('mouseup', endPipeNodeDrag);

    // 去除拖到重合的連續重複節點（至少保留 2 點）
    const props = el.widgetProps;
    const arrPts = props.arrPoints;
    const out = [arrPts[0]];
    for (let i = 1; i < arrPts.length; i++) {
        const prev = out[out.length - 1];
        if (arrPts[i].x !== prev.x || arrPts[i].y !== prev.y) out.push(arrPts[i]);
    }
    if (out.length >= 2) props.arrPoints = out;
    preparePipeWidget(el);
    renderWidget(el);
    if (selectedEl === el) {
        document.querySelector('.property-panel')?.classList.remove('collapsed');
        renderPropPanel(el);
    }
}

// ── 雙擊管身：於最近線段中點插入節點（插入當下共線，拖開即折彎）──
function onPipeBodyDblClick(e, el) {
    e.preventDefault();
    e.stopPropagation();
    const arrPts = el.widgetProps.arrPoints;
    if (!arrPts || arrPts.length < 2) return;
    const rect  = canvas.getBoundingClientRect();
    const nLeft = parseInt(el.style.left) || 0;
    const nTop  = parseInt(el.style.top)  || 0;
    const px = e.clientX - rect.left - nLeft;
    const py = e.clientY - rect.top  - nTop;
    let nBest = 0, fBest = Infinity;
    for (let i = 0; i < arrPts.length - 1; i++) {
        const f = _distToSegment(px, py, arrPts[i], arrPts[i + 1]);
        if (f < fBest) { fBest = f; nBest = i; }
    }
    arrPts.splice(nBest + 1, 0, {
        x: Math.round((arrPts[nBest].x + arrPts[nBest + 1].x) / 2),
        y: Math.round((arrPts[nBest].y + arrPts[nBest + 1].y) / 2)
    });
    renderWidget(el);
}

function _distToSegment(px, py, p1, p2) {
    const dx = p2.x - p1.x, dy = p2.y - p1.y;
    const fLen2 = dx * dx + dy * dy;
    let ft = fLen2 === 0 ? 0 : ((px - p1.x) * dx + (py - p1.y) * dy) / fLen2;
    ft = Math.max(0, Math.min(1, ft));
    const cx = p1.x + ft * dx, cy = p1.y + ft * dy;
    return Math.hypot(px - cx, py - cy);
}

// ── 右鍵節點：刪除（前後不共軸自動補轉角點；少於 2 點禁刪）──
function deletePipeNode(el, nIdx) {
    const arrPts = el.widgetProps.arrPoints;
    if (!arrPts || arrPts.length <= 2) return;
    const bInterior = nIdx > 0 && nIdx < arrPts.length - 1;
    let prev = null, next = null, szPrevOrient = null;
    if (bInterior) {
        prev = arrPts[nIdx - 1];
        next = arrPts[nIdx + 1];
        szPrevOrient = _pipeSegOrient(prev, arrPts[nIdx]);
    }
    arrPts.splice(nIdx, 1);
    if (bInterior && prev.x !== next.x && prev.y !== next.y) {
        // 沿刪除前的前段方向轉彎補一個轉角點，維持正交
        const corner = szPrevOrient === 'h' ? { x: next.x, y: prev.y } : { x: prev.x, y: next.y };
        arrPts.splice(nIdx, 0, corner);
    }
    preparePipeWidget(el);
    renderWidget(el);
    if (selectedEl === el) renderPropPanel(el);
}
