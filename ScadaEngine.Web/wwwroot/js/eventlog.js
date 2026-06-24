(function () {
    var _queryResults = [];

    function t(key, args) {
        return (window.i18n && window.i18n.t) ? window.i18n.t(key, args) : key;
    }

    function escapeHtml(text) {
        if (text == null) return '';
        var div = document.createElement('div');
        div.appendChild(document.createTextNode(text));
        return div.innerHTML;
    }

    function queryEvents() {
        var startTime = document.getElementById('startTime').value;
        var endTime = document.getElementById('endTime').value;

        if (!startTime || !endTime) {
            alert(t('eventlog.alert.select_time'));
            return;
        }

        var params = new URLSearchParams();
        params.append('startTime', startTime);
        params.append('endTime', endTime);

        var eventType = document.getElementById('eventType').value;
        var severity = document.getElementById('severity').value;
        var sidEl = document.getElementById('sidFilter');
        var sid = (sidEl.dataset.sid || '').trim();
        var acknowledged = document.getElementById('acknowledged').value;

        if (eventType !== '') params.append('eventType', eventType);
        if (severity !== '') params.append('severity', severity);
        if (sid !== '') params.append('sid', sid);
        if (acknowledged !== '') params.append('acknowledged', acknowledged);

        var overlay = document.getElementById('loadingOverlay');
        overlay.classList.add('active');
        document.getElementById('btnQuery').disabled = true;

        fetch('/api/eventlog/query?' + params.toString())
            .then(function (r) { return r.json(); })
            .then(function (result) {
                overlay.classList.remove('active');
                document.getElementById('btnQuery').disabled = false;

                if (!result.success) {
                    alert(t('eventlog.alert.query_failed', { 0: result.error || t('eventlog.alert.unknown_error') }));
                    return;
                }

                _queryResults = result.data || [];
                renderTable(_queryResults);
                renderStats(_queryResults);
                document.getElementById('resultInfo').textContent =
                    t('eventlog.result.found', { 0: result.total });
                document.getElementById('btnExport').style.display =
                    result.total > 0 ? '' : 'none';
                document.getElementById('statsRow').style.display =
                    result.total > 0 ? '' : 'none';
            })
            .catch(function (err) {
                overlay.classList.remove('active');
                document.getElementById('btnQuery').disabled = false;
                alert(t('eventlog.alert.query_error', { 0: err.message }));
            });
    }

    function renderTable(data) {
        var tbody = document.getElementById('resultBody');

        if (!data || data.length === 0) {
            tbody.innerHTML = '<tr><td colspan="8" class="text-center text-muted py-5">' +
                '<i class="fas fa-inbox fa-2x mb-2 d-block"></i>' +
                escapeHtml(t('eventlog.result.empty_query')) + '</td></tr>';
            return;
        }

        var html = '';
        for (var i = 0; i < data.length; i++) {
            var e = data[i];
            // 組合 tooltip 明細（觸發值、門檻值、條件）
            var tipParts = [];
            tipParts.push(t('eventlog.tooltip.trigger_value') + ': ' + (e.triggerValue != null ? e.triggerValue.toFixed(2) : '-'));
            tipParts.push(t('eventlog.tooltip.threshold') + ': ' + (e.thresholdValue != null ? e.thresholdValue.toFixed(2) : '-'));
            tipParts.push(t('eventlog.tooltip.condition') + ': ' + (e.operatorSymbol || '-'));
            var tipHtml = tipParts.map(function (txt) { return '<div>' + escapeHtml(txt) + '</div>'; }).join('');

            html += '<tr>';
            html += '<td>' + (i + 1) + '</td>';
            html += '<td>' + escapeHtml(e.occurredAt) + '</td>';
            html += '<td><span class="badge badge-event-' + e.eventType + '">' +
                    escapeHtml(e.eventTypeName) + '</span></td>';
            html += '<td><span class="badge badge-severity-' + e.severity + '">' +
                    escapeHtml(e.severityName) + '</span></td>';
            html += '<td class="msg-cell"><span class="msg-tooltip-wrap">' +
                    escapeHtml(e.message) +
                    '<span class="msg-tip">' + tipHtml + '</span></span></td>';
            // 資訊類事件（EventType=3，控制動作等）為一次性紀錄，無「恢復 / 確認」概念，統一顯示 '-'
            var isInfoEvent = (e.eventType === 3);
            if (isInfoEvent) {
                html += '<td class="text-muted">-</td>';
                html += '<td class="text-muted">-</td>';
                html += '<td class="text-muted">-</td>';
            } else {
                html += '<td>' + (e.isCleared
                    ? '<span class="text-success">' + escapeHtml(e.clearedAt) + '</span>'
                    : '<span class="text-danger fw-bold">' + escapeHtml(t('eventlog.result.cell.active')) + '</span>') + '</td>';
                html += '<td>' + (e.isAcknowledged
                    ? '<span class="badge bg-success">' + escapeHtml(t('eventlog.ack.acked')) + '</span>'
                    : '<span class="badge bg-warning text-dark">' + escapeHtml(t('eventlog.ack.unack')) + '</span>') + '</td>';
                html += '<td>' + escapeHtml(e.acknowledgedBy || '-') + '</td>';
            }
            html += '</tr>';
        }

        tbody.innerHTML = html;
    }

    function renderStats(data) {
        document.getElementById('statTotal').textContent = data.length;
        var nAlarm = 0, nWarning = 0, nCleared = 0, nUnack = 0;
        for (var i = 0; i < data.length; i++) {
            if (data[i].eventType === 0) nAlarm++;
            if (data[i].eventType === 2) nWarning++;
            if (data[i].isCleared) nCleared++;
            if (!data[i].isAcknowledged) nUnack++;
        }
        document.getElementById('statAlarm').textContent = nAlarm;
        document.getElementById('statWarning').textContent = nWarning;
        document.getElementById('statCleared').textContent = nCleared;
        document.getElementById('statUnack').textContent = nUnack;
    }

    function exportCSV() {
        if (!_queryResults || _queryResults.length === 0) return;

        var headers = ['#',
            t('eventlog.col.occurred_at'),
            t('eventlog.col.event_type'),
            t('eventlog.col.severity'),
            t('eventlog.col.message'),
            t('eventlog.col.cleared_at'),
            t('eventlog.col.ack_status'),
            t('eventlog.col.ack_by')];
        var rows = [headers.join(',')];

        for (var i = 0; i < _queryResults.length; i++) {
            var e = _queryResults[i];
            var isInfoEvent = (e.eventType === 3);
            var row = [
                i + 1,
                '"' + e.occurredAt + '"',
                '"' + e.eventTypeName + '"',
                '"' + e.severityName + '"',
                '"' + (e.message || '').replace(/"/g, '""') + '"',
                '"' + (isInfoEvent ? '-' : (e.clearedAt || t('eventlog.result.cell.active'))) + '"',
                isInfoEvent ? '-' : (e.isAcknowledged ? t('eventlog.ack.acked') : t('eventlog.ack.unack')),
                '"' + (isInfoEvent ? '-' : (e.acknowledgedBy || '')) + '"'
            ];
            rows.push(row.join(','));
        }

        var csvContent = '﻿' + rows.join('\n');
        var blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
        var link = document.createElement('a');
        link.href = URL.createObjectURL(blob);
        link.download = t('eventlog.csv.filename') + '_' + new Date().toISOString().slice(0, 10) + '.csv';
        link.click();
    }

    // ── tooltip 動態定位（自動判斷上方/下方）──
    var tableContainer = document.querySelector('.table-container');
    if (tableContainer) {
        tableContainer.addEventListener('mouseenter', function (ev) {
            var wrap = ev.target.closest('.msg-tooltip-wrap');
            if (!wrap) return;
            var tip = wrap.querySelector('.msg-tip');
            if (!tip) return;

            // 先顯示以取得尺寸
            tip.style.display = 'block';
            tip.classList.remove('tip-above', 'tip-below');

            var wrapRect = wrap.getBoundingClientRect();
            var containerRect = tableContainer.getBoundingClientRect();
            var thead = tableContainer.querySelector('thead');
            var headerBottom = thead ? thead.getBoundingClientRect().bottom : containerRect.top;
            var tipH = tip.offsetHeight;

            // 上方空間 = wrap 頂部到標題列底部
            var spaceAbove = wrapRect.top - headerBottom;

            if (spaceAbove >= tipH + 8) {
                // 顯示在上方
                tip.classList.add('tip-above');
                tip.style.left = wrapRect.left + 'px';
                tip.style.top = (wrapRect.top - tipH - 6) + 'px';
            } else {
                // 顯示在下方
                tip.classList.add('tip-below');
                tip.style.left = wrapRect.left + 'px';
                tip.style.top = (wrapRect.bottom + 6) + 'px';
            }
        }, true);

        tableContainer.addEventListener('mouseleave', function (ev) {
            var wrap = ev.target.closest('.msg-tooltip-wrap');
            if (!wrap) return;
            var tip = wrap.querySelector('.msg-tip');
            if (tip) tip.style.display = 'none';
        }, true);
    }

    // ── 時間欄位 picker（24h、依 i18n 切 locale） ──
    var startTimeEl = document.getElementById('startTime');
    var endTimeEl   = document.getElementById('endTime');
    if (window._fpInit) {
        window._fpInit.datetime(startTimeEl);
        window._fpInit.datetime(endTimeEl);
    }

    // ── 快速時間範圍按鈕 ──
    function fmtDtLocal(d) {
        var pad = function (n) { return n < 10 ? '0' + n : n; };
        return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()) +
               ' ' + pad(d.getHours()) + ':' + pad(d.getMinutes());
    }

    function ceilToMinute(d) {
        if (d.getSeconds() > 0 || d.getMilliseconds() > 0) {
            return new Date(d.getFullYear(), d.getMonth(), d.getDate(),
                            d.getHours(), d.getMinutes() + 1, 0, 0);
        }
        return d;
    }

    document.querySelectorAll('.quick-range').forEach(function (btn) {
        btn.addEventListener('click', function () {
            var h = parseInt(this.dataset.hours);
            var now = ceilToMinute(new Date());
            var start = new Date(now - h * 3600000);
            if (startTimeEl._flatpickr) {
                startTimeEl._flatpickr.setDate(start, true);
                endTimeEl._flatpickr.setDate(now, true);
            } else {
                startTimeEl.value = fmtDtLocal(start);
                endTimeEl.value   = fmtDtLocal(now);
            }
        });
    });

    // ── 點位選擇器 ──
    var CALC_DEVICE_ID = -999;
    var _ppDevices = null;
    var _ppPoints = null;
    var _ppModal = null;
    var _ppPickedDevId = -1;
    var _ppPickedModbusId = null;
    var _ppPickedSid = null;
    var _ppPickedName = '';

    function isCalcSid(sid) { return sid && sid.indexOf('CALC-') === 0; }

    function getSidPrefix(szSid) {
        var m = szSid.match(/^(\d+)-S\d+$/);
        return m ? parseInt(m[1], 10) : -1;
    }
    function isOfDevice(szSid, nDevId) {
        var n = getSidPrefix(szSid);
        return n >= nDevId * 65536 && n < (nDevId + 1) * 65536;
    }

    function ppEnsureData() {
        if (_ppDevices && _ppPoints) return Promise.resolve();
        return Promise.all([
            fetch('/Designer/Devices').then(function (r) { return r.json(); }),
            fetch('/Designer/Points').then(function (r) { return r.json(); })
        ]).then(function (res) {
            _ppDevices = res[0];
            _ppPoints = res[1];
            // 為每個點位補上設備標籤
            _ppPoints.forEach(function (p) {
                var nPfx = getSidPrefix(p.szSid);
                p.szDevLabel = '';
                for (var di = 0; di < _ppDevices.length; di++) {
                    var d = _ppDevices[di];
                    if (!isOfDevice(p.szSid, d.nId)) continue;
                    var mids = (d.szModbusID || '').split(',').map(function (s) { return s.trim(); }).filter(Boolean);
                    var dnames = (d.szDeviceName || '').split(',').map(function (s) { return s.trim(); });
                    if (mids.length > 1) {
                        for (var j = 0; j < mids.length; j++) {
                            var mid = parseInt(mids[j], 10);
                            var base = d.nId * 65536 + mid * 256;
                            if (nPfx >= base && nPfx < base + 256) {
                                p.szDevLabel = (j < dnames.length && dnames[j]) ? dnames[j] : d.szName;
                                break;
                            }
                        }
                    } else {
                        p.szDevLabel = d.szName;
                    }
                    break;
                }
            });
        });
    }

    function openPointPicker() {
        ppEnsureData().then(function () {
            _ppPickedSid = null;
            _ppPickedDevId = -1;
            ppShowStep1();
            if (!_ppModal) {
                _ppModal = new bootstrap.Modal(document.getElementById('ppModal'));
            }
            _ppModal.show();
        }).catch(function (err) {
            alert(t('eventlog.picker.load_failed', { 0: err.message }));
        });
    }

    function ppShowStep1() {
        document.getElementById('ppStep1').style.display = '';
        document.getElementById('ppStep2').style.display = 'none';
        document.getElementById('ppTitle').innerHTML =
            '<i class="fas fa-crosshairs text-primary me-1"></i>' + escapeHtml(t('eventlog.picker.title.select_device'));
        document.getElementById('ppConfirmBtn').disabled = true;
        ppRenderDevices();
    }

    function ppRenderDevices() {
        var container = document.getElementById('ppDeviceList');
        if (!_ppDevices || _ppDevices.length === 0) {
            container.innerHTML = '<div class="text-center text-muted py-4"><i class="fas fa-plug fa-2x mb-2 d-block"></i>' +
                escapeHtml(t('eventlog.picker.no_devices')) + '</div>';
            return;
        }
        var html = '';
        for (var i = 0; i < _ppDevices.length; i++) {
            var d = _ppDevices[i];
            var nPts = 0;
            for (var k = 0; k < _ppPoints.length; k++) {
                if (isOfDevice(_ppPoints[k].szSid, d.nId)) nPts++;
            }
            var mids = (d.szModbusID || '').split(',').map(function (s) { return s.trim(); }).filter(Boolean);
            var dnames = (d.szDeviceName || '').split(',').map(function (s) { return s.trim(); });

            if (mids.length > 1) {
                html += '<div class="pp-dev-item" onclick="window._eventLog.ppToggleSub(this)">' +
                    '<i class="fas fa-server text-primary me-2"></i>' +
                    '<span class="flex-fill">' + escapeHtml(d.szName) + '</span>' +
                    '<small class="text-muted me-2">' + escapeHtml(t('eventlog.picker.point_count', { 0: nPts })) + '</small>' +
                    '<i class="fas fa-chevron-down pp-toggle-icon"></i></div>';
                html += '<div class="pp-sub-menu" style="display:none;">';
                for (var j = 0; j < mids.length; j++) {
                    var subLabel = (j < dnames.length && dnames[j]) ? dnames[j] : mids[j];
                    html += '<div class="pp-dev-item pp-sub-item" onclick="window._eventLog.ppSelectDevice(' +
                        d.nId + ',\'' + escapeHtml(subLabel).replace(/'/g, "\\'") + '\',' + parseInt(mids[j], 10) + ')">' +
                        '<i class="fas fa-microchip text-info me-2" style="font-size:.8rem;"></i>' +
                        '<span class="flex-fill">' + escapeHtml(subLabel) + '</span>' +
                        '<i class="fas fa-chevron-right text-muted" style="font-size:.7rem;"></i></div>';
                }
                html += '</div>';
            } else {
                html += '<div class="pp-dev-item" onclick="window._eventLog.ppSelectDevice(' +
                    d.nId + ',\'' + escapeHtml(d.szName).replace(/'/g, "\\'") + '\')">' +
                    '<i class="fas fa-server text-primary me-2"></i>' +
                    '<span class="flex-fill">' + escapeHtml(d.szName) + '</span>' +
                    '<small class="text-muted me-2">' + escapeHtml(t('eventlog.picker.point_count', { 0: nPts })) + '</small>' +
                    '<i class="fas fa-chevron-right text-muted" style="font-size:.7rem;"></i></div>';
            }
        }

        // 計算點位
        var nCalcPts = 0;
        for (var ci = 0; ci < _ppPoints.length; ci++) {
            if (isCalcSid(_ppPoints[ci].szSid)) nCalcPts++;
        }
        if (nCalcPts > 0) {
            var calcLabel = t('eventlog.picker.calc_point');
            html += '<div class="pp-dev-item" onclick="window._eventLog.ppSelectDevice(' +
                CALC_DEVICE_ID + ',\'' + escapeHtml(calcLabel).replace(/'/g, "\\'") + '\')">' +
                '<i class="fas fa-calculator text-warning me-2"></i>' +
                '<span class="flex-fill">' + escapeHtml(calcLabel) + '</span>' +
                '<small class="text-muted me-2">' + escapeHtml(t('eventlog.picker.point_count', { 0: nCalcPts })) + '</small>' +
                '<i class="fas fa-chevron-right text-muted" style="font-size:.7rem;"></i></div>';
        }

        container.innerHTML = html;
    }

    function ppToggleSub(el) {
        var sub = el.nextElementSibling;
        var icon = el.querySelector('.pp-toggle-icon');
        if (sub.style.display === 'none') {
            sub.style.display = '';
            icon.style.transform = 'rotate(180deg)';
        } else {
            sub.style.display = 'none';
            icon.style.transform = '';
        }
    }

    function ppSelectDevice(nDevId, szLabel, nModbusId) {
        _ppPickedDevId = nDevId;
        _ppPickedModbusId = (nModbusId != null) ? nModbusId : null;
        _ppPickedSid = null;
        document.getElementById('ppStep1').style.display = 'none';
        document.getElementById('ppStep2').style.display = '';
        document.getElementById('ppTitle').innerHTML =
            '<i class="fas fa-crosshairs text-primary me-1"></i>' + escapeHtml(t('eventlog.picker.title.select_point'));
        document.getElementById('ppDevName').textContent = szLabel;
        document.getElementById('ppSearch').value = '';
        document.getElementById('ppConfirmBtn').disabled = true;
        ppRenderPoints('');
    }

    function ppBack() {
        ppShowStep1();
    }

    function ppFilter(szKeyword) {
        _ppPickedSid = null;
        document.getElementById('ppConfirmBtn').disabled = true;
        ppRenderPoints(szKeyword);
    }

    function ppRenderPoints(szKeyword) {
        var szQ = (szKeyword || '').trim().toLowerCase();
        var filtered = (_ppPoints || []).filter(function (p) {
            if (_ppPickedDevId === CALC_DEVICE_ID) {
                if (!isCalcSid(p.szSid)) return false;
            } else if (_ppPickedModbusId != null) {
                var nPfx = getSidPrefix(p.szSid);
                var base = _ppPickedDevId * 65536 + _ppPickedModbusId * 256;
                if (nPfx < base || nPfx >= base + 256) return false;
            } else {
                if (!isOfDevice(p.szSid, _ppPickedDevId)) return false;
            }
            return !szQ || p.szName.toLowerCase().indexOf(szQ) >= 0;
        });

        var container = document.getElementById('ppPointList');
        if (filtered.length === 0) {
            container.innerHTML = '<div class="text-center text-muted py-4"><i class="fas fa-inbox fa-2x mb-2 d-block"></i>' +
                escapeHtml(t('eventlog.picker.no_points')) + '</div>';
            return;
        }
        var html = '';
        for (var i = 0; i < filtered.length; i++) {
            var p = filtered[i];
            html += '<div class="pp-point-item" data-sid="' + escapeHtml(p.szSid) + '" ' +
                'onclick="window._eventLog.ppSelectPoint(this,\'' + escapeHtml(p.szSid).replace(/'/g, "\\'") + '\',\'' +
                escapeHtml(p.szName).replace(/'/g, "\\'") + '\')">' +
                '<i class="fas fa-circle text-success me-2" style="font-size:6px;"></i>' +
                '<span class="flex-fill">' + escapeHtml(p.szName) + '</span>' +
                '<small class="text-warning">' + escapeHtml(p.szUnit || '') + '</small></div>';
        }
        container.innerHTML = html;
    }

    function ppSelectPoint(el, szSid, szName) {
        var items = document.querySelectorAll('#ppPointList .pp-point-item');
        for (var i = 0; i < items.length; i++) items[i].classList.remove('selected');
        el.classList.add('selected');
        _ppPickedSid = szSid;
        _ppPickedName = szName;
        document.getElementById('ppConfirmBtn').disabled = false;
    }

    function ppConfirm() {
        if (!_ppPickedSid) return;
        _ppModal.hide();
        document.getElementById('sidFilter').value = _ppPickedName;
        document.getElementById('sidFilter').dataset.sid = _ppPickedSid;
        document.getElementById('btnClearPicker').style.display = '';
    }

    function clearPointPicker() {
        document.getElementById('sidFilter').value = '';
        document.getElementById('sidFilter').dataset.sid = '';
        document.getElementById('btnClearPicker').style.display = 'none';
    }

    // 暴露至 window 供 View onclick 呼叫
    window._eventLog = {
        queryEvents: queryEvents,
        exportCSV: exportCSV,
        openPointPicker: openPointPicker,
        clearPointPicker: clearPointPicker,
        ppBack: ppBack,
        ppFilter: ppFilter,
        ppToggleSub: ppToggleSub,
        ppSelectDevice: ppSelectDevice,
        ppSelectPoint: ppSelectPoint,
        ppConfirm: ppConfirm
    };
})();
