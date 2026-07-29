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
                    inputs: a.inputs || [{ key: 'in', label: 'in' }],
                    outputs: a.outputs || [{ key: 'out', label: 'out' }],
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

    // ── 分帶佈局（單一真相）：把節點垂直可用區間 [20%,80%] 切成「固定帶 + N 組帶」 ──
    // 固定帶（若任一側有 fixed 埠才保留）永遠在最頂，之後每組各佔一條等高帶；
    // 輸入側與輸出側共用同一組帶邊界（bandCount 只看 N 與 hasFixed），確保同組上下對齊、
    // 且組框（= 帶範圍）不會跨到固定帶而吞掉固定埠。
    // 回傳 { pcts:[每個埠的中心 %], bands:[{top,bottom} × N] }（bands 只含組帶，不含固定帶）
    const LAYOUT_TOP = 20, LAYOUT_RANGE = 60, BAND_INSET = 0.18;
    function bandLayout(fixedLen, repeatLen, N, hasFixed) {
        const bandCount = N + (hasFixed ? 1 : 0);
        const bandH = LAYOUT_RANGE / bandCount;
        const placeInBand = (bandIdx, m) => {
            const bt = LAYOUT_TOP + bandIdx * bandH;
            if (m <= 0) return [];
            if (m === 1) return [bt + bandH / 2];
            const lo = bt + bandH * BAND_INSET, hi = bt + bandH * (1 - BAND_INSET);
            const arr = [];
            for (let j = 0; j < m; j++) arr.push(lo + j * (hi - lo) / (m - 1));
            return arr;
        };
        const pcts = [];
        if (hasFixed) placeInBand(0, fixedLen).forEach(p => pcts.push(p));
        const bands = [];
        for (let g = 0; g < N; g++) {
            const bi = (hasFixed ? 1 : 0) + g;
            placeInBand(bi, repeatLen).forEach(p => pcts.push(p));
            bands.push({ top: LAYOUT_TOP + bi * bandH, bottom: LAYOUT_TOP + (bi + 1) * bandH });
        }
        return { pcts, bands };
    }

    // ── 變動埠展開：將 fixed + repeat × N 展開為 [{key, label, topPct?}, ...] ──
    // repeat/fixed 為 [{key, label}, ...]；pcts 若給則依序掛上每個埠的垂直中心 %
    function expandAlgoPorts(repeat, fixedList, n, pcts) {
        const out = [];
        (fixedList || []).forEach(p => out.push({ key: p.key, label: p.label || p.key }));
        const N = Math.max(1, parseInt(n, 10) || 1);
        for (let i = 1; i <= N; i++) {
            (repeat || []).forEach(p => out.push({
                key: `${p.key}${i}`,
                label: `${p.label || p.key} ${i}`
            }));
        }
        if (pcts) out.forEach((p, idx) => { p.topPct = pcts[idx]; });
        return out;
    }

    // ── 取得演算法節點當前的輸入/輸出埠（依 variadic + inputCount 動態展開） ──
    function getAlgoPorts(op, inputCount) {
        if (!op) return { inputs: [], outputs: [{ key: 'out', label: 'out' }] };
        if (op.variadic) {
            const N = Math.max(1, parseInt(inputCount, 10) || 1);
            const hasFixed = (op.inputsFixed || []).length > 0 || (op.outputsFixed || []).length > 0;
            const inPcts = bandLayout((op.inputsFixed || []).length, (op.inputsRepeat || []).length, N, hasFixed).pcts;
            const outPcts = bandLayout((op.outputsFixed || []).length, (op.outputsRepeat || []).length, N, hasFixed).pcts;
            return {
                inputs: expandAlgoPorts(op.inputsRepeat, op.inputsFixed, N, inPcts),
                outputs: expandAlgoPorts(op.outputsRepeat, op.outputsFixed, N, outPcts)
            };
        }
        // 非 variadic：op.inputs/outputs 為 [{key, label}, ...]（兼容舊版純字串陣列）
        const normalize = p => typeof p === 'string'
            ? { key: p, label: p }
            : { key: p.key, label: p.label || p.key };
        return {
            inputs: (op.inputs || [{ key: 'in', label: 'in' }]).map(normalize),
            outputs: (op.outputs || [{ key: 'out', label: 'out' }]).map(normalize)
        };
    }

    // ── variadic 演算法：算出每組在節點內的垂直 % 帶範圍，供畫外框 ──
    // 帶邊界只由 N 與 hasFixed 決定（與 fixed/repeat 埠數無關），故輸入輸出兩側一致。
    // 回傳 [{ index, topPct, bottomPct }, ...]；N<2 或沒有 repeat 埠時回空陣列
    function getAlgoGroupRanges(op, inputCount) {
        if (!op || !op.variadic) return [];
        const N = Math.max(1, parseInt(inputCount, 10) || 1);
        if (N < 2) return [];
        const repeatInLen = (op.inputsRepeat || []).length;
        const repeatOutLen = (op.outputsRepeat || []).length;
        if (repeatInLen === 0 && repeatOutLen === 0) return [];
        const hasFixed = (op.inputsFixed || []).length > 0 || (op.outputsFixed || []).length > 0;
        const bands = bandLayout(0, 0, N, hasFixed).bands;
        return bands.map((b, g) => ({ index: g + 1, topPct: b.top, bottomPct: b.bottom }));
    }

    function _createAlgoMenuItem(key, op, isNodeMenu) {
        const item = document.createElement('div');
        item.className = isNodeMenu ? 'ctx-menu-item node-change-type' : 'ctx-menu-item';
        item.dataset.type = 'algorithm';
        item.dataset.operator = key;
        const langBadge = op.language === 'csharp'
            ? '<span style="font-size:.6rem;background:#178600;color:#fff;padding:0 3px;border-radius:2px;margin-left:4px;">C#</span>'
            : '';
        item.innerHTML = `<span class="ctx-op-symbol ctx-op-wide" style="color:#9b59b6;"><i class="fas fa-microchip"></i></span>${S.escHtml(op.label)}${langBadge}`;
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
