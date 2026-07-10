// LogicFlow Point Picker：點位 / 計算點位 / DB 點位 / 排程 來源選擇 Modal
(function () {
    const S = window.__lfNS;

    function getSidPrefix(szSid) {
        const m = szSid.match(/^(\d+)-S\d+$/);
        return m ? parseInt(m[1], 10) : -1;
    }
    function isCalcSid(szSid) {
        return szSid && szSid.indexOf('CALC-') === 0;
    }
    function isDbSid(szSid) {
        return !!szSid && /^DB\d+-S\d+$/.test(szSid);
    }
    function isPointOfDev(szSid, nDevId) {
        if (nDevId === S.PP_CALC_DEV_ID) return isCalcSid(szSid);
        if (nDevId === S.PP_DB_DEV_ID)   return isDbSid(szSid);
        if (isCalcSid(szSid) || isDbSid(szSid)) return false;
        const p = getSidPrefix(szSid);
        return p >= nDevId * 65536 && p < (nDevId + 1) * 65536;
    }

    async function ppEnsureData() {
        if (S.ppAllDevices && S.ppAllPoints) return;
        const [rDev, rPt] = await Promise.all([
            fetch('/Designer/Devices'), fetch('/Designer/Points')
        ]);
        if (!rDev.ok || !rPt.ok) throw new Error(S.t('logicflow.error.device_load_failed'));
        S.ppAllDevices = await rDev.json();
        S.ppAllPoints  = await rPt.json();
        // 附加設備名稱到點位
        S.ppAllPoints.forEach(p => {
            if (isDbSid(p.szSid)) {
                p._devLabel = p.szGroupName || 'DB';
                return;
            }
            if (isCalcSid(p.szSid)) {
                p._devLabel = p.szGroupName || S.t('logicflow.pp.calc_points');
                return;
            }
            const pfx = getSidPrefix(p.szSid);
            for (const d of S.ppAllDevices) {
                if (!isPointOfDev(p.szSid, d.nId)) continue;
                const mids = (d.szModbusID || '').split(',').map(s => s.trim()).filter(Boolean);
                const names = (d.szDeviceName || '').split(',').map(s => s.trim());
                if (mids.length > 1) {
                    for (let j = 0; j < mids.length; j++) {
                        const base = d.nId * 65536 + parseInt(mids[j], 10) * 256;
                        if (pfx >= base && pfx < base + 256) {
                            p._devLabel = (j < names.length && names[j]) ? names[j] : d.szName;
                            break;
                        }
                    }
                } else {
                    p._devLabel = d.szName;
                }
                break;
            }
        });
    }

    // =========== 歷史值讀取欄位 ===========
    function ppIsHistApplicable() {
        return S.ppPendingType === 'input' || S.ppPendingType === 'contact_no' || S.ppPendingType === 'contact_nc';
    }

    function ppUpdateHistVisibility() {
        var sec = document.getElementById('ppHistSection');
        if (!sec) return;
        sec.style.display = (ppIsHistApplicable() && S.ppSourceMode !== 'schedule') ? '' : 'none';
    }

    function ppHistToggle(isChecked) {
        document.getElementById('ppHistOffsetWrap').style.display = isChecked ? '' : 'none';
    }

    async function openPointPicker() {
        S.ppPickedSid = null;
        S.ppPickedDevId = -1;
        S.ppPickedModbusId = null;
        S.ppPickedScheduleId = null;
        S.ppPickedScheduleName = null;
        S.ppSourceMode = 'point';
        document.getElementById('btnConfirmPoint').disabled = true;
        try {
            await ppEnsureData();
        } catch (e) { alert(e.message); return; }
        if (!S.ppModal) S.ppModal = new bootstrap.Modal(document.getElementById('pointPickerModal'));

        // contact 類型才顯示來源切換
        const isContact = (S.ppPendingType === 'contact_no' || S.ppPendingType === 'contact_nc');
        const toggleEl = document.getElementById('ppSourceToggle');
        if (isContact) { toggleEl.classList.remove('d-none'); } else { toggleEl.classList.add('d-none'); }
        document.getElementById('ppBtnPoint').classList.add('active');
        document.getElementById('ppBtnSchedule').classList.remove('active');
        document.getElementById('ppStep3').style.display = 'none';

        // 歷史值讀取欄位初始化（編輯模式帶回現值，預設不勾）
        const editNodeHist = S.ppEditNodeId != null ? S.canvasNodes.find(n => n.id === S.ppEditNodeId) : null;
        const isHistOn = !!(editNodeHist && editNodeHist.histEnabled);
        document.getElementById('ppHistEnabled').checked = isHistOn;
        document.getElementById('ppHistOffset').value =
            (editNodeHist && editNodeHist.histOffsetMinutes != null) ? editNodeHist.histOffsetMinutes : 60;
        ppHistToggle(isHistOn);
        ppUpdateHistVisibility();

        // 編輯模式：檢查是排程還是點位
        if (S.ppEditNodeId != null) {
            const editNode = S.canvasNodes.find(n => n.id === S.ppEditNodeId);
            // 排程模式
            if (editNode && editNode.scheduleId != null) {
                ppSwitchSource('schedule');
                S.ppPickedScheduleId = editNode.scheduleId;
                S.ppPickedScheduleName = editNode.scheduleName;
                document.getElementById('btnConfirmPoint').disabled = false;
                S.ppModal.show();
                setTimeout(() => {
                    const item = document.querySelector('#scheduleListContainer .pp-list-item[data-schedule-id="' + editNode.scheduleId + '"]');
                    if (item) { item.classList.add('selected'); item.scrollIntoView({ block: 'center', behavior: 'smooth' }); }
                }, 50);
                return;
            }
            const boundSid = editNode && editNode.sid;
            if (boundSid && S.ppAllPoints) {
                const boundPoint = S.ppAllPoints.find(p => p.szSid === boundSid);
                if (boundPoint) {
                    if (isDbSid(boundSid)) {
                        ppSelectDbCoordinator(boundPoint.szGroupName || '');
                        S.ppPickedSid = boundSid;
                        document.getElementById('btnConfirmPoint').disabled = false;
                        S.ppModal.show();
                        setTimeout(() => {
                            const item = document.querySelector('#pointListContainer .pp-list-item[data-sid="' + boundSid + '"]');
                            if (item) { item.classList.add('selected'); item.scrollIntoView({ block: 'center', behavior: 'smooth' }); }
                        }, 50);
                        return;
                    }
                    if (isCalcSid(boundSid)) {
                        ppShowCalcStep();
                        S.ppPickedSid = boundSid;
                        document.getElementById('btnConfirmPoint').disabled = false;
                        S.ppModal.show();
                        setTimeout(() => {
                            const item = document.querySelector('#pointListContainer .pp-list-item[data-sid="' + boundSid + '"]');
                            if (item) { item.classList.add('selected'); item.scrollIntoView({ block: 'center', behavior: 'smooth' }); }
                        }, 50);
                        return;
                    }
                    const pfx = getSidPrefix(boundSid);
                    let foundDev = null, foundModbusId = null, szLabel = '';
                    for (const d of S.ppAllDevices) {
                        if (!isPointOfDev(boundSid, d.nId)) continue;
                        foundDev = d;
                        szLabel = d.szName;
                        const mids = (d.szModbusID || '').split(',').map(s => s.trim()).filter(Boolean);
                        const names = (d.szDeviceName || '').split(',').map(s => s.trim());
                        if (mids.length > 1) {
                            for (let j = 0; j < mids.length; j++) {
                                const mid = parseInt(mids[j], 10);
                                const base = d.nId * 65536 + mid * 256;
                                if (pfx >= base && pfx < base + 256) {
                                    foundModbusId = mid;
                                    szLabel = (j < names.length && names[j]) ? names[j] : String(mid);
                                    break;
                                }
                            }
                        }
                        break;
                    }
                    if (foundDev) {
                        ppSelectDev(foundDev.nId, szLabel, foundModbusId);
                        S.ppPickedSid = boundSid;
                        document.getElementById('btnConfirmPoint').disabled = false;
                        S.ppModal.show();
                        setTimeout(() => {
                            const item = document.querySelector('#pointListContainer .pp-list-item[data-sid="' + boundSid + '"]');
                            if (item) {
                                item.classList.add('selected');
                                item.scrollIntoView({ block: 'center', behavior: 'smooth' });
                            }
                        }, 50);
                        return;
                    }
                }
            }
        }

        ppShowStep1();
        S.ppModal.show();
    }

    function ppShowStep0() {
        document.getElementById('ppModalTitle').textContent = S.t('logicflow.pp.title_select_source');
        document.getElementById('ppStep0').style.display = '';
        document.getElementById('ppStep1').style.display = 'none';
        document.getElementById('ppStep2').style.display = 'none';
        document.getElementById('btnConfirmPoint').disabled = true;
        // 寫入點位不可選計算點位（計算值無法控制）；DB 點位允許寫入
        var calcEl = document.getElementById('ppStep0Calc');
        if (calcEl) calcEl.style.display = (S.ppPendingType === 'output') ? 'none' : '';
        var dbEl = document.getElementById('ppStep0Db');
        if (dbEl) dbEl.style.display = '';
    }

    function ppShowStep1() {
        // 三種來源（設備 / 計算 / DB）統一從 Step 0 入口；output 由 ppShowStep0 隱藏計算點位
        ppShowStep0();
    }

    function ppShowDeviceStep() {
        document.getElementById('ppModalTitle').textContent = S.t('logicflow.pp.title_select_device');
        document.getElementById('ppStep0').style.display = 'none';
        document.getElementById('ppStep1').style.display = '';
        document.getElementById('ppStep2').style.display = 'none';
        // 寫入點位直接進設備清單，不需返回按鈕
        var backBtn = document.getElementById('ppStep1Back');
        if (backBtn) backBtn.style.display = (S.ppPendingType === 'output') ? 'none' : '';
        const container = document.getElementById('deviceListContainer');
        if (!S.ppAllDevices || S.ppAllDevices.length === 0) {
            container.innerHTML = '<div class="text-center text-muted py-4"><i class="fas fa-plug fa-2x mb-2 d-block"></i>' + S.escHtml(S.t('logicflow.pp.empty.no_devices')) + '</div>';
            return;
        }
        container.innerHTML = S.ppAllDevices.map(d => {
            const nPts = (S.ppAllPoints || []).filter(p => isPointOfDev(p.szSid, d.nId)).length;
            const mids = (d.szModbusID || '').split(',').map(s => s.trim()).filter(Boolean);
            const names = (d.szDeviceName || '').split(',').map(s => s.trim());
            if (mids.length > 1) {
                const subs = mids.map((mid, j) => {
                    const label = (j < names.length && names[j]) ? names[j] : mid;
                    return `<div class="pp-list-item" style="padding-left:28px;" onclick="window._lf.ppSelectDev(${d.nId},'${S.escHtml(label)}',${mid})">
                        <i class="fas fa-microchip text-info" style="font-size:12px;"></i>
                        <div style="flex:1;"><div class="pp-point-name">${S.escHtml(label)}</div></div>
                        <i class="fas fa-chevron-right text-muted" style="font-size:11px;"></i></div>`;
                }).join('');
                return `<div class="pp-list-item" onclick="this.nextElementSibling.style.display=this.nextElementSibling.style.display==='none'?'':'none'">
                    <i class="fas fa-server text-primary" style="font-size:14px;"></i>
                    <div style="flex:1;"><div class="pp-point-name">${S.escHtml(d.szName)}</div><div class="pp-point-sid">${S.escHtml(S.t('logicflow.pp.points_count', { count: nPts }))}</div></div>
                    <i class="fas fa-chevron-down text-muted" style="font-size:11px;"></i></div>
                <div style="display:none;">${subs}</div>`;
            }
            return `<div class="pp-list-item" onclick="window._lf.ppSelectDev(${d.nId},'${S.escHtml(d.szName)}')">
                <i class="fas fa-server text-primary" style="font-size:14px;"></i>
                <div style="flex:1;"><div class="pp-point-name">${S.escHtml(d.szName)}</div><div class="pp-point-sid">${S.escHtml(S.t('logicflow.pp.points_count', { count: nPts }))}</div></div>
                <i class="fas fa-chevron-right text-muted" style="font-size:11px;"></i></div>`;
        }).join('');
    }

    function ppShowCalcStep() {
        S.ppPickedDevId = S.PP_CALC_DEV_ID;
        S.ppPickedModbusId = null;
        S.ppPickedSid = null;
        S.ppPickedCalcGroup = null;

        const calcGroups = _ppGetCalcGroups();
        if (calcGroups.length > 0) {
            // 有群組 — 顯示群組清單（Step 1）
            document.getElementById('ppStep0').style.display = 'none';
            document.getElementById('ppStep1').style.display = '';
            document.getElementById('ppStep2').style.display = 'none';
            document.getElementById('ppModalTitle').textContent = S.t('logicflow.pp.title_select_calc_group');
            var backBtn = document.getElementById('ppStep1Back');
            if (backBtn) backBtn.style.display = '';
            _ppRenderCalcGroupList(calcGroups);
        } else {
            // 無群組 — 直接顯示全部計算點位
            _ppShowCalcPointsFlat();
        }
    }

    function _ppGetCalcGroups() {
        if (!S.ppAllPoints) return [];
        const groups = {};
        S.ppAllPoints.forEach(p => {
            if (!isCalcSid(p.szSid)) return;
            const g = p.szGroupName || '';
            if (!groups[g]) groups[g] = 0;
            groups[g]++;
        });
        return Object.keys(groups).filter(g => g !== '').sort();
    }

    function _ppRenderCalcGroupList(groups) {
        const container = document.getElementById('deviceListContainer');
        const hasUngrouped = (S.ppAllPoints || []).some(p => isCalcSid(p.szSid) && !p.szGroupName);
        let html = groups.map(g => {
            const nPts = (S.ppAllPoints || []).filter(p => isCalcSid(p.szSid) && p.szGroupName === g).length;
            return `<div class="pp-list-item" onclick="window._lf.ppSelectCalcGroup('${S.escHtml(g)}')">
                <i class="fas fa-layer-group text-warning" style="font-size:14px;"></i>
                <div style="flex:1;"><div class="pp-point-name">${S.escHtml(g)}</div><div class="pp-point-sid">${S.escHtml(S.t('logicflow.pp.points_count', { count: nPts }))}</div></div>
                <i class="fas fa-chevron-right text-muted" style="font-size:11px;"></i></div>`;
        }).join('');
        if (hasUngrouped) {
            const nPts = (S.ppAllPoints || []).filter(p => isCalcSid(p.szSid) && !p.szGroupName).length;
            html += `<div class="pp-list-item" onclick="window._lf.ppSelectCalcGroup('')">
                <i class="fas fa-inbox text-secondary" style="font-size:14px;"></i>
                <div style="flex:1;"><div class="pp-point-name">${S.escHtml(S.t('logicflow.pp.ungrouped'))}</div><div class="pp-point-sid">${S.escHtml(S.t('logicflow.pp.points_count', { count: nPts }))}</div></div>
                <i class="fas fa-chevron-right text-muted" style="font-size:11px;"></i></div>`;
        }
        container.innerHTML = html;
    }

    function ppSelectCalcGroup(szGroup) {
        S.ppPickedDevId = S.PP_CALC_DEV_ID;
        S.ppPickedModbusId = null;
        S.ppPickedCalcGroup = szGroup;
        S.ppPickedSid = null;
        document.getElementById('ppDeviceName').textContent = szGroup || S.t('logicflow.pp.ungrouped');
        document.getElementById('ppDeviceIcon').className = 'fas fa-layer-group me-1';
        document.getElementById('ppStep0').style.display = 'none';
        document.getElementById('ppStep1').style.display = 'none';
        document.getElementById('ppStep2').style.display = '';
        document.getElementById('ppModalTitle').textContent = S.t('logicflow.pp.title_select_calc_point');
        document.getElementById('ppPointSearch').value = '';
        document.getElementById('btnConfirmPoint').disabled = true;
        ppRenderPoints('');
    }

    function _ppShowCalcPointsFlat() {
        document.getElementById('ppStep0').style.display = 'none';
        document.getElementById('ppStep1').style.display = 'none';
        document.getElementById('ppStep2').style.display = '';
        document.getElementById('ppModalTitle').textContent = '選擇計算點位';
        document.getElementById('ppDeviceName').textContent = S.t('logicflow.pp.calc_points');
        document.getElementById('ppDeviceIcon').className = 'fas fa-calculator me-1';
        document.getElementById('ppPointSearch').value = '';
        document.getElementById('btnConfirmPoint').disabled = true;
        ppRenderPoints('');
    }

    function ppBackToStep0() {
        ppShowStep0();
    }

    // =========== DB 來源 ===========
    function ppShowDbStep() {
        S.ppPickedDevId = S.PP_DB_DEV_ID;
        S.ppPickedModbusId = null;
        S.ppPickedSid = null;
        S.ppPickedDbCoord = null;

        const dbCoords = _ppGetDbCoordinators();
        if (dbCoords.length === 0) {
            _ppShowDbPointsFlat();
            return;
        }
        document.getElementById('ppStep0').style.display = 'none';
        document.getElementById('ppStep1').style.display = '';
        document.getElementById('ppStep2').style.display = 'none';
        document.getElementById('ppModalTitle').textContent = S.t('logicflow.pp.title_select_db_device');
        var backBtn = document.getElementById('ppStep1Back');
        if (backBtn) backBtn.style.display = '';
        _ppRenderDbCoordinatorList(dbCoords);
    }

    function _ppGetDbCoordinators() {
        if (!S.ppAllPoints) return [];
        const groups = {};
        S.ppAllPoints.forEach(p => {
            if (!isDbSid(p.szSid)) return;
            const g = p.szGroupName || '';
            if (!groups[g]) groups[g] = 0;
            groups[g]++;
        });
        return Object.keys(groups).filter(g => g !== '').sort();
    }

    function _ppRenderDbCoordinatorList(coords) {
        const container = document.getElementById('deviceListContainer');
        const hasUngrouped = (S.ppAllPoints || []).some(p => isDbSid(p.szSid) && !p.szGroupName);
        let html = coords.map(g => {
            const nPts = (S.ppAllPoints || []).filter(p => isDbSid(p.szSid) && p.szGroupName === g).length;
            return `<div class="pp-list-item" onclick="window._lf.ppSelectDbCoordinator('${S.escHtml(g)}')">
                <i class="fas fa-database text-info" style="font-size:14px;"></i>
                <div style="flex:1;"><div class="pp-point-name">${S.escHtml(g)}</div><div class="pp-point-sid">${S.escHtml(S.t('logicflow.pp.points_count', { count: nPts }))}</div></div>
                <i class="fas fa-chevron-right text-muted" style="font-size:11px;"></i></div>`;
        }).join('');
        if (hasUngrouped) {
            const nPts = (S.ppAllPoints || []).filter(p => isDbSid(p.szSid) && !p.szGroupName).length;
            html += `<div class="pp-list-item" onclick="window._lf.ppSelectDbCoordinator('')">
                <i class="fas fa-inbox text-secondary" style="font-size:14px;"></i>
                <div style="flex:1;"><div class="pp-point-name">${S.escHtml(S.t('logicflow.pp.ungrouped'))}</div><div class="pp-point-sid">${S.escHtml(S.t('logicflow.pp.points_count', { count: nPts }))}</div></div>
                <i class="fas fa-chevron-right text-muted" style="font-size:11px;"></i></div>`;
        }
        container.innerHTML = html;
    }

    function ppSelectDbCoordinator(szCoord) {
        S.ppPickedDevId = S.PP_DB_DEV_ID;
        S.ppPickedModbusId = null;
        S.ppPickedDbCoord = szCoord;
        S.ppPickedSid = null;
        document.getElementById('ppDeviceName').textContent = szCoord || S.t('logicflow.pp.ungrouped');
        document.getElementById('ppDeviceIcon').className = 'fas fa-database me-1';
        document.getElementById('ppStep0').style.display = 'none';
        document.getElementById('ppStep1').style.display = 'none';
        document.getElementById('ppStep2').style.display = '';
        document.getElementById('ppModalTitle').textContent = S.t('logicflow.pp.title_select_db_point');
        document.getElementById('ppPointSearch').value = '';
        document.getElementById('btnConfirmPoint').disabled = true;
        ppRenderPoints('');
    }

    function _ppShowDbPointsFlat() {
        document.getElementById('ppStep0').style.display = 'none';
        document.getElementById('ppStep1').style.display = 'none';
        document.getElementById('ppStep2').style.display = '';
        document.getElementById('ppModalTitle').textContent = S.t('logicflow.pp.title_select_db_point');
        document.getElementById('ppDeviceName').textContent = S.t('logicflow.pp.db_points');
        document.getElementById('ppDeviceIcon').className = 'fas fa-database me-1';
        document.getElementById('ppPointSearch').value = '';
        document.getElementById('btnConfirmPoint').disabled = true;
        ppRenderPoints('');
    }

    // =========== 排程來源切換 ===========
    function ppSwitchSource(mode) {
        S.ppSourceMode = mode;
        const btnP = document.getElementById('ppBtnPoint');
        const btnS = document.getElementById('ppBtnSchedule');
        if (mode === 'schedule') {
            btnP.classList.remove('active'); btnS.classList.add('active');
            document.getElementById('ppStep0').style.display = 'none';
            document.getElementById('ppStep1').style.display = 'none';
            document.getElementById('ppStep2').style.display = 'none';
            document.getElementById('ppStep3').style.display = '';
            document.getElementById('ppModalTitle').textContent = S.t('logicflow.pp.title_select_schedule');
            S.ppPickedSid = null;
            S.ppPickedScheduleId = null;
            S.ppPickedScheduleName = null;
            document.getElementById('btnConfirmPoint').disabled = true;
            ppRenderScheduleList();
        } else {
            btnS.classList.remove('active'); btnP.classList.add('active');
            document.getElementById('ppStep3').style.display = 'none';
            S.ppPickedScheduleId = null;
            S.ppPickedScheduleName = null;
            ppShowStep1();
        }
        ppUpdateHistVisibility();
    }

    async function ppEnsureSchedules() {
        if (S.ppAllSchedules) return;
        const r = await fetch('/api/schedules');
        if (!r.ok) throw new Error(S.t('logicflow.error.schedule_load_failed'));
        S.ppAllSchedules = await r.json();
    }

    async function ppRenderScheduleList() {
        const container = document.getElementById('scheduleListContainer');
        try {
            await ppEnsureSchedules();
        } catch (e) { container.innerHTML = '<div class="text-center text-muted py-3">' + S.escHtml(e.message) + '</div>'; return; }

        const enabled = S.ppAllSchedules.filter(s => s.isEnabled);
        if (enabled.length === 0) {
            container.innerHTML = '<div class="text-center text-muted py-4"><i class="fas fa-calendar-times fa-2x mb-2 d-block"></i>' + S.escHtml(S.t('logicflow.pp.empty.no_schedules')) + '</div>';
            return;
        }
        const TYPE_LABELS = [S.t('logicflow.pp.schedule.weekly'), S.t('logicflow.pp.schedule.n_weekly'), S.t('logicflow.pp.schedule.monthly'), S.t('logicflow.pp.schedule.n_monthly')];
        container.innerHTML = enabled.map(s => {
            const typeLabel = TYPE_LABELS[s.nRecurrenceType] || '';
            const time = S.escHtml(s.szStartTime) + ' - ' + S.escHtml(s.szEndTime);
            return `<div class="pp-list-item" data-schedule-id="${s.nId}" onclick="window._lf.ppSelectSchedule(${s.nId},'${S.escHtml(s.szName)}',this)">
                <i class="fas fa-calendar-alt text-primary" style="font-size:14px;"></i>
                <div style="flex:1;"><div class="pp-point-name">${S.escHtml(s.szName)}</div><div class="pp-point-sid">${S.escHtml(typeLabel)} ・ ${time}</div></div></div>`;
        }).join('');
    }

    function ppSelectSchedule(nId, szName, el) {
        document.querySelectorAll('#scheduleListContainer .pp-list-item').forEach(i => i.classList.remove('selected'));
        el.classList.add('selected');
        S.ppPickedScheduleId = nId;
        S.ppPickedScheduleName = szName;
        document.getElementById('btnConfirmPoint').disabled = false;
    }

    function ppSelectDev(nDevId, szLabel, nModbusId) {
        S.ppPickedDevId = nDevId;
        S.ppPickedModbusId = nModbusId != null ? nModbusId : null;
        S.ppPickedSid = null;
        document.getElementById('btnConfirmPoint').disabled = true;
        document.getElementById('ppDeviceName').textContent = szLabel;
        document.getElementById('ppDeviceIcon').className = 'fas fa-server me-1';
        document.getElementById('ppModalTitle').textContent = S.t('logicflow.pp.title_select_point');
        document.getElementById('ppStep0').style.display = 'none';
        document.getElementById('ppStep1').style.display = 'none';
        document.getElementById('ppStep2').style.display = '';
        document.getElementById('ppPointSearch').value = '';
        ppRenderPoints('');
    }

    function ppGoBack() {
        if (S.ppPickedDevId === S.PP_CALC_DEV_ID) {
            if (S.ppPickedCalcGroup != null) {
                S.ppPickedCalcGroup = null;
                ppShowCalcStep();
            } else {
                ppShowStep0();
            }
        } else if (S.ppPickedDevId === S.PP_DB_DEV_ID) {
            if (S.ppPickedDbCoord != null) {
                S.ppPickedDbCoord = null;
                ppShowDbStep();
            } else {
                ppShowStep0();
            }
        } else {
            ppShowDeviceStep();
        }
    }

    function ppFilter(val) {
        S.ppPickedSid = null;
        document.getElementById('btnConfirmPoint').disabled = true;
        ppRenderPoints(val);
    }

    function ppRenderPoints(keyword) {
        const q = keyword.trim().toLowerCase();
        const filtered = (S.ppAllPoints || []).filter(p => {
            if (S.ppPickedDevId === S.PP_CALC_DEV_ID && S.ppPickedCalcGroup != null) {
                if (!isCalcSid(p.szSid)) return false;
                if ((p.szGroupName || '') !== S.ppPickedCalcGroup) return false;
            } else if (S.ppPickedDevId === S.PP_DB_DEV_ID) {
                // DB 點位 Coordinator 篩選（ppPickedDbCoord 為 null 時顯示全部 DB）
                if (!isDbSid(p.szSid)) return false;
                if (S.ppPickedDbCoord != null && (p.szGroupName || '') !== S.ppPickedDbCoord) return false;
            } else if (S.ppPickedModbusId != null) {
                const pfx = getSidPrefix(p.szSid);
                const base = S.ppPickedDevId * 65536 + S.ppPickedModbusId * 256;
                if (pfx < base || pfx >= base + 256) return false;
            } else {
                if (!isPointOfDev(p.szSid, S.ppPickedDevId)) return false;
            }
            return !q || p.szName.toLowerCase().includes(q);
        });
        const container = document.getElementById('pointListContainer');
        if (filtered.length === 0) {
            container.innerHTML = '<div class="text-center text-muted py-4"><i class="fas fa-inbox fa-2x mb-2 d-block"></i>' + S.escHtml(S.t('logicflow.pp.empty.no_matching')) + '</div>';
            return;
        }
        container.innerHTML = filtered.map(p => {
            const prefix = p._devLabel ? `<span class="text-info" style="font-size:11px;">${S.escHtml(p._devLabel)}</span><span class="text-muted mx-1">/</span>` : '';
            return `<div class="pp-list-item" data-sid="${S.escHtml(p.szSid)}" onclick="window._lf.ppSelectPoint(this,'${S.escHtml(p.szSid)}')">
                <i class="fas fa-circle text-success" style="font-size:6px;"></i>
                <div style="flex:1;"><div class="pp-point-name">${prefix}${S.escHtml(p.szName)}</div></div>
                <span class="pp-point-unit">${S.escHtml(p.szUnit || '')}</span></div>`;
        }).join('');
    }

    function ppSelectPoint(el, szSid) {
        document.querySelectorAll('#pointListContainer .pp-list-item').forEach(i => i.classList.remove('selected'));
        el.classList.add('selected');
        S.ppPickedSid = szSid;
        document.getElementById('btnConfirmPoint').disabled = false;
    }

    function ppConfirm() {
        // ── 排程模式 ──
        if (S.ppSourceMode === 'schedule') {
            if (S.ppPickedScheduleId == null) return;
            if (S.ppModal) S.ppModal.hide();
            if (S.ppEditNodeId != null) {
                const existing = S.canvasNodes.find(n => n.id === S.ppEditNodeId);
                if (existing) {
                    // 設定排程時自動移除 ctrl 邊線（互斥）
                    S.canvasEdges = S.canvasEdges.filter(e => !(e.target === existing.id && e.targetPort === 'ctrl'));
                    existing.scheduleId = S.ppPickedScheduleId;
                    existing.scheduleName = S.ppPickedScheduleName;
                    delete existing.sid;
                    delete existing.pointName;
                    delete existing.unit;
                    // 排程模式無點位可讀 → 一併移除歷史值讀取設定
                    delete existing.histEnabled;
                    delete existing.histOffsetMinutes;
                }
                S.ppEditNodeId = null;
            } else {
                S.canvasNodes.push({
                    id: S.nextNodeId++,
                    type: S.ppPendingType,
                    x: S.ppPendingPos.x,
                    y: S.ppPendingPos.y,
                    scheduleId: S.ppPickedScheduleId,
                    scheduleName: S.ppPickedScheduleName
                });
            }
            S.renderCanvasNodes();
            return;
        }
        // ── 點位模式（原有邏輯） ──
        if (!S.ppPickedSid || !S.ppAllPoints) return;
        const point = S.ppAllPoints.find(p => p.szSid === S.ppPickedSid);
        if (!point) return;

        // 歷史值讀取設定：先驗證再關 Modal（不勾 = 不寫入新欄位，序列化 JSON 與現在完全相同）
        let isHistEnabled = false, nHistOffset = null;
        if (ppIsHistApplicable()) {
            isHistEnabled = document.getElementById('ppHistEnabled').checked;
            if (isHistEnabled) {
                nHistOffset = parseInt(document.getElementById('ppHistOffset').value, 10);
                if (isNaN(nHistOffset) || nHistOffset < 1 || nHistOffset > 43200) {
                    alert(S.t('logicflow.pp.hist.invalid'));
                    return;
                }
            }
        }
        if (S.ppModal) S.ppModal.hide();

        const fullName = point._devLabel ? point._devLabel + ' / ' + point.szName : point.szName;

        if (S.ppEditNodeId != null) {
            const existing = S.canvasNodes.find(n => n.id === S.ppEditNodeId);
            if (existing) {
                // 設定點位時自動移除 ctrl 邊線（互斥）
                S.canvasEdges = S.canvasEdges.filter(e => !(e.target === existing.id && e.targetPort === 'ctrl'));
                existing.sid = point.szSid;
                existing.pointName = fullName;
                existing.unit = point.szUnit || '';
                delete existing.scheduleId;
                delete existing.scheduleName;
                if (existing.type === 'output') {
                    existing.fMin = point.fMin ?? 0;
                    existing.fMax = point.fMax ?? 100;
                }
                if (isHistEnabled) {
                    existing.histEnabled = true;
                    existing.histOffsetMinutes = nHistOffset;
                } else {
                    delete existing.histEnabled;
                    delete existing.histOffsetMinutes;
                }
            }
            S.ppEditNodeId = null;
        } else {
            const node = {
                id: S.nextNodeId++,
                type: S.ppPendingType,
                x: S.ppPendingPos.x,
                y: S.ppPendingPos.y,
                sid: point.szSid,
                pointName: fullName,
                unit: point.szUnit || ''
            };
            if (S.ppPendingType === 'output') {
                node.fMin = point.fMin ?? 0;
                node.fMax = point.fMax ?? 100;
            }
            if (isHistEnabled) {
                node.histEnabled = true;
                node.histOffsetMinutes = nHistOffset;
            }
            S.canvasNodes.push(node);
        }
        S.renderCanvasNodes();
    }

    // 暴露給其他模組
    S.openPointPicker = openPointPicker;
    S.ppEnsureSchedules = ppEnsureSchedules;
    S.ppShowDeviceStep = ppShowDeviceStep;
    S.ppShowCalcStep = ppShowCalcStep;
    S.ppShowDbStep = ppShowDbStep;
    S.ppBackToStep0 = ppBackToStep0;
    S.ppSelectCalcGroup = ppSelectCalcGroup;
    S.ppSelectDbCoordinator = ppSelectDbCoordinator;
    S.ppSwitchSource = ppSwitchSource;
    S.ppSelectSchedule = ppSelectSchedule;
    S.ppSelectDev = ppSelectDev;
    S.ppGoBack = ppGoBack;
    S.ppFilter = ppFilter;
    S.ppSelectPoint = ppSelectPoint;
    S.ppConfirm = ppConfirm;
    S.ppHistToggle = ppHistToggle;
})();
