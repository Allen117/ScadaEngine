/*
 * EMS 首頁卡片顯示設定頁（/EmsCardSetting）— 版面預覽互動
 * - GET  api/cards 載入生效卡片清單（EmsCardRegistry merge DB；順序即生效順序）
 * - 預覽區：卡片依實際 col-* 欄寬呈現，HTML5 drag & drop 排序、header ✕ 隱藏
 * - 隱藏區：點「加回」回到預覽最後
 * - POST api/cards 儲存整份（陣列順序 = 顯示順序，server 端正規化 SortOrder 1..N）
 * 卡片名稱由 i18n key（emscard.name.{key}）依語系翻譯。
 */
(function () {
    'use strict';

    function t(key, args) { return (window.i18n && window.i18n.t) ? window.i18n.t(key, args) : key; }

    // { cardKey, nameKey, gridCss, icon, isVisible } — 陣列順序 = 顯示順序（可見區塊在前、隱藏在後）
    var _cards = [];

    function el(id) { return document.getElementById(id); }

    function escapeHtml(s) {
        if (s == null) return '';
        return String(s)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    function visibleCards() { return _cards.filter(function (c) { return c.isVisible; }); }
    function hiddenCards() { return _cards.filter(function (c) { return !c.isVisible; }); }

    // ── 浮動提示 ─────────────────────────────────────────────
    function toast(szMessage, isError) {
        var div = document.createElement('div');
        div.className = 'ems-card-toast' + (isError ? ' ems-card-toast-error' : '');
        div.textContent = szMessage;
        document.body.appendChild(div);
        setTimeout(function () { div.classList.add('show'); }, 10);
        setTimeout(function () {
            div.classList.remove('show');
            setTimeout(function () { div.remove(); }, 300);
        }, 3000);
    }

    // ── 載入 ─────────────────────────────────────────────────
    function load() {
        fetch('/EmsCardSetting/api/cards', { credentials: 'same-origin' })
            .then(function (r) { return r.json().then(function (j) { return { ok: r.ok, json: j }; }); })
            .then(function (res) {
                if (!res.ok) { toast(t('emscardsetting.err.load_failed'), true); return; }
                _cards = res.json.cards || [];
                render();
            })
            .catch(function () { toast(t('emscardsetting.err.load_failed'), true); });
    }

    // ── 渲染 ─────────────────────────────────────────────────
    function render() {
        renderPreview();
        renderTray();
    }

    function renderPreview() {
        var grid = el('previewGrid');
        var aVisible = visibleCards();
        if (aVisible.length === 0) {
            grid.innerHTML = '<div class="col-12 text-center text-muted py-4">' +
                escapeHtml(t('emscardsetting.none_visible')) + '</div>';
            return;
        }
        grid.innerHTML = aVisible.map(function (c) {
            return '<div class="' + escapeHtml(c.gridCss) + ' ems-prev-col" draggable="true" data-key="' + escapeHtml(c.cardKey) + '">' +
                '<div class="ems-prev-card">' +
                '<div class="ems-prev-header">' +
                '<span class="ems-prev-title"><i class="fas ' + escapeHtml(c.icon) + ' me-1"></i>' + escapeHtml(t(c.nameKey)) + '</span>' +
                '<button type="button" class="ems-prev-remove" title="' + escapeHtml(t('emscardsetting.remove')) + '"' +
                ' onclick="window._emsCardSetting.remove(\'' + escapeHtml(c.cardKey) + '\')">×</button>' +
                '</div>' +
                '<div class="ems-prev-body"><i class="fas fa-arrows-alt"></i></div>' +
                '</div></div>';
        }).join('');
        bindDrag(grid);
    }

    function renderTray() {
        var tray = el('hiddenTray');
        var aHidden = hiddenCards();
        if (aHidden.length === 0) {
            tray.innerHTML = '<span class="text-muted small">' + escapeHtml(t('emscardsetting.none_hidden')) + '</span>';
            return;
        }
        tray.innerHTML = aHidden.map(function (c) {
            return '<span class="ems-tray-item">' +
                '<i class="fas ' + escapeHtml(c.icon) + ' me-1"></i>' + escapeHtml(t(c.nameKey)) +
                '<button type="button" class="btn btn-sm btn-outline-primary ms-2"' +
                ' onclick="window._emsCardSetting.addBack(\'' + escapeHtml(c.cardKey) + '\')">' +
                '<i class="fas fa-plus me-1"></i>' + escapeHtml(t('emscardsetting.add_back')) + '</button>' +
                '</span>';
        }).join('');
    }

    // ── 拖曳排序（HTML5 DnD；dragover 時即時移動 DOM，dragend 回寫順序） ──
    function bindDrag(grid) {
        grid.querySelectorAll('.ems-prev-col').forEach(function (col) {
            col.addEventListener('dragstart', function (e) {
                col.classList.add('ems-prev-dragging');
                e.dataTransfer.effectAllowed = 'move';
                try { e.dataTransfer.setData('text/plain', col.dataset.key); } catch (err) { /* IE 相容 */ }
            });
            col.addEventListener('dragend', function () {
                col.classList.remove('ems-prev-dragging');
                commitOrderFromDom();
            });
            col.addEventListener('dragover', function (e) {
                e.preventDefault();
                e.dataTransfer.dropEffect = 'move';
                var dragging = grid.querySelector('.ems-prev-dragging');
                if (!dragging || dragging === col) return;
                var aCols = Array.prototype.slice.call(grid.querySelectorAll('.ems-prev-col'));
                if (aCols.indexOf(dragging) < aCols.indexOf(col)) col.after(dragging);
                else col.before(dragging);
            });
        });
    }

    function commitOrderFromDom() {
        var aKeys = Array.prototype.slice.call(document.querySelectorAll('#previewGrid .ems-prev-col'))
            .map(function (n) { return n.dataset.key; });
        var map = {};
        _cards.forEach(function (c) { map[c.cardKey] = c; });
        var aNext = aKeys.map(function (k) { return map[k]; }).filter(Boolean);
        hiddenCards().forEach(function (c) { aNext.push(c); });
        _cards = aNext;
        render();
    }

    // ── 隱藏 / 加回 ─────────────────────────────────────────
    function remove(szKey) {
        var nIdx = _cards.findIndex(function (c) { return c.cardKey === szKey; });
        if (nIdx < 0) return;
        var card = _cards.splice(nIdx, 1)[0];
        card.isVisible = false;
        _cards.push(card); // 隱藏卡集中在陣列尾端
        render();
    }

    function addBack(szKey) {
        var nIdx = _cards.findIndex(function (c) { return c.cardKey === szKey; });
        if (nIdx < 0) return;
        var card = _cards.splice(nIdx, 1)[0];
        card.isVisible = true;
        // 插到可見區塊最後
        var nInsert = 0;
        for (var i = 0; i < _cards.length; i++) {
            if (_cards[i].isVisible) nInsert = i + 1;
        }
        _cards.splice(nInsert, 0, card);
        render();
    }

    // ── 儲存 ─────────────────────────────────────────────────
    function save() {
        var btn = el('btnSave');
        btn.disabled = true;
        fetch('/EmsCardSetting/api/cards', {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                cards: _cards.map(function (c) { return { cardKey: c.cardKey, isVisible: c.isVisible }; })
            })
        })
            .then(function (r) { return r.json().then(function (j) { return { ok: r.ok, json: j }; }); })
            .then(function (res) {
                if (!res.ok) { toast(t('emscardsetting.err.save_failed'), true); return; }
                toast(t('emscardsetting.saved'), false);
            })
            .catch(function () { toast(t('emscardsetting.err.save_failed'), true); })
            .finally(function () { btn.disabled = false; });
    }

    window._emsCardSetting = { save: save, remove: remove, addBack: addBack };

    document.addEventListener('DOMContentLoaded', function () {
        if (window.i18n && window.i18n.ready) window.i18n.ready(load);
        else load();
    });
})();
