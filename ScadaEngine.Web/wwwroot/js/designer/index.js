// ============================================================
// Designer — 入口（initialization + 元件庫收合）
// ============================================================
// 內容：toggleComponentPanel、啟動 renderPageTree + loadPublishedDesign。
// 此檔載入順序為最後，所有 module 已就緒後才執行 init。
// ============================================================

// ============================================================
// 元件庫收合
// ============================================================
function toggleComponentPanel() {
    const panel = document.getElementById('componentPanel');
    const tab   = document.getElementById('compExpandTab');
    const isCollapsed = panel.classList.toggle('collapsed');
    tab.style.display = isCollapsed ? 'flex' : 'none';
}

// ============================================================
// 初始化頁面樹，並嘗試還原已發布設計
// ============================================================
// 等 i18n 字典就緒後再渲染，避免初次顯示時所有動態字串顯示成 key
function _bootDesigner() {
    // 若預設樹仍是初始 '主頁面'、未從 DB 載入過，依當前 culture 覆寫名稱
    if (arrPageTree.length === 1 && arrPageTree[0].szId === 'p1' && arrPageTree[0].szName === '主頁面') {
        arrPageTree[0].szName = t('designer.page.default_name');
    }
    renderPageTree();
    loadPublishedDesign();
}

if (window.i18n && window.i18n.ready) {
    window.i18n.ready(_bootDesigner);
} else {
    _bootDesigner();
}
