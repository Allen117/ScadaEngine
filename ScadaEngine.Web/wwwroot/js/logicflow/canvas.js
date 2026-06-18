// LogicFlow Canvas 互動：context menu、拖曳、resize、複製貼上、變更節點類型、儲存
(function () {
    const S = window.__lfNS;

    // 依埠位數量計算演算法節點建議高度（埠分佈於節點 20%~80% 區間）
    // — 確保相鄰埠中心距至少 PORT_MIN_GAP_PX，避免 N 大時擠在一起
    function computeAlgoNodeHeight(node) {
        if (!node || node.type !== 'algorithm' || !node.operator) return null;
        const aop = S.ALGO_OPS[node.operator];
        if (!aop) return null;
        const ports = S.getAlgoPorts(aop, node.inputCount);
        const maxPorts = Math.max(ports.inputs.length, ports.outputs.length, 1);
        const PORT_MIN_GAP_PX = 30;
        const BASE_H = 50;
        if (maxPorts <= 1) return BASE_H;
        return Math.max(BASE_H, Math.ceil(PORT_MIN_GAP_PX * (maxPorts - 1) / 0.6));
    }

    async function initCanvas(treeId) {
        // 清理舊 timer 的 setTimeout，避免殘留排程
        for (const nd of S.canvasNodes) {
            if (nd._tpTimeout) clearTimeout(nd._tpTimeout);
        }
        S.currentTreeId = treeId;
        S.canvasNodes = [];
        S.canvasEdges = [];
        S.nextNodeId = 1;
        S.nextEdgeId = 1;
        S.selectedEdgeId = null;
        S.draggingEdge = null;
        S.selectedNodeIds = new Set();
        S._outputPrevState = {};

        // 載入已存的 DiagramJson
        S.diagramVersion = 0;
        try {
            const data = await S.apiFetch(`/diagram/${treeId}`);
            if (data) {
                S.diagramVersion = data.version || 0;
                if (data.diagramJson) {
                    const parsed = JSON.parse(data.diagramJson);
                    if (parsed.nodes && Array.isArray(parsed.nodes)) {
                        S.canvasNodes = parsed.nodes;
                        S.nextNodeId = S.canvasNodes.reduce((max, n) => Math.max(max, n.id + 1), 1);
                        // 舊版 timer 遷移：timerSeconds → timerDelay，無 operator 預設 tp
                        for (const nd of S.canvasNodes) {
                            if (nd.type === 'timer') {
                                if (nd.timerDelay == null && nd.timerSeconds != null) {
                                    nd.timerDelay = nd.timerSeconds;
                                    delete nd.timerSeconds;
                                }
                                if (nd.timerDelay == null) nd.timerDelay = 5;
                                if (!nd.operator || !S.TIMER_OPS[nd.operator]) nd.operator = 'tp';
                                if (nd.operator === 'tp' && nd.timerHold == null) nd.timerHold = 2;
                                // TOF 遷移為 TON
                                if (nd.operator === 'tof') nd.operator = 'ton';
                            }
                        }
                    }
                    if (parsed.edges && Array.isArray(parsed.edges)) {
                        S.canvasEdges = parsed.edges;
                        S.nextEdgeId = S.canvasEdges.reduce((max, e) => Math.max(max, e.id + 1), 1);
                    }
                    // 舊版 math 遷移：mathValue → 常數節點 + val 連線
                    for (const nd of [...S.canvasNodes]) {
                        if (nd.type === 'math' && nd.mathValue != null && nd.operator && S.MATH_OPS[nd.operator] && S.MATH_OPS[nd.operator].hasValue) {
                            const cNode = { id: S.nextNodeId++, type: 'constant', x: nd.x, y: nd.y + 80, constValue: nd.mathValue };
                            S.canvasNodes.push(cNode);
                            S.canvasEdges.push({ id: S.nextEdgeId++, source: cNode.id, sourcePort: 'out', target: nd.id, targetPort: 'val' });
                            delete nd.mathValue;
                        }
                    }
                }
            }
        } catch (e) { /* 尚無 diagram 資料，空白畫布 */ }

        // 從 Engine 同步 TP 計時器狀態（避免每次開頁面倒數重算）
        await S.syncTimerStateFromEngine(treeId);

        // 顯示儲存按鈕
        const btnSave = document.getElementById('btnSaveDiagram');
        if (btnSave) btnSave.style.display = '';

        // 畫布有排程接點時，預先載入排程資料（否則 evalScheduleNow 會因 ppAllSchedules===null 回傳 null）
        if (S.canvasNodes.some(n => (n.type === 'contact_no' || n.type === 'contact_nc') && n.scheduleId != null)) {
            S.ppAllSchedules = null;
            try { await S.ppEnsureSchedules(); } catch (_) {}
        }

        S.renderCanvasNodes();
        bindCanvasEvents();
        S.startRealtimePolling();
        if (S.updateAlignToolbarState) S.updateAlignToolbarState();
    }

    function bindCanvasEvents() {
        const canvas = document.getElementById('diagramCanvas');
        if (!canvas) return;

        // 右鍵選單
        canvas.addEventListener('contextmenu', (e) => {
            e.preventDefault();
            hideCtxMenu();

            const nodeEl = e.target.closest('.flow-node');
            if (nodeEl) {
                // 在節點上右鍵 → 顯示節點選單
                S.nodeCtxTargetId = parseInt(nodeEl.dataset.nodeId);
                const node = S.canvasNodes.find(n => n.id === S.nodeCtxTargetId);
                if (!node) return;

                // 標記目前類型（灰掉同類型選項）
                const menu = document.getElementById('nodeCtxMenu');
                menu.querySelectorAll('.node-change-type').forEach(item => {
                    item.style.opacity = item.dataset.type === node.type ? '.4' : '';
                    item.style.pointerEvents = item.dataset.type === node.type ? 'none' : '';
                });

                // 顯示/隱藏「清除綁定」：僅接點類型且已綁定 SID 或排程時顯示
                const clearBtn = menu.querySelector('.node-clear-binding');
                if (clearBtn) {
                    const isContact = node.type === 'contact_no' || node.type === 'contact_nc';
                    const hasBind = isContact && (node.sid || node.scheduleId != null);
                    clearBtn.style.display = hasBind ? '' : 'none';
                }

                showMenuAt(menu, e.clientX, e.clientY);
            } else {
                // 在空白處右鍵 → 顯示新增節點選單
                const rect = canvas.getBoundingClientRect();
                S.ctxPos = { x: e.clientX - rect.left + canvas.scrollLeft, y: e.clientY - rect.top + canvas.scrollTop };
                showMenuAt(document.getElementById('ctxMenu'), e.clientX, e.clientY);
            }
        });

        // 點擊畫布空白處：關閉選單 / 取消選取 / 框選
        canvas.addEventListener('mousedown', (e) => {
            if (e.target.closest('.flow-node') || e.target.closest('.flow-port')) return;
            hideCtxMenu();
            if (S.selectedEdgeId != null) { S.selectedEdgeId = null; S.renderEdges(); }
            if (e.button !== 0) return;

            const cRect = canvas.getBoundingClientRect();
            const sx = e.clientX - cRect.left + canvas.scrollLeft;
            const sy = e.clientY - cRect.top + canvas.scrollTop;
            const selRect = document.createElement('div');
            selRect.className = 'selection-rect';
            canvas.appendChild(selRect);
            let moved = false;

            function onMove(ev) {
                moved = true;
                const cx = ev.clientX - cRect.left + canvas.scrollLeft;
                const cy = ev.clientY - cRect.top + canvas.scrollTop;
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
                    if (!ev.ctrlKey) { S.selectedNodeIds.clear(); updateNodeSelectionVisual(); }
                    return;
                }
                const cx = ev.clientX - cRect.left + canvas.scrollLeft;
                const cy = ev.clientY - cRect.top + canvas.scrollTop;
                const rx = Math.min(sx, cx), ry = Math.min(sy, cy);
                const rw = Math.abs(cx - sx), rh = Math.abs(cy - sy);
                if (!ev.ctrlKey) S.selectedNodeIds.clear();
                for (const n of S.canvasNodes) {
                    const el = canvas.querySelector(`.flow-node[data-node-id="${n.id}"]`);
                    if (!el) continue;
                    if (n.x + el.offsetWidth > rx && n.x < rx + rw && n.y + el.offsetHeight > ry && n.y < ry + rh) {
                        S.selectedNodeIds.add(n.id);
                    }
                }
                updateNodeSelectionVisual();
            }
            document.addEventListener('mousemove', onMove);
            document.addEventListener('mouseup', onUp);
        });
    }

    function hideCtxMenu() {
        document.getElementById('ctxMenu').style.display = 'none';
        document.getElementById('nodeCtxMenu').style.display = 'none';
    }

    function showMenuAt(menu, x, y) {
        menu.style.left = x + 'px';
        menu.style.top = y + 'px';
        menu.style.display = 'block';
        requestAnimationFrame(() => {
            const mr = menu.getBoundingClientRect();
            if (mr.right > window.innerWidth) menu.style.left = (x - mr.width) + 'px';
            if (mr.bottom > window.innerHeight) menu.style.top = (y - mr.height) + 'px';
        });
    }

    function deleteCanvasNode(nodeId) {
        S.canvasNodes = S.canvasNodes.filter(n => n.id !== nodeId);
        S.canvasEdges = S.canvasEdges.filter(e => e.source !== nodeId && e.target !== nodeId);
        if (S.selectedEdgeId != null && !S.canvasEdges.some(e => e.id === S.selectedEdgeId)) S.selectedEdgeId = null;
        S.renderCanvasNodes();
    }

    function changeNodeType(nodeId, newType, operator) {
        const node = S.canvasNodes.find(n => n.id === nodeId);
        if (!node) return;
        // 同類型同運算子 → 不處理
        if (node.type === newType && (!operator || node.operator === operator)) return;
        const newMeta = S.NODE_META[newType] || { inputs: [], outputs: [] };

        // 清除不存在的 port 對應連線
        const keepValPort = newType === 'math' && operator && S.MATH_OPS[operator] && S.MATH_OPS[operator].hasValue;
        const keepTimerPorts = newType === 'timer';
        const isTon = newType === 'timer' && operator === 'ton';
        // algorithm 切換：依新演算法 + 既有 inputCount 展開埠（variadic 沿用 node.inputCount，不存在則 1）
        let algoInputPortKeys = [];
        let algoOutputPortKeys = [];
        if (newType === 'algorithm' && operator && S.ALGO_OPS[operator]) {
            const newAop = S.ALGO_OPS[operator];
            const newN = newAop.variadic ? (node.inputCount || 1) : null;
            const newPorts = S.getAlgoPorts(newAop, newN);
            algoInputPortKeys = newPorts.inputs.map(p => p.key);
            algoOutputPortKeys = newPorts.outputs.map(p => p.key);
        }
        S.canvasEdges = S.canvasEdges.filter(e => {
            if (e.source === nodeId) {
                if (newType === 'algorithm') {
                    if (!algoOutputPortKeys.includes(e.sourcePort)) return false;
                } else if (!newMeta.outputs.includes(e.sourcePort)) return false;
            }
            if (e.target === nodeId && !newMeta.inputs.includes(e.targetPort)) {
                if (e.targetPort === 'val' && keepValPort) return true;
                if (e.targetPort === 'delay' && keepTimerPorts) return true;
                if (e.targetPort === 'hold' && keepTimerPorts && !isTon) return true;
                if (newType === 'algorithm' && algoInputPortKeys.includes(e.targetPort)) return true;
                return false;
            }
            return true;
        });
        if (S.selectedEdgeId != null && !S.canvasEdges.some(e => e.id === S.selectedEdgeId)) S.selectedEdgeId = null;

        // 點位類型 → 非點位類型：移除 sid/pointName
        const POINT_TYPES = ['input', 'output', 'contact_no', 'contact_nc'];
        if (POINT_TYPES.includes(node.type) && !POINT_TYPES.includes(newType)) {
            delete node.sid;
            delete node.pointName;
            delete node.unit;
        }

        // 設定 operator
        if (newType === 'compare' && operator) {
            node.operator = operator;
        } else if (newType === 'math' && operator) {
            node.operator = operator;
        } else if (newType === 'timer' && operator) {
            node.operator = operator;
        } else if (newType === 'algorithm' && operator) {
            node.operator = operator;
            const algo = S.ALGO_OPS[operator];
            if (algo) {
                if (algo.variadic) {
                    if (node.inputCount == null) node.inputCount = 1;
                    const ports = S.getAlgoPorts(algo, node.inputCount);
                    node.algoInputs = ports.inputs.map(p => p.key);
                } else {
                    delete node.inputCount;
                    node.algoInputs = [...algo.inputs];
                }
            }
            // 切換為演算法 → 依埠數重算高度（覆蓋舊節點高度）
            const algoH2 = computeAlgoNodeHeight(node);
            if (algoH2 != null) node.height = algoH2;
        } else if (newType !== 'compare' && newType !== 'math' && newType !== 'timer' && newType !== 'algorithm') {
            delete node.operator;
        }

        // algorithm 類型管理
        if (newType !== 'algorithm') {
            delete node.algoInputs;
            delete node.inputCount;
        }

        // constant 類型管理
        if (newType === 'constant') {
            if (node.constValue == null) node.constValue = 0;
        } else {
            delete node.constValue;
        }

        // counter 類型管理
        if (newType === 'counter') {
            if (node.presetValue == null) node.presetValue = 10;
            if (node.cuMinIntervalMs == null) node.cuMinIntervalMs = 60000;
            node._counterValue = 0;
            node._counterPrevCu = null;
            node._counterLastEdgeAt = 0;
            node._counterQ = 0;
        } else {
            delete node.presetValue;
            delete node.cuMinIntervalMs;
            delete node._counterValue;
            delete node._counterPrevCu;
            delete node._counterLastEdgeAt;
            delete node._counterQ;
        }

        // timer 類型管理
        if (newType === 'timer') {
            if (node.timerDelay == null) node.timerDelay = 5;
            if (!node.operator || !S.TIMER_OPS[node.operator]) node.operator = 'tp';
            if (node.operator === 'tp' && node.timerHold == null) node.timerHold = 2;
            // TON/TPR 不需要 hold → 移除 hold 連線
            if (node.operator === 'ton' || node.operator === 'tpr') {
                S.canvasEdges = S.canvasEdges.filter(e => !(e.target === node.id && e.targetPort === 'hold'));
            }
            node._timerStartTime = null;
            node._timerResult = null;
            node._tpPhase = null;
            node._tonPhase = null;
            node._tonPhaseEnd = null;
            clearTimeout(node._tpTimeout);
        } else {
            delete node.timerDelay;
            delete node.timerHold;
        }

        node.type = newType;
        S.renderCanvasNodes();
    }

    function addNodeToCanvas(type, operator) {
        hideCtxMenu();
        // 對齊到 20px 網格
        const x = Math.round(S.ctxPos.x / 20) * 20;
        const y = Math.round(S.ctxPos.y / 20) * 20;

        // 讀取/寫入點位 → 先彈出點位選擇器
        if (type === 'input' || type === 'output') {
            S.ppEditNodeId = null;
            S.ppPendingType = type;
            S.ppPendingPos = { x, y };
            S.openPointPicker();
            return;
        }

        // A/B 接點 → 直接建立空白節點（可稍後雙擊設定點位/排程，或用 ctrl 埠控制）
        if (type === 'contact_no' || type === 'contact_nc') {
            S.canvasNodes.push({ id: S.nextNodeId++, type, x, y });
            S.renderCanvasNodes();
            return;
        }

        const node = { id: S.nextNodeId++, type, x, y };
        if (type === 'constant') node.constValue = 0;
        if (type === 'compare' && operator) node.operator = operator;
        if (type === 'math' && operator) {
            node.operator = operator;
        }
        if (type === 'timer') {
            node.operator = operator || 'tp';
            node.timerDelay = 5;
            if (node.operator === 'tp') node.timerHold = 2;
        }
        if (type === 'counter') {
            node.presetValue = 10;
            node.cuMinIntervalMs = 60000;
        }
        if (type === 'algorithm' && operator) {
            node.operator = operator;
            const algo = S.ALGO_OPS[operator];
            if (algo) {
                if (algo.variadic) {
                    node.inputCount = 1;
                    const ports = S.getAlgoPorts(algo, 1);
                    node.algoInputs = ports.inputs.map(p => p.key);
                } else {
                    node.algoInputs = [...algo.inputs];
                }
            }
            const algoH = computeAlgoNodeHeight(node);
            if (algoH != null) node.height = algoH;
        }
        S.canvasNodes.push(node);
        S.renderCanvasNodes();
    }

    // 拖曳節點（支援群組拖曳 + Ctrl 切換選取）
    function startDrag(e) {
        if (e.button !== 0) return;
        const el = e.currentTarget;
        const nodeId = parseInt(el.dataset.nodeId);
        const node = S.canvasNodes.find(n => n.id === nodeId);
        if (!node) return;

        // Ctrl+click → 切換選取，不拖曳
        if (e.ctrlKey) {
            if (S.selectedNodeIds.has(nodeId)) S.selectedNodeIds.delete(nodeId);
            else S.selectedNodeIds.add(nodeId);
            updateNodeSelectionVisual();
            e.preventDefault();
            return;
        }

        // 點擊未選取的節點 → 清除舊選取，只選此節點
        if (!S.selectedNodeIds.has(nodeId)) {
            S.selectedNodeIds.clear();
            S.selectedNodeIds.add(nodeId);
            updateNodeSelectionVisual();
        }

        const startX = e.clientX, startY = e.clientY;
        // 記錄所有已選取節點的原始位置
        const origPos = {};
        for (const sid of S.selectedNodeIds) {
            const sn = S.canvasNodes.find(n => n.id === sid);
            if (sn) origPos[sid] = { x: sn.x, y: sn.y };
        }
        el.style.cursor = 'grabbing';

        function onMove(ev) {
            const dx = ev.clientX - startX;
            const dy = ev.clientY - startY;
            for (const sid of S.selectedNodeIds) {
                const sn = S.canvasNodes.find(n => n.id === sid);
                const orig = origPos[sid];
                if (!sn || !orig) continue;
                sn.x = Math.max(0, Math.round((orig.x + dx) / 20) * 20);
                sn.y = Math.max(0, Math.round((orig.y + dy) / 20) * 20);
                const sEl = document.querySelector(`.flow-node[data-node-id="${sid}"]`);
                if (sEl) { sEl.style.left = sn.x + 'px'; sEl.style.top = sn.y + 'px'; }
            }
            S.updateCanvasSize();
            S.renderEdges();
        }
        function onUp() {
            document.removeEventListener('mousemove', onMove);
            document.removeEventListener('mouseup', onUp);
            el.style.cursor = 'grab';
            S.updateCanvasSize();
        }
        document.addEventListener('mousemove', onMove);
        document.addEventListener('mouseup', onUp);
    }

    // 拖動 resize handle → 動態調整節點高度（不觸發 startDrag）
    function onResizeHandleMouseDown(e, nodeId) {
        e.stopPropagation();
        if (e.button !== 0) return;
        const canvas = document.getElementById('diagramCanvas');
        const el = canvas && canvas.querySelector(`.flow-node[data-node-id="${nodeId}"]`);
        const node = S.canvasNodes.find(n => n.id === nodeId);
        if (!el || !node) return;
        const startY = e.clientY;
        const startH = el.offsetHeight;
        const MIN_H = 40;
        function onMove(ev) {
            const newH = Math.max(MIN_H, startH + (ev.clientY - startY));
            el.style.height = newH + 'px';
            node.height = newH;
            S.renderEdges();
        }
        function onUp() {
            document.removeEventListener('mousemove', onMove);
            document.removeEventListener('mouseup', onUp);
        }
        document.addEventListener('mousemove', onMove);
        document.addEventListener('mouseup', onUp);
        e.preventDefault();
    }

    // 更新節點選取外觀（不做完整 re-render）
    function updateNodeSelectionVisual() {
        const canvas = document.getElementById('diagramCanvas');
        if (!canvas) return;
        canvas.querySelectorAll('.flow-node').forEach(el => {
            el.classList.toggle('node-selected', S.selectedNodeIds.has(parseInt(el.dataset.nodeId)));
        });
        if (S.updateAlignToolbarState) S.updateAlignToolbarState();
    }

    // 複製選取的節點 + 內部連線 → localStorage（支援跨邏輯貼上）
    function copySelectedNodes() {
        if (S.selectedNodeIds.size === 0) return;
        const nodes = S.canvasNodes.filter(n => S.selectedNodeIds.has(n.id)).map(n => {
            const o = { id: n.id, type: n.type, x: n.x, y: n.y };
            if (n.operator) o.operator = n.operator;
            if (n.sid) o.sid = n.sid;
            if (n.pointName) o.pointName = n.pointName;
            if (n.unit != null) o.unit = n.unit;
            if (n.constValue != null) o.constValue = n.constValue;
            if (n.timerDelay != null) o.timerDelay = n.timerDelay;
            if (n.timerHold != null) o.timerHold = n.timerHold;
            if (n.fMin != null) o.fMin = n.fMin;
            if (n.fMax != null) o.fMax = n.fMax;
            if (n.scheduleId != null) o.scheduleId = n.scheduleId;
            if (n.scheduleName) o.scheduleName = n.scheduleName;
            if (n.algoInputs) o.algoInputs = n.algoInputs;
            if (n.inputCount != null) o.inputCount = n.inputCount;
            if (n.height != null) o.height = n.height;
            return o;
        });
        const edges = S.canvasEdges.filter(e => S.selectedNodeIds.has(e.source) && S.selectedNodeIds.has(e.target));
        localStorage.setItem('_lf_clipboard', JSON.stringify({ nodes, edges }));
    }

    // 貼上節點：從 localStorage 讀取，重新分配 ID，置於可視區域中央
    function pasteNodes() {
        const canvas = document.getElementById('diagramCanvas');
        if (!canvas) return;
        const raw = localStorage.getItem('_lf_clipboard');
        if (!raw) return;
        let data;
        try { data = JSON.parse(raw); } catch { return; }
        if (!data.nodes || data.nodes.length === 0) return;

        const minX = Math.min(...data.nodes.map(n => n.x));
        const minY = Math.min(...data.nodes.map(n => n.y));
        const maxX = Math.max(...data.nodes.map(n => n.x));
        const maxY = Math.max(...data.nodes.map(n => n.y));
        const vx = canvas.scrollLeft + canvas.clientWidth / 2;
        const vy = canvas.scrollTop + canvas.clientHeight / 2;
        const ox = Math.round((vx - (minX + maxX) / 2) / 20) * 20;
        const oy = Math.round((vy - (minY + maxY) / 2) / 20) * 20;

        const idMap = {};
        const newNodes = [];
        for (const n of data.nodes) {
            const nid = S.nextNodeId++;
            idMap[n.id] = nid;
            const c = { ...n, id: nid, x: Math.max(0, n.x + ox), y: Math.max(0, n.y + oy) };
            Object.keys(c).forEach(k => { if (k.startsWith('_')) delete c[k]; });
            newNodes.push(c);
        }
        const newEdges = (data.edges || [])
            .filter(e => idMap[e.source] != null && idMap[e.target] != null)
            .map(e => ({ id: S.nextEdgeId++, source: idMap[e.source], sourcePort: e.sourcePort, target: idMap[e.target], targetPort: e.targetPort }));

        S.canvasNodes.push(...newNodes);
        S.canvasEdges.push(...newEdges);
        S.selectedNodeIds.clear();
        newNodes.forEach(n => S.selectedNodeIds.add(n.id));
        S.renderCanvasNodes();
    }

    // 刪除所有選取的節點及其相關連線
    function deleteSelectedNodes() {
        if (S.selectedNodeIds.size === 0) return;
        S.canvasNodes = S.canvasNodes.filter(n => !S.selectedNodeIds.has(n.id));
        S.canvasEdges = S.canvasEdges.filter(e => !S.selectedNodeIds.has(e.source) && !S.selectedNodeIds.has(e.target));
        if (S.selectedEdgeId != null && !S.canvasEdges.some(e => e.id === S.selectedEdgeId)) S.selectedEdgeId = null;
        S.selectedNodeIds.clear();
        S.renderCanvasNodes();
    }

    // =========== 儲存流程圖 ===========
    async function saveDiagram() {
        if (!S.currentTreeId) return;
        const btn = document.getElementById('btnSaveDiagram');
        if (btn) { btn.disabled = true; btn.innerHTML = '<i class="fas fa-spinner fa-spin me-1"></i>' + S.escHtml(S.t('logicflow.btn.saving')); }
        try {
            // 只保留需要持久化的欄位，過濾掉執行時暫存資料
            const cleanNodes = S.canvasNodes.map(n => {
                const o = { id: n.id, type: n.type, x: n.x, y: n.y };
                if (n.operator)     o.operator = n.operator;
                if (n.sid)          o.sid = n.sid;
                if (n.pointName)    o.pointName = n.pointName;
                if (n.unit != null) o.unit = n.unit;
                if (n.constValue != null)  o.constValue = n.constValue;
                if (n.timerDelay != null) o.timerDelay = n.timerDelay;
                if (n.timerHold != null) o.timerHold = n.timerHold;
                if (n.fMin != null) o.fMin = n.fMin;
                if (n.fMax != null) o.fMax = n.fMax;
                if (n.scheduleId != null) o.scheduleId = n.scheduleId;
                if (n.scheduleName) o.scheduleName = n.scheduleName;
                if (n.algoInputs)  o.algoInputs = n.algoInputs;
                if (n.inputCount != null) o.inputCount = n.inputCount;
                if (n.height != null) o.height = n.height;
                if (n.presetValue != null) o.presetValue = n.presetValue;
                if (n.cuMinIntervalMs != null) o.cuMinIntervalMs = n.cuMinIntervalMs;
                return o;
            });
            const json = JSON.stringify({ nodes: cleanNodes, edges: S.canvasEdges });
            await S.apiFetch(`/diagram/${S.currentTreeId}`, {
                method: 'PUT',
                body: JSON.stringify({ diagramJson: json, version: S.diagramVersion })
            });
            S.diagramVersion++;
            alert(S.t('logicflow.success.save'));
        } catch (e) {
            if (e.message && (e.message.includes('版本衝突') || e.message.toLowerCase().includes('version conflict'))) {
                alert(S.t('logicflow.error.version_conflict'));
                await initCanvas(S.currentTreeId);
            } else {
                alert(S.t('logicflow.error.save_failed', { msg: e.message }));
            }
        } finally {
            if (btn) { btn.disabled = false; btn.innerHTML = '<i class="fas fa-save me-1"></i>' + S.escHtml(S.t('logicflow.btn.save')); }
        }
    }

    // ── 全域事件監聽（一次性綁定，由 index.js 觸發 attachGlobalEvents） ──
    function attachGlobalEvents() {
        // 全域關閉右鍵選單
        document.addEventListener('click', (e) => {
            if (!e.target.closest('#ctxMenu') && !e.target.closest('#nodeCtxMenu')) hideCtxMenu();
        });

        document.addEventListener('keydown', (e) => {
            const isInput = document.activeElement && (document.activeElement.tagName === 'INPUT' || document.activeElement.tagName === 'TEXTAREA');
            const hasCanvas = !!document.getElementById('diagramCanvas');

            if (e.key === 'Escape') {
                hideCtxMenu();
                if (S.selectedEdgeId != null) { S.selectedEdgeId = null; S.renderEdges(); }
                if (S.selectedNodeIds.size > 0) { S.selectedNodeIds.clear(); updateNodeSelectionVisual(); }
            }
            if (e.key === 'Delete' && !isInput) {
                if (S.selectedNodeIds.size > 0) deleteSelectedNodes();
                else if (S.selectedEdgeId != null) S.deleteSelectedEdge();
            }
            if (!isInput && hasCanvas && (e.ctrlKey || e.metaKey)) {
                if (e.key === 'a') { e.preventDefault(); S.canvasNodes.forEach(n => S.selectedNodeIds.add(n.id)); updateNodeSelectionVisual(); }
                if (e.key === 'c' && S.selectedNodeIds.size > 0) { e.preventDefault(); copySelectedNodes(); }
                if (e.key === 'x' && S.selectedNodeIds.size > 0) { e.preventDefault(); copySelectedNodes(); deleteSelectedNodes(); }
                if (e.key === 'v') { e.preventDefault(); pasteNodes(); }
            }
        });

        // 右鍵選單項目點擊（新增節點）
        document.querySelectorAll('#ctxMenu .ctx-menu-item[data-type]').forEach(item => {
            if (item.closest('.ctx-has-sub') && item.classList.contains('ctx-has-sub')) return;
            item.addEventListener('click', (e) => {
                e.stopPropagation();
                addNodeToCanvas(item.dataset.type, item.dataset.operator || null);
            });
        });

        // 節點右鍵選單 — 變更類型
        document.querySelectorAll('#nodeCtxMenu .node-change-type').forEach(item => {
            item.addEventListener('click', (e) => {
                e.stopPropagation();
                hideCtxMenu();
                if (S.nodeCtxTargetId != null) changeNodeType(S.nodeCtxTargetId, item.dataset.type, item.dataset.operator || null);
            });
        });

        // 節點右鍵選單 — 刪除節點
        document.querySelector('#nodeCtxMenu .node-delete-item').addEventListener('click', () => {
            hideCtxMenu();
            if (S.nodeCtxTargetId != null) deleteCanvasNode(S.nodeCtxTargetId);
        });

        // 清除綁定：移除接點的 SID/排程，恢復為空白接點
        document.querySelector('#nodeCtxMenu .node-clear-binding').addEventListener('click', () => {
            hideCtxMenu();
            if (S.nodeCtxTargetId == null) return;
            var nd = S.canvasNodes.find(function(n) { return n.id === S.nodeCtxTargetId; });
            if (!nd) return;
            delete nd.sid;
            delete nd.pointName;
            delete nd.unit;
            delete nd.scheduleId;
            delete nd.scheduleName;
            S.renderCanvasNodes();
        });
    }

    // 暴露給其他模組
    S.computeAlgoNodeHeight = computeAlgoNodeHeight;
    S.initCanvas = initCanvas;
    S.bindCanvasEvents = bindCanvasEvents;
    S.hideCtxMenu = hideCtxMenu;
    S.showMenuAt = showMenuAt;
    S.deleteCanvasNode = deleteCanvasNode;
    S.changeNodeType = changeNodeType;
    S.addNodeToCanvas = addNodeToCanvas;
    S.startDrag = startDrag;
    S.onResizeHandleMouseDown = onResizeHandleMouseDown;
    S.updateNodeSelectionVisual = updateNodeSelectionVisual;
    S.copySelectedNodes = copySelectedNodes;
    S.pasteNodes = pasteNodes;
    S.deleteSelectedNodes = deleteSelectedNodes;
    S.saveDiagram = saveDiagram;
    S.attachGlobalEvents = attachGlobalEvents;
})();
