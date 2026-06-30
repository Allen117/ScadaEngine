(function () {
    'use strict';

    var POLL_MS = 60000;
    var _circuitId = null;
    var _pollTimer = null;
    var _charts = { month: null, day: null, hour: null };

    // ── 工具函式 ───────────────────────────────────────────────────────────────
    function pad2(n) { return n < 10 ? '0' + n : String(n); }

    function todayStr() {
        var d = new Date();
        return d.getFullYear() + '-' + pad2(d.getMonth() + 1) + '-' + pad2(d.getDate());
    }

    function thisMonthStr() {
        var d = new Date();
        return d.getFullYear() + '-' + pad2(d.getMonth() + 1);
    }

    function thisYearStr() {
        return String(new Date().getFullYear());
    }

    // ── 初始化 ────────────────────────────────────────────────────────────────
    function init() {
        document.getElementById('yearPicker').value  = thisYearStr();
        document.getElementById('monthPicker').value = thisMonthStr();
        document.getElementById('datePicker').value  = todayStr();

        document.getElementById('yearPicker').addEventListener('change', function () {
            if (_circuitId !== null) fetchAndRender('month', this.value);
        });
        document.getElementById('monthPicker').addEventListener('change', function () {
            if (_circuitId !== null) fetchAndRender('day', this.value);
        });
        document.getElementById('datePicker').addEventListener('change', function () {
            if (_circuitId === null) return;
            stopPoll();
            fetchAndRender('hour', this.value);
            startPoll();
        });

        loadTree();
    }

    // ── 迴路樹 ────────────────────────────────────────────────────────────────
    function loadTree() {
        fetch('/EMS/api/circuit-tree')
            .then(function (r) { return r.json(); })
            .then(function (nodes) { renderTree(nodes); })
            .catch(function (e) { console.error('[ems-circuit] 載入迴路樹失敗', e); });
    }

    function renderTree(nodes) {
        var map = {};
        nodes.forEach(function (n) {
            map[n.id] = { id: n.id, name: n.name, parentId: n.parentId, sortOrder: n.sortOrder, children: [] };
        });
        var roots = [];
        nodes.forEach(function (n) {
            if (n.parentId && map[n.parentId]) {
                map[n.parentId].children.push(map[n.id]);
            } else {
                roots.push(map[n.id]);
            }
        });
        sortTree(roots);

        var container = document.getElementById('circuitTree');
        container.innerHTML = '';
        container.appendChild(buildTreeEl(roots));

        container.addEventListener('click', function (e) {
            var item = e.target.closest('.ems-tree-item[data-id]');
            if (!item) return;

            if (e.target.classList.contains('ems-tree-expand')) {
                item.classList.toggle('collapsed');
                return;
            }

            selectNode(item);
        });

        // 預設選取最上層的第一個根節點
        var firstRoot = container.querySelector('.ems-tree-list > .ems-tree-item[data-id]');
        if (firstRoot) selectNode(firstRoot);
    }

    function selectNode(item) {
        var container = document.getElementById('circuitTree');
        container.querySelectorAll('.ems-tree-item').forEach(function (el) {
            el.classList.remove('selected');
        });
        item.classList.add('selected');
        _circuitId = parseInt(item.dataset.id, 10);
        document.getElementById('selectedCircuitName').textContent = item.dataset.name;
        loadAllCharts();
    }

    function sortTree(list) {
        list.sort(function (a, b) { return a.sortOrder - b.sortOrder; });
        list.forEach(function (n) { sortTree(n.children); });
    }

    function buildTreeEl(nodes) {
        var ul = document.createElement('ul');
        ul.className = 'ems-tree-list';
        nodes.forEach(function (n) {
            var li = document.createElement('li');
            li.className = 'ems-tree-item';
            li.dataset.id   = n.id;
            li.dataset.name = n.name;

            var icon = document.createElement('span');
            icon.className = n.children.length > 0 ? 'ems-tree-expand' : 'ems-tree-expand-placeholder';
            if (n.children.length > 0) icon.textContent = '▼';
            li.appendChild(icon);

            var label = document.createElement('span');
            label.className   = 'ems-tree-label';
            label.textContent = n.name;
            li.appendChild(label);

            if (n.children.length > 0) li.appendChild(buildTreeEl(n.children));
            ul.appendChild(li);
        });
        return ul;
    }

    // ── 圖表載入 ──────────────────────────────────────────────────────────────
    function loadAllCharts() {
        var yearPivot  = document.getElementById('yearPicker').value  || thisYearStr();
        var monthPivot = document.getElementById('monthPicker').value || thisMonthStr();
        var datePivot  = document.getElementById('datePicker').value  || todayStr();

        stopPoll();
        fetchAndRender('month', yearPivot);
        fetchAndRender('day',   monthPivot);
        fetchAndRender('hour',  datePivot);
        startPoll();
    }

    function fetchAndRender(granularity, pivot) {
        if (_circuitId === null) return;
        var url = '/EMS/api/circuit-energy?circuitId=' + encodeURIComponent(_circuitId) +
                  '&granularity=' + encodeURIComponent(granularity) +
                  '&pivot='       + encodeURIComponent(pivot);

        fetch(url)
            .then(function (r) { return r.json(); })
            .then(function (data) { renderBar(granularity, data.labels, data.values); })
            .catch(function (e) { console.error('[ems-circuit] fetch 失敗', granularity, e); });
    }

    // ── 長條圖渲染 ────────────────────────────────────────────────────────────
    function renderBar(granularity, labels, values) {
        var canvasIds = { month: 'chartMonth', day: 'chartDay', hour: 'chartHour' };
        var canvas = document.getElementById(canvasIds[granularity]);
        if (!canvas || !window.Chart) return;

        if (_charts[granularity]) {
            _charts[granularity].data.labels           = labels;
            _charts[granularity].data.datasets[0].data = values;
            _charts[granularity].update('none');
            return;
        }

        _charts[granularity] = new Chart(canvas.getContext('2d'), {
            type: 'bar',
            data: {
                labels: labels,
                datasets: [{
                    data: values,
                    backgroundColor: 'rgba(67,160,71,0.55)',
                    borderColor: '#2e7d32',
                    borderWidth: 1,
                    borderRadius: 3
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        callbacks: {
                            label: function (ctx) {
                                var v = ctx.parsed.y;
                                return (v != null ? v.toFixed(1) : '0') + ' kWh';
                            }
                        }
                    }
                },
                scales: {
                    x: {
                        ticks: { font: { size: 11 }, color: '#757575' },
                        grid: { display: false }
                    },
                    y: {
                        beginAtZero: true,
                        ticks: {
                            font: { size: 11 },
                            color: '#757575',
                            callback: function (v) { return v.toFixed(0); }
                        },
                        grid: { color: 'rgba(0,0,0,0.05)' }
                    }
                }
            }
        });
    }

    // ── 自動刷新（日卡片，60s）────────────────────────────────────────────────
    function startPoll() {
        stopPoll();
        _pollTimer = setTimeout(function doPoll() {
            console.log('[ems-circuit] 日卡片 auto-refresh');
            fetchAndRender('hour', document.getElementById('datePicker').value || todayStr());
            _pollTimer = setTimeout(doPoll, POLL_MS);
        }, POLL_MS);
    }

    function stopPoll() {
        if (_pollTimer) { clearTimeout(_pollTimer); _pollTimer = null; }
    }

    window._emsCircuit = { reload: loadAllCharts };
    document.addEventListener('DOMContentLoaded', init);
})();
