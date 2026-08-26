// ============================================================
// scadapage/ctx-menus-equip.js — 泵浦 / 馬達型設備右鍵選單與控制寫入
// ============================================================
// 純搬移自 scadapage.js（plan 2026-08-06-scadapage-js-split）。
// 不包 IIFE、global scope，與 js/designer/ 拆分同模式；
// 共享狀態集中於 state.js。原始 4-space 縮排刻意保留，
// 以保證 template literal 內容 byte-level 不變（純搬移原則）。
// ============================================================

    // ── 水泵右鍵控制選單 ──
    var _pumpContextMenu = null;
    function _removePumpContextMenu() {
        if (_pumpContextMenu) { _pumpContextMenu.remove(); _pumpContextMenu = null; }
    }

    function onPumpContextMenu(e, el) {
        e.preventDefault();
        _removeAllContextMenus();

        var szCidSS   = el.dataset.cidStartStop || '';
        var szCidFQ   = el.dataset.cidFreqSet   || '';
        var szTitle   = el.dataset.szTitle       || '\u6c34\u6cf5';

        var menu = document.createElement('div');
        menu.style.cssText = 'position:fixed;z-index:99999;' +
            'background:#fff;border:1px solid #dee2e6;border-radius:6px;box-shadow:0 4px 12px rgba(0,0,0,.15);' +
            'min-width:160px;padding:4px 0;font-size:13px;';

        var cachedSS = szCidSS ? _aoManualValueMap[szCidSS] : null;
        var cachedFQ = szCidFQ ? _aoManualValueMap[szCidFQ] : null;
        var hasControl = _canControlPage(scadaCurrentId);

        if (hasControl && szCidSS) {
            var parentRow = document.createElement('div');
            parentRow.style.cssText = 'position:relative;display:flex;align-items:center;gap:8px;padding:6px 10px;margin:2px 4px;cursor:pointer;' +
                'transition:background .1s;';
            parentRow.innerHTML = '<i class="fas fa-power-off" style="color:#17a2b8;width:16px;text-align:center;font-size:13px;"></i>' +
                                   '<span>\u555f\u52d5\u505c\u6b62</span>' +
                                   '<i class="fas fa-chevron-right" style="margin-left:auto;font-size:10px;color:#adb5bd;"></i>';

            var subMenu = document.createElement('div');
            subMenu.style.cssText = 'position:absolute;left:100%;top:-1px;background:#fff;border:1px solid #dee2e6;border-radius:6px;' +
                'box-shadow:0 4px 12px rgba(0,0,0,.15);min-width:130px;padding:4px 0;font-size:13px;display:none;';

            [{ label: '\u555f\u52d5',     icon: 'fas fa-play',     color: '#28a745', action: function () { _pumpStartStop(szCidSS, szTitle, 1); },
               isActive: cachedSS && !cachedSS.isAuto && cachedSS.value === 1 },
             { label: '\u505c\u6b62',     icon: 'fas fa-stop',     color: '#dc3545', action: function () { _pumpStartStop(szCidSS, szTitle, 0); },
               isActive: cachedSS && !cachedSS.isAuto && cachedSS.value === 0 },
             { label: '\u81ea\u52d5\u63a7\u5236', icon: 'fas fa-sync-alt',  color: '#6c757d', action: function () { _pumpAutoControl(szCidSS, szTitle); },
               isActive: cachedSS && cachedSS.isAuto }
            ].forEach(function (item) {
                var row = document.createElement('div');
                row.style.cssText = 'display:flex;align-items:center;gap:8px;padding:6px 10px;margin:2px 4px;cursor:pointer;transition:background .1s;';
                row.innerHTML = '<i class="' + item.icon + '" style="color:' + item.color + ';width:16px;text-align:center;font-size:13px;"></i>' +
                                 '<span>' + escViewHtml(item.label) + '</span>';
                if (item.isActive) _applyActiveStyle(row);
                row.addEventListener('mouseenter', function () { if (!item.isActive) row.style.background = '#f0f0f0'; });
                row.addEventListener('mouseleave', function () { if (!item.isActive) row.style.background = ''; });
                row.addEventListener('click', function () { _removePumpContextMenu(); item.action(); });
                subMenu.appendChild(row);
            });

            parentRow.appendChild(subMenu);
            parentRow.addEventListener('mouseenter', function () { parentRow.style.background = '#f0f0f0'; subMenu.style.display = 'block'; });
            parentRow.addEventListener('mouseleave', function () { parentRow.style.background = ''; subMenu.style.display = 'none'; });
            menu.appendChild(parentRow);
        }

        if (hasControl && szCidFQ) {
            var nFqMin = parseFloat(el.dataset.nFreqSetMin) || 0;
            var nFqMax = parseFloat(el.dataset.nFreqSetMax) || 60;
            var szLastFreq = (cachedFQ && !cachedFQ.isAuto && cachedFQ.value != null) ? String(cachedFQ.value) : '';
            var fqParentRow = document.createElement('div');
            fqParentRow.style.cssText = 'position:relative;display:flex;align-items:center;gap:8px;padding:6px 10px;margin:2px 4px;cursor:pointer;' +
                'transition:background .1s;';
            fqParentRow.innerHTML = '<i class="fas fa-tachometer-alt" style="color:#17a2b8;width:16px;text-align:center;font-size:13px;"></i>' +
                                     '<span style="white-space:nowrap;">頻率設定</span>' +
                                     '<i class="fas fa-chevron-right" style="margin-left:auto;font-size:10px;color:#adb5bd;"></i>';

            var fqSubMenu = document.createElement('div');
            fqSubMenu.style.cssText = 'position:absolute;left:100%;top:-1px;background:#fff;border:1px solid #dee2e6;border-radius:6px;' +
                'box-shadow:0 4px 12px rgba(0,0,0,.15);min-width:200px;padding:4px 0;font-size:13px;display:none;';

            var fqInputRow = document.createElement('div');
            fqInputRow.style.cssText = 'display:flex;align-items:center;gap:8px;padding:6px 10px;margin:2px 4px;';
            fqInputRow.innerHTML = '<i class="fas fa-sliders-h" style="color:#17a2b8;width:16px;text-align:center;font-size:13px;"></i>' +
                                    '<input type="number" class="pump-freq-input"' +
                                           ' value="' + szLastFreq + '"' +
                                           ' style="width:70px;padding:2px 5px;border:1px solid #adb5bd;border-radius:4px;' +
                                                  'font-size:12px;text-align:center;background:#fff;color:#212529;"' +
                                           ' step="0.1" min="' + nFqMin + '" max="' + nFqMax + '"' +
                                           ' placeholder="Hz">' +
                                    '<button class="pump-freq-btn"' +
                                            ' style="padding:2px 8px;border:none;border-radius:4px;background:#17a2b8;color:#fff;' +
                                                   'font-size:11px;font-weight:600;cursor:pointer;white-space:nowrap;">' +
                                        '\u78ba\u5b9a' +
                                    '</button>';
            if (cachedFQ && !cachedFQ.isAuto) _applyActiveStyle(fqInputRow);
            fqInputRow.addEventListener('click', function (ev) { ev.stopPropagation(); });
            fqInputRow.querySelector('.pump-freq-btn').addEventListener('click', function () {
                var fVal = parseFloat(fqInputRow.querySelector('.pump-freq-input').value);
                if (isNaN(fVal)) { alert('\u8acb\u8f38\u5165\u6709\u6548\u6578\u503c'); return; }
                if (fVal < nFqMin || fVal > nFqMax) { alert('\u8f38\u5165\u503c ' + fVal + ' \u8d85\u51fa\u7bc4\u570d\uff0c\u5141\u8a31\u7bc4\u570d\uff1a' + nFqMin + ' ~ ' + nFqMax); return; }
                _removePumpContextMenu();
                _pumpFreqSet(szCidFQ, szTitle, fVal, nFqMax);
            });
            fqSubMenu.appendChild(fqInputRow);

            var fqAutoRow = document.createElement('div');
            fqAutoRow.style.cssText = 'display:flex;align-items:center;gap:8px;padding:6px 10px;margin:2px 4px;cursor:pointer;transition:background .1s;';
            fqAutoRow.innerHTML = '<i class="fas fa-sync-alt" style="color:#6c757d;width:16px;text-align:center;font-size:13px;"></i>' +
                                   '<span>自動控制</span>';
            var fqIsActiveAuto = !!(cachedFQ && cachedFQ.isAuto);
            if (fqIsActiveAuto) _applyActiveStyle(fqAutoRow);
            fqAutoRow.addEventListener('mouseenter', function () { if (!fqIsActiveAuto) fqAutoRow.style.background = '#f0f0f0'; });
            fqAutoRow.addEventListener('mouseleave', function () { if (!fqIsActiveAuto) fqAutoRow.style.background = ''; });
            fqAutoRow.addEventListener('click', function () { _removePumpContextMenu(); _pumpAutoControl(szCidFQ, szTitle); });
            fqSubMenu.appendChild(fqAutoRow);

            fqParentRow.appendChild(fqSubMenu);
            fqParentRow.addEventListener('mouseenter', function () { fqParentRow.style.background = '#f0f0f0'; fqSubMenu.style.display = 'block'; });
            fqParentRow.addEventListener('mouseleave', function () { fqParentRow.style.background = ''; fqSubMenu.style.display = 'none'; });
            menu.appendChild(fqParentRow);
        }

        var monitorSids = [
            { key: 'sidRun',   label: '\u904b\u8f49', sid: el.dataset.sidRun   || '' },
            { key: 'sidFault', label: '\u6545\u969c', sid: el.dataset.sidFault || '' },
            { key: 'sidMode',  label: '\u624b\u81ea\u52d5', sid: el.dataset.sidMode  || '' },
            { key: 'sidFreq',  label: '\u983b\u7387', sid: el.dataset.sidFreq  || '' }
        ].filter(function (m) { return m.sid; });

        if (monitorSids.length > 0) {
            var divider = document.createElement('div');
            divider.style.cssText = 'height:1px;background:#dee2e6;margin:4px 8px;';
            menu.appendChild(divider);

            var sectionTitle = document.createElement('div');
            sectionTitle.style.cssText = 'padding:4px 10px 2px;font-size:11px;color:#6c757d;font-weight:600;';
            sectionTitle.textContent = '\u76e3\u63a7\u9ede\u4f4d';
            menu.appendChild(sectionTitle);

            monitorSids.forEach(function (m) {
                var info = _findPointInfo(m.sid);
                var cbRow = document.createElement('div');
                cbRow.style.cssText = 'display:flex;align-items:center;gap:6px;padding:3px 10px;margin:1px 4px;';
                cbRow.innerHTML = '<input type="checkbox" class="pump-trend-cb" data-sid="' + m.sid + '"' +
                    ' data-name="' + escViewHtml(info.name) + '" data-unit="' + escViewHtml(info.unit) + '"' +
                    ' checked style="margin:0;cursor:pointer;">' +
                    '<span style="font-size:12px;">' + m.label + '</span>';
                cbRow.addEventListener('click', function (ev) { ev.stopPropagation(); });
                menu.appendChild(cbRow);
            });

            var btnRow = document.createElement('div');
            btnRow.style.cssText = 'padding:4px 10px 6px;';
            btnRow.innerHTML = '<button class="pump-trend-btn" style="width:100%;padding:4px 0;border:none;border-radius:4px;' +
                'background:#0d6efd;color:#fff;font-size:12px;font-weight:600;cursor:pointer;">' +
                '<i class="fas fa-chart-line" style="margin-right:4px;"></i>\u8da8\u52e2\u5716</button>';
            btnRow.addEventListener('click', function (ev) {
                ev.stopPropagation();
                var cbs = menu.querySelectorAll('.pump-trend-cb:checked');
                if (cbs.length === 0) { alert('\u8acb\u81f3\u5c11\u52fe\u9078\u4e00\u500b\u76e3\u63a7\u9ede\u4f4d'); return; }
                var arr = [];
                cbs.forEach(function (cb) { arr.push({ sid: cb.dataset.sid, name: cb.dataset.name, unit: cb.dataset.unit }); });
                _removePumpContextMenu();
                _addToTrendQueue(arr);
            });
            menu.appendChild(btnRow);
        }

        document.body.appendChild(menu);
        _positionContextMenu(menu, e.clientX, e.clientY);
        _pumpContextMenu = menu;
        var closeHandler = function (ev) {
            if (!menu.contains(ev.target)) { _removePumpContextMenu(); document.removeEventListener('click', closeHandler); }
        };
        setTimeout(function () { document.addEventListener('click', closeHandler); }, 0);
    }

    async function _pumpStartStop(szCid, szTitle, nValue) {
        var szLabel = nValue === 1 ? '\u555f\u52d5' : '\u505c\u6b62';
        if (!confirm('\u78ba\u5b9a\u8981\u57f7\u884c\u300c' + szTitle + '\u300d\u2192 ' + szLabel + '\uff1f')) return;
        try {
            var resp = await fetch('/api/control/write', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ cid: szCid, value: nValue, actionType: 'pump_start_stop', displayName: szTitle })
            });
            var result = await resp.json();
            if (result.success) {
                _aoManualValueMap[szCid] = { value: nValue, isAuto: false };
                _toggleModeBadge(szCid, false);
                showControlToast('\u5df2\u9001\u51fa\u6c34\u6cf5\u63a7\u5236\uff1a' + szTitle + ' \u2192 ' + szLabel);
            } else {
                alert('\u63a7\u5236\u5931\u6557\uff1a' + (result.error || '\u672a\u77e5\u932f\u8aa4'));
            }
        } catch (err) {
            alert('\u63a7\u5236\u8acb\u6c42\u5931\u6557\uff1a' + err.message);
        }
    }

    async function _pumpFreqSet(szCid, szTitle, fValue, nFreqMax) {
        if (!confirm('\u78ba\u5b9a\u8981\u8a2d\u5b9a\u300c' + szTitle + '\u300d\u983b\u7387\u70ba ' + fValue + ' Hz\uff1f')) return;
        try {
            var resp = await fetch('/api/control/write', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ cid: szCid, value: fValue, actionType: 'pump_freq', displayName: szTitle })
            });
            var result = await resp.json();
            if (result.success) {
                _aoManualValueMap[szCid] = { value: fValue, isAuto: false };
                _toggleModeBadge(szCid, false);
                showControlToast('\u5df2\u9001\u51fa\u983b\u7387\u8a2d\u5b9a\uff1a' + szTitle + ' \u2192 ' + fValue + ' Hz');
            } else {
                alert('\u983b\u7387\u8a2d\u5b9a\u5931\u6557\uff1a' + (result.error || '\u672a\u77e5\u932f\u8aa4'));
            }
        } catch (err) {
            alert('\u983b\u7387\u8a2d\u5b9a\u8acb\u6c42\u5931\u6557\uff1a' + err.message);
        }
    }

    async function _pumpAutoControl(szCid, szTitle) {
        if (!confirm('\u78ba\u5b9a\u8981\u5c07\u300c' + szTitle + '\u300d\u5207\u63db\u70ba\u81ea\u52d5\u63a7\u5236\uff1f')) return;
        try {
            var resp = await fetch('/api/control/write', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ cid: szCid, mode: 'auto', actionType: 'pump_auto', displayName: szTitle })
            });
            var result = await resp.json();
            if (result.success) {
                _aoManualValueMap[szCid] = { value: 0, isAuto: true };
                _toggleModeBadge(szCid, true);
                showControlToast('\u5df2\u5207\u63db\u70ba\u81ea\u52d5\u63a7\u5236\uff1a' + szTitle);
            } else {
                alert('\u81ea\u52d5\u63a7\u5236\u5207\u63db\u5931\u6557\uff1a' + (result.error || '\u672a\u77e5\u932f\u8aa4'));
            }
        } catch (err) {
            alert('\u81ea\u52d5\u63a7\u5236\u8acb\u6c42\u5931\u6557\uff1a' + err.message);
        }
    }

    // ── 馬達型設備右鍵控制選單 ──
    var _motorContextMenu = null;
    function _removeMotorContextMenu() {
        if (_motorContextMenu) { _motorContextMenu.remove(); _motorContextMenu = null; }
    }

    function onMotorContextMenu(e, el) {
        e.preventDefault();
        _removeAllContextMenus();

        var szType  = el.dataset.motorType;
        var bVfd    = (szType !== 'chiller');
        var szCidSS = el.dataset.cidStartStop || '';
        var szCidFQ = el.dataset.cidFreqSet   || '';
        var szTitle = el.dataset.szTitle || (szType === 'chiller' ? '冰機' : szType === 'coolingTower' ? '冷卻水塔' : '空調箱風扇');

        var menu = document.createElement('div');
        menu.style.cssText = 'position:fixed;z-index:99999;background:#fff;border:1px solid #dee2e6;border-radius:6px;box-shadow:0 4px 12px rgba(0,0,0,.15);min-width:160px;padding:4px 0;font-size:13px;';

        var cachedSS = szCidSS ? _aoManualValueMap[szCidSS] : null;
        var cachedFQ = szCidFQ ? _aoManualValueMap[szCidFQ] : null;
        var hasControl = _canControlPage(scadaCurrentId);

        // 啟動停止（子選單）
        if (hasControl && szCidSS) {
            var parentRow = document.createElement('div');
            parentRow.style.cssText = 'position:relative;display:flex;align-items:center;gap:8px;padding:6px 10px;margin:2px 4px;cursor:pointer;transition:background .1s;';
            parentRow.innerHTML = '<i class="fas fa-power-off" style="color:#17a2b8;width:16px;text-align:center;font-size:13px;"></i><span>啟動停止</span><i class="fas fa-chevron-right" style="margin-left:auto;font-size:10px;color:#adb5bd;"></i>';
            var subMenu = document.createElement('div');
            subMenu.style.cssText = 'position:absolute;left:100%;top:-1px;background:#fff;border:1px solid #dee2e6;border-radius:6px;box-shadow:0 4px 12px rgba(0,0,0,.15);min-width:130px;padding:4px 0;font-size:13px;display:none;';
            [{ label: '啟動', icon: 'fas fa-play', color: '#28a745', action: function () { _pumpStartStop(szCidSS, szTitle, 1); },
               isActive: cachedSS && !cachedSS.isAuto && cachedSS.value === 1 },
             { label: '停止', icon: 'fas fa-stop', color: '#dc3545', action: function () { _pumpStartStop(szCidSS, szTitle, 0); },
               isActive: cachedSS && !cachedSS.isAuto && cachedSS.value === 0 },
             { label: '自動控制', icon: 'fas fa-sync-alt', color: '#6c757d', action: function () { _pumpAutoControl(szCidSS, szTitle); },
               isActive: cachedSS && cachedSS.isAuto }
            ].forEach(function (item) {
                var row = document.createElement('div');
                row.style.cssText = 'display:flex;align-items:center;gap:8px;padding:6px 10px;margin:2px 4px;cursor:pointer;transition:background .1s;';
                row.innerHTML = '<i class="' + item.icon + '" style="color:' + item.color + ';width:16px;text-align:center;font-size:13px;"></i><span>' + escViewHtml(item.label) + '</span>';
                if (item.isActive) _applyActiveStyle(row);
                row.addEventListener('mouseenter', function () { if (!item.isActive) row.style.background = '#f0f0f0'; });
                row.addEventListener('mouseleave', function () { if (!item.isActive) row.style.background = ''; });
                row.addEventListener('click', function () { _removeMotorContextMenu(); item.action(); });
                subMenu.appendChild(row);
            });
            parentRow.appendChild(subMenu);
            parentRow.addEventListener('mouseenter', function () { parentRow.style.background = '#f0f0f0'; subMenu.style.display = 'block'; });
            parentRow.addEventListener('mouseleave', function () { parentRow.style.background = ''; subMenu.style.display = 'none'; });
            menu.appendChild(parentRow);
        }

        // 頻率設定（子選單）— 僅 VFD 型（水塔/風扇）
        if (bVfd && hasControl && szCidFQ) {
            var nFqMin = parseFloat(el.dataset.nFreqSetMin) || 0;
            var nFqMax = parseFloat(el.dataset.nFreqSetMax) || 60;
            var szLastFreq = (cachedFQ && !cachedFQ.isAuto && cachedFQ.value != null) ? String(cachedFQ.value) : '';
            var fqParentRow = document.createElement('div');
            fqParentRow.style.cssText = 'position:relative;display:flex;align-items:center;gap:8px;padding:6px 10px;margin:2px 4px;cursor:pointer;transition:background .1s;';
            fqParentRow.innerHTML = '<i class="fas fa-tachometer-alt" style="color:#17a2b8;width:16px;text-align:center;font-size:13px;"></i><span style="white-space:nowrap;">頻率設定</span><i class="fas fa-chevron-right" style="margin-left:auto;font-size:10px;color:#adb5bd;"></i>';
            var fqSubMenu = document.createElement('div');
            fqSubMenu.style.cssText = 'position:absolute;left:100%;top:-1px;background:#fff;border:1px solid #dee2e6;border-radius:6px;box-shadow:0 4px 12px rgba(0,0,0,.15);min-width:200px;padding:4px 0;font-size:13px;display:none;';
            var fqInputRow = document.createElement('div');
            fqInputRow.style.cssText = 'display:flex;align-items:center;gap:8px;padding:6px 10px;margin:2px 4px;';
            fqInputRow.innerHTML = '<i class="fas fa-sliders-h" style="color:#17a2b8;width:16px;text-align:center;font-size:13px;"></i>' +
                                    '<input type="number" class="pump-freq-input" value="' + szLastFreq + '" style="width:70px;padding:2px 5px;border:1px solid #adb5bd;border-radius:4px;font-size:12px;text-align:center;background:#fff;color:#212529;" step="0.1" min="' + nFqMin + '" max="' + nFqMax + '" placeholder="Hz">' +
                                    '<button class="pump-freq-btn" style="padding:2px 8px;border:none;border-radius:4px;background:#17a2b8;color:#fff;font-size:11px;font-weight:600;cursor:pointer;white-space:nowrap;">確定</button>';
            if (cachedFQ && !cachedFQ.isAuto) _applyActiveStyle(fqInputRow);
            fqInputRow.addEventListener('click', function (ev) { ev.stopPropagation(); });
            fqInputRow.querySelector('.pump-freq-btn').addEventListener('click', function () {
                var fVal = parseFloat(fqInputRow.querySelector('.pump-freq-input').value);
                if (isNaN(fVal)) { alert('請輸入有效數值'); return; }
                if (fVal < nFqMin || fVal > nFqMax) { alert('輸入值 ' + fVal + ' 超出範圍，允許範圍：' + nFqMin + ' ~ ' + nFqMax); return; }
                _removeMotorContextMenu();
                _pumpFreqSet(szCidFQ, szTitle, fVal, nFqMax);
            });
            fqSubMenu.appendChild(fqInputRow);
            var fqAutoRow = document.createElement('div');
            fqAutoRow.style.cssText = 'display:flex;align-items:center;gap:8px;padding:6px 10px;margin:2px 4px;cursor:pointer;transition:background .1s;';
            fqAutoRow.innerHTML = '<i class="fas fa-sync-alt" style="color:#6c757d;width:16px;text-align:center;font-size:13px;"></i><span>自動控制</span>';
            var fqIsActiveAuto = !!(cachedFQ && cachedFQ.isAuto);
            if (fqIsActiveAuto) _applyActiveStyle(fqAutoRow);
            fqAutoRow.addEventListener('mouseenter', function () { if (!fqIsActiveAuto) fqAutoRow.style.background = '#f0f0f0'; });
            fqAutoRow.addEventListener('mouseleave', function () { if (!fqIsActiveAuto) fqAutoRow.style.background = ''; });
            fqAutoRow.addEventListener('click', function () { _removeMotorContextMenu(); _pumpAutoControl(szCidFQ, szTitle); });
            fqSubMenu.appendChild(fqAutoRow);
            fqParentRow.appendChild(fqSubMenu);
            fqParentRow.addEventListener('mouseenter', function () { fqParentRow.style.background = '#f0f0f0'; fqSubMenu.style.display = 'block'; });
            fqParentRow.addEventListener('mouseleave', function () { fqParentRow.style.background = ''; fqSubMenu.style.display = 'none'; });
            menu.appendChild(fqParentRow);
        }

        // 冰機設定溫度（右鍵入口，另可雙擊右下角）
        if (szType === 'chiller' && hasControl && el.dataset.cidSetTemp) {
            var stRow = document.createElement('div');
            stRow.style.cssText = 'display:flex;align-items:center;gap:8px;padding:6px 10px;margin:2px 4px;cursor:pointer;transition:background .1s;';
            stRow.innerHTML = '<i class="fas fa-temperature-low" style="color:#17a2b8;width:16px;text-align:center;font-size:13px;"></i><span>設定溫度</span>';
            stRow.addEventListener('mouseenter', function () { stRow.style.background = '#f0f0f0'; });
            stRow.addEventListener('mouseleave', function () { stRow.style.background = ''; });
            stRow.addEventListener('click', function () { _removeMotorContextMenu(); _motorPromptSetTemp(el); });
            menu.appendChild(stRow);
        }

        // 監控點位 → 趨勢圖
        var monitorSids = [
            { label: '運轉', sid: el.dataset.sidRun || '' },
            { label: '故障', sid: el.dataset.sidFault || '' },
            { label: '手自動', sid: el.dataset.sidMode || '' }
        ];
        if (szType === 'chiller') {
            monitorSids.push({ label: '負載', sid: el.dataset.sidLoad || '' });
            monitorSids.push({ label: '冰水出水溫', sid: el.dataset.sidChwOut || '' });
        } else {
            monitorSids.push({ label: '頻率', sid: el.dataset.sidFreq || '' });
            if (szType === 'coolingTower') monitorSids.push({ label: '出水溫', sid: el.dataset.sidWaterTemp || '' });
        }
        monitorSids = monitorSids.filter(function (m) { return m.sid; });

        if (monitorSids.length > 0) {
            var divider = document.createElement('div');
            divider.style.cssText = 'height:1px;background:#dee2e6;margin:4px 8px;';
            menu.appendChild(divider);
            var sectionTitle = document.createElement('div');
            sectionTitle.style.cssText = 'padding:4px 10px 2px;font-size:11px;color:#6c757d;font-weight:600;';
            sectionTitle.textContent = '監控點位';
            menu.appendChild(sectionTitle);
            monitorSids.forEach(function (m) {
                var info = _findPointInfo(m.sid);
                var cbRow = document.createElement('div');
                cbRow.style.cssText = 'display:flex;align-items:center;gap:6px;padding:3px 10px;margin:1px 4px;';
                cbRow.innerHTML = '<input type="checkbox" class="pump-trend-cb" data-sid="' + m.sid + '" data-name="' + escViewHtml(info.name) + '" data-unit="' + escViewHtml(info.unit) + '" checked style="margin:0;cursor:pointer;"><span style="font-size:12px;">' + m.label + '</span>';
                cbRow.addEventListener('click', function (ev) { ev.stopPropagation(); });
                menu.appendChild(cbRow);
            });
            var btnRow = document.createElement('div');
            btnRow.style.cssText = 'padding:4px 10px 6px;';
            btnRow.innerHTML = '<button class="pump-trend-btn" style="width:100%;padding:4px 0;border:none;border-radius:4px;background:#0d6efd;color:#fff;font-size:12px;font-weight:600;cursor:pointer;"><i class="fas fa-chart-line" style="margin-right:4px;"></i>趨勢圖</button>';
            btnRow.addEventListener('click', function (ev) {
                ev.stopPropagation();
                var cbs = menu.querySelectorAll('.pump-trend-cb:checked');
                if (cbs.length === 0) { alert('請至少勾選一個監控點位'); return; }
                var arr = [];
                cbs.forEach(function (cb) { arr.push({ sid: cb.dataset.sid, name: cb.dataset.name, unit: cb.dataset.unit }); });
                _removeMotorContextMenu();
                _addToTrendQueue(arr);
            });
            menu.appendChild(btnRow);
        }

        document.body.appendChild(menu);
        _positionContextMenu(menu, e.clientX, e.clientY);
        _motorContextMenu = menu;
        var closeHandler = function (ev) {
            if (!menu.contains(ev.target)) { _removeMotorContextMenu(); document.removeEventListener('click', closeHandler); }
        };
        setTimeout(function () { document.addEventListener('click', closeHandler); }, 0);
    }
