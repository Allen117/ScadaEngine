// ============================================================
// Designer — 儲存 / 載入
// ============================================================
// 內容：saveDesign（POST /Designer/Save）、loadPublishedDesign
// （GET /Designer/Load 並重建頁面樹）。
// 依賴：state.js / page-tree.js（saveCurrentPageState / findPage /
// loadPageState）/ ctx-menu.js（showDesignToast）。
// ============================================================

// ============================================================
// 儲存設計至資料庫
// ============================================================
async function saveDesign() {
    // 1. 先把目前畫布狀態同步回 arrPageTree
    saveCurrentPageState();

    // 2. 將遞迴樹展平為陣列（含 szParentPageSid + nSortOrder）
    const arrPages = [];
    function flattenTree(arr, szParentSid) {
        arr.forEach((page, idx) => {
            arrPages.push({
                szPageSid:        page.szId,
                szParentPageSid:  szParentSid,
                nSortOrder:       idx,
                szPageName:       page.szName,
                szPageIcon:       page.szIcon  || null,
                nCanvasW:         page.nCanvasW || 1200,
                nCanvasH:         page.nCanvasH || 800,
                szBgFileName:     page.szBgFileName  || null,
                szBgDataUrl:      page.szBgDataUrl   || null,
                szWidgetStateJson: (page.arrWidgetState && page.arrWidgetState.length)
                                    ? JSON.stringify(page.arrWidgetState)
                                    : null
            });
            if (page.arrChildren && page.arrChildren.length)
                flattenTree(page.arrChildren, page.szId);
        });
    }
    flattenTree(arrPageTree, null);

    // 3. POST 至後端
    const btnSave = document.getElementById('btnSave');
    btnSave.disabled = true;
    btnSave.innerHTML = '<i class="fas fa-spinner fa-spin me-1"></i>' + escHtml(t('designer.toolbar.saving'));

    try {
        const resp = await fetch('/Designer/Save', {
            method:  'POST',
            headers: { 'Content-Type': 'application/json' },
            // szName 留空，Controller 會依 culture 套上 designer.untitled_design
            body:    JSON.stringify({ szName: '', pages: arrPages })
        });
        const result = await resp.json();
        if (result.success) {
            showDesignToast('success', '<i class="fas fa-check-circle me-1"></i>' + escHtml(t('designer.io.saved_success')));
        } else {
            showDesignToast('danger', '<i class="fas fa-exclamation-circle me-1"></i>' + escHtml(t('designer.io.save_failed', { error: result.error || t('designer.io.unknown_error') })));
        }
    } catch (err) {
        showDesignToast('danger', '<i class="fas fa-exclamation-circle me-1"></i>' + escHtml(t('designer.io.network_error', { error: err.message })));
    } finally {
        btnSave.disabled = false;
        btnSave.innerHTML = '<i class="fas fa-save me-1"></i>' + escHtml(t('designer.toolbar.save'));
    }
}

// ============================================================
// 啟動時從資料庫還原已發布設計
// ============================================================
async function loadPublishedDesign() {
    try {
        const resp = await fetch('/Designer/Load');
        const result = await resp.json();
        if (!result.hasData || !result.pages || !result.pages.length) return;

        // 建立節點對照表
        const nodeMap = {};
        const sortMap = {};
        result.pages.forEach(p => {
            sortMap[p.szPageSid] = p.nSortOrder ?? 0;
            nodeMap[p.szPageSid] = {
                szId:           p.szPageSid,
                szName:         p.szPageName,
                szIcon:         p.szPageIcon  || null,
                arrChildren:    [],
                szBgDataUrl:    p.szBgDataUrl  || null,
                szBgFileName:   p.szBgFileName || null,
                nCanvasW:       p.nCanvasW     || 1200,
                nCanvasH:       p.nCanvasH     || 800,
                arrWidgetState: p.szWidgetStateJson
                                    ? JSON.parse(p.szWidgetStateJson)
                                    : []
            };
        });

        // 重建樹狀結構（根節點另行收集）
        const arrRoots = [];
        result.pages.forEach(p => {
            const node = nodeMap[p.szPageSid];
            if (p.szParentPageSid && nodeMap[p.szParentPageSid]) {
                nodeMap[p.szParentPageSid].arrChildren.push(node);
            } else {
                arrRoots.push(node);
            }
        });

        // 依 nSortOrder 排序各層子節點
        Object.values(nodeMap).forEach(node => {
            node.arrChildren.sort((a, b) => (sortMap[a.szId] || 0) - (sortMap[b.szId] || 0));
        });
        arrRoots.sort((a, b) => (sortMap[a.szId] || 0) - (sortMap[b.szId] || 0));

        // 更新全域計數器，避免新增頁面時 ID 衝突
        let nMax = 0;
        result.pages.forEach(p => {
            const m = p.szPageSid.match(/^p(\d+)/);
            if (m) nMax = Math.max(nMax, parseInt(m[1]));
        });
        nPageIdCounter = nMax;

        // 取代預設頁面樹並導航至第一根頁面
        arrPageTree.length = 0;
        arrRoots.forEach(r => arrPageTree.push(r));
        szCurrentPageId = arrPageTree[0].szId;
        renderPageTree();
        loadPageState(arrPageTree[0]);
        await loadAlarmRuleSids();
    } catch (err) {
        console.warn(t('designer.io.load_failed', { error: err.message }));
    }
}
