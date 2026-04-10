(function () {
    var coordinators = window._commSettingData || [];

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
            '<i class="fas fa-info-circle me-1"></i>\u8a2d\u5099\u8a73\u7d30\u8cc7\u6599 \u2014 ' + (c.szName || '');

        document.getElementById('fieldId').value             = c.Id;
        document.getElementById('fieldName').value           = c.szName || '';
        document.getElementById('fieldModbusID').value       = c.szModbusID || '';
        document.getElementById('fieldDelayTime').value      = c.nDelayTime;
        document.getElementById('fieldMonitorEnabled').value = c.isMonitorEnabled ? '\u662f' : '\u5426';
    }

    function showSubDetail(id, modbusId, name, index, subIndex) {
        nCurrentIndex = index;
        nCurrentSubIndex = subIndex;

        document.getElementById('fullDetail').style.display = 'none';
        document.getElementById('subDetail').style.display = '';
        document.getElementById('detailTitle').innerHTML =
            '<i class="fas fa-microchip me-1"></i>\u88dd\u7f6e\u8cc7\u6599 \u2014 ' + modbusId;

        document.getElementById('subFieldId').value       = id;
        document.getElementById('subFieldModbusID').value = modbusId;
        document.getElementById('subFieldName').value     = name || '';
        document.getElementById('saveStatus').style.display = 'none';
    }

    // \u55ae\u4e00 Coordinator \u9ede\u64ca
    document.querySelectorAll('.coordinator-item').forEach(function (item) {
        item.addEventListener('click', function (e) {
            e.preventDefault();
            clearAllActive();
            this.classList.add('active');
            showFullDetail(parseInt(this.dataset.index));
        });
    });

    // \u5c55\u958b / \u6536\u5408\u5b50\u9078\u55ae
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

    // \u5b50\u9805\u76ee\u9ede\u64ca
    document.querySelectorAll('.sub-item').forEach(function (item) {
        item.addEventListener('click', function (e) {
            e.preventDefault();
            clearAllActive();
            this.classList.add('active');
            showSubDetail(this.dataset.subId, this.dataset.subModbusid, this.dataset.subName,
                parseInt(this.dataset.index), parseInt(this.dataset.subIndex));
        });
    });

    // \u5132\u5b58\u540d\u7a31\u6309\u9215
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
                var resp = await fetch('/CommSetting/UpdateDeviceName', {
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
                    statusEl.textContent = '\u5132\u5b58\u6210\u529f';
                    statusEl.className = 'ms-2 small text-success';
                } else {
                    statusEl.textContent = result.message || '\u5132\u5b58\u5931\u6557';
                    statusEl.className = 'ms-2 small text-danger';
                }
            } catch (ex) {
                statusEl.textContent = '\u5132\u5b58\u5931\u6557\uff1a' + ex.message;
                statusEl.className = 'ms-2 small text-danger';
            }
            statusEl.style.display = '';
        });
    }

    // \u9810\u8a2d\uff1a\u82e5\u7b2c\u4e00\u7b46\u662f\u55ae\u4e00 ModbusID\uff0c\u81ea\u52d5\u8f09\u5165
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
