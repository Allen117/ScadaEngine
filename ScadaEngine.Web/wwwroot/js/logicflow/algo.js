// LogicFlow 演算法：定義載入、動態埠展開、右鍵子選單建構
(function () {
    const S = window.__lfNS;

    async function loadAlgorithms() {
        try {
            const res = await fetch('/LogicFlow/api/algorithms');
            if (!res.ok) return;
            const list = await res.json();
            S.ALGO_OPS = {};
            list.forEach(a => {
                S.ALGO_OPS[a.name] = {
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
        item.innerHTML = `<span class="ctx-op-symbol ctx-op-wide" style="color:#9b59b6;">${S.escHtml(op.symbol)}</span>${S.escHtml(op.label)}${langBadge}`;
        if (isNodeMenu) {
            item.addEventListener('click', (e) => {
                e.stopPropagation();
                S.hideCtxMenu();
                if (S.nodeCtxTargetId != null) S.changeNodeType(S.nodeCtxTargetId, 'algorithm', key);
            });
        } else {
            item.addEventListener('click', (e) => {
                e.stopPropagation();
                S.addNodeToCanvas('algorithm', key);
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

            if (Object.keys(S.ALGO_OPS).length === 0) {
                container.innerHTML = '<div class="ctx-menu-item text-muted" style="font-size:.75rem;">' + S.escHtml(S.t('logicflow.algorithm.no_algorithm')) + '</div>';
                return;
            }

            // 按 group 分類
            const groups = {};
            const ungrouped = [];
            for (const [key, op] of Object.entries(S.ALGO_OPS)) {
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
                groupEl.innerHTML = `<i class="fas fa-folder text-muted me-2" style="font-size:.8rem;"></i>${S.escHtml(groupName)} <i class="fas fa-caret-right ms-auto"></i>`;
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

    // 暴露給其他模組
    S.loadAlgorithms = loadAlgorithms;
    S.getAlgoPorts = getAlgoPorts;
    S.getAlgoGroupRanges = getAlgoGroupRanges;
    S.buildAlgoSubmenu = buildAlgoSubmenu;
})();
