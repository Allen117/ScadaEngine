(function () {
    function t(key, args) { return (window.i18n && window.i18n.t) ? window.i18n.t(key, args) : key; }

    // ── 權限檢查（由 cshtml inline script 設定全域變數 _isAdmin, _scadaPagePerms）──
    function _canViewPage(szPageSid) {
        if (window._isAdmin) return true;
        var p = window._scadaPagePerms[szPageSid];
        return p && p.canView;
    }
    function _canControlPage(szPageSid) {
        if (window._isAdmin) return true;
        var p = window._scadaPagePerms[szPageSid];
        return p && p.canControl;
    }

    var scadaPageTree  = [];
    var scadaCurrentId = null;
    var lastData       = [];

    // ── 頁面樹摺疊狀態（持久化於 localStorage）──
    var COLLAPSED_KEY = 'scadaPage_collapsed_v1';
    var collapsedSet = (function () {
        try {
            var raw = localStorage.getItem(COLLAPSED_KEY);
            return new Set(raw ? JSON.parse(raw) : []);
        } catch (_) { return new Set(); }
    })();
    function _saveCollapsedSet() {
        try { localStorage.setItem(COLLAPSED_KEY, JSON.stringify(Array.from(collapsedSet))); } catch (_) {}
    }
    function _toggleCollapsed(szId) {
        if (collapsedSet.has(szId)) collapsedSet.delete(szId);
        else collapsedSet.add(szId);
        _saveCollapsedSet();
        renderScadaPageTree();
    }

    // ── 警報規則快取 ──
    var _alarmRuleMap = {};
    async function _loadAlarmRules() {
        try {
            var resp = await fetch('/api/alarm-rules');
            if (resp.ok) {
                var arr = await resp.json();
                _alarmRuleMap = {};
                arr.filter(function (r) { return r.isEnabled; }).forEach(function (r) { _alarmRuleMap[r.szSID] = r; });
            }
        } catch (_) { /* ignore */ }
    }

    // ── 手動控制值快取 ──
    var _aoManualValueMap = {};
    async function _loadManualControlValues() {
        try {
            var resp = await fetch('/api/control/manual-values');
            if (resp.ok) _aoManualValueMap = await resp.json();
        } catch (_) { /* ignore */ }
    }

    // ── 排程快取（DI 點位綁定排程用，lazy 載入）──
    // 切到包含排程型 DI 的頁面時 fetch 一次，後續即時更新迴圈直接讀此快取
    var _scheduleCache = null;
    var _scheduleCacheLoading = null;
    async function _ensureScheduleCache() {
        if (_scheduleCache) return _scheduleCache;
        if (_scheduleCacheLoading) return _scheduleCacheLoading;
        _scheduleCacheLoading = (async function () {
            try {
                var resp = await fetch('/api/schedules');
                if (resp.ok) {
                    _scheduleCache = await resp.json();
                } else {
                    _scheduleCache = [];
                }
            } catch (_) {
                _scheduleCache = [];
            } finally {
                _scheduleCacheLoading = null;
            }
            return _scheduleCache;
        })();
        return _scheduleCacheLoading;
    }
    function _pageHasScheduleDi(page) {
        if (!page || !page.arrWidgetState) return false;
        return page.arrWidgetState.some(function (ws) {
            return ws.szType === 'diPoint' && ws.props && ws.props.nScheduleId != null;
        });
    }

    // ── 初始化 ──
    document.addEventListener('DOMContentLoaded', async function () {
        await initScadaViewer();
        await _loadAlarmRules();
        await _loadManualControlValues();
        await fetchAndUpdateGauges();
        setInterval(fetchAndUpdateGauges, 1000);
    });

    // ── 從 /Designer/Load 載入已發布設計 ──
    async function initScadaViewer() {
        try {
            var resp   = await fetch('/Designer/Load');
            var result = await resp.json();

            if (!result.hasData || !result.pages || !result.pages.length) {
                document.getElementById('scadaPageTree').innerHTML =
                    '<p style="font-size:11px;color:#6c757d;text-align:center;margin-top:20px;">' + t('scadapage.tree.empty') + '</p>';
                return;
            }

            var nodeMap = {}, sortMap = {};
            result.pages.forEach(function (p) {
                sortMap[p.szPageSid] = p.nSortOrder || 0;
                nodeMap[p.szPageSid] = {
                    szId:           p.szPageSid,
                    szName:         p.szPageName,
                    szIcon:         p.szPageIcon  || null,
                    arrChildren:    [],
                    szBgDataUrl:    p.szBgDataUrl  || null,
                    szBgFileName:   p.szBgFileName || null,
                    nCanvasW:       p.nCanvasW     || 1200,
                    nCanvasH:       p.nCanvasH     || 800,
                    arrWidgetState: p.szWidgetStateJson ? JSON.parse(p.szWidgetStateJson) : []
                };
            });

            var arrRoots = [];
            result.pages.forEach(function (p) {
                var node = nodeMap[p.szPageSid];
                if (p.szParentPageSid && nodeMap[p.szParentPageSid])
                    nodeMap[p.szParentPageSid].arrChildren.push(node);
                else
                    arrRoots.push(node);
            });
            Object.values(nodeMap).forEach(function (n) {
                n.arrChildren.sort(function (a, b) { return (sortMap[a.szId] || 0) - (sortMap[b.szId] || 0); });
            });
            arrRoots.sort(function (a, b) { return (sortMap[a.szId] || 0) - (sortMap[b.szId] || 0); });

            function filterByPerm(nodes) {
                if (window._isAdmin) return nodes;
                return nodes
                    .filter(function (n) { return _canViewPage(n.szId); })
                    .map(function (n) { return Object.assign({}, n, { arrChildren: filterByPerm(n.arrChildren) }); });
            }

            scadaPageTree = filterByPerm(arrRoots);
            renderScadaPageTree();

            if (scadaPageTree.length > 0) selectScadaPage(scadaPageTree[0].szId);

        } catch (err) {
            console.warn('SCADA Viewer \u521d\u59cb\u5316\u5931\u6557\uff1a', err.message);
            document.getElementById('scadaPageTree').innerHTML =
                '<p style="font-size:16px;color:#dc3545;text-align:center;margin-top:20px;">\u8f09\u5165\u5931\u6557</p>';
        }
    }

    // ── 頁面樹渲染 ──
    function renderScadaPageTree() {
        var wrap = document.getElementById('scadaPageTree');
        wrap.innerHTML = '';
        function renderNodes(arr, depth) {
            arr.forEach(function (page) {
                var isActive    = scadaCurrentId === page.szId;
                var hasChildren = page.arrChildren && page.arrChildren.length > 0;
                var isCollapsed = collapsedSet.has(page.szId);

                var el = document.createElement('div');
                el.style.cssText =
                    'padding:6px 8px 6px ' + (depth * 10 + 6) + 'px;cursor:pointer;border-radius:4px;' +
                    'font-size:16px;margin-bottom:2px;' +
                    'border-left:3px solid ' + (isActive ? '#0d6efd' : 'transparent') + ';' +
                    'background:' + (isActive ? '#e8f0fe' : 'transparent') + ';' +
                    'color:' + (isActive ? '#0d6efd' : '#444') + ';' +
                    'display:flex;align-items:center;' +
                    'user-select:none;';
                if (hasChildren) {
                    el.title = isCollapsed
                        ? t('scadapage.tree.dblclick_expand', { 0: page.arrChildren.length })
                        : t('scadapage.tree.dblclick_collapse');
                }

                var szHtml = '';
                if (hasChildren) {
                    // 摺疊指示器：摺疊時 caret-right（提示「下面還有子畫面」），展開時 caret-down
                    // 純視覺提示，互動由父 row 的雙擊事件處理
                    var szCaretIcon  = isCollapsed ? 'fa-caret-right' : 'fa-caret-down';
                    var szCaretColor = isCollapsed ? '#0d6efd' : '#6c757d';
                    szHtml += '<i class="fas ' + szCaretIcon + '" ' +
                        'style="width:14px;text-align:center;margin-right:4px;font-size:14px;' +
                        'color:' + szCaretColor + ';' +
                        (isCollapsed ? 'font-weight:900;' : '') +
                        '"></i>';
                } else {
                    // 對齊用佔位
                    szHtml += '<span style="display:inline-block;width:14px;margin-right:4px;"></span>';
                }
                szHtml += '<i class="fas ' + (page.szIcon || 'fa-file') + ' me-1" style="font-size:14px;opacity:.65;"></i>' +
                    page.szName;
                el.innerHTML = szHtml;

                el.addEventListener('click', function () { selectScadaPage(page.szId); });
                if (hasChildren) {
                    el.addEventListener('dblclick', function (ev) {
                        ev.preventDefault();
                        _toggleCollapsed(page.szId);
                    });
                }
                wrap.appendChild(el);
                if (hasChildren && !isCollapsed) renderNodes(page.arrChildren, depth + 1);
            });
        }
        renderNodes(scadaPageTree, 0);
    }

    function findScadaPage(szId, arr) {
        arr = arr || scadaPageTree;
        for (var i = 0; i < arr.length; i++) {
            if (arr[i].szId === szId) return arr[i];
            var f = findScadaPage(szId, arr[i].arrChildren);
            if (f) return f;
        }
        return null;
    }

    function selectScadaPage(szId) {
        // 冪等防護：dblclick 會觸發兩次 click，避免重複 renderScadaCanvas 造成畫布閃動跳大小
        if (scadaCurrentId === szId) return;
        scadaCurrentId = szId;
        renderScadaPageTree();
        var page = findScadaPage(szId);
        if (!page) return;
        // 若本頁含綁排程的 DI widget 才 fetch /api/schedules（lazy；plan 決策 + 使用者回覆）
        if (_pageHasScheduleDi(page)) {
            _ensureScheduleCache().then(function () {
                renderScadaCanvas(page);
            });
        } else {
            renderScadaCanvas(page);
        }
    }

    // ── 畫布渲染 ──
    function renderScadaCanvas(page) {
        var canvas = document.getElementById('scadaCanvas');
        canvas.style.width  = (page.nCanvasW || 1200) + 'px';
        canvas.style.height = (page.nCanvasH || 800)  + 'px';

        if (page.szBgDataUrl) {
            canvas.style.backgroundImage    = 'url(' + page.szBgDataUrl + ')';
            canvas.style.backgroundSize     = 'cover';
            canvas.style.backgroundPosition = 'center';
        } else {
            canvas.style.backgroundImage = '';
            canvas.style.background      = '#ffffff';
        }

        canvas.innerHTML = '';
        (page.arrWidgetState || []).forEach(function (ws) { renderScadaWidget(canvas, ws); });

        if (lastData.length > 0) updateScadaWidgets(lastData);
        _applyCanvasScale();
    }

    // ── 畫布等比縮放 ──
    function _applyCanvasScale() {
        var wrap = document.getElementById('scadaCanvasWrap');
        var canvas = document.getElementById('scadaCanvas');
        if (!wrap || !canvas) return;
        var parent = wrap.parentElement;
        if (!parent) return;

        var nCanvasW = parseInt(canvas.style.width)  || 1200;
        var nCanvasH = parseInt(canvas.style.height) || 800;

        var nAvailW = parent.clientWidth  - 24;
        var nAvailH = parent.clientHeight - 24;

        var fScale = Math.min(nAvailW / nCanvasW, nAvailH / nCanvasH);
        if (fScale <= 0 || !isFinite(fScale)) fScale = 1;

        // 使用 transform: scale 取代 CSS zoom：zoom 在 Chromium 會放大內部系統游標，
        // F11 全螢幕時 fScale 通常 > 1，會看到游標進入畫布忽然變大、離開又恢復。
        // wrap 已用 position:absolute + top/left 50% 釘在 viewport 中央，
        // translate(-50%, -50%) 把 wrap 中心對齊 viewport 中心，scale 從 center 縮放維持置中。
        wrap.style.zoom = '';
        wrap.style.transformOrigin = 'center center';
        wrap.style.transform = 'translate(-50%, -50%) scale(' + fScale + ')';
        wrap.style.width  = nCanvasW + 'px';
        wrap.style.height = nCanvasH + 'px';
    }

    window.addEventListener('resize', _applyCanvasScale);

    function renderScadaWidget(canvas, ws) {
        var el = document.createElement('div');
        el.style.cssText =
            'position:absolute;left:' + ws.nX + 'px;top:' + ws.nY + 'px;' +
            'width:' + ws.nW + 'px;height:' + ws.nH + 'px;overflow:hidden;';
        el.dataset.type = ws.szType;

        if (ws.szType === 'gauge') {
            var p = ws.props || {};
            el.dataset.sid     = p.szSid    || '';
            el.dataset.fMin    = p.fMin     || 0;
            el.dataset.fMax    = p.fMax     || 100;
            el.dataset.szUnit  = p.szUnit   || '';
            el.dataset.szColor     = p.szColor     || '#00c0ff';
            el.dataset.szTitle     = p.szTitle     || '';
            el.dataset.szBgColor   = p.szBgColor   || 'transparent';
            el.dataset.szHighColor = p.szHighColor || '#dc3545';
            el.dataset.szLowColor  = p.szLowColor  || '#fd7e14';
            el.classList.add('scada-gauge');
            el.innerHTML = buildGaugeHtml(p);
            if (p.szSid) el.addEventListener('contextmenu', function (ev) { onTrendContextMenu(ev, el.dataset.sid); });
        } else if (ws.szType === 'table') {
            el.classList.add('scada-table');
            // 鎖定尺寸的 table 改用 props 重算 widget 外框，確保與 Designer 一致（plan 2026-06-01）
            var tProps = ws.props || {};
            if (tProps.bTableSizeLocked === true) {
                var nTC = Math.max(1, Math.min(tProps.nCols || 3, 4));
                var nTR = Math.max(1, tProps.nRows || 5);
                var nTDefW = tProps.nDefaultColW || 80;
                var nTDefH = tProps.nDefaultRowH || 20;
                var arrTCW = tProps.arrColWidths || [];
                var arrTRH = tProps.arrRowHeights || [];
                var nNetW = 0;
                for (var iC = 0; iC < nTC; iC++) nNetW += (arrTCW[iC] != null ? +arrTCW[iC] : nTDefW);
                var nNetH = nTDefH; // header row
                for (var iR = 0; iR < nTR; iR++) nNetH += (arrTRH[iR] != null ? +arrTRH[iR] : nTDefH);
                el.style.width  = nNetW + 'px';
                el.style.height = nNetH + 'px';
            }
            el.innerHTML = buildTableHtml(tProps);
            el.addEventListener('contextmenu', function (ev) {
                var td = ev.target.closest('td[data-sid]');
                if (td && td.dataset.sid) onTrendContextMenu(ev, td.dataset.sid);
            });
        } else if (ws.szType === 'text') {
            var p = ws.props || {};
            var szFontStyle = p.isItalic ? 'italic' : 'normal';
            var szBg        = p.szBgColor || 'transparent';
            el.style.overflow = 'visible';
            el.style.background = 'transparent';
            el.innerHTML = '<div style="' +
                'width:100%;height:100%;' +
                'display:flex;align-items:center;justify-content:center;' +
                'font-family:' + (p.szFontFamily || 'inherit') + ';' +
                'font-size:' + (p.nFontSize || 18) + 'px;' +
                'color:' + (p.szFontColor || '#212529') + ';' +
                'font-weight:' + (p.szFontWeight || 'normal') + ';' +
                'font-style:' + szFontStyle + ';' +
                'background:' + szBg + ';' +
                'word-break:break-word;' +
                'text-align:center;' +
                'padding:4px 8px;' +
                'box-sizing:border-box;' +
                'line-height:1.3;' +
                '">' + escViewHtml(p.szText || '') + '</div>';
        } else if (ws.szType === 'controlBtn') {
            var p = ws.props || {};
            el.dataset.cid        = p.szCid       || '';
            el.dataset.szTitle    = p.szTitle      || '\u63a7\u5236';
            el.dataset.szBtnLabel = p.szBtnLabel   || '\u57f7\u884c';
            el.dataset.szBtnIcon  = p.szBtnIcon    || 'fa-hand-pointer';
            el.dataset.fCtrlValue = p.fCtrlValue   || 1;
            el.dataset.szBtnColor = p.szBtnColor   || '#198754';
            el.classList.add('scada-ctrl-btn');
            el.style.overflow = 'visible';
            el.innerHTML = buildCtrlBtnHtml(p);
            if (_canControlPage(scadaCurrentId)) {
                el.querySelector('.ctrl-btn-exec')?.addEventListener('click', function () { onControlBtnClick(el); });
            }
        } else if (ws.szType === 'realtimeValue') {
            var p = ws.props || {};
            el.dataset.sid             = p.szSid            || '';
            el.dataset.szUnit          = p.szUnit           || '';
            el.dataset.szTitle         = p.szTitle          || '';
            el.dataset.nFontSize       = p.nFontSize        || 28;
            el.dataset.szFontColor     = p.szFontColor      || '#212529';
            el.dataset.szBgColor       = p.szBgColor        || 'transparent';
            el.dataset.szHighColor     = p.szHighColor      || '#dc3545';
            el.dataset.szLowColor      = p.szLowColor       || '#fd7e14';
            el.classList.add('scada-rt-value');
            el.style.overflow = 'visible';
            el.innerHTML = buildRealtimeValueViewHtml(p, '--');
            if (p.szSid) el.addEventListener('contextmenu', function (ev) { onTrendContextMenu(ev, el.dataset.sid); });
        } else if (ws.szType === 'diPoint') {
            var p = ws.props || {};
            var bSchedule = p.nScheduleId != null;
            el.dataset.szDisplayMode  = p.szDisplayMode  || 'indicator';
            el.dataset.szOnColor      = p.szOnColor      || '#28a745';
            el.dataset.szOffColor     = p.szOffColor     || '#6c757d';
            el.dataset.szOnLabel      = p.szOnLabel      || 'ON';
            el.dataset.szOffLabel     = p.szOffLabel     || 'OFF';
            el.dataset.nIndicatorSize = p.nIndicatorSize || 28;
            el.dataset.nFontSize      = p.nFontSize      || 24;
            el.dataset.szFontColor    = p.szFontColor    || '#212529';
            el.dataset.szBgColor      = p.szBgColor      || 'transparent';
            el.dataset.szTitle        = p.szTitle        || '';
            el.dataset.szAlarmColor   = p.szAlarmColor   || '#dc3545';
            if (bSchedule) {
                el.dataset.scheduleId   = String(p.nScheduleId);
                el.dataset.scheduleName = p.szScheduleName || '';
                el.classList.add('scada-di-schedule');
                el.style.overflow = 'visible';
                el.innerHTML = buildDiPointViewHtml(p, null);
            } else {
                el.dataset.sid            = p.szSid          || '';
                el.classList.add('scada-di-point');
                el.style.overflow = 'visible';
                el.innerHTML = buildDiPointViewHtml(p, null);
                if (p.szSid) el.addEventListener('contextmenu', function (ev) { onTrendContextMenu(ev, el.dataset.sid); });
            }
        } else if (ws.szType === 'aoPoint') {
            var p = ws.props || {};
            el.dataset.cid               = p.szCid              || '';
            el.dataset.sid               = p.szCid              || '';
            el.dataset.szTitle           = p.szTitle             || 'AO \u9ede\u4f4d';
            el.dataset.szDisplayName     = p.szDisplayName       || p.szTitle || 'AO \u9ede\u4f4d';
            el.dataset.szUnit            = p.szUnit              || '';
            el.dataset.fWriteValue       = p.fWriteValue         || 0;
            el.dataset.fMin              = p.fMin                || 0;
            el.dataset.fMax              = p.fMax                || 100;
            el.dataset.fStep             = p.fStep               || 1;
            el.dataset.nDecimalPlaces    = p.nDecimalPlaces      || 2;
            el.dataset.nFontSize         = p.nFontSize           || 16;
            el.dataset.szFontColor       = p.szFontColor         || '#ffffff';
            el.dataset.szMenuManualLabel = p.szMenuManualLabel   || '\u624b\u52d5\u63a7\u5236';
            el.dataset.szMenuAutoLabel   = p.szMenuAutoLabel     || '\u81ea\u52d5\u63a7\u5236';
            el.dataset.szBgColor         = p.szBgColor           || 'transparent';
            el.classList.add('scada-ao-point');
            el.style.overflow = 'visible';
            el.innerHTML = buildAoPointViewHtml(p);
            if (_canControlPage(scadaCurrentId)) {
                el.addEventListener('contextmenu', function (e) { onAoPointContextMenu(e, el); });
            }
        } else if (ws.szType === 'doPoint') {
            var p = ws.props || {};
            el.dataset.cid            = p.szCid            || '';
            el.dataset.szTitle        = p.szTitle           || 'DO \u9ede\u4f4d';
            el.dataset.szDisplayName  = p.szDisplayName     || p.szTitle || 'DO \u9ede\u4f4d';
            el.dataset.nOnValue       = p.nOnValue          || 1;
            el.dataset.nOffValue      = p.nOffValue         || 0;
            el.dataset.nFontSize      = p.nFontSize         || 16;
            el.dataset.szFontColor    = p.szFontColor       || '#212529';
            el.dataset.szMenuOnLabel  = p.szMenuOnLabel  || '';
            el.dataset.szMenuOffLabel = p.szMenuOffLabel || '';
            el.dataset.szMenuAutoLabel = p.szMenuAutoLabel || '';
            el.dataset.szBgColor      = p.szBgColor         || 'transparent';
            el.classList.add('scada-do-point');
            el.style.overflow = 'visible';
            el.innerHTML = buildDoPointViewHtml(p);
            if (_canControlPage(scadaCurrentId)) {
                el.addEventListener('contextmenu', function (e) { onDoPointContextMenu(e, el); });
            }
        } else if (ws.szType === 'pump') {
            var p = ws.props || {};
            el.dataset.sidRun        = p.szSidRun        || '';
            el.dataset.sidFault      = p.szSidFault      || '';
            el.dataset.sidMode       = p.szSidMode       || '';
            el.dataset.sidFreq       = p.szSidFreq       || '';
            el.dataset.cidStartStop  = p.szCidStartStop  || '';
            el.dataset.cidFreqSet    = p.szCidFreqSet    || '';
            el.dataset.nFreqSetMin   = p.nFreqSetMin     || 0;
            el.dataset.nFreqSetMax   = p.nFreqSetMax     || 60;
            el.dataset.szTitle       = p.szTitle          || '\u6c34\u6cf5';
            el.dataset.szRunColor    = p.szRunColor       || '#28a745';
            el.dataset.szStopColor   = p.szStopColor      || '#6c757d';
            el.dataset.szFaultColor  = p.szFaultColor     || '#dc3545';
            el.dataset.szManualColor = p.szManualColor    || '#ffc107';
            el.dataset.szAutoColor   = p.szAutoColor      || '#0d6efd';
            el.dataset.szOutletDir   = p.szOutletDir      || 'right';
            el.dataset.szBgColor     = p.szBgColor        || 'transparent';
            el.dataset.nFreqMax      = p.nFreqMax         || 60;
            el.classList.add('scada-pump');
            el.style.overflow = 'visible';
            el.innerHTML = buildPumpViewHtml(p, 'stop', '', '');
            el.addEventListener('contextmenu', function (e) { onPumpContextMenu(e, el); });
        }
        canvas.appendChild(el);
    }

    function escViewHtml(s) {
        return String(s)
            .replace(/&/g, '&amp;')
            .replace(/"/g, '&quot;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;');
    }

    // ── 控制按鈕 HTML ──
    function buildCtrlBtnHtml(props) {
        var szCid      = props.szCid       || '';
        var szBtnLabel = props.szBtnLabel  || '\u57f7\u884c';
        var szBtnColor = props.szBtnColor  || '#198754';
        var szLabel    = props.szPointName || props.szTitle || szCid;
        var szBg       = (props.szBgColor && props.szBgColor !== 'transparent')
                       ? props.szBgColor : 'transparent';
        var szTooltipText  = szCid
            ? escViewHtml(szLabel)
            : '<i class="fas fa-unlink" style="margin-right:4px;color:#dc3545;"></i>\u672a\u7d81\u5b9a CID';
        return '<div class="ctrl-btn-wrap" style="position:relative;width:100%;height:100%;' +
                    'display:flex;flex-direction:column;' +
                    'align-items:center;justify-content:center;' +
                    'background:' + szBg + ';container-type:size;">' +
                    '<button class="ctrl-btn-exec"' +
                            ' style="width:100%;height:100%;' +
                                   'display:inline-flex;align-items:center;justify-content:center;' +
                                   'gap:4px;border-radius:8px;' +
                                   'font-size:clamp(9px, 45cqh, 14px);' +
                                   'font-weight:600;border:none;' +
                                   'border-top:1px solid rgba(255,255,255,.25);' +
                                   'color:#fff;cursor:pointer;background:' + szBtnColor + ';' +
                                   'overflow:hidden;white-space:nowrap;"' +
                            (szCid ? '' : ' disabled') + '>' +
                        '<i class="fas ' + (props.szBtnIcon || 'fa-hand-pointer') + '" style="font-size:inherit;"></i>' +
                        escViewHtml(szBtnLabel) +
                    '</button>' +
                    '<div class="scada-hover-label"' +
                         ' style="display:none;position:absolute;bottom:0;left:50%;' +
                                'transform:translate(-50%,100%);white-space:nowrap;' +
                                'background:rgba(33,37,41,.9);color:#e0e0e0;' +
                                'font-size:11px;padding:2px 8px;border-radius:4px;' +
                                'pointer-events:none;z-index:25;margin-top:4px;">' +
                        szTooltipText +
                    '</div>' +
                '</div>';
    }

    async function onControlBtnClick(el) {
        var szCid      = el.dataset.cid   || '';
        var szTitle    = el.dataset.szTitle || '\u63a7\u5236';
        var fCtrlValue = parseFloat(el.dataset.fCtrlValue) || 1;
        if (!szCid) { alert('\u6b64\u6309\u9215\u5c1a\u672a\u7d81\u5b9a CID'); return; }
        if (!confirm('\u78ba\u5b9a\u8981\u57f7\u884c\u300c' + szTitle + '\u300d\u63a7\u5236\u6307\u4ee4\uff1f\uff08\u503c=' + fCtrlValue + '\uff09')) return;
        try {
            var resp = await fetch('/api/control/write', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ cid: szCid, value: fCtrlValue, actionType: 'button', displayName: szTitle })
            });
            var result = await resp.json();
            if (result.success) {
                showControlToast('\u5df2\u9001\u51fa\u63a7\u5236\u6307\u4ee4\uff1a' + szTitle);
            } else {
                alert('\u63a7\u5236\u5931\u6557\uff1a' + (result.error || '\u672a\u77e5\u932f\u8aa4'));
            }
        } catch (err) {
            alert('\u63a7\u5236\u8acb\u6c42\u5931\u6557\uff1a' + err.message);
        }
    }

    function showControlToast(szMsg) {
        var d = document.createElement('div');
        d.style.cssText = 'position:fixed;bottom:24px;right:24px;z-index:9999;min-width:220px;';
        d.innerHTML = '<div class="alert alert-success alert-dismissible fade show mb-0 shadow" style="font-size:13px;">' +
            '<i class="fas fa-check-circle me-1"></i>' + escViewHtml(szMsg) +
            '<button type="button" class="btn-close btn-sm" data-bs-dismiss="alert"></button>' +
            '</div>';
        document.body.appendChild(d);
        setTimeout(function () { d.querySelector('.alert')?.classList.remove('show'); setTimeout(function () { d.remove(); }, 300); }, 3000);
    }

    // ── 即時數值 View HTML ──
    function buildRealtimeValueViewHtml(props, szDisplayVal) {
        var nFs   = props.nFontSize   || 28;
        var szClr = props.szFontColor || '#212529';
        var szBg  = (props.szBgColor && props.szBgColor !== 'transparent') ? props.szBgColor : 'transparent';
        var szUnit = props.szUnit || '';
        var nUnitFs = Math.max(12, Math.round(nFs * 0.45));
        var szTitle = props.szTitle || '';
        var szTooltip = szTitle
            ? '<div class="scada-hover-label" style="display:none;position:absolute;top:100%;margin-top:4px;left:50%;' +
                    'transform:translateX(-50%);background:rgba(33,37,41,.9);color:#e0e0e0;' +
                    'font-size:11px;padding:2px 8px;border-radius:4px;white-space:nowrap;' +
                    'pointer-events:none;z-index:25;">' + escViewHtml(szTitle) + '</div>'
            : '';
        return '<div style="position:relative;width:100%;height:100%;display:flex;flex-direction:column;' +
                        'align-items:center;justify-content:center;background:' + szBg + ';border-radius:4px;">' +
                    '<div style="font-size:' + nFs + 'px;font-weight:700;color:' + szClr + ';' +
                                'font-family:\'Segoe UI\',sans-serif;line-height:1.2;">' +
                        escViewHtml(szDisplayVal) +
                        '<span style="font-size:' + nUnitFs + 'px;font-weight:400;color:#6c757d;margin-left:4px;">' + escViewHtml(szUnit) + '</span>' +
                    '</div>' +
                    szTooltip +
                '</div>';
    }

    // ── DI 點位 View HTML ──
    function buildDiPointViewHtml(props, bIsOn) {
        var szMode = props.szDisplayMode || 'indicator';
        var szBg   = (props.szBgColor && props.szBgColor !== 'transparent') ? props.szBgColor : 'transparent';
        var szScheduleName = props.szScheduleName || '';
        var szTitle = szScheduleName ? '' : (props.szTitle || '');
        var szTooltipInner = szScheduleName
            ? escViewHtml(t('scadapage.di.schedule_label')) + ': ' + escViewHtml(szScheduleName)
            : (szTitle ? escViewHtml(szTitle) : '');
        var szTooltip = szTooltipInner
            ? '<div class="scada-hover-label" style="display:none;position:absolute;top:100%;margin-top:4px;left:50%;' +
                    'transform:translateX(-50%);background:rgba(33,37,41,.85);color:#fff;' +
                    'font-size:11px;padding:2px 8px;border-radius:4px;white-space:nowrap;' +
                    'pointer-events:none;z-index:25;">' + szTooltipInner + '</div>'
            : '';

        var isAlarm = props.isAlarmEnabled &&
            ((props.szAlarmTrigger === 'ON' && bIsOn === true) ||
             (props.szAlarmTrigger === 'OFF' && bIsOn === false));
        var szAlarmColor = props.szAlarmColor || '#dc3545';

        var isOn = bIsOn === true;
        var szContentHtml = '';

        if (bIsOn === null) {
            szContentHtml = '<span style="font-size:13px;color:#adb5bd;">--</span>';
        } else if (szMode === 'text') {
            var nFs    = props.nFontSize || 24;
            var szColor = isAlarm ? szAlarmColor
                        : isOn ? (props.szOnColor || '#28a745') : (props.szOffColor || '#6c757d');
            var szLabel = isOn ? (props.szOnLabel || 'ON') : (props.szOffLabel || 'OFF');
            szContentHtml = '<span style="font-size:' + nFs + 'px;font-weight:700;color:' + szColor + ';' +
                                         'font-family:\'Segoe UI\',sans-serif;">' + escViewHtml(szLabel) + '</span>';
        } else {
            var nSize  = props.nIndicatorSize || 28;
            var szColor = isAlarm ? szAlarmColor
                        : isOn ? (props.szOnColor || '#28a745') : (props.szOffColor || '#6c757d');
            szContentHtml = '<span style="display:inline-block;width:' + nSize + 'px;height:' + nSize + 'px;border-radius:50%;' +
                                         'background:' + szColor + ';box-shadow:0 0 6px ' + szColor + ';' +
                                         (isAlarm ? 'animation:di-alarm-pulse 1s infinite;' : '') + '"></span>';
        }

        return '<div style="position:relative;width:100%;height:100%;display:flex;flex-direction:column;' +
                        'align-items:center;justify-content:center;background:' + szBg + ';border-radius:4px;gap:2px;">' +
                    szContentHtml +
                    szTooltip +
                '</div>';
    }

    // ── AO 點位 View HTML ──
    function buildAoPointViewHtml(props) {
        var szCid  = props.szCid || '';
        var szName = props.szDisplayName || props.szTitle || 'AO \u9ede\u4f4d';
        var nFs    = props.nFontSize  || 16;
        var szClr  = props.szFontColor || '#ffffff';
        var szBlock = props.szBlockColor
                    || (props.szBgColor && props.szBgColor !== 'transparent' ? props.szBgColor : null)
                    || '#0d6efd';

        var szTooltipText = szCid
            ? escViewHtml(props.szPointName || props.szTitle || szCid)
            : '<i class="fas fa-unlink" style="margin-right:4px;color:#dc3545;"></i>\u672a\u7d81\u5b9a CID';

        return '<div class="ao-point-body" style="position:relative;width:100%;height:100%;">' +
                    '<div class="ao-point-label-btn"' +
                         ' style="width:100%;height:100%;display:flex;align-items:center;justify-content:center;' +
                                'font-size:' + nFs + 'px;font-weight:600;color:' + szClr + ';' +
                                'text-align:center;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;' +
                                'border-radius:6px;background:' + szBlock + ';cursor:context-menu;">' +
                        escViewHtml(szName) +
                    '</div>' +
                    _buildModeBadgeHtml(szCid) +
                    '<div class="scada-hover-label"' +
                         ' style="display:none;position:absolute;top:100%;margin-top:4px;left:50%;' +
                                'transform:translateX(-50%);white-space:nowrap;' +
                                'background:rgba(33,37,41,.85);color:#fff;' +
                                'font-size:11px;padding:2px 8px;border-radius:4px;' +
                                'pointer-events:none;z-index:25;">' +
                        szTooltipText +
                    '</div>' +
                '</div>';
    }

    // ── 手動模式 M 角標 HTML（控制元件 AO/DO/Pump 共用）──
    // 初始顯示狀態由 _aoManualValueMap 預判（page load 時已從 /api/control/manual-values 載入）
    // 後續由 /api/realtime/latest polling 的 isAuto 欄位驅動 toggle
    function _buildModeBadgeHtml(szCid, szExtraStyle) {
        if (!szCid) return '';
        var cached = _aoManualValueMap[szCid];
        var bManual = !!(cached && cached.isAuto === false);
        var szDisplay = bManual ? 'block' : 'none';
        var szTitle = t('scadapage.badge.manual_mode_tooltip');
        return '<div class="scada-mode-badge" data-cid="' + escViewHtml(szCid) + '"' +
                    ' title="' + escViewHtml(szTitle) + '"' +
                    ' style="display:' + szDisplay + ';' + (szExtraStyle || '') + '">M</div>';
    }

    // ── 切換指定 CID 對應的 M 角標顯示（optimistic / polling 共用）──
    function _toggleModeBadge(szCid, isAuto) {
        if (!szCid) return;
        var nodes = document.querySelectorAll('.scada-mode-badge[data-cid="' + szCid + '"]');
        for (var i = 0; i < nodes.length; i++) {
            nodes[i].style.display = (isAuto === false) ? 'block' : 'none';
        }
    }

    // ── 控制狀態高亮輔助 ──
    function _applyActiveStyle(el) {
        el.style.boxShadow = 'inset 0 0 0 2px #0d6efd';
        el.style.borderRadius = '4px';
        el.style.background = '#f0f4ff';
    }

    // ── 統一關閉所有右鍵選單 ──
    function _removeAllContextMenus() {
        _removeAoContextMenu();
        _removeDoContextMenu();
        _removePumpContextMenu();
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

    // ── DO 點位 HTML ──
    function buildDoPointViewHtml(props) {
        var szCid      = props.szCid          || '';
        var szName     = props.szDisplayName   || props.szTitle || 'DO \u9ede\u4f4d';
        var nFs        = props.nFontSize       || 16;
        var szClr      = props.szFontColor     || '#212529';
        var szBlock    = props.szBlockColor
                       || (props.szBgColor && props.szBgColor !== 'transparent' ? props.szBgColor : null)
                       || '#0d6efd';
        var szTitle    = props.szPointName || props.szTitle || szCid;

        var szTooltipText = szCid
            ? escViewHtml(szTitle)
            : '<i class="fas fa-unlink" style="margin-right:4px;color:#dc3545;"></i>\u672a\u7d81\u5b9a CID';

        return '<div class="do-point-body" style="position:relative;width:100%;height:100%;">' +
                    '<div class="do-point-label-btn"' +
                         ' style="width:100%;height:100%;display:flex;align-items:center;justify-content:center;' +
                                'font-size:' + nFs + 'px;font-weight:600;color:' + szClr + ';' +
                                'text-align:center;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;' +
                                'border-radius:6px;background:' + szBlock + ';' +
                                'cursor:' + (szCid ? 'pointer' : 'default') + ';' +
                                'user-select:none;">' +
                        escViewHtml(szName) +
                    '</div>' +
                    _buildModeBadgeHtml(szCid) +
                    '<div class="scada-hover-label"' +
                         ' style="display:none;position:absolute;top:100%;margin-top:4px;left:50%;' +
                                'transform:translateX(-50%);white-space:nowrap;' +
                                'background:rgba(33,37,41,.85);color:#fff;' +
                                'font-size:11px;padding:2px 8px;border-radius:4px;' +
                                'pointer-events:none;z-index:25;">' +
                        szTooltipText +
                    '</div>' +
                '</div>';
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

    // ── 水泵 SVG ──
    function buildPumpViewHtml(props, szState, szFreqVal, szModeVal) {
        var szRunColor   = props.szRunColor   || '#28a745';
        var szStopColor  = props.szStopColor  || '#6c757d';
        var szFaultColor = props.szFaultColor || '#dc3545';
        var szCircleColor = szState === 'fault' ? szFaultColor
                          : szState === 'run'   ? szRunColor
                          : szStopColor;
        var szManualColor = props.szManualColor || '#ffc107';
        var szAutoColor   = props.szAutoColor   || '#0d6efd';
        var szBodyColor = '#555';
        if (props.szSidMode && szModeVal !== undefined && szModeVal !== '') {
            var bIsAuto = (szModeVal == 1 || szModeVal === '1' || szModeVal === true || szModeVal === 'true');
            szBodyColor = bIsAuto ? szAutoColor : szManualColor;
        }
        var szBg    = (props.szBgColor && props.szBgColor !== 'transparent') ? props.szBgColor : 'transparent';
        var szTitle = props.szTitle || '\u6c34\u6cf5';
        var szDir   = props.szOutletDir || 'right';

        var szTransform = szDir === 'left'  ? 'translate(120,0) scale(-1,1)'
                        : szDir === 'up'    ? 'rotate(-90,60,50)'
                        : '';

        var szSpinStyle = szState === 'run'
            ? ' style="transform-origin:60px 50px; animation: pump-spin 1.5s linear infinite;"'
            : '';

        var szStateText = szState === 'fault' ? '\u6545\u969c' : szState === 'run' ? '\u904b\u8f49\u4e2d' : '\u505c\u6b62';
        var szModeText = szModeVal !== undefined && szModeVal !== ''
            ? (szModeVal == 1 || szModeVal === '1' || szModeVal === 'true' ? '\u81ea\u52d5' : '\u624b\u52d5')
            : '';
        var szFreqText = szFreqVal !== undefined && szFreqVal !== '' && szFreqVal !== '--'
            ? parseFloat(szFreqVal).toFixed(1) + ' Hz' : '';

        var nFreqMax = parseFloat(props.nFreqMax) || 60;
        var bHasFreq = !!props.szSidFreq;
        var szViewBox = bHasFreq ? '0 0 170 110' : '0 0 120 110';
        var szContrastColor = (function () {
            var bg = szBg;
            if (!bg || bg === 'transparent') return '#333';
            var m = bg.match(/^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i);
            if (!m) return '#333';
            var lum = (parseInt(m[1], 16) * 299 + parseInt(m[2], 16) * 587 + parseInt(m[3], 16) * 114) / 1000;
            return lum > 128 ? '#333' : '#f0f0f0';
        })();
        var szGaugeHtml = '';
        if (bHasFreq) {
            var nBarTop = 22, nBarH = 68;
            var fFreq = (szFreqVal !== undefined && szFreqVal !== '' && szFreqVal !== '--')
                ? parseFloat(szFreqVal) : 0;
            var fRatio = Math.max(0, Math.min(1, fFreq / (nFreqMax || 1)));
            var nFillH = Math.round(nBarH * fRatio);
            var szGaugeColor = szState === 'fault' ? szFaultColor : '#17a2b8';
            var nFillY = nBarTop + nBarH - nFillH;
            szGaugeHtml =
                '<rect x="115" y="' + nBarTop + '" width="10" height="' + nBarH + '" rx="3" fill="#333" stroke="#555" stroke-width="1"/>' +
                '<rect class="pump-gauge-fill" x="115" y="' + nFillY + '" width="10" height="' + nFillH + '" rx="3" fill="' + szGaugeColor + '"/>' +
                '<text x="120" y="' + (nBarTop - 4) + '" text-anchor="middle" font-size="8" fill="' + szContrastColor + '">' + nFreqMax + '</text>' +
                '<text x="120" y="' + (nBarTop + nBarH + 10) + '" text-anchor="middle" font-size="8" fill="' + szContrastColor + '">0</text>' +
                (szFreqText ? '<text class="pump-gauge-text" x="130" y="' + (nFillY + nFillH / 2 + 3) + '" text-anchor="start" font-size="9" fill="' + szContrastColor + '" font-weight="600">' + szFreqText + '</text>' : '') +
                '<rect class="pump-gauge-handle" x="108" y="' + (nBarTop - 4) + '" width="24" height="' + (nBarH + 8) + '" fill="transparent" style="cursor:ns-resize;" />';
        }

        var szTooltipParts = [escViewHtml(szTitle), szStateText];
        if (szModeText) szTooltipParts.push(szModeText);
        if (szFreqText) szTooltipParts.push(szFreqText);

        // 兩個 M badge：泵本體（szCidStartStop）+ 變頻器（szCidFreqSet）各自獨立顯示
        // 有頻率 gauge 時：泵本體 badge 在左上、變頻器 badge 在右上；無 gauge 時：泵本體 badge 在右上
        var szCidSS = props.szCidStartStop || '';
        var szCidFQ = props.szCidFreqSet   || '';
        var szBadgeSS = '';
        var szBadgeFQ = '';
        if (szCidSS) {
            szBadgeSS = bHasFreq
                ? _buildModeBadgeHtml(szCidSS, 'left:-4px;right:auto;')
                : _buildModeBadgeHtml(szCidSS);
        }
        if (szCidFQ && bHasFreq) {
            szBadgeFQ = _buildModeBadgeHtml(szCidFQ);
        }

        return '<div style="position:relative;width:100%;height:100%;background:' + szBg + ';border-radius:4px;">' +
            '<svg viewBox="' + szViewBox + '" xmlns="http://www.w3.org/2000/svg" style="width:100%;height:100%;display:block;">' +
                '<rect x="38" y="86" width="44" height="6" rx="2" fill="#4a4a4a"/>' +
                '<rect x="38" y="92" width="44" height="3" rx="1" fill="#333"/>' +
                '<rect x="39" y="86" width="42" height="2" rx="1" fill="rgba(255,255,255,.12)"/>' +
                '<rect x="48" y="72" width="7" height="15" rx="1.5" fill="#4a4a4a" stroke="#3a3a3a" stroke-width=".5"/>' +
                '<rect x="65" y="72" width="7" height="15" rx="1.5" fill="#4a4a4a" stroke="#3a3a3a" stroke-width=".5"/>' +
                '<rect x="48" y="72" width="3.5" height="15" rx="1" fill="rgba(255,255,255,.08)"/>' +
                '<rect x="65" y="72" width="3.5" height="15" rx="1" fill="rgba(255,255,255,.08)"/>' +
                '<g transform="' + szTransform + '">' +
                    '<path d="M 60,22 L 106,22 L 106,37 L 86,37 A 30,30 0 1,1 60,22 Z" fill="rgba(0,0,0,.15)"/>' +
                    '<path d="M 60,20 L 105,20 L 105,35 L 86,35 A 30,30 0 1,1 60,20 Z" fill="' + szBodyColor + '" stroke="#3a3a3a" stroke-width="2" stroke-linejoin="round"/>' +
                    '<rect x="63" y="21" width="40" height="4" rx="1" fill="rgba(255,255,255,.15)"/>' +
                    '<rect x="103" y="18" width="4" height="19" rx="1" fill="' + szBodyColor + '" stroke="#3a3a3a" stroke-width="1"/>' +
                    '<line x1="104" y1="20" x2="106" y2="20" stroke="rgba(255,255,255,.2)" stroke-width=".8"/>' +
                    '<line x1="104" y1="35" x2="106" y2="35" stroke="rgba(0,0,0,.2)" stroke-width=".8"/>' +
                    '<g' + szSpinStyle + '>' +
                        '<circle cx="60" cy="50" r="12" fill="' + szCircleColor + '" stroke="#333" stroke-width="1.5"/>' +
                        '<path d="M 59,48 C 57,44 58.5,40.5 60,40 C 61.5,40.5 63,44 61,48 Z" fill="rgba(255,255,255,.4)"/>' +
                        '<path d="M 62.2,50.1 C 66.7,50.4 69,53.5 68.7,55 C 67.5,56 63.7,55.6 61.2,51.9 Z" fill="rgba(255,255,255,.4)"/>' +
                        '<path d="M 58.8,51.9 C 56.3,55.6 52.5,56 51.3,55 C 51,53.5 53.3,50.4 57.8,50.1 Z" fill="rgba(255,255,255,.4)"/>' +
                        '<circle cx="60" cy="50" r="3.5" fill="#3a3a3a" stroke="#2a2a2a" stroke-width="1"/>' +
                        '<circle cx="59" cy="49" r="1.2" fill="rgba(255,255,255,.25)"/>' +
                    '</g>' +
                '</g>' + szGaugeHtml +
            '</svg>' +
            szBadgeSS +
            szBadgeFQ +
            '<div class="scada-hover-label"' +
                 ' style="display:none;position:absolute;bottom:4px;left:50%;' +
                        'transform:translateX(-50%);white-space:nowrap;' +
                        'background:rgba(33,37,41,.85);color:#fff;' +
                        'font-size:11px;padding:3px 10px;border-radius:4px;' +
                        'pointer-events:none;z-index:10;">' +
                szTooltipParts.join(' \u2014 ') +
            '</div>' +
            '</div>';
    }

    // ── 水泵 Linear Gauge 拖拽控制 ──
    var _pumpGaugeDrag = null;
    var GAUGE_BAR_TOP = 22, GAUGE_BAR_H = 68;

    document.addEventListener('mousedown', function (e) {
        var handle = e.target.closest('.pump-gauge-handle');
        if (!handle) return;
        var pumpEl = handle.closest('.scada-pump');
        if (!pumpEl) return;
        var szCid = pumpEl.dataset.cidFreqSet;
        if (!szCid) return;
        e.preventDefault();
        e.stopPropagation();
        var svgEl = pumpEl.querySelector('svg');
        var nFreqMax = parseFloat(pumpEl.dataset.nFreqMax) || 60;
        var nFqMin   = parseFloat(pumpEl.dataset.nFreqSetMin) || 0;
        var nFqMax   = parseFloat(pumpEl.dataset.nFreqSetMax) || 60;
        var szTitle  = pumpEl.dataset.szTitle || '\u6c34\u6cf5';
        var fInitFreq = _pumpGaugeYToFreq(e, svgEl, nFreqMax, nFqMin, nFqMax);
        _pumpGaugeDrag = { el: pumpEl, svgEl: svgEl, szCid: szCid, szTitle: szTitle, nFreqMax: nFreqMax, nFqMin: nFqMin, nFqMax: nFqMax, fCurFreq: fInitFreq };
        _pumpGaugeUpdateVisual(svgEl, fInitFreq, nFreqMax);
    });

    document.addEventListener('mousemove', function (e) {
        if (!_pumpGaugeDrag) return;
        e.preventDefault();
        var fFreq = _pumpGaugeYToFreq(e, _pumpGaugeDrag.svgEl, _pumpGaugeDrag.nFreqMax, _pumpGaugeDrag.nFqMin, _pumpGaugeDrag.nFqMax);
        _pumpGaugeDrag.fCurFreq = fFreq;
        _pumpGaugeUpdateVisual(_pumpGaugeDrag.svgEl, fFreq, _pumpGaugeDrag.nFreqMax);
    });

    document.addEventListener('mouseup', function (e) {
        if (!_pumpGaugeDrag) return;
        var szCid = _pumpGaugeDrag.szCid;
        var szTitle = _pumpGaugeDrag.szTitle;
        var fCurFreq = _pumpGaugeDrag.fCurFreq;
        _pumpGaugeDrag = null;
        var fRound = Math.round(fCurFreq * 10) / 10;
        (async function () {
            try {
                var resp = await fetch('/api/control/write', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ cid: szCid, value: fRound, actionType: 'pump_freq', displayName: szTitle })
                });
                var result = await resp.json();
                if (result.success) {
                    _aoManualValueMap[szCid] = { value: fRound, isAuto: false };
                    _toggleModeBadge(szCid, false);
                    showControlToast('\u5df2\u9001\u51fa\u983b\u7387\u8a2d\u5b9a\uff1a' + szTitle + ' \u2192 ' + fRound + ' Hz');
                } else {
                    alert('\u983b\u7387\u8a2d\u5b9a\u5931\u6557\uff1a' + (result.error || '\u672a\u77e5\u932f\u8aa4'));
                }
            } catch (err) {
                alert('\u983b\u7387\u8a2d\u5b9a\u8acb\u6c42\u5931\u6557\uff1a' + err.message);
            }
        })();
    });

    function _pumpGaugeYToFreq(e, svgEl, nFreqMax, nFqMin, nFqMax) {
        var rect = svgEl.getBoundingClientRect();
        var vb = svgEl.viewBox.baseVal;
        var scaleY = vb.height / rect.height;
        var svgY = (e.clientY - rect.top) * scaleY;
        var ratio = (GAUGE_BAR_TOP + GAUGE_BAR_H - svgY) / GAUGE_BAR_H;
        var fRaw = ratio * nFreqMax;
        return Math.max(nFqMin, Math.min(nFqMax, fRaw));
    }

    function _pumpGaugeUpdateVisual(svgEl, fFreq, nFreqMax) {
        var fRatio = Math.max(0, Math.min(1, fFreq / (nFreqMax || 1)));
        var nFillH = Math.round(GAUGE_BAR_H * fRatio);
        var nFillY = GAUGE_BAR_TOP + GAUGE_BAR_H - nFillH;
        var fillRect = svgEl.querySelector('.pump-gauge-fill');
        if (fillRect) {
            fillRect.setAttribute('y', nFillY);
            fillRect.setAttribute('height', nFillH);
        }
        var textEl = svgEl.querySelector('.pump-gauge-text');
        var szText = fFreq.toFixed(1) + ' Hz';
        if (textEl) {
            textEl.textContent = szText;
            textEl.setAttribute('y', nFillY + nFillH / 2 + 3);
        } else {
            var ns = 'http://www.w3.org/2000/svg';
            textEl = document.createElementNS(ns, 'text');
            textEl.classList.add('pump-gauge-text');
            textEl.setAttribute('x', '130');
            textEl.setAttribute('y', nFillY + nFillH / 2 + 3);
            textEl.setAttribute('text-anchor', 'start');
            textEl.setAttribute('font-size', '9');
            textEl.setAttribute('fill', '#333');
            textEl.setAttribute('font-weight', '600');
            textEl.textContent = szText;
            svgEl.appendChild(textEl);
        }
    }

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

    // ── 半圓 SVG Gauge ──
    function buildGaugeHtml(props) {
        var cx = 100, cy = 110, r = 83;
        var fMin = props.fMin   || 0;
        var fMax = props.fMax   || 100;
        var szTitle = props.szTitle || '';
        var szTooltip = szTitle
            ? '<div class="scada-hover-label" style="display:none;position:absolute;bottom:4px;left:50%;' +
                    'transform:translateX(-50%);background:rgba(33,37,41,.85);color:#fff;' +
                    'font-size:11px;padding:3px 10px;border-radius:4px;white-space:nowrap;' +
                    'pointer-events:none;z-index:10;">' + escViewHtml(szTitle) + '</div>'
            : '';
        var szBgPath = 'M ' + (cx - r) + ' ' + cy + ' A ' + r + ' ' + r + ' 0 0 1 ' + (cx + r) + ' ' + cy;
        var szGaugeBg = (props.szBgColor && props.szBgColor !== 'transparent') ? props.szBgColor : 'transparent';

        if (props.isOffline) {
            return '<div style="position:relative;width:100%;height:100%;background:' + szGaugeBg + ';border-radius:4px;">' +
                '<svg viewBox="0 0 200 145" xmlns="http://www.w3.org/2000/svg" style="width:100%;height:100%;display:block;">' +
                    '<path d="' + szBgPath + '" fill="none" stroke="#e9ecef" stroke-width="15" stroke-linecap="round"/>' +
                    '<text x="100" y="96" text-anchor="middle" font-size="22" font-weight="700" fill="#dc3545" font-family="\'Segoe UI\',sans-serif">\u65b7\u7dda</text>' +
                    '<text x="100" y="113" text-anchor="middle" font-size="13" fill="#6c757d" font-family="\'Segoe UI\',sans-serif">' + (props.szUnit || '') + '</text>' +
                    '<text x="' + (cx - r) + '" y="' + (cy + 18) + '" text-anchor="middle" font-size="10" fill="#adb5bd">' + fMin + '</text>' +
                    '<text x="' + (cx + r) + '" y="' + (cy + 18) + '" text-anchor="middle" font-size="10" fill="#adb5bd">' + fMax + '</text>' +
                '</svg>' +
                szTooltip +
            '</div>';
        }

        var fVal = props.fValue || 0;
        var fRaw = (fVal - fMin) / ((fMax - fMin) || 1);
        var fPct = Math.max(0.001, Math.min(0.999, fRaw));
        var thetaEnd = (180 + fPct * 180) * Math.PI / 180;
        var ex = (cx + r * Math.cos(thetaEnd)).toFixed(2);
        var ey = (cy + r * Math.sin(thetaEnd)).toFixed(2);
        var szColor = props.szAlarmOverride || (props.szColor || '#00c0ff');
        var szValFill = props.szAlarmOverride || '#212529';
        var szArc = 'M ' + (cx - r) + ' ' + cy + ' A ' + r + ' ' + r + ' 0 0 1 ' + ex + ' ' + ey;
        return '<div style="position:relative;width:100%;height:100%;background:' + szGaugeBg + ';border-radius:4px;">' +
            '<svg viewBox="0 0 200 145" xmlns="http://www.w3.org/2000/svg" style="width:100%;height:100%;display:block;">' +
                '<path d="' + szBgPath + '" fill="none" stroke="#e9ecef" stroke-width="15" stroke-linecap="round"/>' +
                '<path d="' + szArc + '" fill="none" stroke="' + szColor + '" stroke-width="15" stroke-linecap="round"/>' +
                '<text x="100" y="96" text-anchor="middle" font-size="26" font-weight="700" fill="' + szValFill + '" font-family="\'Segoe UI\',sans-serif">' + Number(fVal).toFixed(1) + '</text>' +
                '<text x="100" y="113" text-anchor="middle" font-size="13" fill="#6c757d" font-family="\'Segoe UI\',sans-serif">' + (props.szUnit || '') + '</text>' +
                '<text x="' + (cx - r) + '" y="' + (cy + 18) + '" text-anchor="middle" font-size="10" fill="#adb5bd">' + fMin + '</text>' +
                '<text x="' + (cx + r) + '" y="' + (cy + 18) + '" text-anchor="middle" font-size="10" fill="#adb5bd">' + fMax + '</text>' +
            '</svg>' +
            szTooltip +
        '</div>';
    }

    // ── Table Widget ──
    function buildTableHtml(props) {
        var nC = Math.max(1, Math.min(props.nCols || 3, 4));
        var nR = Math.max(1, props.nRows || 5);
        var hdrColor = props.szHeaderColor || '#343a40';
        var arrColDec = props.arrColDecimals || [];
        // 表格大小：colgroup + per-row inline height（plan 2026-06-01）
        var nDefW = props.nDefaultColW || 80;
        var nDefH = props.nDefaultRowH || 20;
        var arrCW = props.arrColWidths || [];
        var arrRH = props.arrRowHeights || [];
        var bLocked = props.bTableSizeLocked === true;
        // 底色樣式（plan 2026-07-04）：未設定（null/undefined）= 沿用現行純白渲染
        var szBgOdd   = props.szBodyBgOdd   || '';
        var szBgEven  = props.szBodyBgEven  || '';
        var szBorderC = props.szBorderColor || '#f0f0f0';
        // 鎖定 → 用結構欄寬；未鎖定（舊檔）→ 用 table width:100% 等比例
        var szTableStyle = bLocked
            ? 'border-collapse:collapse;table-layout:fixed;'
            : 'width:100%;border-collapse:collapse;';
        var szColgroup = '';
        if (bLocked) {
            szColgroup = '<colgroup>';
            for (var ci2 = 0; ci2 < nC; ci2++) {
                var w = arrCW[ci2] != null ? +arrCW[ci2] : nDefW;
                szColgroup += '<col style="width:' + w + 'px">';
            }
            szColgroup += '</colgroup>';
        }

        if (props.arrCells && props.arrCells.length > 0) {
            var headerRow = props.arrCells[0] || [];
            var szHdr = headerRow.slice(0, nC).map(function (cell) {
                return '<th style="background:' + hdrColor + ';color:' + (cell.szFontColor || '#fff') + ';' +
                    'padding:4px 6px;font-size:' + (cell.nFontSize || 11) + 'px;' +
                    'font-weight:' + (cell.szFontWeight || '500') + ';' +
                    'text-align:' + (cell.szAlign || 'left') + ';">' + escViewHtml(cell.szText || '') + '</th>';
            }).join('');

            var szRows = Array.from({ length: nR }, function (_, ri) {
                var rowIdx = ri + 1;
                if (rowIdx >= props.arrCells.length) return '';
                var row = props.arrCells[rowIdx];
                var nRowH = arrRH[ri] != null ? +arrRH[ri] : nDefH;
                var szRowStyle = bLocked ? ' style="height:' + nRowH + 'px"' : '';
                var szRowBg = (ri % 2 === 1) ? szBgEven : szBgOdd;
                var szBgStyle = szRowBg ? 'background:' + szRowBg + ';' : '';
                var szCells = row.slice(0, nC).map(function (cell, ci) {
                    var nDec = arrColDec[ci];
                    var szPT = cell.szPointType || 'AI';
                    var szSidAttr = '';
                    if (cell.szSid) {
                        szSidAttr = 'data-sid="' + escViewHtml(cell.szSid) + '" data-point-type="' + szPT + '" data-decimals="' + (nDec !== undefined ? nDec : '') + '" data-orig-color="' + (cell.szFontColor || '#444') + '"';
                        if (szPT === 'DI') {
                            szSidAttr += ' data-sz-on-label="' + escViewHtml(cell.szOnLabel || 'ON') + '"';
                            szSidAttr += ' data-sz-off-label="' + escViewHtml(cell.szOffLabel || 'OFF') + '"';
                            szSidAttr += ' data-sz-alarm-color="' + escViewHtml(cell.szAlarmColor || '#dc3545') + '"';
                        }
                        if (szPT === 'AI') {
                            szSidAttr += ' data-sz-high-color="' + escViewHtml(cell.szHighColor || '#dc3545') + '"';
                            szSidAttr += ' data-sz-low-color="' + escViewHtml(cell.szLowColor || '#fd7e14') + '"';
                        }
                    }
                    var szDisplay = cell.szSid ? '--' : escViewHtml(cell.szText || '');
                    return '<td ' + szSidAttr + ' style="padding:3px 6px;' +
                        'font-size:' + (cell.nFontSize || 12) + 'px;' +
                        'color:' + (cell.szFontColor || '#444') + ';' +
                        'font-weight:' + (cell.szFontWeight || 'normal') + ';' +
                        'text-align:' + (cell.szAlign || 'left') + ';' + szBgStyle +
                        'border-bottom:1px solid ' + szBorderC + ';">' + szDisplay + '</td>';
                }).join('');
                return '<tr' + szRowStyle + '>' + szCells + '</tr>';
            }).join('');

            var szHdrRowStyle = bLocked ? ' style="height:' + nDefH + 'px"' : '';
            return '<table style="' + szTableStyle + '">' + szColgroup + '<thead><tr' + szHdrRowStyle + '>' + szHdr + '</tr></thead><tbody>' + szRows + '</tbody></table>';
        }

        var HEADERS = ['\u540d\u7a31', '\u6578\u503c', '\u72c0\u614b', '\u6642\u9593\u6233'];
        var SAMPLE  = [
            ['\u6eab\u5ea6\u611f\u6e2c\u5668', '85.3\u00b0C',  '\u6b63\u5e38', '--'],
            ['\u58d3\u529b\u611f\u6e2c\u5668', '2.40 bar','\u6b63\u5e38', '--'],
            ['\u6d41\u91cf\u8a08',     '12.7 L/s','\u8b66\u544a', '--'],
            ['\u6db2\u4f4d\u611f\u6e2c\u5668', '67.2%',   '\u6b63\u5e38', '--'],
            ['\u96fb\u6d41\u611f\u6e2c\u5668', '32.1 A',  '\u6b63\u5e38', '--'],
        ];
        var szHdr2 = Array.from({ length: nC }, function (_, i) {
            return '<th style="background:' + hdrColor + ';color:#fff;padding:4px 6px;font-size:11px;">' + (HEADERS[i] || '\u6b04' + (i + 1)) + '</th>';
        }).join('');
        var szRows2 = Array.from({ length: nR }, function (_, ri) {
            var row = SAMPLE[ri % SAMPLE.length];
            return '<tr>' + Array.from({ length: nC }, function (_, ci) {
                return '<td style="padding:3px 6px;font-size:11px;border-bottom:1px solid #f0f0f0;">' + (row[ci] || '-') + '</td>';
            }).join('') + '</tr>';
        }).join('');
        return '<table style="width:100%;border-collapse:collapse;"><thead><tr>' + szHdr2 + '</tr></thead><tbody>' + szRows2 + '</tbody></table>';
    }

    // ── 即時資料輪詢與 Widget 更新 ──
    async function fetchAndUpdateGauges() {
        try {
            var resp = await fetch('/api/realtime/latest');
            if (!resp.ok) {
                console.warn('\u5373\u6642\u8cc7\u6599 API \u56de\u61c9\u7570\u5e38\uff1a', resp.status, resp.statusText);
                return;
            }
            var result = await resp.json();
            if (result.success && result.data) {
                lastData = result.data;
                updateScadaWidgets(result.data);
            }
        } catch (err) {
            console.warn('\u5373\u6642\u8cc7\u6599\u53d6\u5f97\u5931\u6557\uff1a', err.message);
        }
    }

    function updateScadaWidgets(data) {
        var sidMap = {};
        var sidValueMap = {};
        var sidQualityMap = {};
        var sidIsAutoMap = {};
        data.forEach(function (item) {
            if (item.sid) {
                sidValueMap[item.sid] = item.value;
                sidQualityMap[item.sid] = (item.quality || '').toUpperCase();
                if (item.value !== '--' && item.value !== null && item.value !== undefined) {
                    var fParsed = parseFloat(item.value);
                    if (!isNaN(fParsed)) sidMap[item.sid] = fParsed;
                }
                // 同步手動/自動旗標：null 代表非控制點位，不收進 map
                if (item.isAuto === true || item.isAuto === false) {
                    sidIsAutoMap[item.sid] = item.isAuto;
                    // 同步 _aoManualValueMap 讓右鍵選單高亮狀態與 polling 一致
                    if (_aoManualValueMap[item.sid]) {
                        _aoManualValueMap[item.sid].isAuto = item.isAuto;
                    } else {
                        _aoManualValueMap[item.sid] = { value: 0, isAuto: item.isAuto };
                    }
                }
            }
        });

        function isBadQuality(sid) {
            return sidQualityMap[sid] === 'BAD';
        }

        // 更新 Gauge
        document.querySelectorAll('.scada-gauge[data-sid]').forEach(function (el) {
            var sid = el.dataset.sid;
            if (!sid || !(sid in sidValueMap)) return;

            var _rule = _alarmRuleMap[sid];
            var _gHighColor = el.dataset.szHighColor || '#dc3545';
            var _gLowColor  = el.dataset.szLowColor  || '#fd7e14';
            function getGaugeAlarmColor(fVal) {
                if (!_rule) return null;
                if (_rule.isAlarmHigh && fVal >= ((_rule.dAlarmHighValue || 80) - (_rule.dDeadbandHigh || 0))) return _gHighColor;
                if (_rule.isAlarmLow  && fVal <= ((_rule.dAlarmLowValue  || 20) + (_rule.dDeadbandLow  || 0))) return _gLowColor;
                return null;
            }

            if (isBadQuality(sid)) {
                el.innerHTML = buildGaugeHtml({
                    fValue: 0, fMin: parseFloat(el.dataset.fMin) || 0, fMax: parseFloat(el.dataset.fMax) || 100,
                    szUnit: el.dataset.szUnit || '', szColor: el.dataset.szColor || '#00c0ff',
                    szTitle: el.dataset.szTitle || '', szBgColor: el.dataset.szBgColor || 'transparent', isOffline: true
                });
            } else if (sidMap[sid] !== undefined) {
                var fVal = sidMap[sid];
                el.innerHTML = buildGaugeHtml({
                    fValue: fVal, fMin: parseFloat(el.dataset.fMin) || 0, fMax: parseFloat(el.dataset.fMax) || 100,
                    szUnit: el.dataset.szUnit || '', szColor: el.dataset.szColor || '#00c0ff',
                    szTitle: el.dataset.szTitle || '', szBgColor: el.dataset.szBgColor || 'transparent',
                    szAlarmOverride: getGaugeAlarmColor(fVal)
                });
            }
        });

        // 更新即時數值
        document.querySelectorAll('.scada-rt-value[data-sid]').forEach(function (el) {
            var sid = el.dataset.sid;
            if (!sid) return;

            var _rtRule = _alarmRuleMap[sid];
            var _szHighColor = el.dataset.szHighColor || '#dc3545';
            var _szLowColor  = el.dataset.szLowColor  || '#fd7e14';
            function getAlarmColor(fVal) {
                if (!_rtRule) return null;
                if (_rtRule.isAlarmHigh && fVal >= ((_rtRule.dAlarmHighValue || 80) - (_rtRule.dDeadbandHigh || 0))) return _szHighColor;
                if (_rtRule.isAlarmLow  && fVal <= ((_rtRule.dAlarmLowValue  || 20) + (_rtRule.dDeadbandLow  || 0))) return _szLowColor;
                return null;
            }

            if (isBadQuality(sid)) {
                el.innerHTML = buildRealtimeValueViewHtml({
                    nFontSize: parseInt(el.dataset.nFontSize) || 28, szFontColor: '#dc3545',
                    szUnit: '', szTitle: el.dataset.szTitle || '', szBgColor: el.dataset.szBgColor || 'transparent'
                }, '\u65b7\u7dda');
            } else if (sidMap[sid] !== undefined) {
                var fVal = sidMap[sid];
                var szAlmClr = getAlarmColor(fVal);
                el.innerHTML = buildRealtimeValueViewHtml({
                    nFontSize: parseInt(el.dataset.nFontSize) || 28,
                    szFontColor: szAlmClr || (el.dataset.szFontColor || '#212529'),
                    szUnit: el.dataset.szUnit || '', szTitle: el.dataset.szTitle || '',
                    szBgColor: el.dataset.szBgColor || 'transparent'
                }, Number(fVal).toFixed(2));
            } else if (sidValueMap[sid] !== undefined) {
                el.innerHTML = buildRealtimeValueViewHtml({
                    nFontSize: parseInt(el.dataset.nFontSize) || 28,
                    szFontColor: el.dataset.szFontColor || '#212529',
                    szUnit: el.dataset.szUnit || '', szTitle: el.dataset.szTitle || '',
                    szBgColor: el.dataset.szBgColor || 'transparent'
                }, '--');
            }
        });

        // 更新 DI 點位 — 排程綁定
        document.querySelectorAll('.scada-di-schedule[data-schedule-id]').forEach(function (el) {
            var nSchId = parseInt(el.dataset.scheduleId, 10);
            if (isNaN(nSchId)) return;
            var sch = (_scheduleCache || []).find(function (s) { return s.nId === nSchId; });
            var szSchName = (sch && sch.szName) ? sch.szName : (el.dataset.scheduleName || '');
            // 排程已刪除或停用 → 紅字提示（依使用者回覆 + plan 驗收條件）
            if (!sch || !sch.isEnabled) {
                var szTooltipNF = szSchName
                    ? '<div class="scada-hover-label" style="display:none;position:absolute;top:100%;margin-top:4px;left:50%;' +
                            'transform:translateX(-50%);background:rgba(33,37,41,.85);color:#fff;' +
                            'font-size:11px;padding:2px 8px;border-radius:4px;white-space:nowrap;' +
                            'pointer-events:none;z-index:25;">' +
                        escViewHtml(t('scadapage.di.schedule_label')) + ': ' + escViewHtml(szSchName) + '</div>'
                    : '';
                el.innerHTML = '<div style="position:relative;width:100%;height:100%;display:flex;' +
                    'align-items:center;justify-content:center;">' +
                    '<span style="font-size:13px;color:#dc3545;font-weight:700;">' +
                    escViewHtml(t('scadapage.di.schedule_not_found')) + '</span>' +
                    szTooltipNF + '</div>';
                return;
            }
            var bIsOn = (window.ScheduleEval && window.ScheduleEval.evalScheduleNow)
                ? window.ScheduleEval.evalScheduleNow(nSchId, 'contact_no', _scheduleCache)
                : null;
            el.innerHTML = buildDiPointViewHtml({
                szDisplayMode: el.dataset.szDisplayMode || 'indicator',
                szOnColor: el.dataset.szOnColor || '#28a745', szOffColor: el.dataset.szOffColor || '#6c757d',
                szOnLabel: el.dataset.szOnLabel || 'ON', szOffLabel: el.dataset.szOffLabel || 'OFF',
                nIndicatorSize: parseInt(el.dataset.nIndicatorSize) || 28,
                nFontSize: parseInt(el.dataset.nFontSize) || 24,
                szBgColor: el.dataset.szBgColor || 'transparent', szTitle: el.dataset.szTitle || '',
                szScheduleName: szSchName,
                isAlarmEnabled: false,
                szAlarmColor: el.dataset.szAlarmColor || '#dc3545'
            }, bIsOn === true);
        });

        // 更新 DI 點位
        document.querySelectorAll('.scada-di-point[data-sid]').forEach(function (el) {
            var sid = el.dataset.sid;
            if (!sid) return;

            if (isBadQuality(sid)) {
                el.innerHTML = '<div style="position:relative;width:100%;height:100%;display:flex;' +
                    'align-items:center;justify-content:center;">' +
                    '<span style="font-size:13px;color:#dc3545;font-weight:700;">\u65b7\u7dda</span></div>';
            } else if (sidValueMap[sid] !== undefined && sidValueMap[sid] !== '--') {
                var raw = sidValueMap[sid];
                var bIsOn = (raw === 1 || raw === '1' || raw === true || raw === 'true'
                    || (typeof raw === 'string' && raw.toUpperCase() === 'ON')
                    || parseFloat(raw) >= 1);
                var _diRule = _alarmRuleMap[sid];
                el.innerHTML = buildDiPointViewHtml({
                    szDisplayMode: el.dataset.szDisplayMode || 'indicator',
                    szOnColor: el.dataset.szOnColor || '#28a745', szOffColor: el.dataset.szOffColor || '#6c757d',
                    szOnLabel: el.dataset.szOnLabel || 'ON', szOffLabel: el.dataset.szOffLabel || 'OFF',
                    nIndicatorSize: parseInt(el.dataset.nIndicatorSize) || 28,
                    nFontSize: parseInt(el.dataset.nFontSize) || 24,
                    szBgColor: el.dataset.szBgColor || 'transparent', szTitle: el.dataset.szTitle || '',
                    isAlarmEnabled: _diRule?.isDiAlarm || false,
                    szAlarmTrigger: _diRule?.szDiTriggerState || 'ON',
                    szAlarmColor: el.dataset.szAlarmColor || '#dc3545'
                }, bIsOn);
            }
        });

        // 更新水泵
        document.querySelectorAll('.scada-pump').forEach(function (el) {
            if (_pumpGaugeDrag && _pumpGaugeDrag.el === el) return;

            var sidRun   = el.dataset.sidRun   || '';
            var sidFault = el.dataset.sidFault || '';
            var sidMode  = el.dataset.sidMode  || '';
            var sidFreq  = el.dataset.sidFreq  || '';
            if (!sidRun && !sidFault && !sidMode && !sidFreq) return;

            if (sidRun && isBadQuality(sidRun)) {
                el.innerHTML = '<div style="position:relative;width:100%;height:100%;display:flex;' +
                    'align-items:center;justify-content:center;">' +
                    '<span style="font-size:13px;color:#dc3545;font-weight:700;">\u65b7\u7dda</span></div>';
                return;
            }

            var bIsRunning = false;
            if (sidRun && sidValueMap[sidRun] !== undefined && sidValueMap[sidRun] !== '--') {
                var raw = sidValueMap[sidRun];
                bIsRunning = (raw === 1 || raw === '1' || raw === true || raw === 'true'
                    || (typeof raw === 'string' && raw.toUpperCase() === 'ON')
                    || parseFloat(raw) >= 1);
            }

            var bIsFault = false;
            if (sidFault && sidValueMap[sidFault] !== undefined && sidValueMap[sidFault] !== '--') {
                var rawF = sidValueMap[sidFault];
                bIsFault = (rawF === 1 || rawF === '1' || rawF === true || rawF === 'true'
                    || parseFloat(rawF) >= 1);
            }

            var szState = bIsFault ? 'fault' : bIsRunning ? 'run' : 'stop';
            var szFreqVal = (sidFreq && sidValueMap[sidFreq] !== undefined) ? sidValueMap[sidFreq] : '';
            var szModeVal = (sidMode && sidValueMap[sidMode] !== undefined) ? sidValueMap[sidMode] : '';

            var szStateKey = szState + '|' + szModeVal;
            if (el.dataset._pumpKey !== szStateKey) {
                el.dataset._pumpKey = szStateKey;
                el.innerHTML = buildPumpViewHtml({
                    szTitle: el.dataset.szTitle || '\u6c34\u6cf5',
                    szRunColor: el.dataset.szRunColor || '#28a745', szStopColor: el.dataset.szStopColor || '#6c757d',
                    szFaultColor: el.dataset.szFaultColor || '#dc3545', szManualColor: el.dataset.szManualColor || '#ffc107',
                    szAutoColor: el.dataset.szAutoColor || '#0d6efd', szOutletDir: el.dataset.szOutletDir || 'right',
                    szBgColor: el.dataset.szBgColor || 'transparent', nFreqMax: parseFloat(el.dataset.nFreqMax) || 60,
                    szSidFreq: sidFreq, szSidMode: sidMode,
                    szCidStartStop: el.dataset.cidStartStop || '', szCidFreqSet: el.dataset.cidFreqSet || ''
                }, szState, szFreqVal, szModeVal);
            } else if (sidFreq) {
                var nFreqMax = parseFloat(el.dataset.nFreqMax) || 60;
                var fFreq = (szFreqVal !== '' && szFreqVal !== '--') ? parseFloat(szFreqVal) : 0;
                var fRatio = Math.max(0, Math.min(1, fFreq / (nFreqMax || 1)));
                var nBarTop = 22, nBarH = 68;
                var nFillH = Math.round(nBarH * fRatio);
                var nFillY = nBarTop + nBarH - nFillH;
                var szFreqText = (szFreqVal !== '' && szFreqVal !== '--')
                    ? parseFloat(szFreqVal).toFixed(1) + ' Hz' : '';
                var fillRect = el.querySelector('.pump-gauge-fill');
                if (fillRect) { fillRect.setAttribute('y', nFillY); fillRect.setAttribute('height', nFillH); }
                var textEl = el.querySelector('.pump-gauge-text');
                if (textEl) { textEl.textContent = szFreqText; textEl.setAttribute('y', nFillY + nFillH / 2 + 3); }
            }
        });

        // 更新表格 SID 儲存格
        document.querySelectorAll('.scada-table td[data-sid]').forEach(function (td) {
            var sid = td.dataset.sid;
            if (!sid) return;
            var szPT = td.dataset.pointType || 'AI';
            var szOrigColor = td.dataset.origColor || '#444';

            if (isBadQuality(sid)) {
                td.textContent = '\u65b7\u7dda';
                td.style.color = '#dc3545';
                return;
            }

            if (szPT === 'DI') {
                if (sidValueMap[sid] === undefined || sidValueMap[sid] === '--') return;
                var raw = sidValueMap[sid];
                var bIsOn = (raw === 1 || raw === '1' || raw === true || raw === 'true'
                    || (typeof raw === 'string' && raw.toUpperCase() === 'ON')
                    || parseFloat(raw) >= 1);
                td.textContent = bIsOn ? (td.dataset.szOnLabel || 'ON') : (td.dataset.szOffLabel || 'OFF');

                var _tdDiRule = _alarmRuleMap[sid];
                var _tdDiAlarmColor = td.dataset.szAlarmColor || '#dc3545';
                if (_tdDiRule?.isDiAlarm) {
                    var isDiAlarming = (_tdDiRule.szDiTriggerState === 'ON' && bIsOn)
                                    || (_tdDiRule.szDiTriggerState === 'OFF' && !bIsOn);
                    td.style.color = isDiAlarming ? _tdDiAlarmColor : szOrigColor;
                } else {
                    td.style.color = szOrigColor;
                }
            } else {
                var nDecimals = parseInt(td.dataset.decimals);
                if (sidMap[sid] !== undefined) {
                    var fVal = sidMap[sid];
                    td.textContent = isNaN(nDecimals) ? Number(fVal).toFixed(2) : Number(fVal).toFixed(nDecimals);

                    var _tdAiRule = _alarmRuleMap[sid];
                    var _tdHighColor = td.dataset.szHighColor || '#dc3545';
                    var _tdLowColor  = td.dataset.szLowColor  || '#fd7e14';
                    var szColor = szOrigColor;
                    if (_tdAiRule) {
                        if (_tdAiRule.isAlarmHigh && fVal >= ((_tdAiRule.dAlarmHighValue || 80) - (_tdAiRule.dDeadbandHigh || 0))) {
                            szColor = _tdHighColor;
                        } else if (_tdAiRule.isAlarmLow && fVal <= ((_tdAiRule.dAlarmLowValue || 20) + (_tdAiRule.dDeadbandLow || 0))) {
                            szColor = _tdLowColor;
                        }
                    }
                    td.style.color = szColor;
                } else if (sidValueMap[sid] !== undefined) {
                    td.textContent = sidValueMap[sid];
                    td.style.color = szOrigColor;
                }
            }
        });

        // 更新所有控制元件（AO/DO/Pump x2）右上角的 M 角標
        // 必須放在 pump rerender 之後，因 pump 換 innerHTML 會新建 badge DOM
        document.querySelectorAll('.scada-mode-badge[data-cid]').forEach(function (badge) {
            var cid = badge.dataset.cid;
            if (!cid) return;
            var ia = sidIsAutoMap[cid];
            badge.style.display = (ia === false) ? 'block' : 'none';
        });
    }
})();
