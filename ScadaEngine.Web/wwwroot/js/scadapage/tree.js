// ============================================================
// scadapage/tree.js — 頁面樹渲染 / 切頁 / 畫布渲染與等比縮放
// ============================================================
// 純搬移自 scadapage.js（plan 2026-08-06-scadapage-js-split）。
// 不包 IIFE、global scope，與 js/designer/ 拆分同模式；
// 共享狀態集中於 state.js。原始 4-space 縮排刻意保留，
// 以保證 template literal 內容 byte-level 不變（純搬移原則）。
// 頂層僅註冊 window resize listener（同檔函數），載入順序不敏感。
// ============================================================

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
        fetchAndUpdateAccumulations();   // 換頁立即載入累積值，不等 30 秒輪詢
        fetchAndUpdateCircuitMetrics();  // 換頁立即載入迴路指標，不等 30 秒輪詢
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
