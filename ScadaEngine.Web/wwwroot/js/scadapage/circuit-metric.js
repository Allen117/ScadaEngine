// ============================================================
// scadapage/circuit-metric.js — 30 秒慢輪詢家族：SID 累積量顯示 + 迴路指標
// ============================================================
// 純搬移自 scadapage.js（plan 2026-08-06-scadapage-js-split）。
// 不包 IIFE、global scope，與 js/designer/ 拆分同模式；
// 共享狀態集中於 state.js。原始 4-space 縮排刻意保留，
// 以保證 template literal 內容 byte-level 不變（純搬移原則）。
// ============================================================

    // \u2500\u2500 \u7d2f\u7a4d\u91cf\u5143\u4ef6\uff08AI \u9ede\u4f4d\u986f\u793a\u6a21\u5f0f=\u7576\u65e5/\u7576\u6708\u7d2f\u7a4d\uff09\uff1a30 \u79d2\u6162\u8f2a\u8a62\uff0c\u8207 1 \u79d2\u5373\u6642\u8ff4\u5708\u5206\u96e2 \u2500\u2500
    var ACC_POLL_MS = 30000;

    async function fetchAndUpdateAccumulations() {
        var els = document.querySelectorAll('.scada-rt-acc[data-sid]');
        if (!els.length) return;

        // \u4ee5 (sid, mode, kind, max) \u53bb\u91cd \u2014 \u540c\u9801\u591a\u5143\u4ef6\u7d81\u540c\u9ede\u53ea\u67e5\u4e00\u6b21
        var seen = {}, items = [];
        els.forEach(function (el) {
            var szKey = el.dataset.sid + '|' + el.dataset.valueMode + '|' + (el.dataset.accKind || 'meter') + '|' + (el.dataset.maxValue || '');
            if (seen[szKey]) return;
            seen[szKey] = true;
            items.push({
                szSid: el.dataset.sid,
                szAccMode: el.dataset.valueMode,
                szAccKind: el.dataset.accKind || 'meter',
                dMaxValue: el.dataset.maxValue ? parseFloat(el.dataset.maxValue) : null
            });
        });

        try {
            var resp = await fetch('/api/scadapage/accumulation', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ items: items })
            });
            if (!resp.ok) return;
            var result = await resp.json();
            if (!result.success || !result.results) return;
            var map = {};
            result.results.forEach(function (r) { map[r.szSid + '|' + r.szAccMode] = r; });
            els.forEach(function (el) { _renderAccWidget(el, map[el.dataset.sid + '|' + el.dataset.valueMode]); });
        } catch (_) { /* \u7db2\u8def\u5931\u6557\u7dad\u6301\u73fe\u503c\uff0c\u4e0b\u4e00\u8f2a\u518d\u8a66 */ }
    }

    // 累積元件 tooltip：點位名稱 + 日累/月累（畫面上不顯示 badge，僅 hover 提示）
    function _accTooltipTitle(szTitle, szValueMode) {
        var szMode = t(szValueMode === 'month' ? 'scadapage.acc.month_badge' : 'scadapage.acc.day_badge');
        return szTitle ? szTitle + ' ' + szMode : szMode;
    }

    // ── 迴路指標元件（AI 點位 / 表格 cell 綁 EnergyCircuit 四指標）：30 秒慢輪詢，
    //    與 1 秒即時迴圈、30 秒 SID 累積輪詢皆分離（plan 2026-07-23）──
    var CMETRIC_MAX_BATCH = 50;

    function _cmetricUnit(szMetric) {
        if (szMetric === 'period_cost') return t('scadapage.cmetric.unit_cost');
        if (szMetric === 'demand_kw') return 'kW';
        return 'kWh';
    }

    // tooltip：迴路名＋指標名（＋估算註記）
    function _cmetricTooltipTitle(szCircuitName, szMetric, bEstimated) {
        var szLabel = t('scadapage.cmetric.' + szMetric);
        var s = szCircuitName ? szCircuitName + ' ' + szLabel : szLabel;
        if (bEstimated) s += t('scadapage.cmetric.estimated');
        return s;
    }

    async function fetchAndUpdateCircuitMetrics() {
        var els = document.querySelectorAll('.scada-rt-cmetric[data-circuit-id], .scada-cmetric-cell[data-circuit-id]');
        if (!els.length) return;

        // 以 (circuitId, metric) 去重 — 同頁多元件綁同迴路同指標只查一次
        var seen = {}, items = [];
        els.forEach(function (el) {
            var szKey = el.dataset.circuitId + '|' + (el.dataset.metric || 'day_kwh');
            if (seen[szKey]) return;
            seen[szKey] = true;
            items.push({ nCircuitId: parseInt(el.dataset.circuitId), szMetric: el.dataset.metric || 'day_kwh' });
        });

        // 超過 50 筆分批送出，合併結果
        var map = {};
        for (var i = 0; i < items.length; i += CMETRIC_MAX_BATCH) {
            var batch = items.slice(i, i + CMETRIC_MAX_BATCH);
            try {
                var resp = await fetch('/api/scadapage/circuit-metric', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ items: batch })
                });
                if (!resp.ok) continue;
                var result = await resp.json();
                if (!result.success || !result.results) continue;
                result.results.forEach(function (r) { map[r.nCircuitId + '|' + r.szMetric] = r; });
            } catch (_) { /* 網路失敗維持現值，下一輪再試 */ }
        }

        els.forEach(function (el) {
            var r = map[el.dataset.circuitId + '|' + (el.dataset.metric || 'day_kwh')];
            if (el.classList.contains('scada-rt-cmetric')) _renderCmetricWidget(el, r);
            else _renderCmetricCell(el, r);
        });
    }

    function _renderCmetricWidget(el, r) {
        var szMetric = el.dataset.metric || 'day_kwh';
        var bOk = !!(r && r.szStatus === 'ok' && r.dValue != null);
        var bStale = !!(r && r.szStatus === 'stale' && r.dValue != null);
        var props = {
            nFontSize:   parseInt(el.dataset.nFontSize) || 28,
            szFontColor: (bOk ? (el.dataset.szFontColor || '#212529') : '#6c757d'),
            szUnit:      _cmetricUnit(szMetric),
            szTitle:     _cmetricTooltipTitle(el.dataset.circuitName || '', szMetric, !!(r && r.isEstimated)),
            szBgColor:   el.dataset.szBgColor || 'transparent'
        };
        if (!bOk && !bStale) {
            // no_data（迴路刪除/無葉子）、no_plan（無電價方案）→ 灰字 '--'
            el.innerHTML = buildRealtimeValueViewHtml(props, '--');
            return;
        }
        var nDec = parseInt(el.dataset.accDecimals);
        if (isNaN(nDec)) nDec = 1;
        el.innerHTML = buildRealtimeValueViewHtml(props, Number(r.dValue).toFixed(nDec));
    }

    function _renderCmetricCell(td, r) {
        var szOrigColor = td.dataset.origColor || '#444';
        var bHasValue = !!(r && r.dValue != null && (r.szStatus === 'ok' || r.szStatus === 'stale'));
        if (!bHasValue) {
            td.textContent = '--';
            td.style.color = '#6c757d';
            return;
        }
        var nDec = parseInt(td.dataset.decimals);
        if (isNaN(nDec)) nDec = 1;
        td.textContent = Number(r.dValue).toFixed(nDec);
        td.style.color = (r.szStatus === 'stale') ? '#6c757d' : szOrigColor;
        // 子迴路占比分攤估算 → title 追加估算註記（一次即可）
        if (r.isEstimated && td.dataset.estFlag !== '1') {
            td.title = (td.title || '') + t('scadapage.cmetric.estimated');
            td.dataset.estFlag = '1';
        }
    }

    function _renderAccWidget(el, r) {
        var props = {
            nFontSize:   parseInt(el.dataset.nFontSize) || 28,
            szFontColor: el.dataset.szFontColor || '#212529',
            szUnit:      el.dataset.accUnit || el.dataset.szUnit || '',
            szTitle:     _accTooltipTitle(el.dataset.szTitle || '', el.dataset.valueMode),
            szBgColor:   el.dataset.szBgColor || 'transparent'
        };
        if (!r || r.dValue == null) {
            // no_data\uff08\u671f\u521d/\u671f\u5167\u7121\u8cc7\u6599\uff09\u6216 stale \u4e14\u7121\u53ef\u7b97\u503c \u2192 \u7070\u5b57 '--'
            props.szFontColor = '#6c757d';
            el.innerHTML = buildRealtimeValueViewHtml(props, '--');
            return;
        }
        // stale\uff08\u9ede\u4f4d\u8cc7\u6599\u904e\u820a\uff0c\u503c\u70ba\u6700\u5f8c\u53ef\u7b97\u503c\uff09\u2192 \u7070\u8272\u5448\u73fe\u4ee5\u793a\u975e\u5373\u6642
        if (r.szStatus === 'stale') props.szFontColor = '#6c757d';
        var nDec = parseInt(el.dataset.accDecimals);
        if (isNaN(nDec)) nDec = 1;
        el.innerHTML = buildRealtimeValueViewHtml(props, Number(r.dValue).toFixed(nDec));
    }
