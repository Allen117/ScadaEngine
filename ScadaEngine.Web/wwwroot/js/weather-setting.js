/*
 * 氣象資料來源設定頁（/WeatherSetting）
 * - GET  api/setting  載入設定與最近抓取狀態（狀態每 30 秒輪詢刷新，不動表單欄位）
 * - POST api/stations 載入 CWA 測站清單（縣市 → 測站兩層下拉）
 * - POST api/test     測試連線（用表單當下的 key/測站抓一次觀測）
 * - POST api/setting  儲存
 * LastFetchMessage 為機器碼格式（ok|temp|hum|time…），此處依語系翻譯顯示。
 */
(function () {
    'use strict';

    function t(key, args) { return (window.i18n && window.i18n.t) ? window.i18n.t(key, args) : key; }

    var stations = [];        // 測站清單（載入後）
    var savedSetting = null;  // 最近一次從 server 讀到的設定

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

    // ── 浮動提示 ─────────────────────────────────────────────
    function toast(szMessage, isError) {
        var div = document.createElement('div');
        div.className = 'weather-toast' + (isError ? ' weather-toast-error' : '');
        div.textContent = szMessage;
        document.body.appendChild(div);
        setTimeout(function () { div.classList.add('show'); }, 10);
        setTimeout(function () {
            div.classList.remove('show');
            setTimeout(function () { div.remove(); }, 300);
        }, 3000);
    }

    function apiErrorMessage(json) {
        var code = json && json.message ? json.message : 'load_failed';
        return t('weathersetting.err.' + code);
    }

    // ── 設定載入 / 狀態顯示 ──────────────────────────────────
    function loadSetting() {
        return fetch('/WeatherSetting/api/setting', { credentials: 'same-origin' })
            .then(function (r) { return r.json().then(function (j) { return { ok: r.ok, json: j }; }); })
            .then(function (res) {
                if (!res.ok) { toast(apiErrorMessage(res.json), true); return; }
                savedSetting = res.json;
                el('txtApiKey').value = savedSetting.apiKey || '';
                el('numInterval').value = savedSetting.pollIntervalMinutes || 10;
                el('chkEnabled').checked = !!savedSetting.isEnabled;
                renderCurrentStation();
                renderStatus(savedSetting);
            })
            .catch(function () { toast(t('weathersetting.err.load_failed'), true); });
    }

    function refreshStatus() {
        fetch('/WeatherSetting/api/setting', { credentials: 'same-origin' })
            .then(function (r) { return r.ok ? r.json() : null; })
            .then(function (json) { if (json) renderStatus(json); })
            .catch(function () { /* 靜默 — 下一輪再試 */ });
    }

    function renderCurrentStation() {
        var div = el('currentStation');
        if (savedSetting && savedSetting.stationId) {
            div.textContent = t('weathersetting.current_station', {
                county: savedSetting.county || '',
                name: savedSetting.stationName || '',
                id: savedSetting.stationId
            });
        } else {
            div.textContent = t('weathersetting.current_station_none');
        }
    }

    function renderStatus(s) {
        el('lastFetchTime').textContent = s.lastFetchTime || '—';

        var szBadge;
        if (s.lastFetchOk === true) {
            szBadge = '<span class="badge bg-success">' + escapeHtml(t('weathersetting.status.ok')) + '</span>';
        } else if (s.lastFetchOk === false) {
            szBadge = '<span class="badge bg-danger">' + escapeHtml(t('weathersetting.status.fail')) + '</span>';
        } else {
            szBadge = '<span class="badge bg-secondary">—</span>';
        }
        el('lastFetchResult').innerHTML = szBadge;
        el('lastFetchMessage').textContent = translateFetchMessage(s.lastFetchMessage);
    }

    /** LastFetchMessage 機器碼 → 依語系文字 */
    function translateFetchMessage(szRaw) {
        if (!szRaw) return '—';
        var parts = szRaw.split('|');
        switch (parts[0]) {
            case 'ok':      return t('weathersetting.msg.ok',      { temp: parts[1], hum: parts[2], time: parts[3] });
            case 'missing': return t('weathersetting.msg.missing', { temp: parts[1], hum: parts[2], time: parts[3] });
            case 'stale':   return t('weathersetting.msg.stale',   { time: parts[1] });
            case 'error':   return t('weathersetting.msg.error',   { detail: parts.slice(1).join('|') });
            case 'station_not_found':      return t('weathersetting.msg.station_not_found');
            case 'coordinator_not_loaded': return t('weathersetting.msg.coordinator_not_loaded');
            case 'disabled':               return t('weathersetting.msg.disabled');
            default: return szRaw;
        }
    }

    // ── 測站清單 ────────────────────────────────────────────
    function loadStations() {
        var szApiKey = el('txtApiKey').value.trim();
        if (!szApiKey) { toast(t('weathersetting.err.api_key_required'), true); return; }

        var btn = el('btnLoadStations');
        btn.disabled = true;
        fetch('/WeatherSetting/api/stations', {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ apiKey: szApiKey })
        })
            .then(function (r) { return r.json().then(function (j) { return { ok: r.ok, json: j }; }); })
            .then(function (res) {
                if (!res.ok) { toast(apiErrorMessage(res.json), true); return; }
                stations = res.json.stations || [];
                buildCountySelect();
                toast(t('weathersetting.toast.stations_loaded', { count: stations.length }), false);
            })
            .catch(function () { toast(t('weathersetting.err.cwa_unreachable'), true); })
            .finally(function () { btn.disabled = false; });
    }

    function buildCountySelect() {
        var counties = [];
        stations.forEach(function (s) {
            if (s.county && counties.indexOf(s.county) < 0) counties.push(s.county);
        });
        counties.sort(function (a, b) { return a.localeCompare(b, 'zh-Hant'); });

        var sel = el('selCounty');
        sel.innerHTML = '<option value="">' + escapeHtml(t('weathersetting.opt.select_county')) + '</option>' +
            counties.map(function (c) {
                return '<option value="' + escapeHtml(c) + '">' + escapeHtml(c) + '</option>';
            }).join('');
        sel.disabled = false;

        // 已存設定 → 預選縣市 + 測站
        if (savedSetting && savedSetting.county && counties.indexOf(savedSetting.county) >= 0) {
            sel.value = savedSetting.county;
            buildStationSelect(savedSetting.county, savedSetting.datasetId + '|' + savedSetting.stationId);
        } else {
            buildStationSelect('', null);
        }
    }

    function buildStationSelect(szCounty, szPreselect) {
        var sel = el('selStation');
        var list = stations.filter(function (s) { return s.county === szCounty; });

        sel.innerHTML = '<option value="">' + escapeHtml(t('weathersetting.opt.select_station')) + '</option>' +
            list.map(function (s) {
                var szType = s.datasetId === 'O-A0003-001'
                    ? t('weathersetting.dataset.staffed')
                    : t('weathersetting.dataset.auto');
                var szLabel = s.stationName + (s.town ? '（' + s.town + '）' : '') +
                    ' — ' + szType + ' ' + s.stationId;
                return '<option value="' + escapeHtml(s.datasetId + '|' + s.stationId) + '">' +
                    escapeHtml(szLabel) + '</option>';
            }).join('');
        sel.disabled = list.length === 0 && !szCounty;

        if (szPreselect) sel.value = szPreselect; // 選不到就留在空白 option
    }

    /** 取表單當下選擇的測站（未載清單時 fallback 已存設定） */
    function currentSelection() {
        var sel = el('selStation');
        if (sel && sel.value) {
            var parts = sel.value.split('|');
            var s = stations.filter(function (x) {
                return x.datasetId === parts[0] && x.stationId === parts[1];
            })[0];
            return {
                datasetId: parts[0],
                stationId: parts[1],
                stationName: s ? s.stationName : '',
                county: s ? s.county : (el('selCounty').value || '')
            };
        }
        if (savedSetting && savedSetting.stationId) {
            return {
                datasetId: savedSetting.datasetId,
                stationId: savedSetting.stationId,
                stationName: savedSetting.stationName,
                county: savedSetting.county
            };
        }
        return null;
    }

    // ── 測試連線 ────────────────────────────────────────────
    function test() {
        var szApiKey = el('txtApiKey').value.trim();
        var selection = currentSelection();
        if (!szApiKey || !selection) { toast(t('weathersetting.err.test_requires_key_and_station'), true); return; }

        var btn = el('btnTest');
        var div = el('testResult');
        btn.disabled = true;
        div.style.display = '';
        div.innerHTML = '<span class="text-muted small"><span class="spinner-border spinner-border-sm me-1"></span>' +
            escapeHtml(t('weathersetting.testing')) + '</span>';

        fetch('/WeatherSetting/api/test', {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ apiKey: szApiKey, datasetId: selection.datasetId, stationId: selection.stationId })
        })
            .then(function (r) { return r.json().then(function (j) { return { ok: r.ok, json: j }; }); })
            .then(function (res) {
                if (!res.ok) {
                    div.innerHTML = '<div class="alert alert-danger py-2 mb-0 small">' +
                        escapeHtml(apiErrorMessage(res.json)) + '</div>';
                    return;
                }
                if (!res.json.success) {
                    div.innerHTML = '<div class="alert alert-warning py-2 mb-0 small">' +
                        escapeHtml(t('weathersetting.msg.station_not_found')) + '</div>';
                    return;
                }
                var szTemp = res.json.temperature != null ? res.json.temperature : '—';
                var szHum = res.json.humidity != null ? res.json.humidity : '—';
                div.innerHTML = '<div class="alert alert-success py-2 mb-0 small">' +
                    escapeHtml(t('weathersetting.test_result', {
                        name: res.json.stationName, temp: szTemp, hum: szHum, time: res.json.obsTime || '—'
                    })) + '</div>';
            })
            .catch(function () {
                div.innerHTML = '<div class="alert alert-danger py-2 mb-0 small">' +
                    escapeHtml(t('weathersetting.err.cwa_unreachable')) + '</div>';
            })
            .finally(function () { btn.disabled = false; });
    }

    // ── 儲存 ────────────────────────────────────────────────
    function save() {
        var selection = currentSelection();
        var payload = {
            apiKey: el('txtApiKey').value.trim(),
            datasetId: selection ? selection.datasetId : '',
            stationId: selection ? selection.stationId : '',
            stationName: selection ? selection.stationName : '',
            county: selection ? selection.county : '',
            pollIntervalMinutes: parseInt(el('numInterval').value, 10) || 0,
            isEnabled: el('chkEnabled').checked
        };

        if (payload.isEnabled && (!payload.apiKey || !payload.stationId)) {
            toast(t('weathersetting.err.enable_requires_key_and_station'), true);
            return;
        }

        var btn = el('btnSave');
        btn.disabled = true;
        fetch('/WeatherSetting/api/setting', {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        })
            .then(function (r) { return r.json().then(function (j) { return { ok: r.ok, json: j }; }); })
            .then(function (res) {
                if (!res.ok) { toast(apiErrorMessage(res.json), true); return; }
                toast(t('weathersetting.toast.saved'), false);
                return loadSetting();
            })
            .catch(function () { toast(t('weathersetting.err.save_failed'), true); })
            .finally(function () { btn.disabled = false; });
    }

    // ── init ────────────────────────────────────────────────
    function init() {
        el('selCounty').addEventListener('change', function () {
            buildStationSelect(this.value, null);
        });
        loadSetting();
        setInterval(refreshStatus, 30000);
    }

    if (window.i18n && window.i18n.ready) {
        window.i18n.ready(init);
    } else if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    window._weatherSetting = {
        save: save,
        test: test,
        loadStations: loadStations
    };
})();
