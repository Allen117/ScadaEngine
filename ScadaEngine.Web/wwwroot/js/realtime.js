(function () {
    var isRefreshing = false;

    // 由 cshtml inline script 設定的初始資料
    var currentCoordinatorDbId = window._realtimeInitCoordinatorId || 0;
    var currentSubModbusId = null;

    // 排序狀態
    var sortColumn = null;
    var sortDirection = 'asc';

    // 快取全部點位資料（含未收到資料的點位）
    var lastData = window._realtimeInitData || [];

    // 記錄目前表格顯示的 subTopic 順序，用於判斷是否需要重建
    var currentDisplayOrder = [];

    // 排序點擊處理
    function sortTable(column) {
        if (sortColumn === column) {
            sortDirection = sortDirection === 'asc' ? 'desc' : 'asc';
        } else {
            sortColumn = column;
            sortDirection = 'asc';
        }
        updateSortIcons();
        updateTable();
    }

    // 更新欄位排序圖示
    function updateSortIcons() {
        ['name', 'subTopic', 'value', 'unit', 'quality', 'timestamp'].forEach(function (col) {
            var th = document.getElementById('th-' + col);
            if (!th) return;
            th.classList.remove('sort-asc', 'sort-desc');
            th.querySelector('.sort-icon').textContent = '\u21C5';
            if (col === sortColumn) {
                th.classList.add(sortDirection === 'asc' ? 'sort-asc' : 'sort-desc');
                th.querySelector('.sort-icon').textContent = '';
            }
        });
    }

    // 取得 SID 中 S 後的序號（e.g. "196865-S3" → 3）
    function getSNumber(sid) {
        if (!sid) return 0;
        var m = sid.match(/-S(\d+)$/);
        return m ? parseInt(m[1]) : 0;
    }

    // 對資料陣列排序
    function applySorting(data) {
        if (!sortColumn) {
            return [].concat(data).sort(function (a, b) { return getSNumber(a.sid) - getSNumber(b.sid); });
        }
        return [].concat(data).sort(function (a, b) {
            var va = a[sortColumn] != null ? a[sortColumn] : '';
            var vb = b[sortColumn] != null ? b[sortColumn] : '';
            if (sortColumn === 'value') {
                var na = parseFloat(va);
                var nb = parseFloat(vb);
                var aIsNum = !isNaN(na);
                var bIsNum = !isNaN(nb);
                if (aIsNum && bIsNum) { va = na; vb = nb; }
                else if (!aIsNum && bIsNum) return 1;
                else if (aIsNum && !bIsNum) return -1;
            }
            if (va < vb) return sortDirection === 'asc' ? -1 : 1;
            if (va > vb) return sortDirection === 'asc' ? 1 : -1;
            return 0;
        });
    }

    // 依目前選取的 Coordinator 篩選資料
    function filterByCoordinator(data) {
        if (!currentCoordinatorDbId) return data;

        // 計算點位篩選（SID 以 CALC- 開頭）
        if (currentCoordinatorDbId === -999) {
            var filtered = data.filter(function (item) {
                return item.sid && item.sid.indexOf('CALC-') === 0;
            });
            console.log('[\u7BE9\u9078] \u8A08\u7B97\u9EDE\u4F4D, \u7E3D\u8CC7\u6599=' + data.length +
                ', \u7BE9\u9078\u5F8C=' + filtered.length);
            return filtered;
        }

        var rangeBase, rangeEnd;
        if (currentSubModbusId !== null) {
            var mid = parseInt(currentSubModbusId);
            rangeBase = currentCoordinatorDbId * 65536 + mid * 256;
            rangeEnd = rangeBase + 256;
        } else {
            rangeBase = currentCoordinatorDbId * 65536;
            rangeEnd = rangeBase + 65536;
        }

        var filtered = data.filter(function (item) {
            if (!item.sid) return false;
            var hyphen = item.sid.indexOf('-');
            if (hyphen < 0) return false;
            var sidNum = parseInt(item.sid.substring(0, hyphen));
            return !isNaN(sidNum) && sidNum >= rangeBase && sidNum < rangeEnd;
        });
        console.log('[\u7BE9\u9078] CoordinatorId=' + currentCoordinatorDbId +
            ', SubModbusId=' + currentSubModbusId +
            ', \u7BC4\u570D=[' + rangeBase + ', ' + rangeEnd +
            '), \u7E3D\u8CC7\u6599=' + data.length +
            ', \u7BE9\u9078\u5F8C=' + filtered.length);
        if (filtered.length === 0 && data.length > 0) {
            var sample = data.slice(0, 5).map(function (d) { return d.sid; });
            console.warn('[\u7BE9\u9078] \u7BE9\u9078\u7D50\u679C\u70BA\u7A7A\uFF01\u524D 5 \u7B46 SID:', sample);
        }
        return filtered;
    }

    // 建立單行 HTML
    function buildRowHtml(item) {
        var tsDisplay = item.timestamp === '--' ? '--' : item.timestamp.substring(5, 19);
        return '<tr class="' + item.cssClass + '" data-subtopic="' + item.subTopic + '">' +
            '<td>' + item.name + '</td>' +
            '<td><small class="text-muted">' + item.subTopic + '</small></td>' +
            '<td class="text-end"><span class="fw-bold">' + item.value + '</span></td>' +
            '<td><span class="badge bg-light text-dark">' + item.unit + '</span></td>' +
            '<td><span class="badge ' + item.badgeClass + '">' + item.quality + '</span></td>' +
            '<td><small>' + tsDisplay + '</small></td>' +
            '</tr>';
    }

    // 對單一 cell 觸發閃爍動畫
    function flashCell(el) {
        el.classList.remove('cell-updated');
        void el.offsetWidth;
        el.classList.add('cell-updated');
    }

    // 比對單行資料，只更新有變化的欄位
    function updateRowIfChanged(row, item) {
        var valueSpan = row.querySelector('td:nth-child(3) span.fw-bold');
        if (valueSpan && valueSpan.textContent !== item.value) {
            valueSpan.textContent = item.value;
            flashCell(valueSpan);
        }
        var qualityBadge = row.querySelector('td:nth-child(5) span.badge');
        var newQualityClass = 'badge ' + item.badgeClass;
        if (qualityBadge) {
            if (qualityBadge.textContent !== item.quality) qualityBadge.textContent = item.quality;
            if (qualityBadge.className !== newQualityClass) qualityBadge.className = newQualityClass;
        }
        var tsSmall = row.querySelector('td:nth-child(6) small');
        var newTs = item.timestamp === '--' ? '--' : item.timestamp.substring(5, 19);
        if (tsSmall && tsSmall.textContent !== newTs) tsSmall.textContent = newTs;
        if (row.className !== item.cssClass) row.className = item.cssClass;
    }

    // 自動更新函式
    function refreshData() {
        if (isRefreshing) return;
        isRefreshing = true;
        var refreshBtn = document.getElementById('refreshBtn');
        var refreshIcon = document.getElementById('refreshIcon');
        refreshIcon.classList.add('fa-spin');
        refreshBtn.disabled = true;

        fetch('/api/realtime/latest')
            .then(function (response) { return response.json(); })
            .then(function (result) {
                if (result.success) {
                    updateTable(result.data);
                    updateConnectionStatus(result.connectionStatus);
                    updateFooterTime(result.timestamp);
                } else {
                    console.error('\u66F4\u65B0\u8CC7\u6599\u5931\u6557:', result.error);
                    showTemporaryAlert('\u66F4\u65B0\u8CC7\u6599\u5931\u6557: ' + result.error, 'danger');
                }
            })
            .catch(function (error) {
                console.error('AJAX \u8ACB\u6C42\u5931\u6557:', error);
                showTemporaryAlert('\u7DB2\u8DEF\u8ACB\u6C42\u5931\u6557\uFF0C\u8ACB\u6AA2\u67E5\u9023\u7DDA', 'danger');
            })
            .finally(function () {
                isRefreshing = false;
                refreshIcon.classList.remove('fa-spin');
                refreshBtn.disabled = false;
            });
    }

    // 更新連線狀態
    function updateConnectionStatus(isConnected) {
        var statusBadge = document.getElementById('connectionStatus');
        if (isConnected) {
            statusBadge.className = 'badge bg-success me-3';
            statusBadge.innerHTML = '<i class="fas fa-wifi me-1"></i>\u9023\u7DDA\u6B63\u5E38';
        } else {
            statusBadge.className = 'badge bg-danger me-3';
            statusBadge.innerHTML = '<i class="fas fa-exclamation-triangle me-1"></i>\u9023\u7DDA\u7570\u5E38';
        }
    }

    // 更新表格：結構不變時 partial update，否則全部重建
    function updateTable(data) {
        if (data !== undefined && data && data.length > 0) {
            var merged = new Map();
            lastData.forEach(function (item) { merged.set(item.sid, item); });
            data.forEach(function (item) { merged.set(item.sid, item); });
            lastData = Array.from(merged.values());
        }
        var filtered = filterByCoordinator(lastData);
        var tbody = document.getElementById('realtimeTableBody');

        var pointCountEl = document.getElementById('pointCount');
        if (pointCountEl) pointCountEl.textContent = filtered.length;
        if (filtered.length === 0) {
            tbody.innerHTML = '<tr>' +
                '<td colspan="6" class="text-center text-muted py-4">' +
                '<i class="fas fa-inbox fa-2x mb-2 d-block"></i>' +
                '\u5C1A\u7121\u5373\u6642\u8CC7\u6599\uFF0C\u8ACB\u78BA\u8A8D MQTT \u9023\u7DDA\u72C0\u614B' +
                '</td></tr>';
            currentDisplayOrder = [];
            return;
        }

        var sorted = applySorting(filtered);
        var newOrder = sorted.map(function (x) { return x.subTopic; });

        var sameStructure =
            newOrder.length === currentDisplayOrder.length &&
            newOrder.every(function (s, i) { return s === currentDisplayOrder[i]; });

        if (!sameStructure) {
            tbody.innerHTML = sorted.map(function (item) { return buildRowHtml(item); }).join('');
            currentDisplayOrder = newOrder;
            return;
        }

        var rows = Array.from(tbody.querySelectorAll('tr[data-subtopic]'));
        sorted.forEach(function (item, i) {
            if (rows[i]) updateRowIfChanged(rows[i], item);
        });
    }

    // 更新頁腳時間
    function updateFooterTime(timestamp) {
        document.getElementById('footerLastUpdate').textContent = '\u6700\u5F8C\u66F4\u65B0: ' + timestamp;
    }

    // 顯示臨時提醒
    function showTemporaryAlert(message, type) {
        type = type || 'info';
        var alertHtml =
            '<div class="alert alert-' + type + ' alert-dismissible fade show" role="alert">' +
            '<i class="fas fa-info-circle me-2"></i>' +
            message +
            '<button type="button" class="btn-close" data-bs-dismiss="alert"></button>' +
            '</div>';
        var container = document.querySelector('.container-fluid');
        var alertDiv = document.createElement('div');
        alertDiv.innerHTML = alertHtml;
        container.insertBefore(alertDiv, container.firstChild);
        setTimeout(function () {
            var alert = document.querySelector('.alert');
            if (alert) alert.remove();
        }, 3000);
    }

    // 頁面載入完成後啟動
    document.addEventListener('DOMContentLoaded', function () {
        updateTable();

        function clearAllSidebarActive() {
            document.querySelectorAll('.coordinator-item, .coordinator-toggle, .sub-item')
                .forEach(function (el) { el.classList.remove('active'); });
        }

        // 單一設備點擊（無子選單）
        document.querySelectorAll('.coordinator-item').forEach(function (item) {
            item.addEventListener('click', function (e) {
                e.preventDefault();
                clearAllSidebarActive();
                this.classList.add('active');
                currentCoordinatorDbId = parseInt(this.dataset.id) || 0;
                currentSubModbusId = null;
                var nameEl = document.getElementById('currentCoordinatorName');
                if (nameEl) nameEl.textContent = '\u2014 ' + (this.dataset.name || '');
                updateTable();
            });
        });

        // 展開/收合子選單 + 顯示整個 Coordinator 資料
        document.querySelectorAll('.coordinator-toggle').forEach(function (toggle) {
            toggle.addEventListener('click', function (e) {
                e.preventDefault();
                var subMenu = this.nextElementSibling;
                var icon = this.querySelector('.toggle-icon');
                if (subMenu.style.display === 'none') {
                    subMenu.style.display = '';
                    icon.classList.add('open');
                } else {
                    subMenu.style.display = 'none';
                    icon.classList.remove('open');
                }
                clearAllSidebarActive();
                this.classList.add('active');
                currentCoordinatorDbId = parseInt(this.dataset.id) || 0;
                currentSubModbusId = null;
                var nameEl = document.getElementById('currentCoordinatorName');
                if (nameEl) nameEl.textContent = '\u2014 ' + (this.dataset.name || '');
                updateTable();
            });
        });

        // 子項目點擊：篩選特定 ModbusID
        document.querySelectorAll('.sub-item').forEach(function (item) {
            item.addEventListener('click', function (e) {
                e.preventDefault();
                clearAllSidebarActive();
                this.classList.add('active');
                currentCoordinatorDbId = parseInt(this.dataset.id) || 0;
                currentSubModbusId = this.dataset.modbusid;
                var nameEl = document.getElementById('currentCoordinatorName');
                if (nameEl) nameEl.textContent = '\u2014 ' + (this.dataset.name || '');
                updateTable();
            });
        });

        // 1 秒後執行第一次 AJAX 更新
        setTimeout(refreshData, 1000);

        // 每 3 秒自動更新
        setInterval(refreshData, 3000);

        console.log('SCADA \u5373\u6642\u76E3\u63A7\u5100\u8868\u677F\u5DF2\u555F\u52D5\uFF0C\u81EA\u52D5\u66F4\u65B0\u9031\u671F: 3\u79D2');
    });

    // 對外介面掛在 window._realtime 供 onclick 等屬性呼叫
    window._realtime = {
        sortTable: sortTable,
        refreshData: refreshData
    };
})();
