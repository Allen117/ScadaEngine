// 用電報表頁邏輯
(function () {
    'use strict';

    let g_circuits = [];
    let g_chart = null;
    let g_lastResult = null;

    document.addEventListener('DOMContentLoaded', () => {
        initDefaults();
        document.getElementById('erGranularity').addEventListener('change', updatePeriodVisibility);
        updatePeriodVisibility();
        // 等 i18n 字典載入後再 fetch 迴路（其中 placeholder 字串需要翻譯）
        if (window.i18n) {
            window.i18n.ready(loadCircuits);
        } else {
            loadCircuits();
        }
    });

    function t(key, args) {
        return (window.i18n && window.i18n.t) ? window.i18n.t(key, args) : key;
    }

    function initDefaults() {
        const today = (window._erInit && window._erInit.today) || new Date().toISOString().slice(0, 10);
        const d = new Date(today);
        const ym = today.slice(0, 7);
        document.getElementById('erDate').value = today;
        document.getElementById('erDayMonthStart').value = ym;
        document.getElementById('erDayMonthEnd').value = ym;
        document.getElementById('erMonthStart').value = ym;
        document.getElementById('erMonthEnd').value = ym;
        document.getElementById('erYearStart').value = d.getFullYear();
        document.getElementById('erYearEnd').value = d.getFullYear();
    }

    function updatePeriodVisibility() {
        const g = document.getElementById('erGranularity').value;
        document.querySelectorAll('.er-period').forEach(el => el.classList.remove('active'));
        document.querySelectorAll('.er-period-' + g).forEach(el => el.classList.add('active'));
    }

    async function loadCircuits() {
        try {
            const res = await fetch('/EnergyReport/api/circuits');
            g_circuits = await res.json();
            const sel = document.getElementById('erCircuit');
            // 排成樹狀縮排
            const items = buildIndentedList(g_circuits);
            sel.innerHTML = items.length === 0
                ? `<option value="">${escapeHtml(t('energyreport.select.empty'))}</option>`
                : `<option value="">${escapeHtml(t('energyreport.select.placeholder'))}</option>` +
                items.map(it => `<option value="${it.id}">${escapeHtml(it.label)}</option>`).join('');
        } catch (err) {
            console.error(t('energyreport.console.load_circuit_failed'), err);
        }
    }

    function buildIndentedList(nodes) {
        const out = [];
        const byParent = new Map();
        nodes.forEach(n => {
            const k = n.parentId == null ? 'null' : String(n.parentId);
            if (!byParent.has(k)) byParent.set(k, []);
            byParent.get(k).push(n);
        });
        byParent.forEach(arr => arr.sort((a, b) => a.sortOrder - b.sortOrder));
        function walk(parentKey, depth) {
            const arr = byParent.get(parentKey) || [];
            arr.forEach(n => {
                const prefix = '  '.repeat(depth) + (depth > 0 ? '└ ' : '');
                const tag = n.sid ? ' [⚡]' : ' [📁]';
                out.push({ id: n.id, label: prefix + n.name + tag });
                walk(String(n.id), depth + 1);
            });
        }
        walk('null', 0);
        return out;
    }

    function buildRequest() {
        const circuitId = parseInt(document.getElementById('erCircuit').value, 10);
        if (!circuitId) { alert(t('energyreport.alert.select_circuit')); return null; }
        const g = document.getElementById('erGranularity').value;
        let startStr, endStr;
        if (g === 'hour') {
            startStr = document.getElementById('erDate').value + 'T00:00:00';
            endStr = startStr;
        } else if (g === 'day') {
            startStr = document.getElementById('erDayMonthStart').value + '-01T00:00:00';
            endStr = document.getElementById('erDayMonthEnd').value + '-01T00:00:00';
            if (new Date(endStr) < new Date(startStr)) { alert(t('energyreport.alert.month_order')); return null; }
        } else if (g === 'month') {
            startStr = document.getElementById('erMonthStart').value + '-01T00:00:00';
            endStr = document.getElementById('erMonthEnd').value + '-01T00:00:00';
            if (new Date(endStr) < new Date(startStr)) { alert(t('energyreport.alert.month_order')); return null; }
        } else if (g === 'year') {
            const ys = document.getElementById('erYearStart').value;
            const ye = document.getElementById('erYearEnd').value;
            if (parseInt(ye, 10) < parseInt(ys, 10)) { alert(t('energyreport.alert.year_order')); return null; }
            startStr = ys + '-01-01T00:00:00';
            endStr = ye + '-01-01T00:00:00';
        }
        return { circuitId, granularity: g, start: startStr, end: endStr };
    }

    async function query() {
        const req = buildRequest();
        if (!req) return;

        document.getElementById('erTableBody').innerHTML =
            `<tr><td colspan="2" class="text-center text-muted py-3"><div class="spinner-border spinner-border-sm text-primary"></div> ${escapeHtml(t('energyreport.table.querying'))}</td></tr>`;
        document.getElementById('btnExport').disabled = true;

        try {
            const res = await fetch('/EnergyReport/api/query', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(req)
            });
            if (!res.ok) {
                const err = await res.json().catch(() => ({}));
                throw new Error(err.message || res.statusText);
            }
            const data = await res.json();
            g_lastResult = data;
            renderResult(data);
            document.getElementById('btnExport').disabled = false;
        } catch (err) {
            document.getElementById('erTableBody').innerHTML =
                `<tr><td colspan="2" class="text-center text-danger py-3">${escapeHtml(t('energyreport.alert.query_failed', { 0: err.message }))}</td></tr>`;
        }
    }

    function renderResult(data) {
        // 表格
        const tbody = document.getElementById('erTableBody');
        if (!data.buckets || data.buckets.length === 0) {
            tbody.innerHTML = `<tr><td colspan="2" class="text-center text-muted py-3">${escapeHtml(t('energyreport.table.no_data'))}</td></tr>`;
        } else {
            const totalLabel = escapeHtml(t('energyreport.table.total'));
            tbody.innerHTML = data.buckets.map(b => `
                <tr>
                    <td>${escapeHtml(b.szLabel)}</td>
                    <td class="text-end">${b.dKwh.toFixed(3)}</td>
                </tr>`).join('') +
                `<tr class="er-total"><td>${totalLabel}</td><td class="text-end">${data.dTotalKwh.toFixed(3)}</td></tr>`;
        }
        document.getElementById('erTotal').textContent = data.dTotalKwh.toFixed(3);
        document.getElementById('erWarnText').textContent = data.isHasWarning
            ? t('energyreport.warning.kwh_overflow') : '';

        // 圖表
        const labels = data.buckets.map(b => b.szLabel);
        const values = data.buckets.map(b => b.dKwh);
        if (g_chart) g_chart.destroy();
        const ctx = document.getElementById('erChart').getContext('2d');
        g_chart = new Chart(ctx, {
            type: 'bar',
            data: {
                labels,
                datasets: [{
                    label: t('energyreport.chart.dataset_label', { 0: data.szCircuitName }),
                    data: values,
                    backgroundColor: 'rgba(13, 110, 253, 0.6)',
                    borderColor: 'rgba(13, 110, 253, 1)',
                    borderWidth: 1
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: { legend: { display: true } },
                scales: {
                    // 不指定 beginAtZero — 含負值的虛擬迴路（A+B+C-D）需正確顯示負 bar
                    y: { title: { display: true, text: t('energyreport.chart.y_axis') } },
                    x: { title: { display: true, text: t('energyreport.chart.x_axis') } }
                }
            }
        });
    }

    async function exportExcel() {
        const req = buildRequest();
        if (!req) return;
        try {
            const res = await fetch('/EnergyReport/api/export', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(req)
            });
            if (!res.ok) {
                const err = await res.json().catch(() => ({}));
                throw new Error(err.message || res.statusText);
            }
            // 從 Content-Disposition 解析檔名（fallback 預設名）
            const cd = res.headers.get('Content-Disposition') || '';
            let szFileName = 'EnergyReport.xlsx';
            const m = cd.match(/filename\*?=(?:UTF-8'')?["']?([^"';]+)/i);
            if (m) szFileName = decodeURIComponent(m[1]);
            const blob = await res.blob();
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = szFileName;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            URL.revokeObjectURL(url);
        } catch (err) {
            alert(t('energyreport.alert.export_failed', { 0: err.message }));
        }
    }

    function escapeHtml(s) {
        if (s == null) return '';
        return String(s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;')
            .replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#039;');
    }

    window._er = { query, exportExcel };
})();
