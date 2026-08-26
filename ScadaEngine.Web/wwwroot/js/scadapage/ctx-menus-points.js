// ============================================================
// scadapage/ctx-menus-points.js — 右鍵選單共用 helper + 趨勢 / AO / DO 選單與控制寫入
// ============================================================
// 純搬移自 scadapage.js（plan 2026-08-06-scadapage-js-split）。
// 不包 IIFE、global scope，與 js/designer/ 拆分同模式；
// 共享狀態集中於 state.js。原始 4-space 縮排刻意保留，
// 以保證 template literal 內容 byte-level 不變（純搬移原則）。
// ============================================================

    // ── 統一關閉所有右鍵選單 ──
    function _removeAllContextMenus() {
        _removeAoContextMenu();
        _removeDoContextMenu();
        _removePumpContextMenu();
        if (typeof _removeMotorContextMenu === 'function') _removeMotorContextMenu();
        _removeTrendContextMenu();
    }

    // ── 右鍵選單智慧定位 ──
    function _positionContextMenu(menu, clientX, clientY) {
        menu.style.left = clientX + 'px';
        menu.style.top  = clientY + 'px';
        requestAnimationFrame(function () {
            var rect = menu.getBoundingClientRect();
            var vw = window.innerWidth;
            var vh = window.innerHeight;
            if (rect.right > vw)  menu.style.left = Math.max(0, clientX - rect.width) + 'px';
            if (rect.bottom > vh) menu.style.top  = Math.max(0, clientY - rect.height) + 'px';
        });
    }

    // ── 從 lastData 快取查找點位資訊 ──
    function _findPointInfo(szSid) {
        if (!szSid || !lastData.length) return { sid: szSid, name: szSid, unit: '' };
        var found = lastData.find(function (d) { return d.sid === szSid; });
        return found
            ? { sid: szSid, name: found.name || szSid, unit: found.unit || '' }
            : { sid: szSid, name: szSid, unit: '' };
    }

    // ── 加入趨勢圖待查詢清單 ──
    function _addToTrendQueue(arrSids) {
        if (!arrSids || arrSids.length === 0) return;
        var existing = [];
        try {
            var raw = localStorage.getItem('SCADA_TREND_PRELOAD');
            if (raw) existing = JSON.parse(raw).sids || [];
        } catch (_) {}
        var added = 0;
        arrSids.forEach(function (s) {
            if (!existing.some(function (e) { return e.sid === s.sid; })) { existing.push(s); added++; }
        });
        if (added === 0) { showControlToast('\u9078\u53d6\u7684\u9ede\u4f4d\u5df2\u5728\u8da8\u52e2\u5716\u6e05\u55ae\u4e2d'); return; }
        localStorage.setItem('SCADA_TREND_PRELOAD', JSON.stringify({ sids: existing }));
        var names = arrSids.map(function (s) { return s.name; }).join(', ');
        showControlToast('\u5df2\u52a0\u5165\u8da8\u52e2\u5716\u6e05\u55ae\uff1a' + names);
    }

    // ── 趨勢圖右鍵選單 ──
    var _trendContextMenu = null;

    function _removeTrendContextMenu() {
        if (_trendContextMenu) { _trendContextMenu.remove(); _trendContextMenu = null; }
    }

    function onTrendContextMenu(e, szSid) {
        e.preventDefault();
        _removeAllContextMenus();
        if (!szSid) return;
        var info = _findPointInfo(szSid);

        var menu = document.createElement('div');
        menu.style.cssText = 'position:fixed;z-index:99999;' +
            'background:#fff;border:1px solid #dee2e6;border-radius:6px;box-shadow:0 4px 12px rgba(0,0,0,.15);' +
            'min-width:140px;padding:4px 0;font-size:13px;';

        var row = document.createElement('div');
        row.style.cssText = 'display:flex;align-items:center;gap:8px;padding:6px 10px;margin:2px 4px;cursor:pointer;' +
            'transition:background .1s;';
        row.innerHTML = '<i class="fas fa-chart-line" style="color:#0d6efd;width:16px;text-align:center;font-size:13px;"></i>' +
                         '<span>\u8da8\u52e2\u5716</span>';
        row.addEventListener('mouseenter', function () { row.style.background = '#f0f0f0'; });
        row.addEventListener('mouseleave', function () { row.style.background = ''; });
        row.addEventListener('click', function () { _removeTrendContextMenu(); _addToTrendQueue([info]); });
        menu.appendChild(row);

        document.body.appendChild(menu);
        _positionContextMenu(menu, e.clientX, e.clientY);
        _trendContextMenu = menu;
        var closeHandler = function (ev) {
            if (!menu.contains(ev.target)) { _removeTrendContextMenu(); document.removeEventListener('click', closeHandler); }
        };
        setTimeout(function () { document.addEventListener('click', closeHandler); }, 0);
    }

    // ── AO 點位右鍵選單 ──
    var _aoContextMenu = null;

    function _removeAoContextMenu() {
        if (_aoContextMenu) { _aoContextMenu.remove(); _aoContextMenu = null; }
    }

    function onAoPointContextMenu(e, el) {
        e.preventDefault();
        _removeAllContextMenus();

        var szCid   = el.dataset.cid || '';
        var szTitle = el.dataset.szDisplayName || el.dataset.szTitle || 'AO \u9ede\u4f4d';
        if (!szCid) { alert('\u6b64 AO \u9ede\u4f4d\u5c1a\u672a\u7d81\u5b9a CID'); return; }

        var szMenuManual = (el.dataset.szMenuManualLabel || '').trim();
        var szMenuAuto   = (el.dataset.szMenuAutoLabel   || '').trim();

        if (!szMenuManual && !szMenuAuto) return;

        var szSid     = szCid;
        var cached    = _aoManualValueMap[szSid];
        var szLastVal = (cached && !cached.isAuto && cached.value != null) ? String(cached.value) : '';
        var isAuto    = cached?.isAuto || false;

        var menu = document.createElement('div');
        menu.style.cssText = 'position:fixed;z-index:99999;' +
            'background:#fff;border:1px solid #dee2e6;border-radius:6px;box-shadow:0 4px 12px rgba(0,0,0,.15);' +
            'min-width:200px;padding:4px 0;font-size:13px;';

        if (szMenuManual) {
            let manualRow = document.createElement('div');
            manualRow.style.cssText = 'display:flex;align-items:center;gap:8px;padding:6px 10px;margin:2px 4px;';
            manualRow.innerHTML = '<i class="fas fa-pen-square" style="color:#0d6efd;width:16px;text-align:center;font-size:13px;"></i>' +
                             '<span style="white-space:nowrap;">' + escViewHtml(szMenuManual) + '</span>' +
                             '<input type="number" class="ao-ctx-manual-input"' +
                                    ' value="' + szLastVal + '"' +
                                    ' style="width:70px;padding:2px 5px;border:1px solid #adb5bd;border-radius:4px;' +
                                           'font-size:12px;text-align:center;background:#fff;color:#212529;"' +
                                    ' step="' + (el.dataset.fStep || 1) + '"' +
                                    ' min="' + (el.dataset.fMin || 0) + '" max="' + (el.dataset.fMax || 100) + '"' +
                                    ' placeholder="\u8a2d\u5b9a\u503c">' +
                             '<button class="ao-ctx-manual-btn"' +
                                     ' style="padding:2px 8px;border:none;border-radius:4px;background:#0d6efd;color:#fff;' +
                                            'font-size:11px;font-weight:600;cursor:pointer;white-space:nowrap;">' +
                                 '\u78ba\u5b9a' +
                             '</button>';
            if (cached && !cached.isAuto) _applyActiveStyle(manualRow);
            manualRow.addEventListener('click', function (ev) { ev.stopPropagation(); });
            manualRow.querySelector('.ao-ctx-manual-btn').addEventListener('click', function () {
                var fVal = parseFloat(manualRow.querySelector('.ao-ctx-manual-input').value);
                var fMin = parseFloat(el.dataset.fMin || 0);
                var fMax = parseFloat(el.dataset.fMax || 100);
                if (isNaN(fVal)) { alert('\u8acb\u8f38\u5165\u6709\u6548\u6578\u503c'); return; }
                if (fVal < fMin || fVal > fMax) { alert('\u8f38\u5165\u503c ' + fVal + ' \u8d85\u51fa\u7bc4\u570d\uff0c\u5141\u8a31\u7bc4\u570d\uff1a' + fMin + ' ~ ' + fMax); return; }
                _removeAoContextMenu();
                _aoPointManualWrite(el, fVal);
            });
            menu.appendChild(manualRow);
        }

        if (szMenuAuto) {
            let autoRow = document.createElement('div');
            autoRow.style.cssText = 'display:flex;align-items:center;gap:8px;padding:6px 10px;margin:2px 4px;cursor:pointer;' +
                'transition:background .1s;';
            autoRow.innerHTML = '<i class="fas fa-sync-alt" style="color:#6c757d;width:16px;text-align:center;font-size:13px;"></i>' +
                             '<span>' + escViewHtml(szMenuAuto) + '</span>';
            if (cached && cached.isAuto) _applyActiveStyle(autoRow);
            autoRow.addEventListener('mouseenter', function () { autoRow.style.background = '#f0f0f0'; });
            autoRow.addEventListener('mouseleave', function () { autoRow.style.background = ''; });
            autoRow.addEventListener('click', function () { _removeAoContextMenu(); _aoPointAutoWrite(el); });
            menu.appendChild(autoRow);
        }

        document.body.appendChild(menu);
        _positionContextMenu(menu, e.clientX, e.clientY);
        _aoContextMenu = menu;

        var closeHandler = function (ev) {
            if (!menu.contains(ev.target)) { _removeAoContextMenu(); document.removeEventListener('click', closeHandler); }
        };
        setTimeout(function () { document.addEventListener('click', closeHandler); }, 0);
    }

    // ── AO 手動控制寫入 ──
    async function _aoPointManualWrite(el, fValue) {
        var szCid   = el.dataset.cid || '';
        var szTitle = el.dataset.szDisplayName || el.dataset.szTitle || 'AO \u9ede\u4f4d';

        if (isNaN(fValue)) { alert('\u8acb\u8f38\u5165\u6709\u6548\u7684\u6578\u503c'); return; }

        try {
            var resp = await fetch('/api/control/write', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ cid: szCid, value: fValue, mode: 'manual', actionType: 'ao_manual', displayName: szTitle })
            });
            var result = await resp.json();
            if (result.success) {
                _aoManualValueMap[szCid] = { value: fValue, isAuto: false };
                _toggleModeBadge(szCid, false);
                showControlToast('\u5df2\u9001\u51fa AO \u624b\u52d5\u63a7\u5236\uff1a' + szTitle + ' = ' + fValue);
            } else {
                alert('\u5beb\u5165\u5931\u6557\uff1a' + (result.error || '\u672a\u77e5\u932f\u8aa4'));
            }
        } catch (err) {
            alert('\u5beb\u5165\u8acb\u6c42\u5931\u6557\uff1a' + err.message);
        }
    }

    // ── AO 自動控制 ──
    async function _aoPointAutoWrite(el) {
        var szCid   = el.dataset.cid || '';
        var szTitle = el.dataset.szDisplayName || el.dataset.szTitle || 'AO \u9ede\u4f4d';

        if (!confirm('\u78ba\u5b9a\u8981\u5c07\u300c' + szTitle + '\u300d\u5207\u63db\u70ba\u81ea\u52d5\u63a7\u5236\uff1f')) return;

        try {
            var resp = await fetch('/api/control/write', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ cid: szCid, mode: 'auto', actionType: 'ao_auto', displayName: szTitle })
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

    // ── DO 點位右鍵選單 ──
    var _doContextMenu = null;

    function _removeDoContextMenu() {
        if (_doContextMenu) { _doContextMenu.remove(); _doContextMenu = null; }
    }

    function onDoPointContextMenu(e, el) {
        e.preventDefault();
        _removeAllContextMenus();

        var szCid   = el.dataset.cid || '';
        var szTitle = el.dataset.szDisplayName || el.dataset.szTitle || 'DO \u9ede\u4f4d';
        if (!szCid) { alert('\u6b64 DO \u9ede\u4f4d\u5c1a\u672a\u7d81\u5b9a CID'); return; }

        var szMenuOn   = (el.dataset.szMenuOnLabel   || '').trim();
        var szMenuOff  = (el.dataset.szMenuOffLabel  || '').trim();
        var szMenuAuto = (el.dataset.szMenuAutoLabel || '').trim();

        var nOnVal  = parseFloat(el.dataset.nOnValue  || 1);
        var nOffVal = parseFloat(el.dataset.nOffValue || 0);
        var cached  = _aoManualValueMap[szCid];

        var allItems = [
            { label: szMenuOn,   icon: 'fas fa-toggle-on',  color: '#28a745', action: function () { _doPointWrite(el, true); },
              isActive: cached && !cached.isAuto && cached.value === nOnVal },
            { label: szMenuOff,  icon: 'fas fa-toggle-off', color: '#dc3545', action: function () { _doPointWrite(el, false); },
              isActive: cached && !cached.isAuto && cached.value === nOffVal },
            { label: szMenuAuto, icon: 'fas fa-sync-alt',   color: '#6c757d', action: function () { _doPointAuto(el); },
              isActive: cached && cached.isAuto }
        ];
        var items = allItems.filter(function (i) { return i.label; });
        if (items.length === 0) return;

        var menu = document.createElement('div');
        menu.style.cssText = 'position:fixed;z-index:99999;' +
            'background:#fff;border:1px solid #dee2e6;border-radius:6px;box-shadow:0 4px 12px rgba(0,0,0,.15);' +
            'min-width:140px;padding:4px 0;font-size:13px;';

        items.forEach(function (item) {
            var row = document.createElement('div');
            row.style.cssText = 'display:flex;align-items:center;gap:8px;padding:6px 10px;margin:2px 4px;cursor:pointer;' +
                'transition:background .1s;';
            row.innerHTML = '<i class="' + item.icon + '" style="color:' + item.color + ';width:16px;text-align:center;font-size:13px;"></i>' +
                             '<span>' + escViewHtml(item.label) + '</span>';
            if (item.isActive) _applyActiveStyle(row);
            row.addEventListener('mouseenter', function () { if (!item.isActive) row.style.background = '#f0f0f0'; });
            row.addEventListener('mouseleave', function () { if (!item.isActive) row.style.background = ''; });
            row.addEventListener('click', function () { _removeDoContextMenu(); item.action(); });
            menu.appendChild(row);
        });

        document.body.appendChild(menu);
        _positionContextMenu(menu, e.clientX, e.clientY);
        _doContextMenu = menu;

        var closeHandler = function (ev) {
            if (!menu.contains(ev.target)) { _removeDoContextMenu(); document.removeEventListener('click', closeHandler); }
        };
        setTimeout(function () { document.addEventListener('click', closeHandler); }, 0);
    }

    async function _doPointWrite(el, isOn) {
        var szCid   = el.dataset.cid    || '';
        var szTitle = el.dataset.szDisplayName || el.dataset.szTitle || 'DO \u9ede\u4f4d';

        var nValue = isOn
            ? parseFloat(el.dataset.nOnValue  || 1)
            : parseFloat(el.dataset.nOffValue || 0);
        var szLabel = isOn
            ? (el.dataset.szMenuOnLabel || '\u624b\u52d5ON')
            : (el.dataset.szMenuOffLabel || '\u624b\u52d5OFF');

        if (!confirm('\u78ba\u5b9a\u8981\u57f7\u884c\u300c' + szTitle + '\u300d\u2192 ' + szLabel + '\uff08\u503c ' + nValue + '\uff09\uff1f')) return;

        try {
            var resp = await fetch('/api/control/write', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ cid: szCid, value: nValue, actionType: 'do_set', displayName: szTitle })
            });
            var result = await resp.json();
            if (result.success) {
                _aoManualValueMap[szCid] = { value: nValue, isAuto: false };
                _toggleModeBadge(szCid, false);
                showControlToast('\u5df2\u9001\u51fa DO \u5beb\u5165\uff1a' + szTitle + ' \u2192 ' + szLabel + '\uff08\u503c ' + nValue + '\uff09');
            } else {
                alert('\u5beb\u5165\u5931\u6557\uff1a' + (result.error || '\u672a\u77e5\u932f\u8aa4'));
            }
        } catch (err) {
            alert('\u5beb\u5165\u8acb\u6c42\u5931\u6557\uff1a' + err.message);
        }
    }

    async function _doPointAuto(el) {
        var szCid   = el.dataset.cid || '';
        var szTitle = el.dataset.szDisplayName || el.dataset.szTitle || 'DO \u9ede\u4f4d';

        if (!confirm('\u78ba\u5b9a\u8981\u5c07\u300c' + szTitle + '\u300d\u5207\u63db\u70ba\u81ea\u52d5\u63a7\u5236\uff1f')) return;

        try {
            var resp = await fetch('/api/control/write', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ cid: szCid, mode: 'auto', actionType: 'do_auto', displayName: szTitle })
            });
            var result = await resp.json();
            if (result.success) {
                _aoManualValueMap[szCid] = { value: 0, isAuto: true };
                _toggleModeBadge(szCid, true);
                showControlToast('\u5df2\u5207\u63db\u70ba DO \u81ea\u52d5\u63a7\u5236\uff1a' + szTitle);
            } else {
                alert('\u81ea\u52d5\u63a7\u5236\u5207\u63db\u5931\u6557\uff1a' + (result.error || '\u672a\u77e5\u932f\u8aa4'));
            }
        } catch (err) {
            alert('\u81ea\u52d5\u63a7\u5236\u8acb\u6c42\u5931\u6557\uff1a' + err.message);
        }
    }
