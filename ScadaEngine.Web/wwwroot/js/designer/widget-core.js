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

    const szContent = szType === 'table'          ? buildTableHtml(props)
                    : szType === 'controlBtn'     ? buildControlBtnHtml(props)
                    : szType === 'realtimeValue'  ? buildRealtimeValueHtml(props)
                    : szType === 'diPoint'        ? buildDiPointHtml(props)
                    : szType === 'aoPoint'        ? buildAoPointHtml(props)
                    : szType === 'doPoint'        ? buildDoPointHtml(props)
                    : szType === 'pump'           ? buildPumpHtml(props, 'stop')
                    : szType === 'pipe'           ? buildPipeHtml(props, 'flow')
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
