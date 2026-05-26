(function () {
    'use strict';

    function t(key, args) { return (window.i18n && window.i18n.t) ? window.i18n.t(key, args) : key; }

    var coordinators = window._dbCoordData || [];

    function escapeHtml(s) {
        if (s == null) return '';
        return String(s)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    function clearAllActive() {
        document.querySelectorAll('.coordinator-item')
            .forEach(function (el) { el.classList.remove('active'); });
    }

    function showDetail(index) {
        var c = coordinators[index];
        if (!c) return;

        document.getElementById('emptyDetail').style.display = 'none';
        document.getElementById('fullDetail').style.display = '';
        document.getElementById('detailTitle').innerHTML =
            '<i class="fas fa-info-circle me-1"></i>' + t('dbcoord.card.device_detail') + ' — ' + escapeHtml(c.name || '');

        document.getElementById('fieldId').value              = c.id;
        document.getElementById('fieldName').value            = c.name || '';
        document.getElementById('fieldPollingInterval').value = c.pollingInterval;
        document.getElementById('fieldConnectTimeout').value  = c.connectTimeout;
        document.getElementById('fieldMonitorEnabled').value  = c.monitorEnabled ? t('dbcoord.value.yes') : t('dbcoord.value.no');
        document.getElementById('fieldPointCount').value      = (c.points || []).length;
    }

    function renderSidebar() {
        var container = document.getElementById('dbCoordinatorList');
        if (!container) return;

        if (!coordinators.length) {
            container.innerHTML = '<div class="text-center text-muted py-4">' +
                '<i class="fas fa-inbox fa-2x mb-2 d-block"></i>' +
                escapeHtml(t('dbcoord.empty.no_source')) +
            '</div>';
            var empty = document.getElementById('emptyDetailMsg');
            if (empty) empty.textContent = t('dbcoord.empty.not_loaded');
            return;
        }

        var disabledBadge = '<span class="badge bg-secondary ms-1" style="font-size:.6rem;">' + escapeHtml(t('dbcoord.badge.disabled')) + '</span>';
        var html = coordinators.map(function (c, i) {
            var badge = c.monitorEnabled ? '' : disabledBadge;
            return '<a href="#" class="list-group-item list-group-item-action py-2 coordinator-item" data-index="' + i + '">' +
                '<i class="fas fa-database me-1 text-secondary"></i>' +
                '<span class="small">' + escapeHtml(c.name) + '</span>' +
                badge +
            '</a>';
        }).join('');

        container.innerHTML = html;

        container.querySelectorAll('.coordinator-item').forEach(function (item) {
            item.addEventListener('click', function (e) {
                e.preventDefault();
                clearAllActive();
                this.classList.add('active');
                showDetail(parseInt(this.dataset.index));
            });
        });

        // 預設載入第一筆
        var firstItem = container.querySelector('.coordinator-item');
        if (firstItem) {
            firstItem.classList.add('active');
            showDetail(0);
        }
    }

    function refreshPage() {
        window.location.reload();
    }

    function reloadEngine() {
        var btn = document.getElementById('btnReloadEngine');
        if (btn) {
            btn.disabled = true;
            btn.innerHTML = '<i class="fas fa-spinner fa-spin me-1"></i>' + escapeHtml(t('dbcoord.btn.notifying'));
        }

        fetch('/DbCoordinator/Reload', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' }
        })
            .then(function (r) { return r.json(); })
            .then(function (data) {
                var msg = data.message || (data.success ? t('dbcoord.msg.success_default') : t('dbcoord.msg.failure_default'));
                alert(msg);
                if (data.success) {
                    setTimeout(function () { window.location.reload(); }, 1500);
                }
            })
            .catch(function (err) {
                alert(t('dbcoord.msg.call_failed_with', { error: err.message }));
            })
            .finally(function () {
                if (btn) {
                    btn.disabled = false;
                    btn.innerHTML = '<i class="fas fa-bolt me-1"></i>' + escapeHtml(t('dbcoord.btn.reload_engine'));
                }
            });
    }

    window._dbCoord = {
        refreshPage: refreshPage,
        reloadEngine: reloadEngine
    };

    // 等 i18n 字典載入完再 render，避免 sidebar 空狀態出現 key 名而非翻譯
    if (window.i18n && window.i18n.ready) {
        window.i18n.ready(function () {
            if (document.readyState === 'loading') {
                document.addEventListener('DOMContentLoaded', renderSidebar);
            } else {
                renderSidebar();
            }
        });
    } else {
        document.addEventListener('DOMContentLoaded', renderSidebar);
    }
})();
