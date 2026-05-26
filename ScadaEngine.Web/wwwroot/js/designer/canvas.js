// ============================================================
// Designer — 畫布操作
// ============================================================
// 內容：畫布空白點擊（取消選取 / 框選）、畫布右鍵、清除畫布、
// 匯入/移除背景圖、畫布狀態列。
// 依賴：state.js（canvas / 全域狀態 / snapGrid / escHtml）、
// widget-core.js（selectWidget / clearWidgetSelection / 框選相關）、
// ctx-menu.js（showCanvasCtxMenu）、page-tree.js（findPage）。
// ============================================================

// 點擊畫布空白處：關閉選單 / 取消選取 / 框選
canvas.addEventListener('mousedown', e => {
    if (e.target !== canvas) return;

    if (e.button !== 0) { selectWidget(null); clearWidgetSelection(); return; }

    // 開始框選
    const cRect = canvas.getBoundingClientRect();
    const sx = e.clientX - cRect.left;
    const sy = e.clientY - cRect.top;
    const selRect = document.createElement('div');
    selRect.className = 'designer-selection-rect';
    canvas.appendChild(selRect);
    let moved = false;

    function onMove(ev) {
        moved = true;
        const cx = ev.clientX - cRect.left;
        const cy = ev.clientY - cRect.top;
        selRect.style.left = Math.min(sx, cx) + 'px';
        selRect.style.top = Math.min(sy, cy) + 'px';
        selRect.style.width = Math.abs(cx - sx) + 'px';
        selRect.style.height = Math.abs(cy - sy) + 'px';
    }
    function onUp(ev) {
        document.removeEventListener('mousemove', onMove);
        document.removeEventListener('mouseup', onUp);
        selRect.remove();
        if (!moved) {
            if (!ev.ctrlKey) { selectWidget(null); clearWidgetSelection(); }
            return;
        }
        const cx = ev.clientX - cRect.left;
        const cy = ev.clientY - cRect.top;
        const rx = Math.min(sx, cx), ry = Math.min(sy, cy);
        const rw = Math.abs(cx - sx), rh = Math.abs(cy - sy);
        if (!ev.ctrlKey) clearWidgetSelection();
        canvas.querySelectorAll('.canvas-widget').forEach(w => {
            const wx = parseInt(w.style.left) || 0;
            const wy = parseInt(w.style.top) || 0;
            if (wx + w.offsetWidth > rx && wx < rx + rw && wy + w.offsetHeight > ry && wy < ry + rh) {
                selectedWidgetIds.add(w.id);
            }
        });
        updateWidgetSelectionVisual();
        if (selectedWidgetIds.size > 0) {
            const lastId = [...selectedWidgetIds].pop();
            selectWidget(document.getElementById(lastId));
        }
    }
    document.addEventListener('mousemove', onMove);
    document.addEventListener('mouseup', onUp);
});

// 畫布右鍵 → 新增文字選單
canvas.addEventListener('contextmenu', e => {
    if (e.target.closest('.canvas-widget')) return;  // 點到 widget 上不攔截
    e.preventDefault();
    const rect = canvas.getBoundingClientRect();
    const nX = Math.max(0, snapGrid(e.clientX - rect.left));
    const nY = Math.max(0, snapGrid(e.clientY - rect.top));
    showCanvasCtxMenu(e, nX, nY);
});

// ============================================================
// 清除畫布
// ============================================================
function clearCanvas() {
    if (!canvas.hasChildNodes()) return;
    if (confirm(t('designer.canvas.confirm_clear'))) {
        canvas.innerHTML = '';
        selectWidget(null);
        nWidgetCounter = 0;
        // 同步清空目前頁面的 widget 狀態
        const page = findPage(szCurrentPageId);
        if (page) page.arrWidgetState = [];
    }
}

// ============================================================
// 匯入圖片
// ============================================================

// 開啟檔案選擇器
function triggerImportImage() {
    document.getElementById('imgFileInput').click();
}

// 使用者選好檔案後觸發
function onImageFileSelected(input) {
    const file = input.files[0];
    if (!file) return;

    // 重設 input 讓相同檔案可再次選取
    input.value = '';

    const reader = new FileReader();
    reader.onload = function (e) {
        applyCanvasBgImage(e.target.result, file.name);
    };
    reader.readAsDataURL(file);
}

// 將 dataUrl 套用為畫布背景，並依圖片原始尺寸調整畫布
function applyCanvasBgImage(szDataUrl, szFileName) {
    const img = new Image();
    img.onload = function () {
        const nImgW = img.naturalWidth;
        const nImgH = img.naturalHeight;

        // 畫布調整為圖片原始尺寸（最小 400×300）
        const nCanvasW = Math.max(400, nImgW);
        const nCanvasH = Math.max(300, nImgH);
        canvas.style.width  = nCanvasW + 'px';
        canvas.style.height = nCanvasH + 'px';

        // 背景：圖片鋪底，格線疊在最上層形成分割網格
        canvas.style.backgroundImage = [
            'linear-gradient(rgba(0,0,0,0.08) 1px, transparent 1px)',
            'linear-gradient(90deg, rgba(0,0,0,0.08) 1px, transparent 1px)',
            `url('${szDataUrl}')`
        ].join(', ');
        canvas.style.backgroundSize     = '10px 10px, 10px 10px, 100% 100%';
        canvas.style.backgroundPosition = '0 0, 0 0, 0 0';
        canvas.style.backgroundRepeat   = 'repeat, repeat, no-repeat';
        canvas.style.backgroundColor    = '#fff';

        // 追蹤目前背景（供頁面切換儲存用）
        szCurrentBgDataUrl  = szDataUrl;
        szCurrentBgFileName = szFileName;

        // 更新工具列徽章
        document.getElementById('bgImgName').textContent = szFileName;
        document.getElementById('bgImgSize').textContent =
            `${nImgW} × ${nImgH} px`;
        const badge = document.getElementById('bgImgBadge');
        badge.style.display = 'flex';
    };
    img.src = szDataUrl;
}

// 移除背景圖，恢復預設格線白底
function clearBgImage() {
    canvas.style.backgroundImage = [
        'linear-gradient(rgba(0,0,0,0.06) 1px, transparent 1px)',
        'linear-gradient(90deg, rgba(0,0,0,0.06) 1px, transparent 1px)'
    ].join(', ');
    canvas.style.backgroundSize     = '10px 10px';
    canvas.style.backgroundPosition = '0 0';
    canvas.style.backgroundRepeat   = 'repeat';
    canvas.style.backgroundColor    = '#ffffff';
    canvas.style.width  = '1200px';
    canvas.style.height = '800px';

    szCurrentBgDataUrl  = null;
    szCurrentBgFileName = null;
    document.getElementById('bgImgBadge').style.display = 'none';
    document.getElementById('bgImgName').textContent = '';
}

// ============================================================
// 畫布狀態列（hover 顯示點位資訊）
// ============================================================
function showCanvasStatus(szText) {
    const bar = document.getElementById('canvasStatusbar');
    if (!bar || !szText) return;
    bar.innerHTML = '<i class="fas fa-map-pin"></i> ' + escHtml(szText);
    bar.classList.add('visible');
}
function hideCanvasStatus() {
    const bar = document.getElementById('canvasStatusbar');
    if (bar) bar.classList.remove('visible');
}
