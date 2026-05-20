(function () {
    const API = '/LogicFlow/api';

    // =========== 資料模型 ===========
    let flatNodes = [];         // DB 回傳的平坦清單
    let treeData = [];          // 組裝後的巢狀結構
    let expandedSet = new Set(); // 記住展開狀態（前端記憶）
    let selectedId = null;
    let renameTargetId = null;

    // =========== API 工具 ===========
    async function apiFetch(url, opts) {
        const res = await fetch(API + url, {
            headers: { 'Content-Type': 'application/json' },
            ...opts
        });
        if (!res.ok) {
            const txt = await res.text();
            throw new Error(txt || res.statusText);
        }
        return res.json();
    }

    // =========== 平坦 → 樹狀 ===========
    function buildTree(flat) {
        const map = {};
        const roots = [];
        for (const n of flat) {
            map[n.id] = {
                id: n.id, name: n.name, type: n.nodeType, isEnabled: n.isEnabled,
                sortOrder: n.sortOrder, parentId: n.parentId,
                children: [], expanded: expandedSet.has(n.id)
            };
        }
        for (const n of flat) {
            const node = map[n.id];
            if (n.parentId && map[n.parentId]) {
                map[n.parentId].children.push(node);
            } else {
                roots.push(node);
            }
        }
        // 排序
        const sortFn = (a, b) => a.sortOrder - b.sortOrder;
        function sortAll(nodes) { nodes.sort(sortFn); nodes.forEach(n => sortAll(n.children)); }
        sortAll(roots);
        return roots;
    }

    // =========== 載入 ===========
    async function loadTree() {
        try {
            flatNodes = await apiFetch('/tree');
            treeData = buildTree(flatNodes);
            renderTree();
        } catch (e) {
            document.getElementById('treeContainer').innerHTML =
                `<p class="text-danger text-center small mt-5"><i class="fas fa-exclamation-triangle me-1"></i>載入失敗：${escHtml(e.message)}</p>`;
        }
    }

    // =========== 渲染 ===========
    function renderTree() {
        const container = document.getElementById('treeContainer');
        if (treeData.length === 0) {
            container.innerHTML = '<p class="text-muted text-center small mt-5">尚無邏輯項目，請點擊上方 <i class="fas fa-plus"></i> 新增</p>';
            return;
        }
        container.innerHTML = buildNodes(treeData);
    }

    function buildNodes(nodes) {
        let html = '';
        for (const n of nodes) {
            const isFolder = n.type === 'folder';
            const isActive = n.id === selectedId ? ' active' : '';
            const isDisabled = !n.isEnabled ? ' disabled-node' : '';

            let icon, iconTitle;
            if (isFolder && n.isEnabled) {
                icon = n.expanded ? 'fas fa-folder-open text-warning' : 'fas fa-folder text-warning';
                iconTitle = '資料夾';
            } else if (isFolder && !n.isEnabled) {
                icon = n.expanded ? 'fas fa-folder-open text-secondary' : 'fas fa-folder text-secondary';
                iconTitle = '資料夾（已停用）';
            } else if (n.isEnabled) {
                icon = 'fas fa-file-code text-info';
                iconTitle = '邏輯（已啟用）';
            } else {
                icon = 'fas fa-file-code text-secondary';
                iconTitle = '邏輯（已停用）';
            }

            html += '<div class="tree-node">';
            html += `<div class="tree-item${isActive}${isDisabled}" data-id="${n.id}" onclick="window._lf.select(${n.id})" ondblclick="window._lf.toggle(${n.id})">`;

            if (isFolder) {
                html += `<span class="tree-toggle" onclick="event.stopPropagation(); window._lf.toggle(${n.id})">`;
                html += n.expanded ? '<i class="fas fa-caret-down"></i>' : '<i class="fas fa-caret-right"></i>';
                html += '</span>';
            } else {
                html += '<span style="width:16px;display:inline-block"></span>';
            }

            html += `<span class="node-icon" title="${iconTitle}"><i class="${icon}"></i></span>`;
            html += `<span class="node-label" title="${escHtml(n.name)}">${escHtml(n.name)}</span>`;

            // 操作按鈕
            html += '<span class="node-actions">';
            if (isFolder) {
                html += `<button title="新增資料夾" onclick="event.stopPropagation(); window._lf.addChild(${n.id},'folder')"><i class="fas fa-folder-plus"></i></button>`;
                html += `<button title="新增邏輯" onclick="event.stopPropagation(); window._lf.addChild(${n.id},'logic')"><i class="fas fa-file-circle-plus"></i></button>`;
            }
            if (n.isEnabled) {
                html += `<button title="停用" onclick="event.stopPropagation(); window._lf.toggleEnabled(${n.id},false)"><i class="fas fa-toggle-on text-success"></i></button>`;
            } else {
                html += `<button title="啟用" onclick="event.stopPropagation(); window._lf.toggleEnabled(${n.id},true)"><i class="fas fa-toggle-off text-secondary"></i></button>`;
            }
            html += `<button title="重新命名" onclick="event.stopPropagation(); window._lf.rename(${n.id})"><i class="fas fa-pen"></i></button>`;
            html += `<button title="刪除" onclick="event.stopPropagation(); window._lf.remove(${n.id})"><i class="fas fa-trash-alt text-danger"></i></button>`;
            html += '</span>';
            html += '</div>';

            if (isFolder && n.children.length > 0 && n.expanded) {
                html += '<div class="tree-children">' + buildNodes(n.children) + '</div>';
            }

            html += '</div>';
        }
        return html;
    }

    function escHtml(s) { const d = document.createElement('div'); d.textContent = s; return d.innerHTML; }

    // =========== 找節點 ===========
    function findNode(id, nodes) {
        for (const n of nodes) {
            if (n.id === id) return n;
            if (n.children) { const r = findNode(id, n.children); if (r) return r; }
        }
        return null;
    }

    // =========== 操作 ===========
    function select(id) {
        selectedId = id;
        const node = findNode(id, treeData);
        renderTree();
        updateContent(node);
    }

    function toggle(id) {
        const node = findNode(id, treeData);
        if (!node || node.type !== 'folder') return;
        node.expanded = !node.expanded;
        if (node.expanded) expandedSet.add(id); else expandedSet.delete(id);
        renderTree();
    }

    async function addRoot(type) {
        const name = type === 'folder' ? '新資料夾' : '新邏輯';
        const sortOrder = treeData.length;
        try {
            const res = await apiFetch('/tree', {
                method: 'POST',
                body: JSON.stringify({ parentId: null, name, nodeType: type, sortOrder })
            });
            selectedId = res.id;
            await loadTree();
            updateContent(findNode(res.id, treeData));
        } catch (e) { alert('新增失敗：' + e.message); }
    }

    async function addChild(parentId, type) {
        const parent = findNode(parentId, treeData);
        if (!parent || parent.type !== 'folder') return;
        expandedSet.add(parentId);
        const name = type === 'folder' ? '新資料夾' : '新邏輯';
        const sortOrder = parent.children.length;
        try {
            const res = await apiFetch('/tree', {
                method: 'POST',
                body: JSON.stringify({ parentId, name, nodeType: type, sortOrder })
            });
            selectedId = res.id;
            await loadTree();
            updateContent(findNode(res.id, treeData));
        } catch (e) { alert('新增失敗：' + e.message); }
    }

    function rename(id) {
        renameTargetId = id;
        const node = findNode(id, treeData);
        if (!node) return;
        document.getElementById('renameInput').value = node.name;
        new bootstrap.Modal(document.getElementById('renameModal')).show();
        setTimeout(() => document.getElementById('renameInput').select(), 300);
    }

    async function confirmRename() {
        const val = document.getElementById('renameInput').value.trim();
        if (!val || renameTargetId == null) return;
        try {
            await apiFetch(`/tree/${renameTargetId}/rename`, {
                method: 'PUT',
                body: JSON.stringify({ name: val })
            });
            await loadTree();
            if (selectedId === renameTargetId) {
                updateContent(findNode(renameTargetId, treeData));
            }
        } catch (e) { alert('重新命名失敗：' + e.message); }
        bootstrap.Modal.getInstance(document.getElementById('renameModal'))?.hide();
        renameTargetId = null;
    }

    async function remove(id) {
        if (!confirm('確定要刪除此項目？（含所有子項目）')) return;
        try {
            await apiFetch(`/tree/${id}`, { method: 'DELETE' });
            if (selectedId === id) { selectedId = null; clearContent(); }
            await loadTree();
        } catch (e) { alert('刪除失敗：' + e.message); }
    }

    async function toggleEnabled(id, isEnabled) {
        try {
            await apiFetch(`/tree/${id}/toggle`, {
                method: 'PUT',
                body: JSON.stringify({ isEnabled })
            });
            await loadTree();
            if (selectedId === id) {
                updateContent(findNode(id, treeData));
            }
        } catch (e) { alert('切換失敗：' + e.message); }
    }

    // =========== 右側內容 ===========
    function updateContent(node) {
        const title = document.getElementById('contentTitle');
        const area = document.getElementById('contentArea');
        if (!node) { clearContent(); return; }

        if (node.type === 'folder') {
            const folderColor = node.isEnabled ? 'text-warning' : 'text-secondary';
            const folderBadge = node.isEnabled
                ? '<span class="badge bg-success ms-2">已啟用</span>'
                : '<span class="badge bg-secondary ms-2">已停用</span>';
            title.innerHTML = `<i class="fas fa-folder-open ${folderColor} me-1"></i>${escHtml(node.name)}${folderBadge}`;
            const childCount = node.children ? node.children.length : 0;
            area.innerHTML = `
                <div class="text-center text-muted mt-5">
                    <i class="fas fa-folder-open fa-4x mb-3 d-block" style="opacity:.3"></i>
                    <p>資料夾「<strong>${escHtml(node.name)}</strong>」</p>
                    <p class="small">包含 ${childCount} 個項目</p>
                    ${!node.isEnabled ? '<p class="small text-danger"><i class="fas fa-ban me-1"></i>此資料夾已停用，底下所有項目皆不執行</p>' : ''}
                </div>`;
        } else {
            const statusColor = node.isEnabled ? 'text-info' : 'text-secondary';
            const statusBadge = node.isEnabled
                ? '<span class="badge bg-success ms-2">已啟用</span>'
                : '<span class="badge bg-secondary ms-2">已停用</span>';
            title.innerHTML = `<i class="fas fa-file-code ${statusColor} me-1"></i>${escHtml(node.name)}${statusBadge}`;
            area.innerHTML = '<div id="diagramCanvas" class="diagram-canvas"></div>';
            // 只檢查自身邏輯是否啟用（不受上層資料夾影響）
            _isLogicEnabled = !!node.isEnabled;
            initCanvas(node.id);
        }
    }

    // =========== 畫布 ===========
    const NODE_META = {
        input:    { icon: 'fas fa-sign-in-alt text-primary',        label: '讀取點位', inputs: [],         outputs: ['out'] },
        output:   { icon: 'fas fa-sign-out-alt text-success',       label: '寫入點位', inputs: ['in'],     outputs: [] },
        compare:  { icon: 'fas fa-not-equal text-warning',          label: '比較',     inputs: ['a','b'],  outputs: ['out'] },
        math:     { icon: 'fas fa-calculator text-info',            label: '數學運算', inputs: ['in'],     outputs: ['out'] },
        constant: { icon: 'fas fa-hashtag text-dark',               label: '常數',     inputs: [],         outputs: ['out'] },
        and:      { icon: 'fas fa-grip-lines text-danger',          label: 'AND 閘',   inputs: ['a','b'],  outputs: ['out'] },
        or:       { icon: 'fas fa-grip-lines-vertical text-danger', label: 'OR 閘',    inputs: ['a','b'],  outputs: ['out'] },
        not:      { icon: 'fas fa-exclamation text-danger',         label: 'NOT 閘',   inputs: ['in'],     outputs: ['out'] },
        xor:      { icon: 'fas fa-random text-danger',             label: 'XOR 閘',   inputs: ['a','b'],  outputs: ['out'] },
        timer:    { icon: 'fas fa-clock text-secondary',            label: '計時器',   inputs: ['in'],     outputs: ['out'] },
        contact_no: { icon: 'fas fa-toggle-on text-orange',          label: 'A\u63a5\u9ede', inputs: ['in'],     outputs: ['out'] },
        contact_nc: { icon: 'fas fa-toggle-off text-purple',         label: 'B\u63a5\u9ede', inputs: ['in'],     outputs: ['out'] },
        counter:    { icon: 'fas fa-sort-numeric-up text-teal',      label: '計數器', inputs: ['cu','reset','preset'], outputs: ['q','cv'],
                      portLabels: { cu: '計數脈衝 (cu)', reset: '重置 (reset)', preset: '目標值 (preset)', q: '達標輸出 (q)', cv: '目前計數 (cv)' } },
        algorithm:  { icon: 'fas fa-brain text-purple',              label:'\u6f14\u7b97\u6cd5', inputs: ['in'],     outputs: ['out'] }
    };

    const COMPARE_OPS = {
        lt:  { symbol: '<',  label: '小於' },
        gt:  { symbol: '>',  label: '大於' },
        lte: { symbol: '\u2264', label: '小於等於' },
        gte: { symbol: '\u2265', label: '大於等於' },
        eq:  { symbol: '=',  label: '等於' },
        neq: { symbol: '\u2260', label: '不等於' }
    };
    const MATH_OPS = {
        add:   { symbol: '+', label: '加法',     hasValue: true },
        sub:   { symbol: '\u2212', label: '減法',     hasValue: true },
        mul:   { symbol: '\u00d7', label: '乘法',     hasValue: true },
        div:   { symbol: '\u00f7', label: '除法',     hasValue: true },
        mod:   { symbol: '%', label: '取餘數',   hasValue: true },
        pow:   { symbol: '^', label: '次方',     hasValue: true },
        abs:   { symbol: '|x|', label: '絕對值', hasValue: false },
        sqrt:  { symbol: '\u221a', label: '平方根',   hasValue: false },
        round: { symbol: '\u2248', label: '四捨五入', hasValue: false }
    };

    const TIMER_OPS = {
        tp:  { symbol: 'TP',  label: '\u8108\u885d' },
        ton: { symbol: 'TON', label: '\u5ef6\u6642\u958b\u555f' },
        tpr: { symbol: 'TPR', label: '\u91cd\u8907\u8108\u885d' }
    };

    // Python 演算法（動態從 API 載入）
    let ALGO_OPS = {};

    async function loadAlgorithms() {
        try {
            const res = await fetch('/LogicFlow/api/algorithms');
            if (!res.ok) return;
            const list = await res.json();
            ALGO_OPS = {};
            list.forEach(a => {
                ALGO_OPS[a.name] = {
                    symbol: a.label.substring(0, 3),
                    label: a.label,
                    group: a.group || '',
                    inputs: a.inputs || ['in'],
                    outputs: a.outputs || ['out'],
                    description: a.description || '',
                    language: a.language || 'python',
                    variadic: !!a.variadic,
                    inputsRepeat: a.inputsRepeat || [],
                    inputsFixed: a.inputsFixed || [],
                    outputsRepeat: a.outputsRepeat || [],
                    outputsFixed: a.outputsFixed || []
                };
            });
            buildAlgoSubmenu();
        } catch (e) { console.error('[LogicFlow] loadAlgorithms failed:', e); }
    }

    // ── 變動埠展開：將 fixed + repeat × N 展開為 [{key, label}, ...] ──
    // repeat/fixed 為 [{key, label}, ...]
    function expandAlgoPorts(repeat, fixedList, n) {
        const out = [];
        (fixedList || []).forEach(p => out.push({ key: p.key, label: p.label || p.key }));
        const N = Math.max(1, parseInt(n, 10) || 1);
        for (let i = 1; i <= N; i++) {
            (repeat || []).forEach(p => out.push({
                key: `${p.key}${i}`,
                label: `${p.label || p.key} ${i}`
            }));
        }
        return out;
    }

    // ── 取得演算法節點當前的輸入/輸出埠（依 variadic + inputCount 動態展開） ──
    function getAlgoPorts(op, inputCount) {
        if (!op) return { inputs: [], outputs: [{ key: 'out', label: 'out' }] };
        if (op.variadic) {
            return {
                inputs: expandAlgoPorts(op.inputsRepeat, op.inputsFixed, inputCount),
                outputs: expandAlgoPorts(op.outputsRepeat, op.outputsFixed, inputCount)
            };
        }
        // 非 variadic：沿用 inputs/outputs 字串陣列，包成 {key, label} 對齊新格式
        return {
            inputs: (op.inputs || ['in']).map(k => ({ key: k, label: k })),
            outputs: (op.outputs || ['out']).map(k => ({ key: k, label: k }))
        };
    }

    // ── variadic 演算法：算出每組 (repeat #i) 在節點內的垂直 % 範圍，供畫外框 ──
    // 回傳 [{ index, topPct, bottomPct }, ...]；N<2 或沒有 repeat 埠時回空陣列
    function getAlgoGroupRanges(op, inputCount, inputs, outputs) {
        if (!op || !op.variadic) return [];
        const N = Math.max(1, parseInt(inputCount, 10) || 1);
        if (N < 2) return [];
        const fixedInLen = (op.inputsFixed || []).length;
        const repeatInLen = (op.inputsRepeat || []).length;
        const fixedOutLen = (op.outputsFixed || []).length;
        const repeatOutLen = (op.outputsRepeat || []).length;
        if (repeatInLen === 0 && repeatOutLen === 0) return [];
        const totalIn = inputs.length;
        const totalOut = outputs.length;
        const pctAt = (i, total) => total === 1 ? 50 : (20 + i * (60 / (total - 1)));
        const ranges = [];
        for (let g = 0; g < N; g++) {
            const pcts = [];
            for (let k = 0; k < repeatInLen; k++) pcts.push(pctAt(fixedInLen + g * repeatInLen + k, totalIn));
            for (let k = 0; k < repeatOutLen; k++) pcts.push(pctAt(fixedOutLen + g * repeatOutLen + k, totalOut));
            if (pcts.length === 0) continue;
            ranges.push({ index: g + 1, topPct: Math.min(...pcts), bottomPct: Math.max(...pcts) });
        }
        return ranges;
    }

    function _createAlgoMenuItem(key, op, isNodeMenu) {
        const item = document.createElement('div');
        item.className = isNodeMenu ? 'ctx-menu-item node-change-type' : 'ctx-menu-item';
        item.dataset.type = 'algorithm';
        item.dataset.operator = key;
        const langBadge = op.language === 'csharp'
            ? '<span style="font-size:.6rem;background:#178600;color:#fff;padding:0 3px;border-radius:2px;margin-left:4px;">C#</span>'
            : '';
        item.innerHTML = `<span class="ctx-op-symbol ctx-op-wide" style="color:#9b59b6;">${escHtml(op.symbol)}</span>${escHtml(op.label)}${langBadge}`;
        if (isNodeMenu) {
            item.addEventListener('click', (e) => {
                e.stopPropagation();
                hideCtxMenu();
                if (nodeCtxTargetId != null) changeNodeType(nodeCtxTargetId, 'algorithm', key);
            });
        } else {
            item.addEventListener('click', (e) => {
                e.stopPropagation();
                addNodeToCanvas('algorithm', key);
            });
        }
        return item;
    }

    function buildAlgoSubmenu() {
        ['ctxAlgoSub', 'nodeCtxAlgoSub'].forEach(containerId => {
            const container = document.getElementById(containerId);
            if (!container) return;
            container.innerHTML = '';
            const isNodeMenu = containerId === 'nodeCtxAlgoSub';

            if (Object.keys(ALGO_OPS).length === 0) {
                container.innerHTML = '<div class="ctx-menu-item text-muted" style="font-size:.75rem;">\u5c1a\u7121\u6f14\u7b97\u6cd5</div>';
                return;
            }

            // 按 group 分類
            const groups = {};
            const ungrouped = [];
            for (const [key, op] of Object.entries(ALGO_OPS)) {
                if (op.group) {
                    if (!groups[op.group]) groups[op.group] = [];
                    groups[op.group].push({ key, op });
                } else {
                    ungrouped.push({ key, op });
                }
            }

            // 有分類 → 渲染子選單
            for (const [groupName, items] of Object.entries(groups)) {
                const groupEl = document.createElement('div');
                groupEl.className = 'ctx-menu-item ctx-has-sub';
                groupEl.innerHTML = `<i class="fas fa-folder text-muted me-2" style="font-size:.8rem;"></i>${escHtml(groupName)} <i class="fas fa-caret-right ms-auto"></i>`;
                const sub = document.createElement('div');
                sub.className = 'ctx-submenu';
                items.forEach(({ key, op }) => sub.appendChild(_createAlgoMenuItem(key, op, isNodeMenu)));
                groupEl.appendChild(sub);
                container.appendChild(groupEl);
            }

            // 無分類的放在最後（若同時有分類和無分類，加分隔線）
            if (ungrouped.length > 0 && Object.keys(groups).length > 0) {
                const divider = document.createElement('div');
                divider.className = 'ctx-menu-divider';
                container.appendChild(divider);
            }
            ungrouped.forEach(({ key, op }) => container.appendChild(_createAlgoMenuItem(key, op, isNodeMenu)));
        });
    }

    // 千分位格式化
    function fmtNum(v) {
        const n = parseFloat(v);
        if (isNaN(n)) return v;                           // '--' 等非數字原樣回傳
        return n.toLocaleString('en-US', { maximumFractionDigits: 10 });
    }

    // 即時值快取 { [sid]: { value, quality } }
    let _realtimeCache = {};
    let _realtimeTimer = null;

    let canvasNodes = [];   // { id, type, x, y, operator? }
    let canvasEdges = [];   // { id, source, sourcePort, target, targetPort }
    let nextNodeId = 1;
    let nextEdgeId = 1;
    let currentTreeId = null;
    let diagramVersion = 0;       // 樂觀鎖版本號
    let ctxPos = { x: 0, y: 0 }; // 右鍵點擊在畫布上的座標
    let draggingEdge = null;      // 拖曳中的連線
    let selectedEdgeId = null;    // 選中的連線 Id
    let nodeCtxTargetId = null;   // 節點右鍵選單的目標節點 Id
    let _outputPrevState = {};    // 輸出節點上一輪狀態 { green: bool, value: number|null }
    let _controlModeCache = {};   // 手動/自動快取 { [sid]: { value, isAuto } }
    let _isLogicEnabled = true;   // 目前邏輯項目是否生效（含上層資料夾）
    let selectedNodeIds = new Set();   // 框選/點選的節點 Id 集合

    async function initCanvas(treeId) {
        // 清理舊 timer 的 setTimeout，避免殘留排程
        for (const nd of canvasNodes) {
            if (nd._tpTimeout) clearTimeout(nd._tpTimeout);
        }
        currentTreeId = treeId;
        canvasNodes = [];
        canvasEdges = [];
        nextNodeId = 1;
        nextEdgeId = 1;
        selectedEdgeId = null;
        draggingEdge = null;
        selectedNodeIds = new Set();
        _outputPrevState = {};

        // 載入已存的 DiagramJson
        diagramVersion = 0;
        try {
            const data = await apiFetch(`/diagram/${treeId}`);
            if (data) {
                diagramVersion = data.version || 0;
                if (data.diagramJson) {
                    const parsed = JSON.parse(data.diagramJson);
                    if (parsed.nodes && Array.isArray(parsed.nodes)) {
                        canvasNodes = parsed.nodes;
                        nextNodeId = canvasNodes.reduce((max, n) => Math.max(max, n.id + 1), 1);
                        // 舊版 timer 遷移：timerSeconds → timerDelay，無 operator 預設 tp
                        for (const nd of canvasNodes) {
                            if (nd.type === 'timer') {
                                if (nd.timerDelay == null && nd.timerSeconds != null) {
                                    nd.timerDelay = nd.timerSeconds;
                                    delete nd.timerSeconds;
                                }
                                if (nd.timerDelay == null) nd.timerDelay = 5;
                                if (!nd.operator || !TIMER_OPS[nd.operator]) nd.operator = 'tp';
                                if (nd.operator === 'tp' && nd.timerHold == null) nd.timerHold = 2;
                                // TOF 遷移為 TON
                                if (nd.operator === 'tof') nd.operator = 'ton';
                            }
                        }
                    }
                    if (parsed.edges && Array.isArray(parsed.edges)) {
                        canvasEdges = parsed.edges;
                        nextEdgeId = canvasEdges.reduce((max, e) => Math.max(max, e.id + 1), 1);
                    }
                    // 舊版 math 遷移：mathValue → 常數節點 + val 連線
                    for (const nd of [...canvasNodes]) {
                        if (nd.type === 'math' && nd.mathValue != null && nd.operator && MATH_OPS[nd.operator] && MATH_OPS[nd.operator].hasValue) {
                            const cNode = { id: nextNodeId++, type: 'constant', x: nd.x, y: nd.y + 80, constValue: nd.mathValue };
                            canvasNodes.push(cNode);
                            canvasEdges.push({ id: nextEdgeId++, source: cNode.id, sourcePort: 'out', target: nd.id, targetPort: 'val' });
                            delete nd.mathValue;
                        }
                    }
                }
            }
        } catch (e) { /* 尚無 diagram 資料，空白畫布 */ }

        // 從 Engine 同步 TP 計時器狀態（避免每次開頁面倒數重算）
        await syncTimerStateFromEngine(treeId);

        // 顯示儲存按鈕
        const btnSave = document.getElementById('btnSaveDiagram');
        if (btnSave) btnSave.style.display = '';

        // 畫布有排程接點時，預先載入排程資料（否則 evalScheduleNow 會因 ppAllSchedules===null 回傳 null）
        if (canvasNodes.some(n => (n.type === 'contact_no' || n.type === 'contact_nc') && n.scheduleId != null)) {
            ppAllSchedules = null; // 強制重新載入（排程可能已變更）
            try { await ppEnsureSchedules(); } catch (_) {}
        }

        renderCanvasNodes();
        bindCanvasEvents();
        startRealtimePolling();
    }

    /// 從 Engine（經 MQTT → Web API）取得 TP 計時器的實際狀態，注入到前端節點
    async function syncTimerStateFromEngine(treeId) {
        try {
            const resp = await fetch(API + '/timer-state/' + treeId);
            if (!resp.ok) return;
            const states = await resp.json();
            // states 格式: { "treeId-nodeId": { phase, phaseEndMs, hasHeld } }
            for (const nd of canvasNodes) {
                if (nd.type !== 'timer') continue;
                const key = treeId + '-' + nd.id;
                const st = states[key];
                if (!st || !st.phase) continue;
                if (nd.operator === 'ton') {
                    nd._tonPhase = st.phase;
                    nd._tonPhaseEnd = st.phaseEndMs;
                } else {
                    nd._tpPhase = st.phase;
                    nd._tpPhaseEnd = st.phaseEndMs;
                    nd._tpHasHeld = st.hasHeld;
                }
                const remaining = st.phaseEndMs - Date.now();
                if (remaining > 0) {
                    nd._tpTimeout = setTimeout(() => evaluateNodes(), remaining);
                }
            }
        } catch (e) {
            // 取不到就走原本的前端初始化邏輯
        }
    }

    // =========== 即時值輪詢 ===========
    var _pollInterval = 300; // 輪詢間隔 (ms)
    function startRealtimePolling() {
        stopRealtimePolling();
        fetchRealtimeValues();                       // 立即拉一次
    }
    function stopRealtimePolling() {
        if (_realtimeTimer) { clearTimeout(_realtimeTimer); _realtimeTimer = null; }
        stopTimerEval();
    }
    // 計時器專用 1 秒 interval（讓倒數與輸出即時更新）
    let _timerEvalInterval = null;
    function startTimerEval() {
        stopTimerEval();
        var needInterval = canvasNodes.some(n => n.type === 'timer')
            || canvasNodes.some(n => (n.type === 'contact_no' || n.type === 'contact_nc') && n.scheduleId != null)
            || canvasNodes.some(n => n.type === 'counter');
        if (needInterval) {
            _timerEvalInterval = setInterval(() => evaluateNodes(), 1000);
        }
    }
    function stopTimerEval() {
        if (_timerEvalInterval) { clearInterval(_timerEvalInterval); _timerEvalInterval = null; }
    }
    async function fetchRealtimeValues() {
        // 收集畫布上所有有 sid 的節點（只請求需要的）
        const sids = canvasNodes.filter(n => n.sid).map(n => n.sid);
        if (sids.length === 0) {
            // 純排程畫布也需要 evaluate + render
            evaluateNodes();
            renderCanvasNodes();
            _realtimeTimer = setTimeout(fetchRealtimeValues, _pollInterval);
            return;
        }

        // ── 1. 拉即時值（獨立 try-catch，不受控制模式 API 影響） ──
        try {
            const resp = await fetch('/api/realtime/by-sids', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(sids)
            });
            const json = await resp.json();
            if (json.success && json.data) {
                const returnedSids = json.data.map(d => d.sid);
                const missingSids = sids.filter(s => !returnedSids.includes(s));
                if (missingSids.length > 0) {
                    console.warn('[LogicFlow] API \u672a\u56de\u50b3\u7684 SID:', missingSids);
                }
                for (const item of json.data) {
                    _realtimeCache[item.sid] = { value: item.value, quality: item.quality };
                }
            } else {
                console.warn('[LogicFlow] API \u56de\u61c9\u7570\u5e38:', json);
            }
        } catch (e) {
            console.warn('[LogicFlow] realtime fetch error:', e);
        }

        // ── 2. 拉控制模式（獨立 try-catch） ──
        try {
            const ctrlResp = await fetch('/api/control/manual-values');
            if (ctrlResp.ok) {
                const ctrlJson = await ctrlResp.json();
                _controlModeCache = ctrlJson || {};
            }
        } catch (_) { /* 控制模式拉取失敗不影響即時值顯示 */ }

        // ── 3. 局部更新 DOM（用節點唯一 ID 定位，避免重複 SID 問題） ──
        const canvas = document.getElementById('diagramCanvas');
        if (canvas) {
            for (const nd of canvasNodes) {
                if (!nd.sid) continue;
                const lv = _realtimeCache[nd.sid];
                if (!lv) continue;
                // 用唯一 node ID 找到該節點的 DOM 元素
                const nodeEl = canvas.querySelector(`.flow-node[data-node-id="${nd.id}"]`);
                if (!nodeEl) continue;
                // 更新即時值（在該節點內部找 .node-live-value）
                const valEl = nodeEl.querySelector('.node-live-value');
                if (valEl) {
                    const unitText = nd.unit ? ` ${nd.unit}` : '';
                    valEl.textContent = fmtNum(lv.value) + unitText;
                    valEl.classList.toggle('quality-bad', lv.quality === 'Bad');
                }
                // 更新 A/B接點的狀態標籤
                if (nd.type === 'contact_no' || nd.type === 'contact_nc') {
                    const stateEl = nodeEl.querySelector('.contact-state');
                    if (stateEl) {
                        const pv = parseFloat(lv.value);
                        const isOn = !isNaN(pv) && (nd.type === 'contact_no' ? pv === 1 : pv === 0);
                        stateEl.textContent = isOn ? '\u25cf ON' : '\u25cb OFF';
                        stateEl.classList.toggle('contact-on', isOn);
                        stateEl.classList.toggle('contact-off', !isOn);
                    }
                }
                // 更新輸出節點的模式標籤
                if (nd.type === 'output') {
                    const ctrl = _controlModeCache[nd.sid];
                    const isManual = ctrl && !ctrl.isAuto;
                    const badge = nodeEl.querySelector('.node-mode-badge');
                    if (badge) {
                        badge.textContent = isManual ? '\u624b\u52d5' : '\u81ea\u52d5';
                        badge.classList.toggle('mode-manual', isManual);
                        badge.classList.toggle('mode-auto', !isManual);
                    }
                    nodeEl.classList.toggle('output-manual', isManual);
                }
            }
        }
        // ── 4. 更新排程接點的 ON/OFF 狀態 ──
        if (canvas) {
            for (const nd of canvasNodes) {
                if ((nd.type !== 'contact_no' && nd.type !== 'contact_nc') || nd.scheduleId == null) continue;
                const nodeEl = canvas.querySelector(`.flow-node[data-node-id="${nd.id}"]`);
                if (!nodeEl) continue;
                const stateEl = nodeEl.querySelector('.contact-state');
                if (!stateEl) continue;
                const isOn = evalScheduleNow(nd.scheduleId, nd.type);
                stateEl.textContent = isOn != null ? (isOn ? '\u25cf ON' : '\u25cb OFF') : '--';
                stateEl.classList.toggle('contact-on', !!isOn);
                stateEl.classList.toggle('contact-off', isOn != null && !isOn);
            }
        }
        // 即時求值比較節點
        evaluateNodes();
        // ── 5. 更新 ctrl 埠驅動的接點 ON/OFF 狀態 ──
        if (canvas) {
            for (const nd of canvasNodes) {
                if (nd.type !== 'contact_no' && nd.type !== 'contact_nc') continue;
                var hasCtrl = canvasEdges.some(function(e) { return e.target === nd.id && e.targetPort === 'ctrl'; });
                if (!hasCtrl) continue;
                var nodeEl = canvas.querySelector('.flow-node[data-node-id="' + nd.id + '"]');
                if (!nodeEl) continue;
                var stateEl = nodeEl.querySelector('.contact-state');
                if (!stateEl) continue;
                var isOn = nd._contactOn !== undefined ? nd._contactOn : (nd._contactResult != null && nd._contactResult !== 0);
                var evaluated = nd._contactOn !== undefined || nd._contactResult != null;
                stateEl.textContent = evaluated ? (isOn ? '\u25cf ON' : '\u25cb OFF') : '--';
                stateEl.classList.toggle('contact-on', isOn);
                stateEl.classList.toggle('contact-off', evaluated && !isOn);
            }
        }
        // 永遠排下一次輪詢（不論成功或失敗）
        _realtimeTimer = setTimeout(fetchRealtimeValues, _pollInterval);
    }

    // ── 標記 input 節點的 Bad 品質 ──
    function markBadInputs() {
        for (const nd of canvasNodes) {
            if ((nd.type === 'input' || nd.type === 'contact_no' || nd.type === 'contact_nc') && nd.sid) {
                const lv = _realtimeCache[nd.sid];
                nd._isBad = lv ? lv.quality === 'Bad' : false;
            } else {
                nd._isBad = false;
            }
        }
    }

    // ── 檢查某節點的上游是否有 Bad 品質來源（遞迴） ──
    function hasUpstreamBad(nodeId, visited) {
        if (!visited) visited = new Set();
        if (visited.has(nodeId)) return false;
        visited.add(nodeId);
        const nd = canvasNodes.find(n => n.id === nodeId);
        if (!nd) return false;
        if (nd.type === 'input' || nd.type === 'contact_no' || nd.type === 'contact_nc') return !!nd._isBad;
        const inEdges = canvasEdges.filter(e => e.target === nodeId);
        for (const e of inEdges) {
            if (hasUpstreamBad(e.source, visited)) return true;
        }
        return false;
    }

    // ── 取得某節點的輸出數值（演算法節點支援多輸出，依 sourcePort 索引） ──
    function getNodeOutputValue(nodeId, sourcePort) {
        const nd = canvasNodes.find(n => n.id === nodeId);
        if (!nd) return null;
        const port = sourcePort || 'out';
        if (nd.type === 'input' && nd.sid) {
            if (nd._isBad) return null;
            const lv = _realtimeCache[nd.sid];
            return lv != null ? parseFloat(lv.value) : null;
        }
        if (nd.type === 'constant') {
            return nd.constValue != null ? parseFloat(nd.constValue) : 0;
        }
        if (nd.type === 'math') {
            return nd._mathResult != null ? nd._mathResult : null;
        }
        if (nd.type === 'compare') {
            if (nd._compareResult === true) return 1;
            if (nd._compareResult === false) return 0;
            return null;
        }
        if (['and','or','not','xor'].includes(nd.type)) {
            return nd._gateResult != null ? nd._gateResult : null;
        }
        if (nd.type === 'timer') {
            return nd._timerResult != null ? nd._timerResult : null;
        }
        if (nd.type === 'contact_no' || nd.type === 'contact_nc') {
            return nd._contactResult != null ? nd._contactResult : null;
        }
        if (nd.type === 'algorithm') {
            // 多輸出：以 dict 儲存（key 對應 sourcePort）；單輸出舊行為仍以 {out: value} 表示
            const live = nd._algoResult;
            if (live && typeof live === 'object' && live[port] != null) return live[port];
            // 非同步等待期間：沿用上次快取值，避免輸出 0 導致下游閃爍
            const cached = nd._algoCachedResult;
            if (cached && typeof cached === 'object' && cached[port] != null) return cached[port];
            return null;
        }
        if (nd.type === 'counter') {
            // 多輸出：q（達 preset 為 1）/ cv（目前累加值）
            if (port === 'cv') return nd._counterValue != null ? nd._counterValue : 0;
            // q：跨 tick 保留以支援 q→reset 自回授
            return nd._counterQ != null ? nd._counterQ : 0;
        }
        return null;
    }

    // ── 取得連到某節點某埠的來源節點輸出值 ──
    function getInputValue(nodeId, portName) {
        const edge = canvasEdges.find(e => e.target === nodeId && e.targetPort === portName);
        if (!edge) return null;
        return getNodeOutputValue(edge.source, edge.sourcePort);
    }

    // ── TPR 回饋偵測：從指定節點往下游找 output 節點，讀取其即時值 ──
    function getTprFeedbackValue(startNodeId) {
        var visited = new Set([startNodeId]);
        var queue = [];
        canvasEdges.filter(e => e.source === startNodeId).forEach(e => queue.push(e.target));
        while (queue.length > 0) {
            var nid = queue.shift();
            if (visited.has(nid)) continue;
            visited.add(nid);
            var target = canvasNodes.find(n => n.id === nid);
            if (!target) continue;
            if (target.type === 'output' && target.sid) {
                var lv = _realtimeCache[target.sid];
                return lv ? parseFloat(lv.value) : null;
            }
            canvasEdges.filter(e => e.source === nid).forEach(e => queue.push(e.target));
        }
        return null;
    }

    // ── 排程即時評估（前端用，回傳 boolean | null） ──
    function evalScheduleNow(scheduleId, nodeType) {
        if (!ppAllSchedules) return null;
        var s = ppAllSchedules.find(function(x) { return x.nId === scheduleId; });
        if (!s || !s.isEnabled) return null;
        var now = new Date();
        // 日期條件
        var dayMatch = false;
        if (s.nRecurrenceType === 0) {
            dayMatch = _schCheckDaysOfWeek(now, s.szDaysOfWeek);
        } else if (s.nRecurrenceType === 1) {
            dayMatch = _schCheckWeekCycle(now, s) && _schCheckDaysOfWeek(now, s.szDaysOfWeek);
        } else if (s.nRecurrenceType === 2) {
            dayMatch = _schCheckDaysOfMonth(now, s.szDaysOfMonth);
        } else if (s.nRecurrenceType === 3) {
            dayMatch = _schCheckMonthCycle(now, s) && _schCheckDaysOfMonth(now, s.szDaysOfMonth);
        }
        // 時間條件
        var timeMatch = _schCheckTimeWindow(now, s.szStartTime, s.szEndTime);
        var isActive = dayMatch && timeMatch;
        // A接點=常開：排程啟動→導通；B接點=常閉：排程啟動→不導通
        return nodeType === 'contact_no' ? isActive : !isActive;
    }
    function _schCheckDaysOfWeek(now, daysStr) {
        if (!daysStr) return false;
        var jsDay = now.getDay(); // 0=Sun
        var isoDay = jsDay === 0 ? 7 : jsDay; // 1=Mon..7=Sun
        return daysStr.split(',').some(function(d) { return parseInt(d) === isoDay; });
    }
    function _schCheckDaysOfMonth(now, daysStr) {
        if (!daysStr) return false;
        var dom = now.getDate();
        return daysStr.split(',').some(function(d) { return parseInt(d) === dom; });
    }
    function _schCheckTimeWindow(now, startStr, endStr) {
        if (!startStr || !endStr) return false;
        var nowMin = now.getHours() * 60 + now.getMinutes();
        var sp = startStr.split(':'), ep = endStr.split(':');
        var startMin = parseInt(sp[0]) * 60 + parseInt(sp[1]);
        var endMin = parseInt(ep[0]) * 60 + parseInt(ep[1]);
        if (endMin <= startMin) return nowMin >= startMin || nowMin < endMin;
        return nowMin >= startMin && nowMin < endMin;
    }
    function _schCheckWeekCycle(now, s) {
        if (!s.dtAnchorDateTime && !s.anchorDateTime) return false;
        if (!s.nRunLength && !s.runLength) return false;
        if (!s.nRestLength && !s.restLength) return false;
        var anchor = new Date(s.dtAnchorDateTime || s.anchorDateTime);
        var runLen = s.nRunLength || s.runLength;
        var restLen = s.nRestLength || s.restLength;
        var elapsedMs = now.getTime() - anchor.getTime();
        if (elapsedMs < 0) return false;
        var totalCycle = runLen + restLen;
        var elapsedWeeks = Math.floor(elapsedMs / (7 * 24 * 60 * 60000));
        return (elapsedWeeks % totalCycle) < runLen;
    }
    function _schCheckMonthCycle(now, s) {
        if (!s.dtAnchorDateTime && !s.anchorDateTime) return false;
        if (!s.nRunLength && !s.runLength) return false;
        if (!s.nRestLength && !s.restLength) return false;
        var anchor = new Date(s.dtAnchorDateTime || s.anchorDateTime);
        var runLen = s.nRunLength || s.runLength;
        var restLen = s.nRestLength || s.restLength;
        var totalMonths = (now.getFullYear() - anchor.getFullYear()) * 12 + (now.getMonth() - anchor.getMonth());
        if (totalMonths < 0) return false;
        var totalCycle = runLen + restLen;
        return (totalMonths % totalCycle) < runLen;
    }

    // ── 單節點求值（回傳是否成功算出結果） ──
    function evalOneNode(nd) {
        if (nd.type === 'math' && nd.operator && MATH_OPS[nd.operator]) {
            nd._mathResult = null;
            if (hasUpstreamBad(nd.id)) return false;
            const v = getInputValue(nd.id, 'in');
            if (v == null) return false;
            const mop = MATH_OPS[nd.operator];
            let p = 0;
            if (mop.hasValue) {
                const pVal = getInputValue(nd.id, 'val');
                if (pVal == null) return false;
                p = pVal;
            }
            switch (nd.operator) {
                case 'add':   nd._mathResult = v + p;            break;
                case 'sub':   nd._mathResult = v - p;            break;
                case 'mul':   nd._mathResult = v * p;            break;
                case 'div':   nd._mathResult = p !== 0 ? v / p : null; break;
                case 'mod':   nd._mathResult = p !== 0 ? v % p : null; break;
                case 'pow':   nd._mathResult = Math.pow(v, p);   break;
                case 'abs':   nd._mathResult = Math.abs(v);      break;
                case 'sqrt':  nd._mathResult = v >= 0 ? Math.sqrt(v) : null; break;
                case 'round': nd._mathResult = Math.round(v);    break;
            }
            return nd._mathResult != null;
        }
        if (nd.type === 'compare' && nd.operator && COMPARE_OPS[nd.operator]) {
            nd._compareResult = null;
            if (hasUpstreamBad(nd.id)) return false;
            const a = getInputValue(nd.id, 'a');
            const b = getInputValue(nd.id, 'b');
            if (a == null || b == null) return false;
            switch (nd.operator) {
                case 'lt':  nd._compareResult = a < b;  break;
                case 'gt':  nd._compareResult = a > b;  break;
                case 'lte': nd._compareResult = a <= b; break;
                case 'gte': nd._compareResult = a >= b; break;
                case 'eq':  nd._compareResult = a === b; break;
                case 'neq': nd._compareResult = a !== b; break;
                default:    return false;
            }
            return true;
        }
        const GATE_TYPES = ['and', 'or', 'not', 'xor'];
        if (GATE_TYPES.includes(nd.type)) {
            nd._gateResult = null;
            if (hasUpstreamBad(nd.id)) return false;
            if (nd.type === 'not') {
                const v = getInputValue(nd.id, 'in');
                if (v == null || (v !== 0 && v !== 1)) return false;
                nd._gateResult = v === 0 ? 1 : 0;
            } else {
                const a = getInputValue(nd.id, 'a');
                const b = getInputValue(nd.id, 'b');
                if (a == null || b == null || (a !== 0 && a !== 1) || (b !== 0 && b !== 1)) return false;
                switch (nd.type) {
                    case 'and': nd._gateResult = (a === 1 && b === 1) ? 1 : 0; break;
                    case 'or':  nd._gateResult = (a === 1 || b === 1) ? 1 : 0; break;
                    case 'xor': nd._gateResult = (a !== b) ? 1 : 0;            break;
                }
            }
            return nd._gateResult != null;
        }
        // ── 計時器 ──
        if (nd.type === 'timer') {
            // ── TON 延時開啟 ──
            if (nd.operator === 'ton') {
                const inEdge = canvasEdges.find(e => e.target === nd.id && e.targetPort === 'in');
                let inputVal = null;
                if (inEdge) {
                    if (hasUpstreamBad(nd.id)) {
                        nd._tonPhase = null;
                        nd._tonPhaseEnd = null;
                        nd._timerResult = null;
                        nd._timerDone = true;
                        return true;
                    }
                    inputVal = getNodeOutputValue(inEdge.source, inEdge.sourcePort);
                    if (inputVal == null) return false;
                } else {
                    inputVal = 1; // 無輸入預設 ON
                }
                const isInputOn = inputVal !== 0;
                const effDelay = getInputValue(nd.id, 'delay') ?? nd.timerDelay ?? 5;
                const delayMs = Math.max(effDelay * 1000, 500);
                const now = Date.now();

                if (!isInputOn) {
                    // 輸入 OFF → 立即重置，輸出 0
                    nd._tonPhase = null;
                    nd._tonPhaseEnd = null;
                    nd._timerResult = 0;
                    nd._timerDone = true;
                    return true;
                }
                // 輸入 ON
                if (!nd._tonPhase) {
                    // 開始計時
                    nd._tonPhase = 'timing';
                    nd._tonPhaseEnd = now + delayMs;
                    clearTimeout(nd._tpTimeout);
                    nd._tpTimeout = setTimeout(() => evaluateNodes(), delayMs);
                }
                if (now >= nd._tonPhaseEnd) {
                    // 計時完成 → 輸出 ON（透傳輸入值）
                    nd._tonPhase = 'on';
                    nd._timerResult = inputVal;
                } else {
                    // 計時中 → 輸出 0
                    nd._timerResult = 0;
                }
                nd._timerDone = true;
                return true;
            }
            // TPR 延遲導通 + 回饋重送：
            // delay 倒數中輸出 null（值變會 debounce 重置）；
            // 倒數結束輸出 passValue 並進入 confirmed（同時開始 settling 倒數）；
            // confirmed 內若輸入值變或下游回饋偏離 → 回 delay。
            // _tpPhaseEnd 雙語意：delay 階段=倒數結束時間；confirmed 階段=settling 結束時間。
            if (nd.operator === 'tpr') {
                const inEdge = canvasEdges.find(e => e.target === nd.id && e.targetPort === 'in');
                let passValue = 1;
                let currentInput = null;

                if (inEdge) {
                    if (hasUpstreamBad(nd.id)) {
                        clearTimeout(nd._tpTimeout);
                        nd._tpPhase = null; nd._tpPhaseEnd = null;
                        nd._tprPrevInput = null; nd._tprLastSentValue = null;
                        nd._timerResult = null; nd._timerDone = true;
                        return true;
                    }
                    const v = getNodeOutputValue(inEdge.source, inEdge.sourcePort);
                    if (v != null) { currentInput = v; passValue = v; }
                    else {
                        const srcNd = canvasNodes.find(n => n.id === inEdge.source);
                        if (srcNd && srcNd.type !== 'input' && srcNd.type !== 'constant' && !srcNd._evalDone) return false;
                    }
                } else {
                    currentInput = 1;
                }

                // 輸入 null → 中止
                if (currentInput == null) {
                    clearTimeout(nd._tpTimeout);
                    nd._tpPhase = null; nd._tpPhaseEnd = null;
                    nd._tprPrevInput = null; nd._tprLastSentValue = null;
                    nd._timerResult = null; nd._timerDone = true;
                    return true;
                }

                const feedback = getTprFeedbackValue(nd.id);
                const effDelay = getInputValue(nd.id, 'delay') ?? nd.timerDelay ?? 5;
                const delayMs = Math.max(effDelay * 1000, 500);
                const now = Date.now();

                // 初始：進入 delay 倒數，輸出 null
                if (!nd._tpPhase) {
                    nd._tpPhase = 'delay';
                    nd._tpPhaseEnd = now + delayMs;
                    nd._tprPrevInput = currentInput;
                    clearTimeout(nd._tpTimeout);
                    nd._tpTimeout = setTimeout(() => evaluateNodes(), delayMs);
                    nd._timerResult = null;
                    nd._timerDone = true;
                    return true;
                }

                // confirmed：已輸出 passValue，視 settling 與回饋狀況決定是否回 delay
                if (nd._tpPhase === 'confirmed') {
                    // (a) 輸入值變化 → 回 delay 重啟倒數
                    if (nd._tprLastSentValue != null
                        && Math.abs(passValue - nd._tprLastSentValue) >= 0.001) {
                        nd._tpPhase = 'delay';
                        nd._tpPhaseEnd = now + delayMs;
                        nd._tprPrevInput = currentInput;
                        clearTimeout(nd._tpTimeout);
                        nd._tpTimeout = setTimeout(() => evaluateNodes(), delayMs);
                        nd._timerResult = null;
                        nd._timerDone = true;
                        return true;
                    }

                    // (b) settling 中（now < _tpPhaseEnd）→ 不檢查回饋，持續輸出 passValue
                    if (nd._tpPhaseEnd != null && now < nd._tpPhaseEnd) {
                        nd._timerResult = passValue;
                        nd._timerDone = true;
                        return true;
                    }

                    // (c) settling 已過 → 檢查回饋
                    const isFeedbackMatch = feedback != null && Math.abs(feedback - passValue) < 0.001;
                    if (isFeedbackMatch) {
                        nd._timerResult = passValue;
                        nd._timerDone = true;
                        return true;
                    }
                    // 回饋偏離 → 回 delay 重新計時 + 重送
                    nd._tpPhase = 'delay';
                    nd._tpPhaseEnd = now + delayMs;
                    nd._tprPrevInput = currentInput;
                    clearTimeout(nd._tpTimeout);
                    nd._tpTimeout = setTimeout(() => evaluateNodes(), delayMs);
                    nd._timerResult = null;
                    nd._timerDone = true;
                    return true;
                }

                // delay：倒數中
                // (a) 值變偵測 → debounce 重置倒數
                if (nd._tprPrevInput != null
                    && Math.abs(currentInput - nd._tprPrevInput) >= 0.001) {
                    nd._tpPhaseEnd = now + delayMs;
                    nd._tprPrevInput = currentInput;
                    clearTimeout(nd._tpTimeout);
                    nd._tpTimeout = setTimeout(() => evaluateNodes(), delayMs);
                    nd._timerResult = null;
                    nd._timerDone = true;
                    return true;
                }

                // (b) 倒數結束 → 進入 confirmed、輸出 passValue、啟動 settling
                if (now >= nd._tpPhaseEnd) {
                    nd._tpPhase = 'confirmed';
                    nd._tprLastSentValue = passValue;
                    nd._tpPhaseEnd = now + delayMs;  // settling 結束時間
                    clearTimeout(nd._tpTimeout);
                    nd._tpTimeout = setTimeout(() => evaluateNodes(), delayMs);
                    nd._timerResult = passValue;
                    nd._timerDone = true;
                    return true;
                }

                // (c) 倒數中且輸入未變 → 輸出 null
                nd._timerResult = null;
                nd._timerDone = true;
                return true;
            }
            // TP 脈衝：質變觸發 — 輸入值改變 → delay → hold(輸出) → 閒置等待下次質變
            const inEdge = canvasEdges.find(e => e.target === nd.id && e.targetPort === 'in');
            let passValue = 1;

            // ── 1. 取得當前輸入值 ──
            let currentInput = null;
            if (inEdge) {
                if (hasUpstreamBad(nd.id)) {
                    clearTimeout(nd._tpTimeout);
                    nd._tpPhase = null;
                    nd._tpPhaseEnd = null;
                    nd._tpHasHeld = false;
                    nd._tpPrevInput = undefined;
                    nd._timerResult = null;
                    nd._timerDone = true;
                    return true;
                }
                const v = getNodeOutputValue(inEdge.source, inEdge.sourcePort);
                if (v != null) {
                    currentInput = v;
                    passValue = v;
                } else {
                    // v == null → 區分「上游尚未評估」vs「上游已評估但輸出 null」
                    const srcNd = canvasNodes.find(n => n.id === inEdge.source);
                    if (srcNd && srcNd.type !== 'input' && srcNd.type !== 'constant' && !srcNd._evalDone) {
                        return false;  // 上游尚未完成本輪評估，等下一 pass
                    }
                    // 上游已完成但輸出 null → currentInput 保持 null
                }
            } else {
                currentInput = 1;  // 無輸入連線 → 視為常數 1
            }

            const effDelay = getInputValue(nd.id, 'delay') ?? nd.timerDelay ?? 5;
            const effHold  = getInputValue(nd.id, 'hold')  ?? nd.timerHold  ?? 2;
            const delayMs = Math.max(effDelay * 1000, 500);
            const holdMs  = Math.max(effHold  * 1000, 500);
            const now = Date.now();

            // ── 2. 質變偵測：輸入值與上次不同時觸發新週期 ──
            // _tpPrevInput 三態：undefined=首次載入(不觸發), null=上游曾 null(會觸發), 數值=比較用
            if (currentInput != null) {
                if (nd._tpPrevInput === undefined) {
                    // 首次載入：只記錄當前值，不啟動計時（避免進頁面就倒數）
                    nd._tpPrevInput = currentInput;
                } else if (nd._tpPrevInput === null || nd._tpPrevInput !== currentInput) {
                    // 真正質變：null→有值 或 值改變 → 啟動新週期
                    nd._tpPrevInput = currentInput;
                    clearTimeout(nd._tpTimeout);
                    nd._tpPhase = 'delay';
                    nd._tpPhaseEnd = now + delayMs;
                    nd._tpHasHeld = false;
                    nd._timerStartTime = now;
                    nd._tpTimeout = setTimeout(() => evaluateNodes(), delayMs);
                }
            } else {
                // 上游輸出 null → 中止計時週期，等待下次有值時質變觸發
                nd._tpPrevInput = null;
                if (nd._tpPhase) {
                    clearTimeout(nd._tpTimeout);
                    nd._tpPhase = null;
                    nd._tpPhaseEnd = null;
                    nd._tpHasHeld = false;
                }
            }

            // ── 3. 閒置狀態：等待質變 ──
            if (!nd._tpPhase) {
                nd._timerResult = null;
                nd._timerDone = true;
                return true;
            }

            // ── 4. 階段轉換 ──
            if (now >= nd._tpPhaseEnd) {
                if (nd._tpPhase === 'delay') {
                    nd._tpPhase = 'hold';
                    nd._tpPhaseEnd = now + holdMs;
                    nd._tpHasHeld = true;
                    clearTimeout(nd._tpTimeout);
                    nd._tpTimeout = setTimeout(() => evaluateNodes(), holdMs);
                } else {
                    // hold 結束 → 回到閒置
                    nd._tpPhase = null;
                    nd._tpPhaseEnd = null;
                    clearTimeout(nd._tpTimeout);
                    nd._timerResult = null;
                    nd._timerDone = true;
                    return true;
                }
            }

            // ── 5. 輸出：hold 階段送 passValue，delay 階段 null ──
            nd._timerResult = nd._tpPhase === 'hold' ? passValue : null;
            nd._timerDone = true;
            return true;
        }
        // ── A/B 接點 — ctrl 埠模式（邏輯閘控制導通，優先於排程/點位）──
        if (nd.type === 'contact_no' || nd.type === 'contact_nc') {
            var ctrlEdge = canvasEdges.find(function(e) { return e.target === nd.id && e.targetPort === 'ctrl'; });
            if (ctrlEdge) {
                nd._contactResult = null;
                nd._contactOn = undefined;
                var ctrlVal = getNodeOutputValue(ctrlEdge.source, ctrlEdge.sourcePort);
                if (ctrlVal == null) return false;  // 上游未就緒
                var isOnCtrl = nd.type === 'contact_no' ? (ctrlVal === 1) : (ctrlVal === 0);
                nd._contactOn = isOnCtrl;
                var inValCtrl = getInputValue(nd.id, 'in');
                // 守衛：in 埠有連線但值為 null
                var inEdgeCtrl = canvasEdges.find(function(e) { return e.target === nd.id && e.targetPort === 'in'; });
                if (inEdgeCtrl && inValCtrl == null) {
                    var srcCtrl = canvasNodes.find(function(n) { return n.id === inEdgeCtrl.source; });
                    if (srcCtrl && srcCtrl.type !== 'input' && srcCtrl.type !== 'constant' && !srcCtrl._evalDone) return false;
                    // 上游已完成但輸出 null → 傳遞 null（不用自己的 ON/OFF 預設值）
                    nd._contactResult = null;
                    return true;
                }
                nd._contactResult = isOnCtrl ? (inValCtrl != null ? inValCtrl : 1) : (inValCtrl != null ? null : 0);
                return true;
            }
        }
        // ── A/B 接點 — 排程模式 ──
        if ((nd.type === 'contact_no' || nd.type === 'contact_nc') && nd.scheduleId != null) {
            nd._contactResult = null;
            nd._contactOn = undefined;
            var isOn = evalScheduleNow(nd.scheduleId, nd.type);
            if (isOn == null) return false;
            nd._contactOn = isOn;
            var inVal = getInputValue(nd.id, 'in');
            // 守衛：in 埠有連線但值為 null
            var inEdgeSch = canvasEdges.find(function(e) { return e.target === nd.id && e.targetPort === 'in'; });
            if (inEdgeSch && inVal == null) {
                var srcSch = canvasNodes.find(function(n) { return n.id === inEdgeSch.source; });
                if (srcSch && srcSch.type !== 'input' && srcSch.type !== 'constant' && !srcSch._evalDone) return false;
                nd._contactResult = null;
                return true;
            }
            if (isOn) {
                nd._contactResult = inVal != null ? inVal : 1;
            } else {
                nd._contactResult = inVal != null ? null : 0;  // 有 in 斷路不傳值，無 in 送 0
            }
            return true;
        }
        // ── A接點（常開）/ B接點（常閉）— 點位模式 ──
        if ((nd.type === 'contact_no' || nd.type === 'contact_nc') && nd.sid) {
            nd._contactResult = null;
            nd._contactOn = undefined;
            if (hasUpstreamBad(nd.id)) return false;
            const lv = _realtimeCache[nd.sid];
            if (!lv) return false;
            const pointVal = parseFloat(lv.value);
            if (isNaN(pointVal)) return false;
            // A接點：值===1 導通；B接點：值===0 導通
            const isOn = nd.type === 'contact_no' ? (pointVal === 1) : (pointVal === 0);
            nd._contactOn = isOn;
            const inVal = getInputValue(nd.id, 'in');
            // 守衛：in 埠有連線但值為 null
            var inEdgePt = canvasEdges.find(function(e) { return e.target === nd.id && e.targetPort === 'in'; });
            if (inEdgePt && inVal == null) {
                var srcPt = canvasNodes.find(function(n) { return n.id === inEdgePt.source; });
                if (srcPt && srcPt.type !== 'input' && srcPt.type !== 'constant' && !srcPt._evalDone) return false;
                nd._contactResult = null;
                return true;
            }
            if (isOn) {
                nd._contactResult = inVal != null ? inVal : 1;   // 導通：透傳輸入值，無輸入則 1
            } else {
                nd._contactResult = inVal != null ? null : 0;    // 有 in 斷路不傳值，無 in 送 0
            }
            return true;
        }

        // ── 計數器節點（CTU）──
        if (nd.type === 'counter') {
            // preset：優先輸入腳，其次節點設定
            let preset = nd.presetValue != null ? nd.presetValue : 10;
            const presetEdge = canvasEdges.find(e => e.target === nd.id && e.targetPort === 'preset');
            if (presetEdge) {
                const pv = getNodeOutputValue(presetEdge.source, presetEdge.sourcePort);
                if (pv != null) preset = Math.max(1, Math.floor(pv));
                else {
                    const srcP = canvasNodes.find(n => n.id === presetEdge.source);
                    if (srcP && srcP.type !== 'input' && srcP.type !== 'constant' && !srcP._evalDone) return false;
                }
            }
            if (preset < 1) preset = 1;

            // cu：標準等待，上游 Bad 保留 prevCu
            const cuEdge = canvasEdges.find(e => e.target === nd.id && e.targetPort === 'cu');
            let cuVal = null;
            let isCuBad = false;
            if (cuEdge) {
                const srcCu = canvasNodes.find(n => n.id === cuEdge.source);
                // 用 hasUpstreamBad(cuEdge.source) 檢查 cu 那條鏈
                if (srcCu && hasUpstreamBad(cuEdge.source)) {
                    isCuBad = true;
                } else {
                    const v = getNodeOutputValue(cuEdge.source, cuEdge.sourcePort);
                    if (v != null) cuVal = v;
                    else if (srcCu && srcCu.type !== 'input' && srcCu.type !== 'constant' && !srcCu._evalDone) return false;
                }
            }

            // reset：自回授特例（直接讀 nd._counterQ 上一 tick 的 q），其餘走標準等待
            const resetEdge = canvasEdges.find(e => e.target === nd.id && e.targetPort === 'reset');
            let resetVal = null;
            if (resetEdge) {
                if (resetEdge.source === nd.id) {
                    // 自回授：用上一 tick 的 q（不清除以保留）
                    resetVal = nd._counterQ != null ? nd._counterQ : 0;
                } else {
                    const srcR = canvasNodes.find(n => n.id === resetEdge.source);
                    if (srcR && hasUpstreamBad(resetEdge.source)) {
                        resetVal = null; // Bad → 忽略 reset
                    } else {
                        const v = getNodeOutputValue(resetEdge.source, resetEdge.sourcePort);
                        if (v != null) resetVal = v;
                        else if (srcR && srcR.type !== 'input' && srcR.type !== 'constant' && !srcR._evalDone) return false;
                    }
                }
            }

            // 初始化狀態欄位
            if (nd._counterValue == null) nd._counterValue = 0;
            if (nd._counterLastEdgeAt == null) nd._counterLastEdgeAt = 0;

            const isReset = resetVal != null && resetVal !== 0;
            if (isReset) {
                nd._counterValue = 0;
                if (cuVal != null) nd._counterPrevCu = cuVal;
            } else if (cuVal != null && !isCuBad) {
                if (nd._counterPrevCu == null) {
                    nd._counterPrevCu = cuVal;
                } else {
                    const isEdge = nd._counterPrevCu === 0 && cuVal !== 0;
                    if (isEdge) {
                        const minMs = Math.max(0, nd.cuMinIntervalMs != null ? nd.cuMinIntervalMs : 60000);
                        const now = Date.now();
                        const isFirstEdge = nd._counterLastEdgeAt === 0;
                        if (isFirstEdge || (now - nd._counterLastEdgeAt) >= minMs) {
                            if (nd._counterValue < preset) nd._counterValue++;
                            nd._counterLastEdgeAt = now;
                        }
                    }
                    nd._counterPrevCu = cuVal;
                }
            } else if (cuVal == null && !isCuBad && cuEdge) {
                // 上游已完成但輸出 null → 視為 0
                nd._counterPrevCu = 0;
            }
            // isCuBad：保留 _counterPrevCu

            nd._counterQ = nd._counterValue >= preset ? 1 : 0;
            return true;
        }

        // ── 演算法節點：呼叫後端 API 取得實際計算結果 ──
        if (nd.type === 'algorithm' && nd.operator && ALGO_OPS[nd.operator]) {
            nd._algoResult = null;
            nd._algoReady = false;
            if (hasUpstreamBad(nd.id)) return false;
            var algo = ALGO_OPS[nd.operator];
            var ports = getAlgoPorts(algo, nd.inputCount);
            var algoInputs = ports.inputs;  // [{key, label}, ...]
            var inputValues = {};
            for (var i = 0; i < algoInputs.length; i++) {
                var portKey = algoInputs[i].key;
                var v = getInputValue(nd.id, portKey);
                if (v == null) return false;
                inputValues[portKey] = v;
            }
            nd._algoReady = true;

            // 快取機制：輸入值/N 不變時直接用上次結果
            var nForCache = algo.variadic ? (nd.inputCount || 1) : '_';
            var cacheKey = nd.operator + '|N' + nForCache + '|' + algoInputs.map(function(p) { return p.key + '=' + inputValues[p.key]; }).join('|');
            if (nd._algoCacheKey === cacheKey && nd._algoCachedResult != null) {
                nd._algoResult = nd._algoCachedResult;
                return true;
            }

            // 非同步呼叫後端 API（不阻塞，結果回來後下一個 render 週期顯示）
            if (!nd._algoFetching) {
                nd._algoFetching = true;
                var payload = { inputs: inputValues };
                if (algo.variadic) payload.n = nd.inputCount || 1;
                fetch('/LogicFlow/api/algo-eval/' + encodeURIComponent(nd.operator), {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(payload)
                })
                .then(function(r) { return r.ok ? r.json() : Promise.reject(r.status); })
                .then(function(data) {
                    var res = data.result || data;
                    // 統一保留為 dict（多輸出用 sourcePort 索引）；若回傳純數字則包成 {out: value}
                    var resDict = (typeof res === 'number') ? { out: res } : res;
                    nd._algoCachedResult = resDict;
                    nd._algoCacheKey = cacheKey;
                    nd._algoResult = resDict;
                    // 演算法狀態：merged（codeId/codeName/severity）+ perOutput（每個輸出 port 各自 status）
                    // 舊 response 缺 perOutput → fallback：所有 result key 套 merged，避免畫面崩
                    var mergedCodeId = (data && data.statusCodeId != null) ? data.statusCodeId : 0;
                    var mergedCodeName = (data && data.statusCodeName) ? data.statusCodeName : 'OK';
                    var mergedSeverity = (data && data.severity) ? data.severity : 'Info';
                    var perOutput = (data && data.perOutput && typeof data.perOutput === 'object') ? data.perOutput : null;
                    if (!perOutput) {
                        perOutput = {};
                        Object.keys(resDict).forEach(function(k) {
                            perOutput[k] = { statusCodeId: mergedCodeId, statusCodeName: mergedCodeName, severity: mergedSeverity };
                        });
                    }
                    nd._algoStatus = {
                        codeId: mergedCodeId,
                        codeName: mergedCodeName,
                        severity: mergedSeverity,
                        perOutput: perOutput
                    };
                    nd._algoFetching = false;
                    // 節點本身只在「所有 perOutput 都 Error」時才整顆反灰；
                    // 混合狀態下節點維持正常外觀，由邊線各自反灰／顯 tooltip。
                    var algoEl = document.querySelector('.flow-node[data-node-id="' + nd.id + '"]');
                    if (algoEl) {
                        algoEl.classList.remove('algo-status-bad', 'algo-status-warning', 'algo-status-error');
                        var poKeys = Object.keys(perOutput);
                        var allError = poKeys.length > 0 && poKeys.every(function(k) {
                            return perOutput[k] && perOutput[k].severity === 'Error';
                        });
                        if (allError) {
                            algoEl.classList.add('algo-status-bad', 'algo-status-error');
                            var algoLabel = (ALGO_OPS[nd.operator] && ALGO_OPS[nd.operator].label) || nd.operator;
                            algoEl.title = algoLabel + ' : ' + mergedCodeName + ' (' + mergedSeverity + ')';
                        } else {
                            algoEl.removeAttribute('title');
                        }
                    }
                })
                .catch(function(err) {
                    console.error('algo-eval failed:', nd.operator, err);
                    nd._algoFetching = false;
                });
            }
            return true;
        }

        return false;
    }

    // ── 全部節點求值（多輪迭代，讓任意串接順序都能正確傳播） ──
    function evaluateNodes() {
        markBadInputs();

        const EVAL_TYPES = ['math', 'compare', 'and', 'or', 'not', 'xor', 'timer', 'contact_no', 'contact_nc', 'algorithm', 'counter'];
        const evalNodes = canvasNodes.filter(n => EVAL_TYPES.includes(n.type));

        // 清除舊結果（_timerStartTime、_tpPhase、_counterValue 等狀態機欄位不清除）
        for (const nd of evalNodes) {
            nd._mathResult = null;
            nd._compareResult = null;
            nd._gateResult = null;
            nd._timerResult = null;
            nd._timerDone = false;
            nd._contactResult = null;
            nd._algoResult = null;
            nd._algoReady = false;
            // counter._counterQ 跨 tick 保留以支援 q→reset 自回授；不清掉。
            nd._evalDone = false;  // 通用旗標：本輪是否已完成評估
        }

        // 多輪迭代：每輪嘗試求值所有未完成節點，直到沒有新結果產生
        const maxRounds = evalNodes.length + 1;
        for (let round = 0; round < maxRounds; round++) {
            let changed = false;
            for (const nd of evalNodes) {
                if (nd._evalDone) continue;
                if (evalOneNode(nd)) {
                    nd._evalDone = true;
                    changed = true;
                }
            }
            if (!changed) break;
        }

        // 輸出節點狀態更新（display-only，控制命令由 Engine 後端執行）
        for (const nd of canvasNodes) {
            if (nd.type !== 'output' || !nd.sid) continue;
            const val = getInputValue(nd.id, 'in');
            const isGreen = !hasUpstreamBad(nd.id) && val != null;
            const prev = _outputPrevState[nd.id] || { green: false, value: null };
            const ctrl = _controlModeCache[nd.sid];
            const isManual = ctrl && !ctrl.isAuto;
            const fMin = nd.fMin ?? 0;
            const fMax = nd.fMax ?? 100;
            nd._isOutOfRange = isGreen && (val < fMin || val > fMax);
            // 前端僅記錄狀態供顯示，不送出控制命令（Engine BackgroundService 負責）
            if (_isLogicEnabled && isGreen && (!prev.green || val !== prev.value) && !isManual && !nd._isOutOfRange) {
                console.log('[LogicFlow] Engine \u57f7\u884c\u4e2d\uff0c\u524d\u7aef\u4e0d\u9001\u51fa:', nd.sid, '=', val);
            }
            _outputPrevState[nd.id] = { green: isGreen, value: isGreen ? val : null };
        }

        // 更新數學運算節點的注入值顯示
        const cvs = document.getElementById('diagramCanvas');
        if (cvs) {
            for (const nd of canvasNodes) {
                if (nd.type !== 'math' || !nd.operator || !MATH_OPS[nd.operator] || !MATH_OPS[nd.operator].hasValue) continue;
                const dispEl = cvs.querySelector(`.math-value-display[data-node-id="${nd.id}"]`);
                if (!dispEl) continue;
                const valFromPort = getInputValue(nd.id, 'val');
                dispEl.textContent = valFromPort != null ? fmtNum(valFromPort) : '--';
            }

            // 更新比較節點的注入值顯示
            for (const nd of canvasNodes) {
                if (nd.type !== 'compare') continue;
                for (const field of ['a', 'b']) {
                    const dispEl = cvs.querySelector(`.compare-value-display[data-node-id="${nd.id}"][data-field="${field}"]`);
                    if (!dispEl) continue;
                    const injVal = getInputValue(nd.id, field);
                    dispEl.textContent = injVal != null ? fmtNum(injVal) : '--';
                }
            }

            // 更新計數器節點的 cv/q 即時顯示
            for (const nd of canvasNodes) {
                if (nd.type !== 'counter') continue;
                const liveEl = cvs.querySelector(`.counter-live[data-node-id="${nd.id}"]`);
                if (!liveEl) continue;
                const cvText = nd._counterValue != null ? String(nd._counterValue) : '0';
                const qText = nd._counterQ === 1 ? '1' : '0';
                liveEl.textContent = `cv:${cvText} / q:${qText}`;
            }

            // 更新計時器節點的注入值顯示
            for (const nd of canvasNodes) {
                if (nd.type !== 'timer') continue;
                const timerFields = (nd.operator === 'ton' || nd.operator === 'tpr') ? ['delay'] : ['delay', 'hold'];
                for (const field of timerFields) {
                    const dispEl = cvs.querySelector(`.timer-value-display[data-node-id="${nd.id}"][data-field="${field}"]`);
                    if (!dispEl) continue;
                    const injVal = getInputValue(nd.id, field);
                    const defVal = field === 'delay' ? (nd.timerDelay ?? 5) : (nd.timerHold ?? 2);
                    dispEl.textContent = fmtNum(injVal != null ? injVal : defVal);
                }
            }
        }

        renderEdges();
    }

    // ── 送出輸出控制命令 ──
    function sendOutputControl(szCid, dValue) {
        fetch('/api/control/write', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ cid: szCid, value: dValue, mode: 'logicflow' })
        }).then(res => {
            if (!res.ok) console.warn('[LogicFlow] \u63a7\u5236\u5beb\u5165\u5931\u6557:', szCid, res.statusText);
            else console.log('[LogicFlow] \u63a7\u5236\u5beb\u5165:', szCid, '=', dValue);
        }).catch(err => console.warn('[LogicFlow] \u63a7\u5236\u5beb\u5165\u7570\u5e38:', szCid, err));
    }

    function renderCanvasNodes() {
        const canvas = document.getElementById('diagramCanvas');
        if (!canvas) return;
        canvas.querySelectorAll('.flow-node').forEach(el => el.remove());

        // 確保 SVG 連線層存在
        if (!canvas.querySelector('svg.edge-layer')) {
            const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
            svg.classList.add('edge-layer');
            svg.setAttribute('style', 'position:absolute;top:0;left:0;width:100%;height:100%;pointer-events:none;z-index:5;');
            canvas.insertBefore(svg, canvas.firstChild);
        }

        for (const n of canvasNodes) {
            const meta = NODE_META[n.type] || { icon: 'fas fa-question', label: n.type, inputs: [], outputs: [] };
            const el = document.createElement('div');
            el.className = `flow-node node-${n.type}${selectedNodeIds.has(n.id) ? ' node-selected' : ''}`;
            el.dataset.nodeId = n.id;
            el.style.left = n.x + 'px';
            el.style.top = n.y + 'px';
            // 使用者手動調整過的高度（目前僅 variadic 演算法支援）
            if (n.height) el.style.height = n.height + 'px';
            let label = n.pointName ? n.pointName : meta.label;

            // compare 節點：a值 運算子 b值（唯讀顯示）
            if (n.type === 'compare' && n.operator && COMPARE_OPS[n.operator]) {
                el.innerHTML = `<span class="compare-value-display" data-node-id="${n.id}" data-field="a">--</span>`
                    + `<span class="compare-op-symbol">${escHtml(COMPARE_OPS[n.operator].symbol)}</span>`
                    + `<span class="compare-value-display" data-node-id="${n.id}" data-field="b">--</span>`;
            // constant 節點：icon + textbox
            } else if (n.type === 'constant') {
                const val = n.constValue != null ? n.constValue : 0;
                el.innerHTML = `<i class="${meta.icon}"></i>`
                    + `<input type="text" class="const-value-input" value="${fmtNum(val)}" data-node-id="${n.id}" data-raw="${val}">`;
            // math 節點：運算子符號 + 唯讀注入值顯示
            } else if (n.type === 'math' && n.operator && MATH_OPS[n.operator]) {
                const mop = MATH_OPS[n.operator];
                let html = `<span class="math-op-badge">${escHtml(mop.symbol)}</span>`;
                if (mop.hasValue) {
                    html += `<span class="math-value-display" data-node-id="${n.id}">--</span>`;
                } else {
                    html += `<span style="font-size:.75rem">${escHtml(mop.label)}</span>`;
                }
                el.innerHTML = html;
            // timer 節點：TP 顯示延+持；TON/TPR 只顯示延
            } else if (n.type === 'timer') {
                const delEdge = canvasEdges.find(e => e.target === n.id && e.targetPort === 'delay');
                const del = delEdge ? (getNodeOutputValue(delEdge.source, delEdge.sourcePort) ?? n.timerDelay ?? 5) : (n.timerDelay ?? 5);
                const delHtml = delEdge
                    ? `<span class="timer-value-display" data-node-id="${n.id}" data-field="delay">${fmtNum(del)}</span>`
                    : `<input type="text" class="timer-value-input" value="${fmtNum(del)}" data-node-id="${n.id}" data-field="delay" data-raw="${del}">`;
                const top = n.operator && TIMER_OPS[n.operator] ? TIMER_OPS[n.operator] : TIMER_OPS['tp'];
                if (n.operator === 'ton' || n.operator === 'tpr') {
                    // TON/TPR：只顯示「延」
                    el.innerHTML = `<i class="${meta.icon}"></i>`
                        + `<span class="timer-type-badge">${escHtml(top.symbol)}</span>`
                        + `<span class="timer-label">\u5ef6</span>`
                        + delHtml
                        + `<span class="timer-unit">\u79d2</span>`;
                } else {
                    // TP：顯示延+持
                    const hldEdge = canvasEdges.find(e => e.target === n.id && e.targetPort === 'hold');
                    const hld = hldEdge ? (getNodeOutputValue(hldEdge.source, hldEdge.sourcePort) ?? n.timerHold ?? 2) : (n.timerHold ?? 2);
                    const hldHtml = hldEdge
                        ? `<span class="timer-value-display" data-node-id="${n.id}" data-field="hold">${fmtNum(hld)}</span>`
                        : `<input type="text" class="timer-value-input" value="${fmtNum(hld)}" data-node-id="${n.id}" data-field="hold" data-raw="${hld}">`;
                    el.innerHTML = `<i class="${meta.icon}"></i>`
                        + `<span class="timer-type-badge">${escHtml(top.symbol)}</span>`
                        + `<span class="timer-label">\u5ef6</span>`
                        + delHtml
                        + `<span class="timer-label">\u6301</span>`
                        + hldHtml
                        + `<span class="timer-unit">\u79d2</span>`;
                }
            // counter 節點：圖示 + CTU + preset 編輯 + cv 即時顯示
            } else if (n.type === 'counter') {
                const presetVal = n.presetValue != null ? n.presetValue : 10;
                const intervalSec = Math.round(((n.cuMinIntervalMs != null ? n.cuMinIntervalMs : 60000)) / 1000);
                const cvText = n._counterValue != null ? String(n._counterValue) : '0';
                const qText = n._counterQ === 1 ? '1' : '0';
                el.innerHTML = `<i class="${meta.icon}"></i>`
                    + `<span class="counter-type-badge">CTU</span>`
                    + `<span class="counter-label">≥</span>`
                    + `<input type="text" class="counter-preset-input" value="${presetVal}" data-node-id="${n.id}" data-field="presetValue" data-raw="${presetVal}" title="達到這個數字後 q=1">`
                    + `<span class="counter-label">間隔</span>`
                    + `<input type="text" class="counter-interval-input" value="${intervalSec}" data-node-id="${n.id}" data-field="cuMinIntervalSec" data-raw="${intervalSec}" title="cu 邊緣最小間隔（秒）">`
                    + `<span class="counter-unit">秒</span>`
                    + `<div class="node-live-value counter-live" data-node-id="${n.id}">cv:${cvText} / q:${qText}</div>`;
            // algorithm 節點：演算法符號 + 名稱
            } else if (n.type === 'algorithm' && n.operator && ALGO_OPS[n.operator]) {
                const aop = ALGO_OPS[n.operator];
                const langIcon = aop.language === 'csharp' ? ' \u26a1' : '';
                let aHtml = `<span class="algo-op-badge">${escHtml(aop.symbol)}</span>`
                    + `<span style="font-size:.75rem">${escHtml(aop.label)}${langIcon}</span>`;
                if (aop.variadic) {
                    const nVal = n.inputCount || 1;
                    aHtml += `<input type="number" class="algo-n-input" min="1" value="${nVal}" data-node-id="${n.id}" title="數量">`;
                }
                // 節點反灰策略：僅當「所有 perOutput 都 Error」才整顆反灰，
                // 混合狀態（部分 Error、部分 OK）下節點維持正常外觀，由邊線各自反灰 + tooltip。
                el.classList.remove('algo-status-bad', 'algo-status-warning', 'algo-status-error');
                if (n._algoStatus && n._algoStatus.perOutput) {
                    const poKeys = Object.keys(n._algoStatus.perOutput);
                    const allError = poKeys.length > 0 && poKeys.every(k =>
                        n._algoStatus.perOutput[k] && n._algoStatus.perOutput[k].severity === 'Error');
                    if (allError) {
                        el.classList.add('algo-status-bad', 'algo-status-error');
                        const nodeName = aop.label || n.operator;
                        el.title = nodeName + ' : ' + n._algoStatus.codeName + ' (' + n._algoStatus.severity + ')';
                    } else {
                        el.removeAttribute('title');
                    }
                } else {
                    el.removeAttribute('title');
                }
                el.innerHTML = aHtml;
            } else if ((n.type === 'contact_no' || n.type === 'contact_nc') && n.scheduleId != null) {
                // 排程綁定的接點
                const isOn = evalScheduleNow(n.scheduleId, n.type);
                const contactState = isOn != null ? (isOn ? '\u25cf ON' : '\u25cb OFF') : '--';
                const stateClass = isOn != null ? (isOn ? 'contact-on' : 'contact-off') : '';
                el.innerHTML = `<i class="${meta.icon}"></i><span>${escHtml(n.scheduleName || '\u6392\u7a0b')}</span>`
                    + `<span class="contact-state ${stateClass}">${contactState}</span>`
                    + `<div class="node-live-value"><i class="fas fa-calendar-alt me-1"></i>\u6392\u7a0b</div>`;
            } else if ((n.type === 'contact_no' || n.type === 'contact_nc') && n.sid) {
                const lv = _realtimeCache[n.sid];
                const valText = lv ? fmtNum(lv.value) : '--';
                const unitText = n.unit ? ` ${n.unit}` : '';
                const badClass = (lv && lv.quality === 'Bad') ? ' quality-bad' : '';
                const isOn = lv ? (n.type === 'contact_no' ? parseFloat(lv.value) === 1 : parseFloat(lv.value) === 0) : false;
                const contactState = lv ? (isOn ? '\u25cf ON' : '\u25cb OFF') : '--';
                const stateClass = lv ? (isOn ? 'contact-on' : 'contact-off') : '';
                el.innerHTML = `<i class="${meta.icon}"></i><span>${escHtml(label)}</span>`
                    + `<span class="contact-state ${stateClass}">${contactState}</span>`
                    + `<div class="node-live-value${badClass}" data-sid="${escHtml(n.sid)}">${escHtml(valText)}${escHtml(unitText)}</div>`;
            } else if ((n.type === 'contact_no' || n.type === 'contact_nc') && !n.sid && n.scheduleId == null) {
                // 無 SID、無排程的接點（純邏輯控制或未設定）
                el.innerHTML = `<i class="${meta.icon}"></i><span>${escHtml(label)}</span>`
                    + `<span class="contact-state">--</span>`
                    + `<div class="node-live-value"><i class="fas fa-project-diagram me-1"></i>\u908f\u8f2f\u63a7\u5236</div>`;
            } else if ((n.type === 'input' || n.type === 'output') && n.sid) {
                const lv = _realtimeCache[n.sid];
                const valText = lv ? fmtNum(lv.value) : '--';
                const unitText = n.unit ? ` ${n.unit}` : '';
                const badClass = (lv && lv.quality === 'Bad') ? ' quality-bad' : '';
                let modeBadgeHtml = '';
                if (n.type === 'output') {
                    const ctrl = _controlModeCache[n.sid];
                    const isManual = ctrl && !ctrl.isAuto;
                    const modeClass = isManual ? 'mode-manual' : 'mode-auto';
                    const modeText = isManual ? '\u624b\u52d5' : '\u81ea\u52d5';
                    modeBadgeHtml = `<span class="node-mode-badge ${modeClass}" data-sid="${escHtml(n.sid)}">${modeText}</span>`;
                    if (isManual) el.classList.add('output-manual');
                }
                el.innerHTML = `<i class="${meta.icon}"></i><span>${escHtml(label)}</span>`
                    + modeBadgeHtml
                    + `<div class="node-live-value${badClass}" data-sid="${escHtml(n.sid)}">${escHtml(valText)}${escHtml(unitText)}</div>`;
            } else {
                el.innerHTML = `<i class="${meta.icon}"></i><span>${escHtml(label)}</span>`;
            }

            // 輸入埠（左側圓點）— algorithm 節點使用動態展開 inputs（含 variadic N 倍展開）
            let nodeInputPorts;
            let nodeOutputPorts;
            if (n.type === 'algorithm' && n.operator && ALGO_OPS[n.operator]) {
                const ports = getAlgoPorts(ALGO_OPS[n.operator], n.inputCount);
                nodeInputPorts = ports.inputs;
                nodeOutputPorts = ports.outputs;
            } else {
                const portLbl = meta.portLabels || {};
                nodeInputPorts = (meta.inputs || []).map(k => ({ key: k, label: portLbl[k] || k }));
                nodeOutputPorts = (meta.outputs || []).map(k => ({ key: k, label: portLbl[k] || k }));
            }

            // variadic 演算法：每組 repeat 埠用虛線框圍起來（N≥2 才顯示，hover 出 tooltip）
            if (n.type === 'algorithm' && n.operator && ALGO_OPS[n.operator] && ALGO_OPS[n.operator].variadic) {
                const ranges = getAlgoGroupRanges(ALGO_OPS[n.operator], n.inputCount, nodeInputPorts, nodeOutputPorts);
                ranges.forEach(r => {
                    const fr = document.createElement('div');
                    fr.className = 'algo-group-frame';
                    fr.style.top = `calc(${r.topPct}% - 10px)`;
                    fr.style.height = `calc(${r.bottomPct - r.topPct}% + 20px)`;
                    fr.title = `組 ${r.index}`;
                    el.appendChild(fr);
                });
            }

            nodeInputPorts.forEach((p, i, arr) => {
                const pe = document.createElement('div');
                pe.className = 'flow-port flow-port-in';
                pe.dataset.port = p.key; pe.dataset.nodeId = n.id; pe.dataset.dir = 'in';
                const pct = arr.length === 1 ? 50 : (20 + i * (60 / (arr.length - 1)));
                pe.style.top = `calc(${pct}% - 5px)`;
                pe.title = p.label;
                pe.addEventListener('mousedown', onPortMouseDown);
                el.appendChild(pe);
            });

            // 輸出埠（右側圓點）
            nodeOutputPorts.forEach((p, i, arr) => {
                const pe = document.createElement('div');
                pe.className = 'flow-port flow-port-out';
                pe.dataset.port = p.key; pe.dataset.nodeId = n.id; pe.dataset.dir = 'out';
                pe.style.top = `calc(${arr.length === 1 ? 50 : 20 + i * (60 / (arr.length - 1))}% - 5px)`;
                pe.title = p.label;
                pe.addEventListener('mousedown', onPortMouseDown);
                el.appendChild(pe);
            });

            // 數學運算底部注入埠（val）
            if (n.type === 'math' && n.operator && MATH_OPS[n.operator] && MATH_OPS[n.operator].hasValue) {
                const bp = document.createElement('div');
                bp.className = 'flow-port flow-port-in flow-port-bottom';
                bp.dataset.port = 'val'; bp.dataset.nodeId = n.id; bp.dataset.dir = 'in';
                bp.title = 'val';
                bp.addEventListener('mousedown', onPortMouseDown);
                el.appendChild(bp);
            }

            // 計時器底部注入埠：TON/TPR 只有 delay；TP 有 delay + hold
            if (n.type === 'timer') {
                const timerPorts = (n.operator === 'ton' || n.operator === 'tpr') ? ['delay'] : ['delay', 'hold'];
                timerPorts.forEach((pName, i) => {
                    const bp = document.createElement('div');
                    bp.className = 'flow-port flow-port-in flow-port-bottom';
                    bp.dataset.port = pName; bp.dataset.nodeId = n.id; bp.dataset.dir = 'in';
                    if (timerPorts.length === 1) {
                        bp.style.left = 'calc(50% - 5px)';
                    } else {
                        bp.style.left = `calc(${33 + i * 34}% - 5px)`;
                    }
                    bp.title = pName === 'delay' ? '\u5ef6' : '\u6301';
                    bp.addEventListener('mousedown', onPortMouseDown);
                    el.appendChild(bp);
                });
            }

            // A/B 接點底部控制埠：可接邏輯閘(0/1)控制導通
            if (n.type === 'contact_no' || n.type === 'contact_nc') {
                const hasBinding = !!(n.sid || n.scheduleId != null && n.scheduleId !== undefined);
                const cp = document.createElement('div');
                cp.className = 'flow-port flow-port-in flow-port-bottom' + (hasBinding ? ' port-disabled' : '');
                cp.dataset.port = 'ctrl'; cp.dataset.nodeId = n.id; cp.dataset.dir = 'in';
                cp.style.left = 'calc(50% - 5px)';
                cp.title = hasBinding ? '\u5df2\u7d81\u5b9a\u9ede\u4f4d/\u6392\u7a0b\uff0c\u4e0d\u53ef\u4f7f\u7528' : '\u63a7\u5236';
                cp.addEventListener('mousedown', onPortMouseDown);
                el.appendChild(cp);
            }

            // 雙擊 input/output/contact_no 節點 → 編輯點位
            if (n.type === 'input' || n.type === 'output' || n.type === 'contact_no' || n.type === 'contact_nc') {
                el.addEventListener('dblclick', (e) => {
                    e.preventDefault();
                    ppEditNodeId = n.id;
                    ppPendingType = n.type;
                    ppPendingPos = { x: n.x, y: n.y };
                    openPointPicker();
                });
            }

            // constant textbox：存值 + 阻止拖曳 + 千分位 focus/blur
            el.querySelectorAll('.const-value-input').forEach(inp => {
                inp.addEventListener('mousedown', (e) => e.stopPropagation());
                inp.addEventListener('focus', (e) => { e.target.value = e.target.dataset.raw ?? e.target.value; e.target.select(); });
                inp.addEventListener('blur', (e) => {
                    const raw = parseFloat(e.target.value) || 0;
                    e.target.dataset.raw = raw;
                    e.target.value = fmtNum(raw);
                    const nid = parseInt(e.target.dataset.nodeId);
                    const nd = canvasNodes.find(x => x.id === nid);
                    if (nd) {
                        if (e.target.classList.contains('const-value-input')) nd.constValue = raw;
                    }
                });
            });

            // counter preset / interval textbox：存值 + 阻止拖曳
            el.querySelectorAll('.counter-preset-input, .counter-interval-input').forEach(inp => {
                inp.addEventListener('mousedown', (e) => e.stopPropagation());
                inp.addEventListener('focus', (e) => { e.target.value = e.target.dataset.raw ?? e.target.value; e.target.select(); });
                inp.addEventListener('blur', (e) => {
                    const raw = parseInt(e.target.value, 10);
                    const field = e.target.dataset.field;
                    const nid = parseInt(e.target.dataset.nodeId);
                    const ndRef = canvasNodes.find(x => x.id === nid);
                    if (!ndRef) return;
                    if (field === 'presetValue') {
                        const val = (isNaN(raw) || raw < 1) ? 1 : raw;
                        ndRef.presetValue = val;
                        e.target.dataset.raw = val;
                        e.target.value = String(val);
                    } else if (field === 'cuMinIntervalSec') {
                        const valSec = (isNaN(raw) || raw < 0) ? 0 : raw;
                        ndRef.cuMinIntervalMs = valSec * 1000;
                        e.target.dataset.raw = valSec;
                        e.target.value = String(valSec);
                    }
                });
            });

            // timer textbox：存值 + 阻止拖曳 + 千分位 focus/blur
            el.querySelectorAll('.timer-value-input').forEach(inp => {
                inp.addEventListener('mousedown', (e) => e.stopPropagation());
                inp.addEventListener('focus', (e) => { e.target.value = e.target.dataset.raw ?? e.target.value; e.target.select(); });
                inp.addEventListener('blur', (e) => {
                    const raw = parseFloat(e.target.value);
                    const val = (isNaN(raw) || raw <= 0) ? 1 : raw;
                    e.target.dataset.raw = val;
                    e.target.value = fmtNum(val);
                    const nid = parseInt(e.target.dataset.nodeId);
                    const field = e.target.dataset.field;
                    const nd = canvasNodes.find(x => x.id === nid);
                    if (nd) {
                        if (field === 'delay') nd.timerDelay = val;
                        else if (field === 'hold') nd.timerHold = val;
                    }
                });
            });

            // variadic algorithm：底部加 resize handle（拖動調整節點高度）
            if (n.type === 'algorithm' && n.operator && ALGO_OPS[n.operator] && ALGO_OPS[n.operator].variadic) {
                const handle = document.createElement('div');
                handle.className = 'flow-node-resize-handle';
                handle.title = '拖動調整高度';
                handle.addEventListener('mousedown', (ev) => onResizeHandleMouseDown(ev, n.id));
                el.appendChild(handle);
            }

            // variadic algorithm 的 N 輸入框：改 N → 更新 inputCount + 清掉超出範圍的連線 + 重繪
            el.querySelectorAll('.algo-n-input').forEach(inp => {
                inp.addEventListener('mousedown', (e) => e.stopPropagation());
                inp.addEventListener('change', (e) => {
                    const nid = parseInt(e.target.dataset.nodeId);
                    const nd = canvasNodes.find(x => x.id === nid);
                    if (!nd) return;
                    const raw = parseInt(e.target.value, 10);
                    const newN = (isNaN(raw) || raw < 1) ? 1 : raw;
                    nd.inputCount = newN;
                    e.target.value = newN;
                    // 重新展開埠，清掉指向不存在 port 的連線（input 端 + output 端皆需檢查）
                    const aop = ALGO_OPS[nd.operator];
                    if (aop) {
                        const ports = getAlgoPorts(aop, newN);
                        const inputKeys = new Set(ports.inputs.map(p => p.key));
                        const outputKeys = new Set(ports.outputs.map(p => p.key));
                        canvasEdges = canvasEdges.filter(ed => {
                            if (ed.target === nid && !inputKeys.has(ed.targetPort)) return false;
                            if (ed.source === nid && !outputKeys.has(ed.sourcePort)) return false;
                            return true;
                        });
                        // 同步更新節點 algoInputs（執行階段以此為依據）
                        nd.algoInputs = ports.inputs.map(p => p.key);
                    }
                    // 清掉演算法快取，避免 N 變動後仍套用舊結果
                    nd._algoResult = null;
                    nd._algoCachedResult = null;
                    nd._algoCacheKey = null;
                    nd._algoFetching = false;
                    nd._algoStatus = null;
                    renderCanvasNodes();
                });
            });

            el.addEventListener('mousedown', startDrag);
            canvas.appendChild(el);
        }
        renderEdges();
        updateCanvasSize();
        startTimerEval(); // 有 timer 節點時啟動 1 秒 interval
    }

    // =========== 畫布自動延伸 ===========
    function updateCanvasSize() {
        const canvas = document.getElementById('diagramCanvas');
        if (!canvas) return;
        const PAD = 120;
        let maxR = 0, maxB = 0;
        canvas.querySelectorAll('.flow-node').forEach(el => {
            maxR = Math.max(maxR, el.offsetLeft + el.offsetWidth);
            maxB = Math.max(maxB, el.offsetTop + el.offsetHeight);
        });
        maxR += PAD;
        maxB += PAD;

        // SVG 連線層需覆蓋整個可捲動區域
        const svg = canvas.querySelector('svg.edge-layer');
        if (svg) {
            svg.style.width = Math.max(maxR, canvas.clientWidth) + 'px';
            svg.style.height = Math.max(maxB, canvas.clientHeight) + 'px';
        }
    }

    // =========== 連線 (Edges) ===========
    function getPortPos(nodeId, portName) {
        const canvas = document.getElementById('diagramCanvas');
        if (!canvas) return null;
        const portEl = canvas.querySelector(`.flow-port[data-node-id="${nodeId}"][data-port="${portName}"]`);
        if (!portEl) return null;
        const cr = canvas.getBoundingClientRect();
        const pr = portEl.getBoundingClientRect();
        return { x: pr.left + pr.width / 2 - cr.left + canvas.scrollLeft, y: pr.top + pr.height / 2 - cr.top + canvas.scrollTop };
    }

    function bezierPath(x1, y1, x2, y2) {
        const dx = Math.max(Math.abs(x2 - x1) * 0.5, 40);
        return `M${x1},${y1} C${x1+dx},${y1} ${x2-dx},${y2} ${x2},${y2}`;
    }

    // 底部注入埠專用：從來源右側出發，彎入目標下方（箭頭朝上）
    function bezierPathToBottom(x1, y1, x2, y2) {
        const dx = Math.max(Math.abs(x2 - x1) * 0.3, 30);
        const dy = Math.max(Math.abs(y2 - y1) * 0.5, 40);
        return `M${x1},${y1} C${x1+dx},${y1} ${x2},${y2+dy} ${x2},${y2}`;
    }

    function renderEdges() {
        const canvas = document.getElementById('diagramCanvas');
        if (!canvas) return;
        const svg = canvas.querySelector('svg.edge-layer');
        if (!svg) return;

        let html = '<defs>'
            + '<marker id="ah" markerWidth="8" markerHeight="6" refX="8" refY="3" orient="auto"><polygon points="0 0,8 3,0 6" fill="#6c757d"/></marker>'
            + '<marker id="ah-s" markerWidth="8" markerHeight="6" refX="8" refY="3" orient="auto"><polygon points="0 0,8 3,0 6" fill="#0d6efd"/></marker>'
            + '<marker id="ah-ok" markerWidth="8" markerHeight="6" refX="8" refY="3" orient="auto"><polygon points="0 0,8 3,0 6" fill="#198754"/></marker>'
            + '<marker id="ah-bad" markerWidth="8" markerHeight="6" refX="8" refY="3" orient="auto"><polygon points="0 0,8 3,0 6" fill="#dc3545"/></marker>'
            + '<marker id="ah-warn" markerWidth="8" markerHeight="6" refX="8" refY="3" orient="auto"><polygon points="0 0,8 3,0 6" fill="#fd7e14"/></marker>'
            + '</defs>';

        for (const edge of canvasEdges) {
            const from = getPortPos(edge.source, edge.sourcePort);
            const to = getPortPos(edge.target, edge.targetPort);
            if (!from || !to) continue;
            const isBottomPort = edge.targetPort === 'val' || edge.targetPort === 'delay' || edge.targetPort === 'hold' || edge.targetPort === 'ctrl';
            const d = isBottomPort
                ? bezierPathToBottom(from.x, from.y, to.x, to.y)
                : bezierPath(from.x, from.y, to.x, to.y);
            const sel = edge.id === selectedEdgeId;

            // 決定邊線顏色
            let edgeColor = '#6c757d';
            let edgeMarker = 'ah';
            let edgeLabel = null; // 邊線上的文字
            let edgeTooltip = null; // SVG <title> 子元素內容（hover 邊線顯示）
            if (sel) {
                edgeColor = '#0d6efd'; edgeMarker = 'ah-s';
            } else {
                const srcNode = canvasNodes.find(n => n.id === edge.source);
                if (srcNode) {
                    // input 節點 Bad 品質 → 紅色；有值 → 綠色
                    if (srcNode.type === 'input' && srcNode._isBad) {
                        edgeColor = '#dc3545'; edgeMarker = 'ah-bad';
                    } else if (srcNode.type === 'input' && srcNode.sid) {
                        const lv = _realtimeCache[srcNode.sid];
                        if (lv != null && lv.value != null) {
                            edgeColor = '#198754'; edgeMarker = 'ah-ok';
                            edgeLabel = fmtNum(parseFloat(lv.value));
                        }
                    // 常數節點 → 綠色
                    } else if (srcNode.type === 'constant') {
                        edgeColor = '#198754'; edgeMarker = 'ah-ok';
                        edgeLabel = fmtNum(srcNode.constValue != null ? parseFloat(srcNode.constValue) : 0);
                    // 比較節點
                    } else if (srcNode.type === 'compare') {
                        if (srcNode._compareResult === true) {
                            edgeColor = '#198754'; edgeMarker = 'ah-ok';
                            edgeLabel = '1';
                        } else if (srcNode._compareResult === false) {
                            edgeColor = '#adb5bd';
                            edgeLabel = '0';
                        } else {
                            edgeColor = '#adb5bd';
                        }
                    // 數學運算節點
                    } else if (srcNode.type === 'math') {
                        if (srcNode._mathResult != null) {
                            edgeColor = '#198754'; edgeMarker = 'ah-ok';
                            edgeLabel = fmtNum(srcNode._mathResult);
                        } else {
                            edgeColor = '#adb5bd';
                        }
                    // 邏輯閘節點
                    } else if (['and','or','not','xor'].includes(srcNode.type)) {
                        if (srcNode._gateResult != null) {
                            edgeColor = '#198754'; edgeMarker = 'ah-ok';
                            edgeLabel = String(srcNode._gateResult);
                        } else {
                            edgeColor = '#adb5bd';
                        }
                    // A/B接點節點：以導通狀態決定顏色（導通即綠，不受透傳值 0 影響）
                    } else if (srcNode.type === 'contact_no' || srcNode.type === 'contact_nc') {
                        if (srcNode._contactOn === true && srcNode._contactResult != null) {
                            edgeColor = '#198754'; edgeMarker = 'ah-ok';
                            edgeLabel = fmtNum(srcNode._contactResult);
                        } else if (srcNode._contactOn === false) {
                            edgeColor = '#adb5bd';
                            edgeLabel = srcNode._contactResult === 0 ? '0' : null;
                        } else {
                            edgeColor = '#adb5bd';
                        }
                    // 計時器節點（狀態機版本）
                    } else if (srcNode.type === 'timer') {
                        if (srcNode.operator === 'ton') {
                            // TON 邊線顯示
                            if (srcNode._tonPhase === 'on' && srcNode._timerResult != null) {
                                edgeColor = '#198754'; edgeMarker = 'ah-ok';
                                edgeLabel = fmtNum(srcNode._timerResult);
                            } else if (srcNode._tonPhase === 'timing' && srcNode._tonPhaseEnd) {
                                edgeColor = '#adb5bd';
                                const rem = Math.ceil((srcNode._tonPhaseEnd - Date.now()) / 1000);
                                edgeLabel = Math.max(rem, 0) + 's';
                            } else if (srcNode._timerResult === 0) {
                                edgeColor = '#adb5bd';
                                edgeLabel = '0';
                            } else {
                                edgeColor = '#adb5bd';
                            }
                        } else if (srcNode.operator === 'tpr') {
                            // TPR 邊線顯示
                            if (srcNode._tpPhase === 'confirmed' && srcNode._timerResult != null) {
                                edgeColor = '#198754'; edgeMarker = 'ah-ok';
                                edgeLabel = fmtNum(srcNode._timerResult);
                            } else if (srcNode._tpPhase === 'delay' && srcNode._tpPhaseEnd) {
                                edgeColor = '#adb5bd';
                                const rem = Math.ceil((srcNode._tpPhaseEnd - Date.now()) / 1000);
                                edgeLabel = Math.max(rem, 0) + 's';
                            } else {
                                edgeColor = '#adb5bd';
                            }
                        } else {
                            // TP 邊線顯示
                            if (srcNode._tpPhase === 'hold' && srcNode._timerResult != null) {
                                edgeColor = '#198754'; edgeMarker = 'ah-ok';
                                edgeLabel = fmtNum(srcNode._timerResult);
                            } else if (srcNode._tpPhase === 'delay' && srcNode._tpPhaseEnd) {
                                edgeColor = '#adb5bd';
                                const rem = Math.ceil((srcNode._tpPhaseEnd - Date.now()) / 1000);
                                edgeLabel = Math.max(rem, 0) + 's';
                            } else if (srcNode._timerResult === 0) {
                                edgeColor = '#adb5bd';
                                edgeLabel = '0';
                            } else {
                                edgeColor = '#adb5bd';
                            }
                        }
                    // 計數器節點：q（達 preset 為 1）/ cv（目前累加值）
                    } else if (srcNode.type === 'counter') {
                        if (edge.sourcePort === 'cv') {
                            edgeColor = '#198754'; edgeMarker = 'ah-ok';
                            edgeLabel = String(srcNode._counterValue != null ? srcNode._counterValue : 0);
                        } else {
                            // q
                            const q = srcNode._counterQ != null ? srcNode._counterQ : 0;
                            if (q === 1) {
                                edgeColor = '#198754'; edgeMarker = 'ah-ok';
                                edgeLabel = '1';
                            } else {
                                edgeColor = '#adb5bd';
                                edgeLabel = '0';
                            }
                        }
                    // 演算法節點：per-output 反灰／顏色／tooltip。依 edge.sourcePort 取對應輸出值與 status。
                    } else if (srcNode.type === 'algorithm') {
                        var portKey = edge.sourcePort || 'out';
                        var algoOutVal = null;
                        var resObj = srcNode._algoResult || srcNode._algoCachedResult;
                        if (resObj && typeof resObj === 'object') {
                            algoOutVal = resObj[portKey];
                        }
                        var portStatus = (srcNode._algoStatus && srcNode._algoStatus.perOutput)
                            ? srcNode._algoStatus.perOutput[portKey] : null;
                        var portSev = portStatus ? (portStatus.severity || 'Info') : 'Info';
                        if (portSev === 'Error') {
                            edgeColor = '#dc3545'; edgeMarker = 'ah-bad';
                            if (algoOutVal != null) edgeLabel = fmtNum(algoOutVal);
                        } else if (portSev === 'Warning') {
                            edgeColor = '#fd7e14'; edgeMarker = 'ah-warn';
                            if (algoOutVal != null) edgeLabel = fmtNum(algoOutVal);
                        } else if (algoOutVal != null) {
                            edgeColor = '#198754'; edgeMarker = 'ah-ok';
                            edgeLabel = fmtNum(algoOutVal);
                        } else if (srcNode._algoReady) {
                            edgeColor = '#198754'; edgeMarker = 'ah-ok';
                        } else {
                            edgeColor = '#adb5bd';
                        }
                        // Hover tooltip：非 OK 才顯示「{演算法} : {portKey} : {codeName} ({severity})」
                        if (portStatus && portStatus.statusCodeId !== 0) {
                            var algoLbl = (ALGO_OPS[srcNode.operator] && ALGO_OPS[srcNode.operator].label) || srcNode.operator || '';
                            edgeTooltip = algoLbl + ' : ' + portKey + ' : ' + portStatus.statusCodeName + ' (' + portSev + ')';
                        }
                    }
                }
            }

            // 目標為超限的 output 節點：邊線標紅 + 附加「超限」文字
            const tgtNode = canvasNodes.find(n => n.id === edge.target);
            if (tgtNode && tgtNode.type === 'output' && tgtNode._isOutOfRange && !sel) {
                edgeColor = '#dc3545'; edgeMarker = 'ah-bad';
                if (edgeLabel != null) edgeLabel += ' \u8d85\u9650';
                else edgeLabel = '\u8d85\u9650';
            }

            // 自閉合 <path/> 不能含 <title> 子元素；有 tooltip 時改開合 tag 包 <title>
            if (edgeTooltip != null) {
                html += `<path d="${d}" class="edge-line${sel ? ' selected' : ''}" data-edge-id="${edge.id}"
                          stroke="${edgeColor}" stroke-width="${sel ? 3 : 2}" fill="none"
                          marker-end="url(#${edgeMarker})" style="pointer-events:stroke;cursor:pointer;"><title>${escHtml(edgeTooltip)}</title></path>`;
                html += `<path d="${d}" stroke="transparent" stroke-width="14" fill="none"
                          style="pointer-events:stroke;cursor:pointer;" data-edge-id="${edge.id}"><title>${escHtml(edgeTooltip)}</title></path>`;
            } else {
                html += `<path d="${d}" class="edge-line${sel ? ' selected' : ''}" data-edge-id="${edge.id}"
                          stroke="${edgeColor}" stroke-width="${sel ? 3 : 2}" fill="none"
                          marker-end="url(#${edgeMarker})" style="pointer-events:stroke;cursor:pointer;"/>`;
                html += `<path d="${d}" stroke="transparent" stroke-width="14" fill="none"
                          style="pointer-events:stroke;cursor:pointer;" data-edge-id="${edge.id}"/>`;
            }

            // 邊線上顯示結果文字
            if (edgeLabel != null) {
                const mx = (from.x + to.x) / 2, my = (from.y + to.y) / 2;
                const tw = Math.max(edgeLabel.length * 7.5 + 8, 20);
                html += `<rect x="${mx - tw/2}" y="${my - 9}" width="${tw}" height="18" rx="4" fill="#fff" stroke="${edgeColor}" stroke-width="1"/>`;
                html += `<text x="${mx}" y="${my + 5}" text-anchor="middle" font-size="12" font-weight="700" fill="${edgeColor}">${edgeLabel}</text>`;
            }
        }

        // 拖曳中的臨時虛線
        if (draggingEdge && draggingEdge.tempX != null) {
            const d = bezierPath(draggingEdge.startX, draggingEdge.startY, draggingEdge.tempX, draggingEdge.tempY);
            html += `<path d="${d}" stroke="#0d6efd" stroke-width="2" fill="none" stroke-dasharray="6,3" style="pointer-events:none;"/>`;
        }

        svg.innerHTML = html;

        // 綁定連線點擊事件（切換選取）
        svg.querySelectorAll('[data-edge-id]').forEach(p => {
            p.addEventListener('click', (e) => {
                e.stopPropagation();
                const eid = parseInt(p.dataset.edgeId);
                selectedEdgeId = selectedEdgeId === eid ? null : eid;
                renderEdges();
            });
        });
    }

    function onPortMouseDown(e) {
        const dir = e.currentTarget.dataset.dir;
        if (dir !== 'out') return; // 輸入埠不攔截，讓事件繼續冒泡（可拖曳節點）
        e.stopPropagation();
        e.preventDefault();

        const nodeId = parseInt(e.currentTarget.dataset.nodeId);
        const portName = e.currentTarget.dataset.port;
        const pos = getPortPos(nodeId, portName);
        if (!pos) return;

        draggingEdge = { sourceId: nodeId, sourcePort: portName, startX: pos.x, startY: pos.y, tempX: pos.x, tempY: pos.y };

        function onMove(ev) {
            const cvs = document.getElementById('diagramCanvas');
            const cr = cvs.getBoundingClientRect();
            draggingEdge.tempX = ev.clientX - cr.left + cvs.scrollLeft;
            draggingEdge.tempY = ev.clientY - cr.top + cvs.scrollTop;
            renderEdges();
        }
        function onUp(ev) {
            document.removeEventListener('mousemove', onMove);
            document.removeEventListener('mouseup', onUp);
            const tgt = document.elementFromPoint(ev.clientX, ev.clientY);
            if (tgt && tgt.classList.contains('flow-port') && tgt.dataset.dir === 'in') {
                const tId = parseInt(tgt.dataset.nodeId);
                const tPort = tgt.dataset.port;
                if (tId !== draggingEdge.sourceId) {
                    // ctrl 埠防呆：已設定點位或排程的接點不可使用 ctrl 控制埠
                    if (tPort === 'ctrl') {
                        var tNode = canvasNodes.find(function(nn) { return nn.id === tId; });
                        if (tNode && (tNode.sid || tNode.scheduleId != null)) {
                            alert('\u6b64\u63a5\u9ede\u5df2\u8a2d\u5b9a\u9ede\u4f4d\u6216\u6392\u7a0b\uff0c\u4e0d\u53ef\u4f7f\u7528 ctrl \u63a7\u5236\u57e0\u3002\n\u8acb\u5148\u79fb\u9664\u9ede\u4f4d/\u6392\u7a0b\u8a2d\u5b9a\u518d\u9023\u7dda\u3002');
                            draggingEdge = null;
                            renderEdges();
                            return;
                        }
                    }
                    // 每個 input port 只允許一條連線
                    const occupied = canvasEdges.some(e => e.target === tId && e.targetPort === tPort);
                    if (!occupied) {
                        canvasEdges.push({ id: nextEdgeId++, source: draggingEdge.sourceId, sourcePort: draggingEdge.sourcePort, target: tId, targetPort: tPort });
                    }
                }
            }
            draggingEdge = null;
            renderEdges();
        }
        document.addEventListener('mousemove', onMove);
        document.addEventListener('mouseup', onUp);
    }

    function deleteSelectedEdge() {
        if (selectedEdgeId == null) return;
        canvasEdges = canvasEdges.filter(e => e.id !== selectedEdgeId);
        selectedEdgeId = null;
        renderEdges();
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
                nodeCtxTargetId = parseInt(nodeEl.dataset.nodeId);
                const node = canvasNodes.find(n => n.id === nodeCtxTargetId);
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
                ctxPos = { x: e.clientX - rect.left + canvas.scrollLeft, y: e.clientY - rect.top + canvas.scrollTop };
                showMenuAt(document.getElementById('ctxMenu'), e.clientX, e.clientY);
            }
        });

        // 點擊畫布空白處：關閉選單 / 取消選取 / 框選
        canvas.addEventListener('mousedown', (e) => {
            if (e.target.closest('.flow-node') || e.target.closest('.flow-port')) return;
            hideCtxMenu();
            if (selectedEdgeId != null) { selectedEdgeId = null; renderEdges(); }
            if (e.button !== 0) return; // 只有左鍵啟動框選

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
                    if (!ev.ctrlKey) { selectedNodeIds.clear(); updateNodeSelectionVisual(); }
                    return;
                }
                const cx = ev.clientX - cRect.left + canvas.scrollLeft;
                const cy = ev.clientY - cRect.top + canvas.scrollTop;
                const rx = Math.min(sx, cx), ry = Math.min(sy, cy);
                const rw = Math.abs(cx - sx), rh = Math.abs(cy - sy);
                if (!ev.ctrlKey) selectedNodeIds.clear();
                for (const n of canvasNodes) {
                    const el = canvas.querySelector(`.flow-node[data-node-id="${n.id}"]`);
                    if (!el) continue;
                    if (n.x + el.offsetWidth > rx && n.x < rx + rw && n.y + el.offsetHeight > ry && n.y < ry + rh) {
                        selectedNodeIds.add(n.id);
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
        canvasNodes = canvasNodes.filter(n => n.id !== nodeId);
        canvasEdges = canvasEdges.filter(e => e.source !== nodeId && e.target !== nodeId);
        if (selectedEdgeId != null && !canvasEdges.some(e => e.id === selectedEdgeId)) selectedEdgeId = null;
        renderCanvasNodes();
    }

    function changeNodeType(nodeId, newType, operator) {
        const node = canvasNodes.find(n => n.id === nodeId);
        if (!node) return;
        // 同類型同運算子 → 不處理
        if (node.type === newType && (!operator || node.operator === operator)) return;
        const newMeta = NODE_META[newType] || { inputs: [], outputs: [] };

        // 清除不存在的 port 對應連線
        const keepValPort = newType === 'math' && operator && MATH_OPS[operator] && MATH_OPS[operator].hasValue;
        const keepTimerPorts = newType === 'timer';
        const isTon = newType === 'timer' && operator === 'ton';
        // algorithm 切換：依新演算法 + 既有 inputCount 展開埠（variadic 沿用 node.inputCount，不存在則 2）
        let algoInputPortKeys = [];
        let algoOutputPortKeys = [];
        if (newType === 'algorithm' && operator && ALGO_OPS[operator]) {
            const newAop = ALGO_OPS[operator];
            const newN = newAop.variadic ? (node.inputCount || 2) : null;
            const newPorts = getAlgoPorts(newAop, newN);
            algoInputPortKeys = newPorts.inputs.map(p => p.key);
            algoOutputPortKeys = newPorts.outputs.map(p => p.key);
        }
        canvasEdges = canvasEdges.filter(e => {
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
        if (selectedEdgeId != null && !canvasEdges.some(e => e.id === selectedEdgeId)) selectedEdgeId = null;

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
            const algo = ALGO_OPS[operator];
            if (algo) {
                if (algo.variadic) {
                    if (node.inputCount == null) node.inputCount = 2;
                    const ports = getAlgoPorts(algo, node.inputCount);
                    node.algoInputs = ports.inputs.map(p => p.key);
                } else {
                    delete node.inputCount;
                    node.algoInputs = [...algo.inputs];
                }
            }
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
            // 重置運行時狀態
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
            if (!node.operator || !TIMER_OPS[node.operator]) node.operator = 'tp';
            if (node.operator === 'tp' && node.timerHold == null) node.timerHold = 2;
            // TON/TPR 不需要 hold → 移除 hold 連線
            if (node.operator === 'ton' || node.operator === 'tpr') {
                canvasEdges = canvasEdges.filter(e => !(e.target === node.id && e.targetPort === 'hold'));
            }
            // 重置計時器運行狀態
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
        renderCanvasNodes();
    }

    function addNodeToCanvas(type, operator) {
        hideCtxMenu();
        // 對齊到 20px 網格
        const x = Math.round(ctxPos.x / 20) * 20;
        const y = Math.round(ctxPos.y / 20) * 20;

        // 讀取/寫入點位 → 先彈出點位選擇器
        if (type === 'input' || type === 'output') {
            ppEditNodeId = null;
            ppPendingType = type;
            ppPendingPos = { x, y };
            openPointPicker();
            return;
        }

        // A/B 接點 → 直接建立空白節點（可稍後雙擊設定點位/排程，或用 ctrl 埠控制）
        if (type === 'contact_no' || type === 'contact_nc') {
            canvasNodes.push({ id: nextNodeId++, type, x, y });
            renderCanvasNodes();
            return;
        }

        const node = { id: nextNodeId++, type, x, y };
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
            const algo = ALGO_OPS[operator];
            if (algo) {
                if (algo.variadic) {
                    node.inputCount = 2;
                    const ports = getAlgoPorts(algo, 2);
                    node.algoInputs = ports.inputs.map(p => p.key);
                } else {
                    node.algoInputs = [...algo.inputs];
                }
            }
        }
        canvasNodes.push(node);
        renderCanvasNodes();
    }

    // =========== 點位選擇器 ===========
    let ppAllDevices = null;
    let ppAllPoints  = null;
    let ppPickedDevId = -1;
    let ppPickedModbusId = null;
    let ppPickedSid = null;
    let ppPendingType = null;   // 'input' or 'output'
    let ppPendingPos = { x: 0, y: 0 };
    let ppEditNodeId = null;    // 非 null 表示編輯已有節點的點位
    let ppModal = null;
    let ppAllSchedules = null;
    let ppSourceMode = 'point'; // 'point' | 'schedule'
    let ppPickedScheduleId = null;
    let ppPickedScheduleName = null;
    let ppPickedCalcGroup = null;   // 計算點位群組篩選
    let ppPickedDbCoord = null;     // DB 來源 Coordinator 名稱篩選

    const PP_CALC_DEV_ID = -999;
    const PP_DB_DEV_ID   = -998;

    function getSidPrefix(szSid) {
        const m = szSid.match(/^(\d+)-S\d+$/);
        return m ? parseInt(m[1], 10) : -1;
    }
    function isCalcSid(szSid) {
        return szSid && szSid.indexOf('CALC-') === 0;
    }
    function isDbSid(szSid) {
        return !!szSid && /^DB\d+-S\d+$/.test(szSid);
    }
    function isPointOfDev(szSid, nDevId) {
        if (nDevId === PP_CALC_DEV_ID) return isCalcSid(szSid);
        if (nDevId === PP_DB_DEV_ID)   return isDbSid(szSid);
        if (isCalcSid(szSid) || isDbSid(szSid)) return false;
        const p = getSidPrefix(szSid);
        return p >= nDevId * 65536 && p < (nDevId + 1) * 65536;
    }

    async function ppEnsureData() {
        if (ppAllDevices && ppAllPoints) return;
        const [rDev, rPt] = await Promise.all([
            fetch('/Designer/Devices'), fetch('/Designer/Points')
        ]);
        if (!rDev.ok || !rPt.ok) throw new Error('無法載入設備/點位清單');
        ppAllDevices = await rDev.json();
        ppAllPoints  = await rPt.json();
        // 附加設備名稱到點位
        ppAllPoints.forEach(p => {
            if (isDbSid(p.szSid)) {
                p._devLabel = p.szGroupName || 'DB';
                return;
            }
            if (isCalcSid(p.szSid)) {
                p._devLabel = p.szGroupName || '\u8a08\u7b97\u9ede\u4f4d';
                return;
            }
            const pfx = getSidPrefix(p.szSid);
            for (const d of ppAllDevices) {
                if (!isPointOfDev(p.szSid, d.nId)) continue;
                const mids = (d.szModbusID || '').split(',').map(s => s.trim()).filter(Boolean);
                const names = (d.szDeviceName || '').split(',').map(s => s.trim());
                if (mids.length > 1) {
                    for (let j = 0; j < mids.length; j++) {
                        const base = d.nId * 65536 + parseInt(mids[j], 10) * 256;
                        if (pfx >= base && pfx < base + 256) {
                            p._devLabel = (j < names.length && names[j]) ? names[j] : d.szName;
                            break;
                        }
                    }
                } else {
                    p._devLabel = d.szName;
                }
                break;
            }
        });
    }

    async function openPointPicker() {
        ppPickedSid = null;
        ppPickedDevId = -1;
        ppPickedModbusId = null;
        ppPickedScheduleId = null;
        ppPickedScheduleName = null;
        ppSourceMode = 'point';
        document.getElementById('btnConfirmPoint').disabled = true;
        try {
            await ppEnsureData();
        } catch (e) { alert(e.message); return; }
        if (!ppModal) ppModal = new bootstrap.Modal(document.getElementById('pointPickerModal'));

        // contact 類型才顯示來源切換
        const isContact = (ppPendingType === 'contact_no' || ppPendingType === 'contact_nc');
        const toggleEl = document.getElementById('ppSourceToggle');
        if (isContact) { toggleEl.classList.remove('d-none'); } else { toggleEl.classList.add('d-none'); }
        document.getElementById('ppBtnPoint').classList.add('active');
        document.getElementById('ppBtnSchedule').classList.remove('active');
        document.getElementById('ppStep3').style.display = 'none';

        // 編輯模式：檢查是排程還是點位
        if (ppEditNodeId != null) {
            const editNode = canvasNodes.find(n => n.id === ppEditNodeId);
            // 排程模式
            if (editNode && editNode.scheduleId != null) {
                ppSwitchSource('schedule');
                ppPickedScheduleId = editNode.scheduleId;
                ppPickedScheduleName = editNode.scheduleName;
                document.getElementById('btnConfirmPoint').disabled = false;
                ppModal.show();
                setTimeout(() => {
                    const item = document.querySelector('#scheduleListContainer .pp-list-item[data-schedule-id="' + editNode.scheduleId + '"]');
                    if (item) { item.classList.add('selected'); item.scrollIntoView({ block: 'center', behavior: 'smooth' }); }
                }, 50);
                return;
            }
            const boundSid = editNode && editNode.sid;
            if (boundSid && ppAllPoints) {
                const boundPoint = ppAllPoints.find(p => p.szSid === boundSid);
                if (boundPoint) {
                    if (isDbSid(boundSid)) {
                        // DB 點位 — 直接跳到對應 Coordinator 的點位列表
                        ppSelectDbCoordinator(boundPoint.szGroupName || '');
                        ppPickedSid = boundSid;
                        document.getElementById('btnConfirmPoint').disabled = false;
                        ppModal.show();
                        setTimeout(() => {
                            const item = document.querySelector('#pointListContainer .pp-list-item[data-sid="' + boundSid + '"]');
                            if (item) { item.classList.add('selected'); item.scrollIntoView({ block: 'center', behavior: 'smooth' }); }
                        }, 50);
                        return;
                    }
                    if (isCalcSid(boundSid)) {
                        // 計算點位 — 直接跳到計算點位列表
                        ppShowCalcStep();
                        ppPickedSid = boundSid;
                        document.getElementById('btnConfirmPoint').disabled = false;
                        ppModal.show();
                        setTimeout(() => {
                            const item = document.querySelector('#pointListContainer .pp-list-item[data-sid="' + boundSid + '"]');
                            if (item) { item.classList.add('selected'); item.scrollIntoView({ block: 'center', behavior: 'smooth' }); }
                        }, 50);
                        return;
                    }
                    const pfx = getSidPrefix(boundSid);
                    let foundDev = null, foundModbusId = null, szLabel = '';
                    for (const d of ppAllDevices) {
                        if (!isPointOfDev(boundSid, d.nId)) continue;
                        foundDev = d;
                        szLabel = d.szName;
                        const mids = (d.szModbusID || '').split(',').map(s => s.trim()).filter(Boolean);
                        const names = (d.szDeviceName || '').split(',').map(s => s.trim());
                        if (mids.length > 1) {
                            for (let j = 0; j < mids.length; j++) {
                                const mid = parseInt(mids[j], 10);
                                const base = d.nId * 65536 + mid * 256;
                                if (pfx >= base && pfx < base + 256) {
                                    foundModbusId = mid;
                                    szLabel = (j < names.length && names[j]) ? names[j] : String(mid);
                                    break;
                                }
                            }
                        }
                        break;
                    }
                    if (foundDev) {
                        ppSelectDev(foundDev.nId, szLabel, foundModbusId);
                        ppPickedSid = boundSid;
                        document.getElementById('btnConfirmPoint').disabled = false;
                        ppModal.show();
                        setTimeout(() => {
                            const item = document.querySelector('#pointListContainer .pp-list-item[data-sid="' + boundSid + '"]');
                            if (item) {
                                item.classList.add('selected');
                                item.scrollIntoView({ block: 'center', behavior: 'smooth' });
                            }
                        }, 50);
                        return;
                    }
                }
            }
        }

        ppShowStep1();
        ppModal.show();
    }

    function ppShowStep0() {
        document.getElementById('ppModalTitle').textContent = '\u9078\u64c7\u9ede\u4f4d\u4f86\u6e90';
        document.getElementById('ppStep0').style.display = '';
        document.getElementById('ppStep1').style.display = 'none';
        document.getElementById('ppStep2').style.display = 'none';
        document.getElementById('btnConfirmPoint').disabled = true;
        // 寫入點位不可選計算點位（計算值無法控制）；DB 點位允許寫入
        var calcEl = document.getElementById('ppStep0Calc');
        if (calcEl) calcEl.style.display = (ppPendingType === 'output') ? 'none' : '';
        var dbEl = document.getElementById('ppStep0Db');
        if (dbEl) dbEl.style.display = '';
    }

    function ppShowStep1() {
        // 三種來源（設備 / 計算 / DB）統一從 Step 0 入口；output 由 ppShowStep0 隱藏計算點位
        ppShowStep0();
    }

    function ppShowDeviceStep() {
        document.getElementById('ppModalTitle').textContent = '\u9078\u64c7\u8a2d\u5099';
        document.getElementById('ppStep0').style.display = 'none';
        document.getElementById('ppStep1').style.display = '';
        document.getElementById('ppStep2').style.display = 'none';
        // 寫入點位直接進設備清單，不需返回按鈕
        var backBtn = document.getElementById('ppStep1Back');
        if (backBtn) backBtn.style.display = (ppPendingType === 'output') ? 'none' : '';
        const container = document.getElementById('deviceListContainer');
        if (!ppAllDevices || ppAllDevices.length === 0) {
            container.innerHTML = '<div class="text-center text-muted py-4"><i class="fas fa-plug fa-2x mb-2 d-block"></i>尚無設備</div>';
            return;
        }
        container.innerHTML = ppAllDevices.map(d => {
            const nPts = (ppAllPoints || []).filter(p => isPointOfDev(p.szSid, d.nId)).length;
            const mids = (d.szModbusID || '').split(',').map(s => s.trim()).filter(Boolean);
            const names = (d.szDeviceName || '').split(',').map(s => s.trim());
            if (mids.length > 1) {
                const subs = mids.map((mid, j) => {
                    const label = (j < names.length && names[j]) ? names[j] : mid;
                    return `<div class="pp-list-item" style="padding-left:28px;" onclick="window._lf.ppSelectDev(${d.nId},'${escHtml(label)}',${mid})">
                        <i class="fas fa-microchip text-info" style="font-size:12px;"></i>
                        <div style="flex:1;"><div class="pp-point-name">${escHtml(label)}</div></div>
                        <i class="fas fa-chevron-right text-muted" style="font-size:11px;"></i></div>`;
                }).join('');
                return `<div class="pp-list-item" onclick="this.nextElementSibling.style.display=this.nextElementSibling.style.display==='none'?'':'none'">
                    <i class="fas fa-server text-primary" style="font-size:14px;"></i>
                    <div style="flex:1;"><div class="pp-point-name">${escHtml(d.szName)}</div><div class="pp-point-sid">${nPts} 個點位</div></div>
                    <i class="fas fa-chevron-down text-muted" style="font-size:11px;"></i></div>
                <div style="display:none;">${subs}</div>`;
            }
            return `<div class="pp-list-item" onclick="window._lf.ppSelectDev(${d.nId},'${escHtml(d.szName)}')">
                <i class="fas fa-server text-primary" style="font-size:14px;"></i>
                <div style="flex:1;"><div class="pp-point-name">${escHtml(d.szName)}</div><div class="pp-point-sid">${nPts} 個點位</div></div>
                <i class="fas fa-chevron-right text-muted" style="font-size:11px;"></i></div>`;
        }).join('');
    }

    function ppShowCalcStep() {
        ppPickedDevId = PP_CALC_DEV_ID;
        ppPickedModbusId = null;
        ppPickedSid = null;
        ppPickedCalcGroup = null;

        // 取得計算點位的群組清單
        const calcGroups = _ppGetCalcGroups();
        if (calcGroups.length > 0) {
            // 有群組 — 顯示群組清單（Step 1）
            document.getElementById('ppStep0').style.display = 'none';
            document.getElementById('ppStep1').style.display = '';
            document.getElementById('ppStep2').style.display = 'none';
            document.getElementById('ppModalTitle').textContent = '\u9078\u64c7\u8a08\u7b97\u9ede\u4f4d\u7fa4\u7d44';
            var backBtn = document.getElementById('ppStep1Back');
            if (backBtn) backBtn.style.display = '';
            _ppRenderCalcGroupList(calcGroups);
        } else {
            // 無群組 — 直接顯示全部計算點位
            _ppShowCalcPointsFlat();
        }
    }

    function _ppGetCalcGroups() {
        if (!ppAllPoints) return [];
        const groups = {};
        ppAllPoints.forEach(p => {
            if (!isCalcSid(p.szSid)) return;
            const g = p.szGroupName || '';
            if (!groups[g]) groups[g] = 0;
            groups[g]++;
        });
        return Object.keys(groups).filter(g => g !== '').sort();
    }

    function _ppRenderCalcGroupList(groups) {
        const container = document.getElementById('deviceListContainer');
        const hasUngrouped = (ppAllPoints || []).some(p => isCalcSid(p.szSid) && !p.szGroupName);
        let html = groups.map(g => {
            const nPts = (ppAllPoints || []).filter(p => isCalcSid(p.szSid) && p.szGroupName === g).length;
            return `<div class="pp-list-item" onclick="window._lf.ppSelectCalcGroup('${escHtml(g)}')">
                <i class="fas fa-layer-group text-warning" style="font-size:14px;"></i>
                <div style="flex:1;"><div class="pp-point-name">${escHtml(g)}</div><div class="pp-point-sid">${nPts} \u500b\u9ede\u4f4d</div></div>
                <i class="fas fa-chevron-right text-muted" style="font-size:11px;"></i></div>`;
        }).join('');
        if (hasUngrouped) {
            const nPts = (ppAllPoints || []).filter(p => isCalcSid(p.szSid) && !p.szGroupName).length;
            html += `<div class="pp-list-item" onclick="window._lf.ppSelectCalcGroup('')">
                <i class="fas fa-inbox text-secondary" style="font-size:14px;"></i>
                <div style="flex:1;"><div class="pp-point-name">\u672a\u5206\u7d44</div><div class="pp-point-sid">${nPts} \u500b\u9ede\u4f4d</div></div>
                <i class="fas fa-chevron-right text-muted" style="font-size:11px;"></i></div>`;
        }
        container.innerHTML = html;
    }

    function ppSelectCalcGroup(szGroup) {
        ppPickedDevId = PP_CALC_DEV_ID;
        ppPickedModbusId = null;
        ppPickedCalcGroup = szGroup;
        ppPickedSid = null;
        document.getElementById('ppDeviceName').textContent = szGroup || '\u672a\u5206\u7d44';
        document.getElementById('ppDeviceIcon').className = 'fas fa-layer-group me-1';
        document.getElementById('ppStep0').style.display = 'none';
        document.getElementById('ppStep1').style.display = 'none';
        document.getElementById('ppStep2').style.display = '';
        document.getElementById('ppModalTitle').textContent = '\u9078\u64c7\u8a08\u7b97\u9ede\u4f4d';
        document.getElementById('ppPointSearch').value = '';
        document.getElementById('btnConfirmPoint').disabled = true;
        ppRenderPoints('');
    }

    function _ppShowCalcPointsFlat() {
        document.getElementById('ppStep0').style.display = 'none';
        document.getElementById('ppStep1').style.display = 'none';
        document.getElementById('ppStep2').style.display = '';
        document.getElementById('ppModalTitle').textContent = '\u9078\u64c7\u8a08\u7b97\u9ede\u4f4d';
        document.getElementById('ppDeviceName').textContent = '\u8a08\u7b97\u9ede\u4f4d';
        document.getElementById('ppDeviceIcon').className = 'fas fa-calculator me-1';
        document.getElementById('ppPointSearch').value = '';
        document.getElementById('btnConfirmPoint').disabled = true;
        ppRenderPoints('');
    }

    function ppBackToStep0() {
        ppShowStep0();
    }

    // =========== DB 來源 ===========

    function ppShowDbStep() {
        ppPickedDevId = PP_DB_DEV_ID;
        ppPickedModbusId = null;
        ppPickedSid = null;
        ppPickedDbCoord = null;

        const dbCoords = _ppGetDbCoordinators();
        if (dbCoords.length === 0) {
            // 無 DB 點位 — 直接顯示空清單
            _ppShowDbPointsFlat();
            return;
        }
        document.getElementById('ppStep0').style.display = 'none';
        document.getElementById('ppStep1').style.display = '';
        document.getElementById('ppStep2').style.display = 'none';
        document.getElementById('ppModalTitle').textContent = '選擇 DB 來源裝置';
        var backBtn = document.getElementById('ppStep1Back');
        if (backBtn) backBtn.style.display = '';
        _ppRenderDbCoordinatorList(dbCoords);
    }

    function _ppGetDbCoordinators() {
        if (!ppAllPoints) return [];
        const groups = {};
        ppAllPoints.forEach(p => {
            if (!isDbSid(p.szSid)) return;
            const g = p.szGroupName || '';
            if (!groups[g]) groups[g] = 0;
            groups[g]++;
        });
        return Object.keys(groups).filter(g => g !== '').sort();
    }

    function _ppRenderDbCoordinatorList(coords) {
        const container = document.getElementById('deviceListContainer');
        const hasUngrouped = (ppAllPoints || []).some(p => isDbSid(p.szSid) && !p.szGroupName);
        let html = coords.map(g => {
            const nPts = (ppAllPoints || []).filter(p => isDbSid(p.szSid) && p.szGroupName === g).length;
            return `<div class="pp-list-item" onclick="window._lf.ppSelectDbCoordinator('${escHtml(g)}')">
                <i class="fas fa-database text-info" style="font-size:14px;"></i>
                <div style="flex:1;"><div class="pp-point-name">${escHtml(g)}</div><div class="pp-point-sid">${nPts} 個點位</div></div>
                <i class="fas fa-chevron-right text-muted" style="font-size:11px;"></i></div>`;
        }).join('');
        if (hasUngrouped) {
            const nPts = (ppAllPoints || []).filter(p => isDbSid(p.szSid) && !p.szGroupName).length;
            html += `<div class="pp-list-item" onclick="window._lf.ppSelectDbCoordinator('')">
                <i class="fas fa-inbox text-secondary" style="font-size:14px;"></i>
                <div style="flex:1;"><div class="pp-point-name">未分組</div><div class="pp-point-sid">${nPts} 個點位</div></div>
                <i class="fas fa-chevron-right text-muted" style="font-size:11px;"></i></div>`;
        }
        container.innerHTML = html;
    }

    function ppSelectDbCoordinator(szCoord) {
        ppPickedDevId = PP_DB_DEV_ID;
        ppPickedModbusId = null;
        ppPickedDbCoord = szCoord;
        ppPickedSid = null;
        document.getElementById('ppDeviceName').textContent = szCoord || '未分組';
        document.getElementById('ppDeviceIcon').className = 'fas fa-database me-1';
        document.getElementById('ppStep0').style.display = 'none';
        document.getElementById('ppStep1').style.display = 'none';
        document.getElementById('ppStep2').style.display = '';
        document.getElementById('ppModalTitle').textContent = '選擇 DB 點位';
        document.getElementById('ppPointSearch').value = '';
        document.getElementById('btnConfirmPoint').disabled = true;
        ppRenderPoints('');
    }

    function _ppShowDbPointsFlat() {
        document.getElementById('ppStep0').style.display = 'none';
        document.getElementById('ppStep1').style.display = 'none';
        document.getElementById('ppStep2').style.display = '';
        document.getElementById('ppModalTitle').textContent = '選擇 DB 點位';
        document.getElementById('ppDeviceName').textContent = 'DB 點位';
        document.getElementById('ppDeviceIcon').className = 'fas fa-database me-1';
        document.getElementById('ppPointSearch').value = '';
        document.getElementById('btnConfirmPoint').disabled = true;
        ppRenderPoints('');
    }

    // =========== 排程來源切換 ===========

    function ppSwitchSource(mode) {
        ppSourceMode = mode;
        const btnP = document.getElementById('ppBtnPoint');
        const btnS = document.getElementById('ppBtnSchedule');
        if (mode === 'schedule') {
            btnP.classList.remove('active'); btnS.classList.add('active');
            document.getElementById('ppStep0').style.display = 'none';
            document.getElementById('ppStep1').style.display = 'none';
            document.getElementById('ppStep2').style.display = 'none';
            document.getElementById('ppStep3').style.display = '';
            document.getElementById('ppModalTitle').textContent = '\u9078\u64c7\u6392\u7a0b';
            ppPickedSid = null;
            ppPickedScheduleId = null;
            ppPickedScheduleName = null;
            document.getElementById('btnConfirmPoint').disabled = true;
            ppRenderScheduleList();
        } else {
            btnS.classList.remove('active'); btnP.classList.add('active');
            document.getElementById('ppStep3').style.display = 'none';
            ppPickedScheduleId = null;
            ppPickedScheduleName = null;
            ppShowStep1();
        }
    }

    async function ppEnsureSchedules() {
        if (ppAllSchedules) return;
        const r = await fetch('/api/schedules');
        if (!r.ok) throw new Error('\u7121\u6cd5\u8f09\u5165\u6392\u7a0b\u6e05\u55ae');
        ppAllSchedules = await r.json();
    }

    async function ppRenderScheduleList() {
        const container = document.getElementById('scheduleListContainer');
        try {
            await ppEnsureSchedules();
        } catch (e) { container.innerHTML = '<div class="text-center text-muted py-3">' + escHtml(e.message) + '</div>'; return; }

        const enabled = ppAllSchedules.filter(s => s.isEnabled);
        if (enabled.length === 0) {
            container.innerHTML = '<div class="text-center text-muted py-4"><i class="fas fa-calendar-times fa-2x mb-2 d-block"></i>\u5c1a\u7121\u555f\u7528\u7684\u6392\u7a0b</div>';
            return;
        }
        const TYPE_LABELS = ['\u6bcf\u9031', 'N\u9031\u5faa\u74b0', '\u6bcf\u6708', 'N\u6708\u5faa\u74b0'];
        container.innerHTML = enabled.map(s => {
            const typeLabel = TYPE_LABELS[s.nRecurrenceType] || '';
            const time = escHtml(s.szStartTime) + ' - ' + escHtml(s.szEndTime);
            return `<div class="pp-list-item" data-schedule-id="${s.nId}" onclick="window._lf.ppSelectSchedule(${s.nId},'${escHtml(s.szName)}',this)">
                <i class="fas fa-calendar-alt text-primary" style="font-size:14px;"></i>
                <div style="flex:1;"><div class="pp-point-name">${escHtml(s.szName)}</div><div class="pp-point-sid">${escHtml(typeLabel)} \u30fb ${time}</div></div></div>`;
        }).join('');
    }

    function ppSelectSchedule(nId, szName, el) {
        document.querySelectorAll('#scheduleListContainer .pp-list-item').forEach(i => i.classList.remove('selected'));
        el.classList.add('selected');
        ppPickedScheduleId = nId;
        ppPickedScheduleName = szName;
        document.getElementById('btnConfirmPoint').disabled = false;
    }

    function ppSelectDev(nDevId, szLabel, nModbusId) {
        ppPickedDevId = nDevId;
        ppPickedModbusId = nModbusId != null ? nModbusId : null;
        ppPickedSid = null;
        document.getElementById('btnConfirmPoint').disabled = true;
        document.getElementById('ppDeviceName').textContent = szLabel;
        document.getElementById('ppDeviceIcon').className = 'fas fa-server me-1';
        document.getElementById('ppModalTitle').textContent = '\u9078\u64c7\u9ede\u4f4d';
        document.getElementById('ppStep0').style.display = 'none';
        document.getElementById('ppStep1').style.display = 'none';
        document.getElementById('ppStep2').style.display = '';
        document.getElementById('ppPointSearch').value = '';
        ppRenderPoints('');
    }

    function ppGoBack() {
        if (ppPickedDevId === PP_CALC_DEV_ID) {
            if (ppPickedCalcGroup != null) {
                // 從計算點位列表返回群組列表
                ppPickedCalcGroup = null;
                ppShowCalcStep();
            } else {
                ppShowStep0();
            }
        } else if (ppPickedDevId === PP_DB_DEV_ID) {
            if (ppPickedDbCoord != null) {
                // 從 DB 點位列表返回 Coordinator 列表
                ppPickedDbCoord = null;
                ppShowDbStep();
            } else {
                ppShowStep0();
            }
        } else {
            ppShowDeviceStep();
        }
    }

    function ppFilter(val) {
        ppPickedSid = null;
        document.getElementById('btnConfirmPoint').disabled = true;
        ppRenderPoints(val);
    }

    function ppRenderPoints(keyword) {
        const q = keyword.trim().toLowerCase();
        const filtered = (ppAllPoints || []).filter(p => {
            if (ppPickedDevId === PP_CALC_DEV_ID && ppPickedCalcGroup != null) {
                // 計算點位群組篩選
                if (!isCalcSid(p.szSid)) return false;
                if ((p.szGroupName || '') !== ppPickedCalcGroup) return false;
            } else if (ppPickedDevId === PP_DB_DEV_ID) {
                // DB 點位 Coordinator 篩選（ppPickedDbCoord 為 null 時顯示全部 DB）
                if (!isDbSid(p.szSid)) return false;
                if (ppPickedDbCoord != null && (p.szGroupName || '') !== ppPickedDbCoord) return false;
            } else if (ppPickedModbusId != null) {
                const pfx = getSidPrefix(p.szSid);
                const base = ppPickedDevId * 65536 + ppPickedModbusId * 256;
                if (pfx < base || pfx >= base + 256) return false;
            } else {
                if (!isPointOfDev(p.szSid, ppPickedDevId)) return false;
            }
            return !q || p.szName.toLowerCase().includes(q);
        });
        const container = document.getElementById('pointListContainer');
        if (filtered.length === 0) {
            container.innerHTML = '<div class="text-center text-muted py-4"><i class="fas fa-inbox fa-2x mb-2 d-block"></i>無符合點位</div>';
            return;
        }
        container.innerHTML = filtered.map(p => {
            const prefix = p._devLabel ? `<span class="text-info" style="font-size:11px;">${escHtml(p._devLabel)}</span><span class="text-muted mx-1">/</span>` : '';
            return `<div class="pp-list-item" data-sid="${escHtml(p.szSid)}" onclick="window._lf.ppSelectPoint(this,'${escHtml(p.szSid)}')">
                <i class="fas fa-circle text-success" style="font-size:6px;"></i>
                <div style="flex:1;"><div class="pp-point-name">${prefix}${escHtml(p.szName)}</div></div>
                <span class="pp-point-unit">${escHtml(p.szUnit || '')}</span></div>`;
        }).join('');
    }

    function ppSelectPoint(el, szSid) {
        document.querySelectorAll('#pointListContainer .pp-list-item').forEach(i => i.classList.remove('selected'));
        el.classList.add('selected');
        ppPickedSid = szSid;
        document.getElementById('btnConfirmPoint').disabled = false;
    }

    function ppConfirm() {
        // ── 排程模式 ──
        if (ppSourceMode === 'schedule') {
            if (ppPickedScheduleId == null) return;
            if (ppModal) ppModal.hide();
            if (ppEditNodeId != null) {
                const existing = canvasNodes.find(n => n.id === ppEditNodeId);
                if (existing) {
                    // 設定排程時自動移除 ctrl 邊線（互斥）
                    canvasEdges = canvasEdges.filter(e => !(e.target === existing.id && e.targetPort === 'ctrl'));
                    existing.scheduleId = ppPickedScheduleId;
                    existing.scheduleName = ppPickedScheduleName;
                    delete existing.sid;
                    delete existing.pointName;
                    delete existing.unit;
                }
                ppEditNodeId = null;
            } else {
                canvasNodes.push({
                    id: nextNodeId++,
                    type: ppPendingType,
                    x: ppPendingPos.x,
                    y: ppPendingPos.y,
                    scheduleId: ppPickedScheduleId,
                    scheduleName: ppPickedScheduleName
                });
            }
            renderCanvasNodes();
            return;
        }
        // ── 點位模式（原有邏輯） ──
        if (!ppPickedSid || !ppAllPoints) return;
        const point = ppAllPoints.find(p => p.szSid === ppPickedSid);
        if (!point) return;
        if (ppModal) ppModal.hide();

        const fullName = point._devLabel ? point._devLabel + ' / ' + point.szName : point.szName;

        if (ppEditNodeId != null) {
            const existing = canvasNodes.find(n => n.id === ppEditNodeId);
            if (existing) {
                // 設定點位時自動移除 ctrl 邊線（互斥）
                canvasEdges = canvasEdges.filter(e => !(e.target === existing.id && e.targetPort === 'ctrl'));
                existing.sid = point.szSid;
                existing.pointName = fullName;
                existing.unit = point.szUnit || '';
                delete existing.scheduleId;
                delete existing.scheduleName;
                if (existing.type === 'output') {
                    existing.fMin = point.fMin ?? 0;
                    existing.fMax = point.fMax ?? 100;
                }
            }
            ppEditNodeId = null;
        } else {
            const node = {
                id: nextNodeId++,
                type: ppPendingType,
                x: ppPendingPos.x,
                y: ppPendingPos.y,
                sid: point.szSid,
                pointName: fullName,
                unit: point.szUnit || ''
            };
            if (ppPendingType === 'output') {
                node.fMin = point.fMin ?? 0;
                node.fMax = point.fMax ?? 100;
            }
            canvasNodes.push(node);
        }
        renderCanvasNodes();
    }

    // 拖曳節點（支援群組拖曳 + Ctrl 切換選取）
    function startDrag(e) {
        if (e.button !== 0) return;
        const el = e.currentTarget;
        const nodeId = parseInt(el.dataset.nodeId);
        const node = canvasNodes.find(n => n.id === nodeId);
        if (!node) return;

        // Ctrl+click → 切換選取，不拖曳
        if (e.ctrlKey) {
            if (selectedNodeIds.has(nodeId)) selectedNodeIds.delete(nodeId);
            else selectedNodeIds.add(nodeId);
            updateNodeSelectionVisual();
            e.preventDefault();
            return;
        }

        // 點擊未選取的節點 → 清除舊選取，只選此節點
        if (!selectedNodeIds.has(nodeId)) {
            selectedNodeIds.clear();
            selectedNodeIds.add(nodeId);
            updateNodeSelectionVisual();
        }

        const startX = e.clientX, startY = e.clientY;
        // 記錄所有已選取節點的原始位置
        const origPos = {};
        for (const sid of selectedNodeIds) {
            const sn = canvasNodes.find(n => n.id === sid);
            if (sn) origPos[sid] = { x: sn.x, y: sn.y };
        }
        el.style.cursor = 'grabbing';

        function onMove(ev) {
            const dx = ev.clientX - startX;
            const dy = ev.clientY - startY;
            for (const sid of selectedNodeIds) {
                const sn = canvasNodes.find(n => n.id === sid);
                const orig = origPos[sid];
                if (!sn || !orig) continue;
                sn.x = Math.max(0, Math.round((orig.x + dx) / 20) * 20);
                sn.y = Math.max(0, Math.round((orig.y + dy) / 20) * 20);
                const sEl = document.querySelector(`.flow-node[data-node-id="${sid}"]`);
                if (sEl) { sEl.style.left = sn.x + 'px'; sEl.style.top = sn.y + 'px'; }
            }
            updateCanvasSize();
            renderEdges();
        }
        function onUp() {
            document.removeEventListener('mousemove', onMove);
            document.removeEventListener('mouseup', onUp);
            el.style.cursor = 'grab';
            updateCanvasSize();
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
        const node = canvasNodes.find(n => n.id === nodeId);
        if (!el || !node) return;
        const startY = e.clientY;
        const startH = el.offsetHeight;
        const MIN_H = 40;
        function onMove(ev) {
            const newH = Math.max(MIN_H, startH + (ev.clientY - startY));
            el.style.height = newH + 'px';
            node.height = newH;
            renderEdges();  // 埠位置會跟著節點高度變動，連線需即時重畫
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
            el.classList.toggle('node-selected', selectedNodeIds.has(parseInt(el.dataset.nodeId)));
        });
    }

    // 複製選取的節點 + 內部連線 → localStorage（支援跨邏輯貼上）
    function copySelectedNodes() {
        if (selectedNodeIds.size === 0) return;
        const nodes = canvasNodes.filter(n => selectedNodeIds.has(n.id)).map(n => {
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
        const edges = canvasEdges.filter(e => selectedNodeIds.has(e.source) && selectedNodeIds.has(e.target));
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
            const nid = nextNodeId++;
            idMap[n.id] = nid;
            const c = { ...n, id: nid, x: Math.max(0, n.x + ox), y: Math.max(0, n.y + oy) };
            Object.keys(c).forEach(k => { if (k.startsWith('_')) delete c[k]; });
            newNodes.push(c);
        }
        const newEdges = (data.edges || [])
            .filter(e => idMap[e.source] != null && idMap[e.target] != null)
            .map(e => ({ id: nextEdgeId++, source: idMap[e.source], sourcePort: e.sourcePort, target: idMap[e.target], targetPort: e.targetPort }));

        canvasNodes.push(...newNodes);
        canvasEdges.push(...newEdges);
        selectedNodeIds.clear();
        newNodes.forEach(n => selectedNodeIds.add(n.id));
        renderCanvasNodes();
    }

    // 刪除所有選取的節點及其相關連線
    function deleteSelectedNodes() {
        if (selectedNodeIds.size === 0) return;
        canvasNodes = canvasNodes.filter(n => !selectedNodeIds.has(n.id));
        canvasEdges = canvasEdges.filter(e => !selectedNodeIds.has(e.source) && !selectedNodeIds.has(e.target));
        if (selectedEdgeId != null && !canvasEdges.some(e => e.id === selectedEdgeId)) selectedEdgeId = null;
        selectedNodeIds.clear();
        renderCanvasNodes();
    }

    // 全域關閉右鍵選單
    document.addEventListener('click', (e) => {
        if (!e.target.closest('#ctxMenu') && !e.target.closest('#nodeCtxMenu')) hideCtxMenu();
    });
    document.addEventListener('keydown', (e) => {
        const isInput = document.activeElement && (document.activeElement.tagName === 'INPUT' || document.activeElement.tagName === 'TEXTAREA');
        const hasCanvas = !!document.getElementById('diagramCanvas');

        if (e.key === 'Escape') {
            hideCtxMenu();
            if (selectedEdgeId != null) { selectedEdgeId = null; renderEdges(); }
            if (selectedNodeIds.size > 0) { selectedNodeIds.clear(); updateNodeSelectionVisual(); }
        }
        if (e.key === 'Delete' && !isInput) {
            if (selectedNodeIds.size > 0) deleteSelectedNodes();
            else if (selectedEdgeId != null) deleteSelectedEdge();
        }
        if (!isInput && hasCanvas && (e.ctrlKey || e.metaKey)) {
            if (e.key === 'a') { e.preventDefault(); canvasNodes.forEach(n => selectedNodeIds.add(n.id)); updateNodeSelectionVisual(); }
            if (e.key === 'c' && selectedNodeIds.size > 0) { e.preventDefault(); copySelectedNodes(); }
            if (e.key === 'x' && selectedNodeIds.size > 0) { e.preventDefault(); copySelectedNodes(); deleteSelectedNodes(); }
            if (e.key === 'v') { e.preventDefault(); pasteNodes(); }
        }
    });

    // 右鍵選單項目點擊（新增節點）
    document.querySelectorAll('#ctxMenu .ctx-menu-item[data-type]').forEach(item => {
        if (item.closest('.ctx-has-sub') && item.classList.contains('ctx-has-sub')) return; // 父項不觸發
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
            if (nodeCtxTargetId != null) changeNodeType(nodeCtxTargetId, item.dataset.type, item.dataset.operator || null);
        });
    });

    // 節點右鍵選單 — 刪除節點
    document.querySelector('#nodeCtxMenu .node-delete-item').addEventListener('click', () => {
        hideCtxMenu();
        if (nodeCtxTargetId != null) deleteCanvasNode(nodeCtxTargetId);
    });

    // 清除綁定：移除接點的 SID/排程，恢復為空白接點
    document.querySelector('#nodeCtxMenu .node-clear-binding').addEventListener('click', () => {
        hideCtxMenu();
        if (nodeCtxTargetId == null) return;
        var nd = canvasNodes.find(function(n) { return n.id === nodeCtxTargetId; });
        if (!nd) return;
        delete nd.sid;
        delete nd.pointName;
        delete nd.unit;
        delete nd.scheduleId;
        delete nd.scheduleName;
        renderCanvasNodes();
    });

    function clearContent() {
        stopRealtimePolling();
        document.getElementById('contentTitle').innerHTML = '<i class="fas fa-hand-pointer me-1"></i>請從左側選取邏輯項目';
        document.getElementById('contentArea').innerHTML = `
            <div class="text-center text-muted mt-5">
                <i class="fas fa-project-diagram fa-4x mb-3 d-block" style="opacity:.3"></i>
                <p>選取左側的邏輯項目後，即可在此編輯流程圖</p>
            </div>`;
    }

    // =========== 儲存流程圖 ===========
    async function saveDiagram() {
        if (!currentTreeId) return;
        const btn = document.getElementById('btnSaveDiagram');
        if (btn) { btn.disabled = true; btn.innerHTML = '<i class="fas fa-spinner fa-spin me-1"></i>\u5132\u5b58\u4e2d\u2026'; }
        try {
            // 只保留需要持久化的欄位，過濾掉執行時暫存資料
            const cleanNodes = canvasNodes.map(n => {
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
            const json = JSON.stringify({ nodes: cleanNodes, edges: canvasEdges });
            await apiFetch(`/diagram/${currentTreeId}`, {
                method: 'PUT',
                body: JSON.stringify({ diagramJson: json, version: diagramVersion })
            });
            diagramVersion++;
            alert('\u5132\u5b58\u6210\u529f\uff01');
        } catch (e) {
            if (e.message && e.message.includes('\u7248\u672c\u885d\u7a81')) {
                alert('\u7248\u672c\u885d\u7a81\uff0c\u5c07\u91cd\u65b0\u8f09\u5165\u6700\u65b0\u8cc7\u6599');
                await initCanvas(currentTreeId);
            } else {
                alert('\u5132\u5b58\u5931\u6557\uff1a' + e.message);
            }
        } finally {
            if (btn) { btn.disabled = false; btn.innerHTML = '<i class="fas fa-save me-1"></i>\u5132\u5b58'; }
        }
    }

    // =========== 初始化 ===========
    loadAlgorithms();
    loadTree();

    // 暴露給 inline onclick
    window._lf = {
        select, toggle, addChild, addRoot, rename, confirmRename, remove, toggleEnabled,
        saveDiagram,
        ppGoBack: ppGoBack, ppFilter: ppFilter, ppSelectDev: ppSelectDev,
        ppSelectPoint: ppSelectPoint, ppConfirm: ppConfirm,
        ppSwitchSource: ppSwitchSource, ppSelectSchedule: ppSelectSchedule,
        ppShowDeviceStep: ppShowDeviceStep, ppShowCalcStep: ppShowCalcStep, ppBackToStep0: ppBackToStep0,
        ppSelectCalcGroup: ppSelectCalcGroup,
        ppShowDbStep: ppShowDbStep, ppSelectDbCoordinator: ppSelectDbCoordinator
    };
})();
