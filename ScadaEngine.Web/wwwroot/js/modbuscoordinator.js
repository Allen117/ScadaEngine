(function () {
    function t(key, args) { return (window.i18n && window.i18n.t) ? window.i18n.t(key, args) : key; }

    var coordinators = window._modbusCoordinatorData || [];

    var nCurrentIndex = -1;
    var nCurrentSubIndex = -1;

    function clearAllActive() {
        document.querySelectorAll('.coordinator-item, .coordinator-toggle, .sub-item')
            .forEach(function (el) { el.classList.remove('active'); });
    }

    function showFullDetail(index) {
        var c = coordinators[index];
        if (!c) return;

        document.getElementById('fullDetail').style.display = '';
        document.getElementById('subDetail').style.display = 'none';
        document.getElementById('detailTitle').innerHTML =
            '<i class="fas fa-info-circle me-1"></i>' + t('modbuscoordinator.card.device_detail') + ' — ' + (c.szName || '');

        document.getElementById('fieldId').value             = c.Id;
        document.getElementById('fieldName').value           = c.szName || '';
        document.getElementById('fieldModbusID').value       = c.szModbusID || '';
        document.getElementById('fieldDelayTime').value      = c.nDelayTime;
        document.getElementById('fieldMonitorEnabled').value = c.isMonitorEnabled ? t('modbuscoordinator.value.yes') : t('modbuscoordinator.value.no');

        showOpenPointsButton(c.szName || '');
    }

    function showSubDetail(id, modbusId, name, index, subIndex) {
        nCurrentIndex = index;
        nCurrentSubIndex = subIndex;

        document.getElementById('fullDetail').style.display = 'none';
        document.getElementById('subDetail').style.display = '';
        document.getElementById('detailTitle').innerHTML =
            '<i class="fas fa-microchip me-1"></i>' + t('modbuscoordinator.card.device_info') + ' — ' + modbusId;

        document.getElementById('subFieldId').value       = id;
        document.getElementById('subFieldModbusID').value = modbusId;
        document.getElementById('subFieldName').value     = name || '';
        document.getElementById('saveStatus').style.display = 'none';

        hideOpenPointsButton();
    }

    // 單一 Coordinator 點擊
    document.querySelectorAll('.coordinator-item').forEach(function (item) {
        item.addEventListener('click', function (e) {
            e.preventDefault();
            clearAllActive();
            this.classList.add('active');
            showFullDetail(parseInt(this.dataset.index));
        });
    });

    // 展開 / 收合子選單
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
            clearAllActive();
            this.classList.add('active');
            showFullDetail(parseInt(this.dataset.index));
        });
    });

    // 子項目點擊
    document.querySelectorAll('.sub-item').forEach(function (item) {
        item.addEventListener('click', function (e) {
            e.preventDefault();
            clearAllActive();
            this.classList.add('active');
            showSubDetail(this.dataset.subId, this.dataset.subModbusid, this.dataset.subName,
                parseInt(this.dataset.index), parseInt(this.dataset.subIndex));
        });
    });

    // 儲存名稱按鈕
    var btnSave = document.getElementById('btnSaveName');
    if (btnSave) {
        btnSave.addEventListener('click', async function () {
            if (nCurrentIndex < 0 || nCurrentSubIndex < 0) return;

            var c = coordinators[nCurrentIndex];
            var deviceNames = (c.szDeviceName || '').split(',').map(function (s) { return s.trim(); });
            var modbusIds = (c.szModbusID || '').split(',');
            while (deviceNames.length < modbusIds.length) deviceNames.push('');

            deviceNames[nCurrentSubIndex] = document.getElementById('subFieldName').value.trim();

            var szNewDeviceName = deviceNames.join(',');
            var statusEl = document.getElementById('saveStatus');

            try {
                var resp = await fetch('/ModbusCoordinator/UpdateDeviceName', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ id: c.Id, deviceName: szNewDeviceName })
                });
                var result = await resp.json();
                if (result.success) {
                    c.szDeviceName = szNewDeviceName;
                    var subItems = document.querySelectorAll('.sub-item[data-index="' + nCurrentIndex + '"]');
                    subItems.forEach(function (el, j) {
                        var name = deviceNames[j] || '';
                        el.dataset.subName = name;
                        var span = el.querySelector('span');
                        if (span) span.textContent = name || el.dataset.subModbusid;
                    });
                    statusEl.textContent = t('modbuscoordinator.msg.save_success');
                    statusEl.className = 'ms-2 small text-success';
                } else {
                    statusEl.textContent = result.message || t('modbuscoordinator.msg.save_failed');
                    statusEl.className = 'ms-2 small text-danger';
                }
            } catch (ex) {
                statusEl.textContent = t('modbuscoordinator.msg.save_failed_with', { error: ex.message });
                statusEl.className = 'ms-2 small text-danger';
            }
            statusEl.style.display = '';
        });
    }

    /* ============================================================
     * 點位熱編輯（限 Admin — 非 Admin 時 #pointsModal / #btnOpenPoints 不存在，以下全部短路）
     *
     * 入口：設備詳情卡片標題列的「點位設定」按鈕 → 彈出 Modal。
     * 只准原地改 Name / Address / Ratio / Unit / Min / Max；
     * DataType 唯讀、禁止增刪與排序（SID 由陣列索引產生，結構一變即歷史錯位）。
     * ============================================================ */

    var pointsModalEl = document.getElementById('pointsModal');
    var btnOpenPoints = document.getElementById('btnOpenPoints');
    var szPointsCoordinator = null;   // 目前選中的 Coordinator 名稱（= JSON 檔名）
    var aOriginalPoints = [];         // 原始點位（存檔時 diff 用）

    function showOpenPointsButton(szName) {
        szPointsCoordinator = szName || null;
        if (btnOpenPoints) btnOpenPoints.style.display = szName ? '' : 'none';
    }

    function hideOpenPointsButton() {
        if (btnOpenPoints) btnOpenPoints.style.display = 'none';
    }

    if (btnOpenPoints && pointsModalEl) {
        btnOpenPoints.addEventListener('click', function () {
            if (!szPointsCoordinator) return;
            bootstrap.Modal.getOrCreateInstance(pointsModalEl).show();
            loadPoints(szPointsCoordinator);
        });
    }

    function setPointsStatus(szText, isError) {
        var el = document.getElementById('pointsStatus');
        if (!el) return;
        el.textContent = szText;
        el.className = 'small ' + (isError ? 'text-danger' : 'text-success');
        el.style.display = szText ? '' : 'none';
    }

    /** Engine 支援的資料型態 — 與後端 ModbusConfigFileService.SupportedDataTypes 一致 */
    var DATA_TYPES = ['INTEGER', 'UINTEGER', 'FLOATINGPT', 'SWAPPEDFP', 'DOUBLE', 'SWAPPEDDOUBLE', 'UINT32BE'];

    /**
     * Modbus 位址驗證 — 與後端 ModbusConfigFileService.IsValidAddress 一致：
     * 5 位數慣例（0xxxx Coil 1-9999、1xxxx Discrete、3xxxx Input、4xxxx Holding）
     * 或 6 位數擴充慣例（000001-065536 / 1xxxxx / 3xxxxx / 4xxxxx），靠字串長度（含前導 0）區分
     */
    function isValidAddress(sz) {
        if (!/^\d{1,6}$/.test(sz)) return false;
        var n = parseInt(sz, 10);
        if (sz.length === 6) {
            return (n >= 1 && n <= 65536) || (n >= 100001 && n <= 165536) ||
                   (n >= 300001 && n <= 365536) || (n >= 400001 && n <= 465536);
        }
        return (n >= 1 && n <= 9999) || (n >= 10000 && n <= 19999) ||
               (n >= 30000 && n <= 39999) || (n >= 40000 && n <= 49999);
    }

    function makeCellInput(szValue, szField, szCssExtra) {
        var td = document.createElement('td');
        var input = document.createElement('input');
        input.type = 'text';
        input.className = 'form-control form-control-sm point-input' + (szCssExtra ? ' ' + szCssExtra : '');
        input.value = szValue == null ? '' : szValue;
        input.dataset.field = szField;
        input.autocomplete = 'off';
        td.appendChild(input);
        return td;
    }

    function renderPoints(data) {
        var tbody = document.getElementById('pointsTbody');
        tbody.innerHTML = '';

        var infoEl = document.getElementById('pointsDeviceInfo');
        infoEl.textContent = t('modbuscoordinator.points.device_info', {
            ip: data.ip, port: data.port, modbusId: data.modbusId, timeout: data.connectTimeout
        });

        (data.points || []).forEach(function (p, i) {
            var tr = document.createElement('tr');

            var tdSeq = document.createElement('td');
            tdSeq.className = 'text-center text-muted points-col-seq';
            tdSeq.textContent = i + 1;
            tr.appendChild(tdSeq);

            tr.appendChild(makeCellInput(p.name, 'name'));
            tr.appendChild(makeCellInput(p.address, 'address', 'point-input-address'));

            // DataType 下拉 — 限 Engine 支援清單；原值不在清單（大小寫或舊格式）時保留為第一個選項，避免無操作也被視為變更
            var tdType = document.createElement('td');
            var sel = document.createElement('select');
            sel.className = 'form-select form-select-sm point-select';
            sel.dataset.field = 'dataType';
            var szCurrent = p.dataType || '';
            if (DATA_TYPES.indexOf(szCurrent) < 0 && szCurrent !== '') {
                var optKeep = document.createElement('option');
                optKeep.value = szCurrent;
                optKeep.textContent = szCurrent;
                sel.appendChild(optKeep);
            }
            DATA_TYPES.forEach(function (szType) {
                var opt = document.createElement('option');
                opt.value = szType;
                opt.textContent = szType;
                sel.appendChild(opt);
            });
            sel.value = szCurrent;
            tdType.appendChild(sel);
            tr.appendChild(tdType);

            tr.appendChild(makeCellInput(p.ratio, 'ratio', 'point-input-num'));
            tr.appendChild(makeCellInput(p.unit, 'unit', 'point-input-num'));
            tr.appendChild(makeCellInput(p.min, 'min', 'point-input-num'));
            tr.appendChild(makeCellInput(p.max, 'max', 'point-input-num'));

            tbody.appendChild(tr);
        });
    }

    async function loadPoints(szName) {
        if (!pointsModalEl || !szName) return;

        szPointsCoordinator = szName;
        aOriginalPoints = [];
        setPointsStatus('', false);

        var titleEl = document.getElementById('pointsTitle');
        titleEl.innerHTML = '<i class="fas fa-list-ol me-1"></i>';
        titleEl.appendChild(document.createTextNode(t('modbuscoordinator.points.title') + ' — ' + szName));

        document.getElementById('pointsTbody').innerHTML =
            '<tr><td colspan="8" class="text-center text-muted py-3">' + t('modbuscoordinator.points.msg_loading') + '</td></tr>';

        try {
            var resp = await fetch('/ModbusCoordinator/Points/' + encodeURIComponent(szName), { credentials: 'same-origin' });
            if (!resp.ok) throw new Error('HTTP ' + resp.status);
            var data = await resp.json();
            aOriginalPoints = data.points || [];
            renderPoints(data);
        } catch (ex) {
            document.getElementById('pointsTbody').innerHTML = '';
            setPointsStatus(t('modbuscoordinator.points.msg_load_failed') + ' (' + ex.message + ')', true);
        }
    }

    /** 從表格收集點位（DataType 取原始值 — 唯讀欄位不進輸入框）；驗證失敗回傳 null */
    function collectAndValidatePoints() {
        var rows = document.querySelectorAll('#pointsTbody tr');
        if (rows.length !== aOriginalPoints.length) return null;

        var aPoints = [];
        for (var i = 0; i < rows.length; i++) {
            var get = function (field) {
                var el = rows[i].querySelector('[data-field="' + field + '"]');
                return el ? el.value.trim() : '';
            };
            var p = {
                name: get('name'),
                address: get('address'),
                dataType: get('dataType'),
                ratio: get('ratio'),
                unit: get('unit'),
                min: get('min'),
                max: get('max')
            };

            var nRow = i + 1;
            if (!p.name) {
                setPointsStatus(t('modbuscoordinator.points.err_name_empty', { row: nRow }), true);
                return null;
            }
            if (!isValidAddress(p.address)) {
                setPointsStatus(t('modbuscoordinator.points.err_address_invalid', { row: nRow }), true);
                return null;
            }
            if (p.ratio === '' || isNaN(Number(p.ratio))) {
                setPointsStatus(t('modbuscoordinator.points.err_ratio_invalid', { row: nRow }), true);
                return null;
            }
            if ((p.min !== '' && isNaN(Number(p.min))) || (p.max !== '' && isNaN(Number(p.max)))) {
                setPointsStatus(t('modbuscoordinator.points.err_minmax_invalid', { row: nRow }), true);
                return null;
            }
            aPoints.push(p);
        }
        return aPoints;
    }

    function countChanges(aPoints) {
        var nChanged = 0;
        for (var i = 0; i < aPoints.length; i++) {
            var o = aOriginalPoints[i], p = aPoints[i];
            if (o.name !== p.name || o.address !== p.address || (o.dataType || '') !== p.dataType ||
                o.ratio !== p.ratio || o.unit !== p.unit || o.min !== p.min || o.max !== p.max) nChanged++;
        }
        return nChanged;
    }

    var btnSavePoints = document.getElementById('btnSavePoints');
    if (btnSavePoints) {
        btnSavePoints.addEventListener('click', async function () {
            if (!szPointsCoordinator || aOriginalPoints.length === 0) return;

            var aPoints = collectAndValidatePoints();
            if (!aPoints) return;

            var nChanged = countChanges(aPoints);
            if (nChanged === 0) {
                setPointsStatus(t('modbuscoordinator.points.msg_no_change'), false);
                return;
            }

            if (!window.confirm(t('modbuscoordinator.points.confirm_save', { count: nChanged }))) return;

            btnSavePoints.disabled = true;
            try {
                var resp = await fetch('/ModbusCoordinator/UpdatePoints', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    credentials: 'same-origin',
                    body: JSON.stringify({ coordinatorName: szPointsCoordinator, points: aPoints })
                });
                var result = await resp.json();
                if (resp.ok && result.success) {
                    aOriginalPoints = aPoints;
                    setPointsStatus(t('modbuscoordinator.points.msg_save_success'), false);
                } else {
                    setPointsStatus(result.message || t('modbuscoordinator.msg.save_failed'), true);
                }
            } catch (ex) {
                setPointsStatus(t('modbuscoordinator.msg.save_failed_with', { error: ex.message }), true);
            } finally {
                btnSavePoints.disabled = false;
            }
        });
    }

    var btnReloadPoints = document.getElementById('btnReloadPoints');
    if (btnReloadPoints) {
        btnReloadPoints.addEventListener('click', function () {
            if (szPointsCoordinator) loadPoints(szPointsCoordinator);
        });
    }

    // 預設：若第一筆是單一 ModbusID，自動載入
    if (coordinators.length > 0) {
        var first = coordinators[0];
        var ids = (first.szModbusID || '').split(',');
        if (ids.length <= 1) {
            var firstItem = document.querySelector('.coordinator-item');
            if (firstItem) {
                firstItem.classList.add('active');
                if (window.i18n && window.i18n.ready) {
                    window.i18n.ready(function () { showFullDetail(0); });
                } else {
                    showFullDetail(0);
                }
            }
        }
    }
})();
