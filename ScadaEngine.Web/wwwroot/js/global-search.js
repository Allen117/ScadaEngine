/*
 * 全站搜尋（Ctrl+K 命令面板）
 * - 索引來源：GET /api/globalsearch/index（後端已依權限過濾，含 zh/en 雙語標題 + 同義關鍵字）
 * - 比對：前綴 > 子字串 > 子序列（模糊），zh-TW / en / 關鍵字 / 路由皆可命中
 * - 排序：比對分數 + 使用頻率加權（localStorage，換裝置歸零屬可接受行為）
 * - 空字串：顯示最近使用（依最後點擊時間排序），無紀錄則列出全部頁面
 */
(function () {
    'use strict';

    var FREQ_KEY = 'scadaGlobalSearchFreq';
    var MAX_RESULTS = 12;
    var FREQ_BONUS_CAP = 20;

    var _aIndex = null;      // 後端索引快取（本頁生命週期）
    var _fetching = null;    // 進行中的 fetch Promise（避免重複請求）
    var _modal = null;       // bootstrap.Modal 實例
    var _nSelected = 0;      // 目前鍵盤選取的項目 index
    var _aRendered = [];     // 目前畫面上的項目（與 DOM 順序一致）

    function t(szKey, args) {
        return window.i18n && window.i18n.t ? window.i18n.t(szKey, args) : szKey;
    }

    function isEnglish() {
        return !!(window.i18n && window.i18n.currentCulture && window.i18n.currentCulture().indexOf('en') === 0);
    }

    function escapeHtml(sz) {
        return String(sz).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    // ── 使用頻率（localStorage: { route: { n: 次數, t: 最後點擊 epoch ms } }）──

    function loadFreq() {
        try { return JSON.parse(localStorage.getItem(FREQ_KEY)) || {}; }
        catch (e) { return {}; }
    }

    function recordUse(szRoute) {
        var freq = loadFreq();
        var rec = freq[szRoute] || { n: 0, t: 0 };
        rec.n += 1;
        rec.t = Date.now();
        freq[szRoute] = rec;
        try { localStorage.setItem(FREQ_KEY, JSON.stringify(freq)); } catch (e) { /* 容量滿等異常直接放棄記錄 */ }
    }

    // ── 比對與計分 ──

    /** q 是否為 s 的子序列（輸入 wtr 可命中 water） */
    function isSubsequence(szQ, szS) {
        var i = 0;
        for (var j = 0; j < szS.length && i < szQ.length; j++) {
            if (szS[j] === szQ[i]) i++;
        }
        return i === szQ.length;
    }

    function scoreText(szQ, szText) {
        if (!szText) return 0;
        var szLower = szText.toLowerCase();
        if (szLower.indexOf(szQ) === 0) return 100;
        if (szLower.indexOf(szQ) >= 0) return 70;
        if (szQ.length >= 2 && isSubsequence(szQ, szLower)) return 35;
        return 0;
    }

    /** 英文標題字首縮寫（Energy Report → er），供 PM / er 這類縮寫輸入比對 */
    function acronym(szTitle) {
        if (!szTitle) return '';
        var aWords = szTitle.split(/[^A-Za-z]+/).filter(function (w) { return w.length > 0; });
        if (aWords.length < 2) return '';
        return aWords.map(function (w) { return w[0]; }).join('').toLowerCase();
    }

    function scoreEntry(entry, szQ, freq) {
        var nBest = Math.max(
            scoreText(szQ, entry.szTitleZh),
            scoreText(szQ, entry.szTitleEn),
            Math.min(scoreText(szQ, entry.szRoute.substring(1)), 80),   // 路由比對上限 80，避免壓過標題前綴
            Math.min(scoreText(szQ, entry.szKeywords), 60)              // 關鍵字僅輔助，上限 60
        );
        // 縮寫比對：純英文 query 對英文標題字首（pm → Power Meter）
        if (szQ.length >= 2 && /^[a-z]+$/.test(szQ)) {
            var szAcr = acronym(entry.szTitleEn);
            if (szAcr && szAcr.indexOf(szQ) === 0) nBest = Math.max(nBest, 90);
        }
        if (nBest === 0) return 0;
        var rec = freq[entry.szRoute];
        return nBest + Math.min(rec ? rec.n : 0, FREQ_BONUS_CAP) * 3;
    }

    // ── 索引載入 ──

    function ensureIndex() {
        if (_aIndex) return Promise.resolve(_aIndex);
        if (_fetching) return _fetching;
        _fetching = fetch('/api/globalsearch/index', { credentials: 'same-origin' })
            .then(function (r) {
                if (!r.ok) throw new Error('globalsearch index fetch failed: ' + r.status);
                return r.json();
            })
            .then(function (json) {
                _aIndex = (json && json.entries) || [];
                return _aIndex;
            })
            .catch(function (err) {
                console.error('[global-search]', err);
                _fetching = null;   // 允許下次重試
                throw err;
            });
        return _fetching;
    }

    // ── 渲染 ──

    function highlightTitle(szTitle, szQ) {
        var szEscaped = escapeHtml(szTitle);
        if (!szQ) return szEscaped;
        var nPos = szTitle.toLowerCase().indexOf(szQ);
        if (nPos < 0) return szEscaped;
        // 對原始字串切片後各自 escape，避免 escape 造成位移
        return escapeHtml(szTitle.substring(0, nPos)) +
            '<mark>' + escapeHtml(szTitle.substring(nPos, nPos + szQ.length)) + '</mark>' +
            escapeHtml(szTitle.substring(nPos + szQ.length));
    }

    function render(aEntries, szQ, szHeaderKey) {
        var elList = document.getElementById('globalSearchResults');
        if (!elList) return;
        _aRendered = aEntries;
        _nSelected = 0;

        if (aEntries.length === 0) {
            elList.innerHTML = '<div class="gs-empty text-muted">' + escapeHtml(t('layout.search.no_result')) + '</div>';
            return;
        }

        var isEn = isEnglish();
        var szHtml = szHeaderKey
            ? '<div class="gs-header text-muted">' + escapeHtml(t(szHeaderKey)) + '</div>'
            : '';
        for (var i = 0; i < aEntries.length; i++) {
            var e = aEntries[i];
            var szTitle = isEn ? e.szTitleEn : e.szTitleZh;
            szHtml +=
                '<div class="gs-item' + (i === 0 ? ' active' : '') + '" data-idx="' + i + '">' +
                    '<i class="' + escapeHtml(e.szIcon) + ' gs-icon text-primary"></i>' +
                    '<span class="gs-title">' + highlightTitle(szTitle, szQ) + '</span>' +
                    (e.isEms ? '<i class="fas fa-leaf gs-ems-mark text-success"></i>' : '') +
                '</div>';
        }
        elList.innerHTML = szHtml;

        elList.querySelectorAll('.gs-item').forEach(function (el) {
            el.addEventListener('click', function () { go(parseInt(this.getAttribute('data-idx'), 10)); });
            el.addEventListener('mouseenter', function () { setSelected(parseInt(this.getAttribute('data-idx'), 10)); });
        });
    }

    function renderError() {
        var elList = document.getElementById('globalSearchResults');
        if (elList) {
            elList.innerHTML = '<div class="gs-empty text-danger">' + escapeHtml(t('layout.search.error')) + '</div>';
        }
    }

    function setSelected(nIdx) {
        if (nIdx < 0 || nIdx >= _aRendered.length) return;
        _nSelected = nIdx;
        var elList = document.getElementById('globalSearchResults');
        elList.querySelectorAll('.gs-item').forEach(function (el, i) {
            el.classList.toggle('active', i === nIdx);
        });
        var elActive = elList.querySelector('.gs-item.active');
        if (elActive && elActive.scrollIntoView) elActive.scrollIntoView({ block: 'nearest' });
    }

    function go(nIdx) {
        var entry = _aRendered[nIdx];
        if (!entry) return;
        recordUse(entry.szRoute);
        window.location.href = entry.szRoute;
    }

    // ── 查詢入口 ──

    function search(szRaw) {
        var szQ = (szRaw || '').trim().toLowerCase();
        ensureIndex().then(function (aIndex) {
            if (szQ === '') {
                // 開窗預設不列整份選項：只有累積過使用紀錄才顯示「最近使用」，否則留白
                var freq = loadFreq();
                var aRecent = aIndex
                    .filter(function (e) { return freq[e.szRoute]; })
                    .sort(function (a, b) { return freq[b.szRoute].t - freq[a.szRoute].t; })
                    .slice(0, 8);
                if (aRecent.length > 0) {
                    render(aRecent, '', 'layout.search.recent');
                } else {
                    _aRendered = [];
                    _nSelected = 0;
                    var elList = document.getElementById('globalSearchResults');
                    if (elList) elList.innerHTML = '';
                }
                return;
            }
            var freqMap = loadFreq();
            var aScored = aIndex
                .map(function (e) { return { e: e, n: scoreEntry(e, szQ, freqMap) }; })
                .filter(function (x) { return x.n > 0; })
                .sort(function (a, b) { return b.n - a.n; })
                .slice(0, MAX_RESULTS)
                .map(function (x) { return x.e; });
            render(aScored, szQ, null);
        }).catch(renderError);
    }

    // ── 開關與鍵盤 ──

    function open(e) {
        if (e && e.preventDefault) e.preventDefault();
        var elModal = document.getElementById('globalSearchModal');
        if (!elModal) return;
        if (!_modal) _modal = new bootstrap.Modal(elModal);
        _modal.show();
    }

    function init() {
        var elModal = document.getElementById('globalSearchModal');
        var elInput = document.getElementById('globalSearchInput');
        if (!elModal || !elInput) return;   // 未登入頁面不掛載

        elModal.addEventListener('shown.bs.modal', function () {
            elInput.focus();
            elInput.select();
            search(elInput.value);
        });

        elInput.addEventListener('input', function () { search(this.value); });

        elInput.addEventListener('keydown', function (ev) {
            if (ev.key === 'ArrowDown') { ev.preventDefault(); setSelected(_nSelected + 1); }
            else if (ev.key === 'ArrowUp') { ev.preventDefault(); setSelected(_nSelected - 1); }
            else if (ev.key === 'Enter') { ev.preventDefault(); go(_nSelected); }
        });

        document.addEventListener('keydown', function (ev) {
            if ((ev.ctrlKey || ev.metaKey) && ev.key && ev.key.toLowerCase() === 'k') {
                ev.preventDefault();
                open();
            }
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    window._globalSearch = { open: open };
})();
