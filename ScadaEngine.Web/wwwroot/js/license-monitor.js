(function () {
    'use strict';

    var POLL_INTERVAL_MS = 30000;
    var _pollTimer = null;

    function init() {
        poll();
        _pollTimer = setInterval(poll, POLL_INTERVAL_MS);
    }

    function poll() {
        fetch('/api/license/status')
            .then(function (r) { return r.json(); })
            .then(function (data) { applyStatus(data.valid); })
            .catch(function () {
                // MQTT broker 斷線或 Web API 無回應 → 不改變現有顯示
            });
    }

    function applyStatus(isValid) {
        var banner = document.getElementById('license-warning-banner');
        if (!banner) return;
        if (isValid) {
            banner.style.display = 'none';
        } else {
            banner.style.display = 'flex';
        }
    }

    window._licenseMonitor = {
        triggerVerify: function () {
            var btn = document.getElementById('license-verify-btn');
            if (btn) {
                btn.disabled = true;
                btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>驗證中...';
            }

            fetch('/api/license/verify', { method: 'POST' })
                .then(function () {
                    // 5 秒後自動恢復按鈕，並立即 poll 一次
                    setTimeout(function () {
                        if (btn) {
                            btn.disabled = false;
                            btn.innerHTML = '<i class="fas fa-sync-alt me-1"></i>立即重新驗證';
                        }
                        poll();
                    }, 5000);
                })
                .catch(function () {
                    if (btn) {
                        btn.disabled = false;
                        btn.innerHTML = '<i class="fas fa-sync-alt me-1"></i>立即重新驗證';
                    }
                });
        }
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
