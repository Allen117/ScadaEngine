// ============================================================
// scadapage/tree.js — 頁面樹渲染 / 切頁 / 畫布渲染與等比縮放 / 側欄摺疊
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

    // ── 畫布工作區高度：把「畫面剩下的高度」全給畫布，警報面板永遠貼著螢幕底 ──
    // 原本寫死 calc(100vh - 240px)，警報面板收合後空出的高度沒人接手，
    // 面板就懸在畫面中間、下方留一段空白。改成量測實際面板高度反推畫布高度：
    // 面板收合 → 畫布長高、面板跟著貼底；面板展開 → 畫布縮回，兩種狀態都不會出現空白帶。
    var VIEWPORT_BOTTOM_GAP = 8;   // 面板與 footer 之間留白
    var ALARM_PANEL_MARGIN  = 16;  // .active-alarm-panel 的 margin-top: 1rem

    function _applyCanvasViewportHeight() {
        var viewport = document.querySelector('.scada-canvas-viewport');
        if (!viewport) return;

        var panel  = document.getElementById('activeAlarmPanel');
        var footer = document.querySelector('footer.footer');

        // 文件座標的 top：畫布上方的內容不受畫布自身高度影響，故無循環依賴
        var nTop     = viewport.getBoundingClientRect().top + window.scrollY;
        var nPanelH  = panel  ? panel.offsetHeight + ALARM_PANEL_MARGIN : 0;
        var nFooterH = footer ? footer.offsetHeight : 0;
        var nTargetH = window.innerHeight - nTop - nPanelH - nFooterH - VIEWPORT_BOTTOM_GAP;
        if (!isFinite(nTargetH) || nTargetH <= 0) return;   // 極端視窗尺寸交給 CSS min-height 兜底

        // 差異 < 1px 不重設，避免 ResizeObserver 反覆互相觸發
        if (Math.abs(viewport.getBoundingClientRect().height - nTargetH) < 1) return;
        viewport.style.height = nTargetH + 'px';
    }

    // ── 畫布等比縮放 ──
    function _applyCanvasScale() {
        var wrap = document.getElementById('scadaCanvasWrap');
        var canvas = document.getElementById('scadaCanvas');
        if (!wrap || !canvas) return;
        var parent = wrap.parentElement;
        if (!parent) return;

        _applyCanvasViewportHeight();

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

    // ── 左側「系統總覽」側欄摺疊（狀態持久化於 localStorage）──
    var TREE_SIDE_KEY = 'scadaPage_sideCollapsed_v1';

    function _isTreeSideCollapsed() {
        try { return localStorage.getItem(TREE_SIDE_KEY) === '1'; } catch (_) { return false; }
    }

    function _setTreeToggleTitle(btn, isCollapsed) {
        var szKey = isCollapsed ? 'scadapage.tree.expand' : 'scadapage.tree.collapse';
        // i18n 字典是 DOMContentLoaded 後才 fetch，未 ready 時 t() 只會回傳 key，故延後套用
        var apply = function () { btn.title = t(szKey); };
        if (window.i18n && window.i18n.ready) window.i18n.ready(apply);
        else apply();
    }

    function _applyTreeSideState(isCollapsed, isAnimate) {
        var side = document.getElementById('scadaTreeSide');
        var btn  = document.getElementById('scadaTreeToggle');
        if (!side) return;
        if (!isAnimate) side.classList.add('notrans');
        side.classList.toggle('collapsed', isCollapsed);
        if (btn) {
            _setTreeToggleTitle(btn, isCollapsed);
            btn.setAttribute('aria-expanded', isCollapsed ? 'false' : 'true');
        }
        if (!isAnimate) {
            // 還原初始狀態不做動畫；下一影格才開回 transition，之後點按鈕才有滑動效果
            requestAnimationFrame(function () { side.classList.remove('notrans'); });
        }
    }

    // ── 版面初始化：側欄摺疊狀態還原 + 尺寸變化觀察（由 index.js 於 DOMContentLoaded 呼叫）──
    function _initScadaLayout() {
        var btn = document.getElementById('scadaTreeToggle');
        if (btn) {
            _applyTreeSideState(_isTreeSideCollapsed(), false);
            btn.addEventListener('click', function () {
                var isCollapsed = !_isTreeSideCollapsed();
                try { localStorage.setItem(TREE_SIDE_KEY, isCollapsed ? '1' : '0'); } catch (_) {}
                _applyTreeSideState(isCollapsed, true);
            });
        }

        _applyCanvasViewportHeight();

        // 側欄摺疊（寬度 .2s）與警報面板摺疊（高度 .3s）都是動畫，
        // 用 ResizeObserver 隨每一影格重算，比 transitionend 一次性重算平順。
        // canvasWrap 是 absolute，不影響 viewport 尺寸，不會遞迴觸發。
        if (window.ResizeObserver) {
            var ro = new ResizeObserver(function () { _applyCanvasScale(); });
            var viewport = document.querySelector('.scada-canvas-viewport');
            var panel    = document.getElementById('activeAlarmPanel');
            if (viewport) ro.observe(viewport);
            if (panel)    ro.observe(panel);   // 面板高度變 → 畫布高度補上，面板維持貼底
        }
    }
