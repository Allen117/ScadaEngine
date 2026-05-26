// ============================================================
// Designer — 共用狀態 + 工具函式
// ============================================================
// 拆檔說明：本檔必須最先載入，建立全域 mutable 狀態與工具函式，
// 供後續 widget-defs / widget-core / prop-panel / picker / canvas /
// page-tree / ctx-menu / io / index 等 module 直接使用。
// 採 global scope（與 logicflow 命名空間隔離模式不同，見 plan 決策 2）。
// ============================================================

// i18n 全域 helper（所有 designer/*.js 共用）
function t(szKey, args) {
    return (window.i18n && window.i18n.t) ? window.i18n.t(szKey, args) : szKey;
}

// ============================================================
// 狀態
// ============================================================
let nWidgetCounter = 0;
let selectedEl = null;
let selectedWidgetIds = new Set();   // 多選元件 Id 集合

// 移動
let isMoving = false;
let moveStartMouse = { x: 0, y: 0 };    // 群組拖曳起始滑鼠座標
let moveOrigPositions = {};               // 群組拖曳原始位置 { widgetId: {x,y} }

// 縮放
let isResizing = false;
let resizingEl = null;
let resizeStart = { mx: 0, my: 0, w: 0, h: 0 };

// 目前背景圖追蹤（供頁面切換時儲存）
let szCurrentBgDataUrl  = null;
let szCurrentBgFileName = null;

// ============================================================
// 頁面樹資料結構
// ============================================================
let nPageIdCounter = 1;
let szCurrentPageId = 'p1';

// 預設主頁面：i18n 字典載入後會在 index.js 內以 culture-aware 名稱覆寫此預設值
// （前提：仍是預設樹、未從 DB 載入、使用者未編輯名稱）
let arrPageTree = [{
    szId:           'p1',
    szName:         '主頁面',
    szIcon:         'fa-home',
    arrChildren:    [],
    szBgDataUrl:    null,
    szBgFileName:   null,
    nCanvasW:       1200,
    nCanvasH:       800,
    arrWidgetState: []
}];

// ── 警報規則快取（僅用於鈴鐺圖示顯示）──
let _setAlarmSids = new Set();
async function loadAlarmRuleSids() {
    try {
        const resp = await fetch('/api/alarm-rules');
        if (resp.ok) {
            const arr = await resp.json();
            _setAlarmSids = new Set(arr.filter(r => r.isEnabled).map(r => r.szSID));
        }
    } catch (ex) { console.warn('載入警報規則失敗:', ex); }
}
function hasAlarmRule(szSid) { return szSid && _setAlarmSids.has(szSid); }

const canvas = document.getElementById('canvas');

// ============================================================
// 計算 Designer 高度（動態適應 navbar）
// ============================================================
function adjustHeight() {
    const navbar = document.querySelector('.navbar');
    const navH = navbar ? navbar.offsetHeight : 56;
    document.getElementById('designerOuter').style.height = (window.innerHeight - navH) + 'px';
}
adjustHeight();
window.addEventListener('resize', adjustHeight);

// ============================================================
// 工具函式
// ============================================================
function snapGrid(n, nGrid = 10) {
    return Math.round(n / nGrid) * nGrid;
}

function escHtml(s) {
    return String(s)
        .replace(/&/g, '&amp;')
        .replace(/"/g, '&quot;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;');
}

// 判斷色碼是否為深色（亮度 < 128 視為深色），支援 hex 與 rgb() 格式
function isDarkColor(sz) {
    if (!sz || sz === 'transparent') return false;
    let r, g, b;
    const mHex = sz.match(/^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i);
    if (mHex) {
        r = parseInt(mHex[1], 16); g = parseInt(mHex[2], 16); b = parseInt(mHex[3], 16);
    } else {
        const mRgb = sz.match(/rgb\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)/);
        if (!mRgb) return false;
        r = +mRgb[1]; g = +mRgb[2]; b = +mRgb[3];
    }
    return (r * 299 + g * 587 + b * 114) / 1000 < 128;
}

function syncPosInputs(x, y) {
    const pX = document.getElementById('pX');
    const pY = document.getElementById('pY');
    if (pX) pX.value = x;
    if (pY) pY.value = y;
}

function syncSizeInputs(w, h) {
    const pw = document.getElementById('pW');
    const ph = document.getElementById('pH');
    if (pw) pw.value = w;
    if (ph) ph.value = h;
}
