// LogicFlow 入口：組裝 window._lf 對外 API + 觸發初始化
// 載入順序由 Index.cshtml 控制：state → algo → eval → render → canvas → tree → picker → index
(function () {
    const S = window.__lfNS;

    // 對外 API（供 Razor View 的 inline onclick 呼叫）
    // 簽名須與拆檔前 logicflow.js 末端 window._lf 一致
    window._lf = {
        select: S.select,
        toggle: S.toggle,
        addChild: S.addChild,
        addRoot: S.addRoot,
        rename: S.rename,
        confirmRename: S.confirmRename,
        remove: S.remove,
        toggleEnabled: S.toggleEnabled,
        saveDiagram: S.saveDiagram,
        ppGoBack: S.ppGoBack,
        ppFilter: S.ppFilter,
        ppSelectDev: S.ppSelectDev,
        ppSelectPoint: S.ppSelectPoint,
        ppConfirm: S.ppConfirm,
        ppSwitchSource: S.ppSwitchSource,
        ppSelectSchedule: S.ppSelectSchedule,
        ppShowDeviceStep: S.ppShowDeviceStep,
        ppShowCalcStep: S.ppShowCalcStep,
        ppBackToStep0: S.ppBackToStep0,
        ppSelectCalcGroup: S.ppSelectCalcGroup,
        ppShowDbStep: S.ppShowDbStep,
        ppSelectDbCoordinator: S.ppSelectDbCoordinator,
        alignLeft: S.alignLeft,
        alignRight: S.alignRight,
        alignTop: S.alignTop,
        alignBottom: S.alignBottom,
        alignCenterH: S.alignCenterH,
        alignCenterV: S.alignCenterV,
        distributeHorizontally: S.distributeHorizontally,
        distributeVertically: S.distributeVertically
    };

    // 一次性綁定全域事件（keydown / 右鍵選單項目 / 全域點擊關選單）
    S.attachGlobalEvents();

    // 載入演算法清單（非同步，buildAlgoSubmenu 在拉到後自行觸發）
    S.loadAlgorithms();

    // 等 i18n 字典就緒後再 loadTree，以免初始樹節點/節點 label 顯示 i18n key
    if (window.i18n && window.i18n.ready) {
        window.i18n.ready(S.loadTree);
    } else {
        S.loadTree();
    }
})();
