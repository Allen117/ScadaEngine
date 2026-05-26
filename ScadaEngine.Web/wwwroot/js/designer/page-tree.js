// ============================================================
// Designer — 頁面樹（左側 sidebar）
// ============================================================
// 內容：渲染樹狀結構、切換頁面（儲存舊狀態 / 載入新狀態）、
// 新增 / 刪除 / 編輯頁面、SCADA 圖示選擇器、stripAlarmProps。
// 依賴：state.js / widget-defs.js（WIDGET_DEFS）/
// widget-core.js（renderWidget / selectWidget）/
// canvas.js（applyCanvasBgImage / clearBgImage）/
// ctx-menu.js（showCtxMenu）。
// ============================================================

// ============================================================
// 頁面樹 — 渲染
// ============================================================
function renderPageTree() {
    const wrap = document.getElementById('pageTreeWrap');
    wrap.innerHTML = '';
    arrPageTree.forEach(p => renderPageNode(wrap, p, 0));

    // 空白區域：右鍵 → 新增根頁面
    const blankZone = document.createElement('div');
    blankZone.className = 'page-blank-zone';
    blankZone.addEventListener('contextmenu', e => {
        e.preventDefault();
        showCtxMenu(e, null);
    });
    wrap.appendChild(blankZone);
}

function renderPageNode(container, page, nDepth) {
    const isActive   = page.szId === szCurrentPageId;
    const hasChildren = page.arrChildren && page.arrChildren.length > 0;

    const el = document.createElement('div');
    el.className = 'page-node' + (isActive ? ' active' : '');
    el.dataset.id = page.szId;
    el.style.paddingLeft = (8 + nDepth * 14) + 'px';

    const szIconClass = page.szIcon || (hasChildren ? 'fa-folder-open' : 'fa-file-alt');
    el.innerHTML = `<i class="fas ${szIconClass} pn-icon"></i><span class="pn-name">${escHtml(page.szName)}</span>`;

    el.addEventListener('click',        ()  => selectPage(page.szId));
    el.addEventListener('contextmenu',  e  => { e.preventDefault(); e.stopPropagation(); showCtxMenu(e, page.szId); });

    container.appendChild(el);

    if (hasChildren) {
        page.arrChildren.forEach(child => renderPageNode(container, child, nDepth + 1));
    }
}

// ============================================================
// 頁面樹 — 切換（儲存舊 / 載入新）
// ============================================================
function selectPage(szId) {
    if (szId === szCurrentPageId) return;
    saveCurrentPageState();
    szCurrentPageId = szId;
    const page = findPage(szId);
    if (page) loadPageState(page);
    renderPageTree();
}

// 儲存前過濾掉警報相關屬性（警報由 AlarmSetting 頁面集中管理）
const _ALARM_KEYS = new Set([
    'isAlarmEnabled', 'isAlarmHigh', 'fAlarmHigh', 'fDeadbandHigh', 'szAlarmHighColor',
    'isAlarmLow', 'fAlarmLow', 'fDeadbandLow', 'szAlarmLowColor',
    'szAlarmTrigger', 'szAlarmColor'
]);
function stripAlarmProps(objProps) {
    const result = {};
    for (const [k, v] of Object.entries(objProps)) {
        if (_ALARM_KEYS.has(k)) continue;
        if (k === 'arrCells' && Array.isArray(v)) {
            result[k] = v.map(row =>
                Array.isArray(row)
                    ? row.map(cell => {
                        if (typeof cell !== 'object' || cell === null) return cell;
                        const stripped = {};
                        for (const [ck, cv] of Object.entries(cell)) {
                            if (!_ALARM_KEYS.has(ck)) stripped[ck] = cv;
                        }
                        return stripped;
                    })
                    : row
            );
        } else {
            result[k] = v;
        }
    }
    return result;
}

function saveCurrentPageState() {
    const page = findPage(szCurrentPageId);
    if (!page) return;
    page.szBgDataUrl    = szCurrentBgDataUrl;
    page.szBgFileName   = szCurrentBgFileName;
    page.nCanvasW       = parseInt(canvas.style.width)  || 1200;
    page.nCanvasH       = parseInt(canvas.style.height) || 800;
    page.arrWidgetState = [];
    canvas.querySelectorAll('.canvas-widget').forEach(el => {
        page.arrWidgetState.push({
            szType: el.dataset.type,
            nX:     parseInt(el.style.left) || 0,
            nY:     parseInt(el.style.top)  || 0,
            nW:     el.offsetWidth,
            nH:     el.offsetHeight,
            props:  stripAlarmProps(el.widgetProps)
        });
    });
}

function loadPageState(page) {
    canvas.innerHTML = '';
    selectWidget(null);
    nWidgetCounter = 0;

    canvas.style.width  = (page.nCanvasW || 1200) + 'px';
    canvas.style.height = (page.nCanvasH || 800)  + 'px';

    if (page.szBgDataUrl) {
        applyCanvasBgImage(page.szBgDataUrl, page.szBgFileName || '');
    } else {
        clearBgImage();
    }

    (page.arrWidgetState || []).forEach(ws => restoreWidget(ws));
}

function restoreWidget(ws) {
    const def = WIDGET_DEFS[ws.szType];
    if (!def) return;
    const szId = 'w' + (++nWidgetCounter);
    const el   = document.createElement('div');
    el.id           = szId;
    el.className    = 'canvas-widget';
    el.dataset.type = ws.szType;
    el.style.left   = ws.nX + 'px';
    el.style.top    = ws.nY + 'px';
    el.style.width  = ws.nW + 'px';
    el.style.height = ws.nH + 'px';
    el.widgetProps  = { ...ws.props };
    renderWidget(el);
    canvas.appendChild(el);
}

// ============================================================
// 頁面樹 — 新增 / 刪除
// ============================================================
function addPage(szParentId) {
    const szDefault = t('designer.page.new_name');
    const szName = prompt(t('designer.page.prompt_name'), szDefault);
    if (szName === null) return;

    const newPage = {
        szId:           'p' + (++nPageIdCounter),
        szName:         szName.trim() || szDefault,
        szIcon:         'fa-file-alt',
        arrChildren:    [],
        szBgDataUrl:    null,
        szBgFileName:   null,
        nCanvasW:       1200,
        nCanvasH:       800,
        arrWidgetState: []
    };

    if (szParentId === null) {
        arrPageTree.push(newPage);
    } else {
        const parent = findPage(szParentId);
        if (parent) parent.arrChildren.push(newPage);
    }
    renderPageTree();
}

function deletePage(szId) {
    const nTotal = countPages(arrPageTree);
    if (nTotal <= 1) { alert(t('designer.page.must_keep_one')); return; }

    const page = findPage(szId);
    const szMsg = (page && page.arrChildren.length > 0)
        ? t('designer.page.confirm_delete_with_children', { name: page.szName })
        : t('designer.page.confirm_delete', { name: page?.szName });
    if (!confirm(szMsg)) return;

    // 若刪除目前頁面（或其祖先），先切換至其他頁面
    if (szCurrentPageId === szId || isDescendantOf(szId, szCurrentPageId)) {
        const szFallback = findFirstExcluding(szId, arrPageTree);
        if (szFallback) {
            saveCurrentPageState();
            szCurrentPageId = szFallback;
            const fb = findPage(szFallback);
            if (fb) loadPageState(fb);
        }
    }

    removeFromTree(szId, arrPageTree);
    renderPageTree();
}

// ============================================================
// 頁面樹 — 工具函式
// ============================================================
function findPage(szId, arr) {
    arr = arr || arrPageTree;
    for (const p of arr) {
        if (p.szId === szId) return p;
        const found = findPage(szId, p.arrChildren);
        if (found) return found;
    }
    return null;
}

function removeFromTree(szId, arr) {
    const idx = arr.findIndex(p => p.szId === szId);
    if (idx >= 0) { arr.splice(idx, 1); return true; }
    for (const p of arr) {
        if (removeFromTree(szId, p.arrChildren)) return true;
    }
    return false;
}

function countPages(arr) {
    return (arr || []).reduce((n, p) => n + 1 + countPages(p.arrChildren), 0);
}

// 判斷 szTargetId 是否在以 szAncestorId 為根的子樹內
function isDescendantOf(szAncestorId, szTargetId) {
    const ancestor = findPage(szAncestorId);
    if (!ancestor) return false;
    return !!findPage(szTargetId, ancestor.arrChildren);
}

// 找第一個不屬於 szExcludeId 子樹的頁面 id
function findFirstExcluding(szExcludeId, arr) {
    for (const p of arr) {
        if (p.szId !== szExcludeId && !isDescendantOf(szExcludeId, p.szId)) return p.szId;
        const found = findFirstExcluding(szExcludeId, p.arrChildren);
        if (found) return found;
    }
    return null;
}

// ============================================================
// 頁面編輯（名稱 + 圖示）
// ============================================================
// 圖示 label 透過 _resolveIconLabels() 在 render 時動態以當前 culture 解析
const SCADA_ICONS = [
    { szClass: 'fa-home',                 szLabelKey: 'designer.icon.home'          },
    { szClass: 'fa-file-alt',             szLabelKey: 'designer.icon.general'       },
    { szClass: 'fa-folder-open',          szLabelKey: 'designer.icon.group'         },
    { szClass: 'fa-chart-line',           szLabelKey: 'designer.icon.trend'         },
    { szClass: 'fa-tachometer-alt',       szLabelKey: 'designer.icon.gauge'         },
    { szClass: 'fa-thermometer-half',     szLabelKey: 'designer.icon.temperature'   },
    { szClass: 'fa-bolt',                 szLabelKey: 'designer.icon.power'         },
    { szClass: 'fa-water',                szLabelKey: 'designer.icon.water'         },
    { szClass: 'fa-wind',                 szLabelKey: 'designer.icon.hvac'          },
    { szClass: 'fa-fire',                 szLabelKey: 'designer.icon.combustion'    },
    { szClass: 'fa-snowflake',            szLabelKey: 'designer.icon.refrigeration' },
    { szClass: 'fa-fan',                  szLabelKey: 'designer.icon.fan'           },
    { szClass: 'fa-pump-soap',            szLabelKey: 'designer.icon.pump'          },
    { szClass: 'fa-cog',                  szLabelKey: 'designer.icon.device'        },
    { szClass: 'fa-industry',             szLabelKey: 'designer.icon.factory'       },
    { szClass: 'fa-server',               szLabelKey: 'designer.icon.server'        },
    { szClass: 'fa-network-wired',        szLabelKey: 'designer.icon.network'       },
    { szClass: 'fa-plug',                 szLabelKey: 'designer.icon.plug'          },
    { szClass: 'fa-sun',                  szLabelKey: 'designer.icon.solar'         },
    { szClass: 'fa-map-marked-alt',       szLabelKey: 'designer.icon.map'           },
    { szClass: 'fa-exclamation-triangle', szLabelKey: 'designer.icon.alarm'         },
    { szClass: 'fa-bell',                 szLabelKey: 'designer.icon.bell'          },
    { szClass: 'fa-eye',                  szLabelKey: 'designer.icon.eye'           },
    { szClass: 'fa-database',             szLabelKey: 'designer.icon.database'      },
    { szClass: 'fa-tools',                szLabelKey: 'designer.icon.tools'         },
];

let szEditingPageId    = null;
let szEditingIconClass = null;
let _pageEditModal     = null;

function editPage(szId) {
    const page = findPage(szId);
    if (!page) return;
    szEditingPageId    = szId;
    szEditingIconClass = page.szIcon || 'fa-file-alt';
    document.getElementById('editPageNameInput').value = page.szName;
    renderIconPicker();
    if (!_pageEditModal) {
        _pageEditModal = new bootstrap.Modal(document.getElementById('pageEditModal'));
    }
    _pageEditModal.show();
}

function renderIconPicker() {
    const grid = document.getElementById('iconPickerGrid');
    grid.innerHTML = SCADA_ICONS.map(icon => {
        const szLabel = t(icon.szLabelKey);
        return `
        <div class="icon-pick-item${szEditingIconClass === icon.szClass ? ' selected' : ''}"
             title="${escHtml(szLabel)}"
             onclick="selectPickedIcon(this,'${icon.szClass}')">
            <i class="fas ${icon.szClass}"></i>
            <span>${escHtml(szLabel)}</span>
        </div>
    `;
    }).join('');
}

function selectPickedIcon(el, szClass) {
    document.querySelectorAll('#iconPickerGrid .icon-pick-item')
            .forEach(i => i.classList.remove('selected'));
    el.classList.add('selected');
    szEditingIconClass = szClass;
}

function confirmPageEdit() {
    const szName = document.getElementById('editPageNameInput').value.trim();
    if (!szName) {
        document.getElementById('editPageNameInput').focus();
        return;
    }
    const page = findPage(szEditingPageId);
    if (!page) return;
    page.szName = szName;
    page.szIcon = szEditingIconClass;
    _pageEditModal.hide();
    renderPageTree();
}
