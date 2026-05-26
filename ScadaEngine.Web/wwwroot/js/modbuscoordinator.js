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

    // 預設：若第一筆是單一 ModbusID，自動載入
    if (coordinators.length > 0) {
        var first = coordinators[0];
        var ids = (first.szModbusID || '').split(',');
        if (ids.length <= 1) {
            var firstItem = document.querySelector('.coordinator-item');
            if (firstItem) {
                firstItem.classList.add('active');
                showFullDetail(0);
            }
        }
    }
})();
