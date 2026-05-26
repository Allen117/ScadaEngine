// LogicFlow 共用狀態 + 常數 + 基礎工具
// 內部命名空間 window.__lfNS — 其他 logicflow/*.js 透過此物件共用狀態與函式
// 對外 API 仍以 window._lf 暴露（由 index.js 組裝）
(function () {
    const S = window.__lfNS = window.__lfNS || {};

    // ── 常數 ──
    S.API = '/LogicFlow/api';
    S.PP_CALC_DEV_ID = -999;
    S.PP_DB_DEV_ID   = -998;

    // ── 節點 metadata ──
    S.NODE_META = {
        input:    { icon: 'fas fa-sign-in-alt text-primary',        label: 'logicflow.node.input',    inputs: [],         outputs: ['out'] },
        output:   { icon: 'fas fa-sign-out-alt text-success',       label: 'logicflow.node.output',   inputs: ['in'],     outputs: [] },
        compare:  { icon: 'fas fa-not-equal text-warning',          label: 'logicflow.node.compare',  inputs: ['a','b'],  outputs: ['out'] },
        math:     { icon: 'fas fa-calculator text-info',            label: 'logicflow.node.math',     inputs: ['in'],     outputs: ['out'] },
        constant: { icon: 'fas fa-hashtag text-dark',               label: 'logicflow.node.constant', inputs: [],         outputs: ['out'] },
        and:      { icon: 'fas fa-grip-lines text-danger',          label: 'logicflow.node.and_gate', inputs: ['a','b'],  outputs: ['out'] },
        or:       { icon: 'fas fa-grip-lines-vertical text-danger', label: 'logicflow.node.or_gate',  inputs: ['a','b'],  outputs: ['out'] },
        not:      { icon: 'fas fa-exclamation text-danger',         label: 'logicflow.node.not_gate', inputs: ['in'],     outputs: ['out'] },
        xor:      { icon: 'fas fa-random text-danger',              label: 'logicflow.node.xor_gate', inputs: ['a','b'],  outputs: ['out'] },
        timer:    { icon: 'fas fa-clock text-secondary',            label: 'logicflow.node.timer',    inputs: ['in'],     outputs: ['out'] },
        contact_no: { icon: 'fas fa-toggle-on text-orange',          label: 'logicflow.node.contact_no', inputs: ['in'],     outputs: ['out'] },
        contact_nc: { icon: 'fas fa-toggle-off text-purple',         label: 'logicflow.node.contact_nc', inputs: ['in'],     outputs: ['out'] },
        counter:    { icon: 'fas fa-sort-numeric-up text-teal',      label: 'logicflow.counter.ctu', inputs: ['cu','reset','preset'], outputs: ['q','cv'],
                      portLabels: { cu: 'logicflow.counter.port.cu', reset: 'logicflow.counter.port.reset', preset: 'logicflow.counter.port.preset', q: 'logicflow.counter.port.q', cv: 'logicflow.counter.port.cv' } },
        algorithm:  { icon: 'fas fa-brain text-purple',              label: 'logicflow.node.algorithm', inputs: ['in'],     outputs: ['out'] }
    };

    S.COMPARE_OPS = {
        lt:  { symbol: '<',  label: 'logicflow.compare.lt' },
        gt:  { symbol: '>',  label: 'logicflow.compare.gt' },
        lte: { symbol: '≤', label: 'logicflow.compare.lte' },
        gte: { symbol: '≥', label: 'logicflow.compare.gte' },
        eq:  { symbol: '=',  label: 'logicflow.compare.eq' },
        neq: { symbol: '≠', label: 'logicflow.compare.neq' }
    };

    S.MATH_OPS = {
        add:   { symbol: '+', label: 'logicflow.math.add',     hasValue: true },
        sub:   { symbol: '−', label: 'logicflow.math.sub',     hasValue: true },
        mul:   { symbol: '×', label: 'logicflow.math.mul',     hasValue: true },
        div:   { symbol: '÷', label: 'logicflow.math.div',     hasValue: true },
        mod:   { symbol: '%', label: 'logicflow.math.mod',   hasValue: true },
        pow:   { symbol: '^', label: 'logicflow.math.pow',     hasValue: true },
        abs:   { symbol: '|x|', label: 'logicflow.math.abs', hasValue: false },
        sqrt:  { symbol: '√', label: 'logicflow.math.sqrt',   hasValue: false },
        round: { symbol: '≈', label: 'logicflow.math.round', hasValue: false }
    };

    S.TIMER_OPS = {
        tp:  { symbol: 'TP',  label: 'logicflow.timer.tp' },
        ton: { symbol: 'TON', label: 'logicflow.timer.ton' },
        tpr: { symbol: 'TPR', label: 'logicflow.timer.tpr' }
    };

    // Python / C# 演算法（由 algo.js loadAlgorithms 動態填入）
    S.ALGO_OPS = {};

    // ── 樹狀資料 ──
    S.flatNodes = [];
    S.treeData = [];
    S.expandedSet = new Set();
    S.selectedId = null;
    S.renameTargetId = null;

    // ── 畫布資料 ──
    S.canvasNodes = [];
    S.canvasEdges = [];
    S.nextNodeId = 1;
    S.nextEdgeId = 1;
    S.currentTreeId = null;
    S.diagramVersion = 0;
    S.ctxPos = { x: 0, y: 0 };
    S.draggingEdge = null;
    S.selectedEdgeId = null;
    S.nodeCtxTargetId = null;
    S._outputPrevState = {};
    S._controlModeCache = {};
    S._isLogicEnabled = true;
    S.selectedNodeIds = new Set();

    // ── 即時值輪詢 ──
    S._realtimeCache = {};
    S._realtimeTimer = null;
    S._timerEvalInterval = null;
    S._pollInterval = 300;

    // ── Point Picker 狀態 ──
    S.ppAllDevices = null;
    S.ppAllPoints  = null;
    S.ppPickedDevId = -1;
    S.ppPickedModbusId = null;
    S.ppPickedSid = null;
    S.ppPendingType = null;
    S.ppPendingPos = { x: 0, y: 0 };
    S.ppEditNodeId = null;
    S.ppModal = null;
    S.ppAllSchedules = null;
    S.ppSourceMode = 'point';
    S.ppPickedScheduleId = null;
    S.ppPickedScheduleName = null;
    S.ppPickedCalcGroup = null;
    S.ppPickedDbCoord = null;

    // ── 基礎工具 ──
    S.t = function (key, args) {
        return (window.i18n && window.i18n.t) ? window.i18n.t(key, args) : key;
    };

    S.escHtml = function (s) {
        const d = document.createElement('div');
        d.textContent = s;
        return d.innerHTML;
    };

    // 千分位格式化（'--' 等非數字原樣回傳）
    S.fmtNum = function (v) {
        const n = parseFloat(v);
        if (isNaN(n)) return v;
        return n.toLocaleString('en-US', { maximumFractionDigits: 10 });
    };

    S.apiFetch = async function (url, opts) {
        const res = await fetch(S.API + url, {
            headers: { 'Content-Type': 'application/json' },
            ...opts
        });
        if (!res.ok) {
            const txt = await res.text();
            throw new Error(txt || res.statusText);
        }
        return res.json();
    };

    // 樹狀結構遞迴查找
    S.findNode = function (id, nodes) {
        for (const n of nodes) {
            if (n.id === id) return n;
            if (n.children) {
                const r = S.findNode(id, n.children);
                if (r) return r;
            }
        }
        return null;
    };
})();
