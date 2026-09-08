// ============================================================
// scadapage/index.js — 初始化進入點（DOMContentLoaded → initScadaViewer + 輪詢 setInterval 掛載）
// ============================================================
// 純搬移自 scadapage.js（plan 2026-08-06-scadapage-js-split）。
// 不包 IIFE、global scope，與 js/designer/ 拆分同模式；
// 共享狀態集中於 state.js。原始 4-space 縮排刻意保留，
// 以保證 template literal 內容 byte-level 不變（純搬移原則）。
// 本檔必須最後載入。
// ============================================================

    // ── 初始化 ──
    document.addEventListener('DOMContentLoaded', async function () {
        _initScadaLayout();
        await initScadaViewer();
        await _loadAlarmRules();
        await _loadManualControlValues();
        await fetchAndUpdateGauges();
        setInterval(fetchAndUpdateGauges, 1000);
        setInterval(fetchAndUpdateAccumulations, ACC_POLL_MS);
        setInterval(fetchAndUpdateCircuitMetrics, ACC_POLL_MS);
    });

    // ── 從 /Designer/Load 載入已發布設計 ──
    async function initScadaViewer() {
        try {
            var resp   = await fetch('/Designer/Load');
            var result = await resp.json();

            if (!result.hasData || !result.pages || !result.pages.length) {
                document.getElementById('scadaPageTree').innerHTML =
                    '<p style="font-size:11px;color:#6c757d;text-align:center;margin-top:20px;">' + t('scadapage.tree.empty') + '</p>';
                return;
            }

            var nodeMap = {}, sortMap = {};
            result.pages.forEach(function (p) {
                sortMap[p.szPageSid] = p.nSortOrder || 0;
                nodeMap[p.szPageSid] = {
                    szId:           p.szPageSid,
                    szName:         p.szPageName,
                    szIcon:         p.szPageIcon  || null,
                    arrChildren:    [],
                    szBgDataUrl:    p.szBgDataUrl  || null,
                    szBgFileName:   p.szBgFileName || null,
                    nCanvasW:       p.nCanvasW     || 1200,
                    nCanvasH:       p.nCanvasH     || 800,
                    arrWidgetState: p.szWidgetStateJson ? JSON.parse(p.szWidgetStateJson) : []
                };
            });

            var arrRoots = [];
            result.pages.forEach(function (p) {
                var node = nodeMap[p.szPageSid];
                if (p.szParentPageSid && nodeMap[p.szParentPageSid])
                    nodeMap[p.szParentPageSid].arrChildren.push(node);
                else
                    arrRoots.push(node);
            });
            Object.values(nodeMap).forEach(function (n) {
                n.arrChildren.sort(function (a, b) { return (sortMap[a.szId] || 0) - (sortMap[b.szId] || 0); });
            });
            arrRoots.sort(function (a, b) { return (sortMap[a.szId] || 0) - (sortMap[b.szId] || 0); });

            function filterByPerm(nodes) {
                if (window._isAdmin) return nodes;
                return nodes
                    .filter(function (n) { return _canViewPage(n.szId); })
                    .map(function (n) { return Object.assign({}, n, { arrChildren: filterByPerm(n.arrChildren) }); });
            }

            scadaPageTree = filterByPerm(arrRoots);
            renderScadaPageTree();

            if (scadaPageTree.length > 0) selectScadaPage(scadaPageTree[0].szId);

        } catch (err) {
            console.warn('SCADA Viewer \u521d\u59cb\u5316\u5931\u6557\uff1a', err.message);
            document.getElementById('scadaPageTree').innerHTML =
                '<p style="font-size:16px;color:#dc3545;text-align:center;margin-top:20px;">\u8f09\u5165\u5931\u6557</p>';
        }
    }
