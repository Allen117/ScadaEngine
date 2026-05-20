/*
 * SCADA Web i18n helper
 * 提供 window.i18n.t(key, args?) / currentCulture() / setCulture(culture)
 *
 * 使用流程：
 * 1. _Layout.cshtml 在 <head> 內 inline 預設 culture（避免 fetch 前找不到）
 * 2. 頁面 load 時 fetch /api/i18n/{culture} 取整份 key/value 字典
 * 3. JS 字串改成 window.i18n.t('key', { name: 'X' }) — 會做 {name} placeholder 替換
 * 4. 漏 key fallback：先嘗試 zh-TW 字典；都查無 → 回傳 key 本身（讓開發者知道該補）
 *
 * 切換語系：window.i18n.setCulture('en') → POST /api/i18n/set-culture → reload 整頁
 */
(function () {
    'use strict';

    var COOKIE_NAME = '.AspNetCore.Culture';
    var SUPPORTED = ['zh-TW', 'en'];
    var DEFAULT_CULTURE = 'zh-TW';

    var _dict = {};            // 主語系字典
    var _fallbackDict = {};    // zh-TW fallback（當 _dict 缺 key 時查）
    var _culture = DEFAULT_CULTURE;
    var _ready = false;
    var _readyCallbacks = [];
    var _cultureChangeCallbacks = [];

    function readCookie(name) {
        var aPair = document.cookie.split(';');
        for (var i = 0; i < aPair.length; i++) {
            var p = aPair[i].trim();
            if (p.indexOf(name + '=') === 0) return decodeURIComponent(p.substring(name.length + 1));
        }
        return null;
    }

    /**
     * 從 cookie 解析 culture（格式 "c=zh-TW|uic=zh-TW"）
     */
    function detectCulture() {
        // 1. <html lang="..."> 由 server 渲染，最準確
        var szLang = document.documentElement.getAttribute('lang');
        if (szLang && SUPPORTED.indexOf(szLang) >= 0) return szLang;

        // 2. cookie fallback
        var szCookie = readCookie(COOKIE_NAME);
        if (szCookie) {
            var m = szCookie.match(/uic=([^|]+)/);
            if (m && SUPPORTED.indexOf(m[1]) >= 0) return m[1];
        }
        return DEFAULT_CULTURE;
    }

    /**
     * fetch 字典並儲存到 _dict / _fallbackDict
     */
    function fetchDict(culture) {
        return fetch('/api/i18n/' + encodeURIComponent(culture), { credentials: 'same-origin' })
            .then(function (r) {
                if (!r.ok) throw new Error('i18n fetch failed: ' + r.status);
                return r.json();
            })
            .then(function (json) {
                return json && json.keys ? json.keys : {};
            });
    }

    /**
     * 初始化：偵測 culture、fetch 主字典與 fallback 字典
     */
    function init() {
        _culture = detectCulture();
        var aPromise = [fetchDict(_culture).then(function (d) { _dict = d || {}; })];
        if (_culture !== DEFAULT_CULTURE) {
            aPromise.push(fetchDict(DEFAULT_CULTURE).then(function (d) { _fallbackDict = d || {}; }));
        }
        return Promise.all(aPromise).then(function () {
            _ready = true;
            _readyCallbacks.forEach(function (cb) { try { cb(); } catch (e) { console.error(e); } });
            _readyCallbacks = [];
        }).catch(function (err) {
            console.error('[i18n] init failed:', err);
            _ready = true; // 仍然標記 ready 否則 UI 會卡住
        });
    }

    /**
     * 翻譯 key，args 為 { name: value } 形式做 {name} 樣板替換
     * 漏 key 時：先 fallback 到 zh-TW 字典；再缺則回傳 key 本身
     */
    function t(szKey, args) {
        if (!szKey) return '';
        var szValue = _dict[szKey];
        if (szValue === undefined) szValue = _fallbackDict[szKey];
        if (szValue === undefined) {
            if (window.console && console.warn) console.warn('[i18n] missing key:', szKey);
            return szKey;
        }
        if (args && typeof args === 'object') {
            for (var k in args) {
                if (Object.prototype.hasOwnProperty.call(args, k)) {
                    szValue = szValue.split('{' + k + '}').join(args[k] == null ? '' : String(args[k]));
                    // 同時支援 {0} {1}（C# 風格）— 用 args[0]/args[1] 也填一遍
                    szValue = szValue.split('{' + k + '}').join(args[k] == null ? '' : String(args[k]));
                }
            }
        }
        return szValue;
    }

    function currentCulture() {
        return _culture;
    }

    function isReady() { return _ready; }

    /**
     * 等待 init() 完成；已 ready 則直接執行 cb
     */
    function ready(cb) {
        if (_ready) cb();
        else _readyCallbacks.push(cb);
    }

    /**
     * 訂閱語系切換事件（reload 後新頁面載入時觸發 ready）
     * 註：這個 callback 只在 setCulture 呼叫前後同一頁有效，多用於 chart 重建
     */
    function onCultureChange(cb) {
        _cultureChangeCallbacks.push(cb);
    }

    /**
     * 切換語系：POST /api/i18n/set-culture，server 寫 cookie 後前端 reload
     */
    function setCulture(culture) {
        if (SUPPORTED.indexOf(culture) < 0) culture = DEFAULT_CULTURE;
        if (culture === _culture) return Promise.resolve();

        return fetch('/api/i18n/set-culture?culture=' + encodeURIComponent(culture), {
            method: 'POST',
            credentials: 'same-origin'
        }).then(function () {
            _cultureChangeCallbacks.forEach(function (cb) { try { cb(culture); } catch (e) { console.error(e); } });
            window.location.reload();
        });
    }

    window.i18n = {
        init: init,
        t: t,
        currentCulture: currentCulture,
        setCulture: setCulture,
        ready: ready,
        isReady: isReady,
        onCultureChange: onCultureChange,
        SUPPORTED: SUPPORTED.slice()
    };

    // 自動初始化
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
