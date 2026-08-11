// 能源日報設定頁 — 寄送設定 / 內容區塊 / 收件人清單 / 測試寄送
(function () {
    'use strict';

    document.addEventListener('DOMContentLoaded', function () {
        loadSetting();
        loadRecipients();
    });

    function $(id) { return document.getElementById(id); }

    // ── 設定 ──
    function loadSetting() {
        fetch('/DailyReport/api/setting')
            .then(function (res) { return res.json(); })
            .then(function (setting) {
                $('drsMailEnabled').checked = !!setting.isMailEnabled;
                $('drsLanguage').value = setting.szLanguage || 'zh-TW';
                $('drsThreshold').value = setting.dDiffThresholdPercent;
                $('drsHolidayHint').checked = !!setting.isHolidayHintEnabled;
                document.querySelectorAll('.dr-section-flag').forEach(function (el) {
                    el.checked = (setting.nSectionFlags & parseInt(el.value, 10)) !== 0;
                });
            })
            .catch(function (err) { showToast('設定載入失敗：' + err.message, 'danger'); });
    }

    function saveSetting() {
        var nFlags = 0;
        document.querySelectorAll('.dr-section-flag').forEach(function (el) {
            if (el.checked) nFlags |= parseInt(el.value, 10);
        });
        var setting = {
            nId: 1,
            isMailEnabled: $('drsMailEnabled').checked,
            szLanguage: $('drsLanguage').value,
            dDiffThresholdPercent: parseFloat($('drsThreshold').value) || 15,
            isHolidayHintEnabled: $('drsHolidayHint').checked,
            nSectionFlags: nFlags
        };
        fetch('/DailyReport/api/setting', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(setting)
        })
            .then(handleJson)
            .then(function () { showToast('設定已儲存', 'success'); })
            .catch(function (err) { showToast('儲存失敗：' + err.message, 'danger'); });
    }

    // ── 收件人 ──
    function loadRecipients() {
        fetch('/DailyReport/api/recipients')
            .then(function (res) { return res.json(); })
            .then(renderRecipients)
            .catch(function (err) { showToast('收件人載入失敗：' + err.message, 'danger'); });
    }

    function renderRecipients(list) {
        var body = $('drsRecipientBody');
        if (!list || list.length === 0) {
            body.innerHTML = '<tr><td colspan="4" class="text-center text-muted py-3">尚無收件人</td></tr>';
            return;
        }
        var html = '';
        list.forEach(function (r) {
            html += '<tr class="' + (r.isEnabled ? '' : 'text-muted') + '">' +
                '<td>' + escapeHtml(r.szEmailAddress) + '</td>' +
                '<td>' + escapeHtml(r.szDisplayName || '') + '</td>' +
                '<td class="text-center">' +
                '<div class="form-check form-switch d-inline-block"><input class="form-check-input" type="checkbox" ' +
                (r.isEnabled ? 'checked' : '') + ' onchange="window._drs.toggleRecipient(' + r.nId + ')"></div></td>' +
                '<td class="text-center"><button class="btn btn-sm btn-outline-danger" onclick="window._drs.deleteRecipient(' + r.nId + ')">' +
                '<i class="fas fa-trash-alt"></i></button></td>' +
                '</tr>';
        });
        body.innerHTML = html;
    }

    function addRecipient() {
        var szEmail = $('drsNewEmail').value.trim();
        if (!szEmail) { showToast('請輸入 Email', 'danger'); return; }
        fetch('/DailyReport/api/recipients', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ nId: 0, szEmailAddress: szEmail, szDisplayName: $('drsNewName').value.trim() || null, isEnabled: true })
        })
            .then(handleJson)
            .then(function () {
                $('drsNewEmail').value = '';
                $('drsNewName').value = '';
                showToast('收件人已新增', 'success');
                loadRecipients();
            })
            .catch(function (err) { showToast('新增失敗：' + err.message, 'danger'); });
    }

    function toggleRecipient(nId) {
        fetch('/DailyReport/api/recipients/' + nId + '/toggle', { method: 'POST' })
            .then(handleJson)
            .then(loadRecipients)
            .catch(function (err) { showToast('切換失敗：' + err.message, 'danger'); loadRecipients(); });
    }

    function deleteRecipient(nId) {
        if (!confirm('確定刪除此收件人？')) return;
        fetch('/DailyReport/api/recipients/' + nId, { method: 'DELETE' })
            .then(handleJson)
            .then(function () { showToast('已刪除', 'success'); loadRecipients(); })
            .catch(function (err) { showToast('刪除失敗：' + err.message, 'danger'); });
    }

    // ── 測試寄送 ──
    function testSend() {
        var btn = $('drsTestBtn');
        btn.disabled = true;
        btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>寄送中…';
        fetch('/DailyReport/api/test-send', { method: 'POST' })
            .then(handleJson)
            .then(function (result) { showToast(result.message || '測試寄送完成', 'success'); })
            .catch(function (err) { showToast('測試寄送失敗：' + err.message, 'danger'); })
            .finally(function () {
                btn.disabled = false;
                btn.innerHTML = '<i class="fas fa-paper-plane me-1"></i>測試寄送';
            });
    }

    // ── helpers ──
    function handleJson(res) {
        return res.json().catch(function () { return {}; }).then(function (json) {
            if (!res.ok || json.success === false) throw new Error(json.message || res.statusText);
            return json;
        });
    }

    function showToast(szMessage, szKind) {
        var el = document.createElement('div');
        el.className = 'alert alert-' + (szKind || 'success') + ' position-fixed shadow';
        el.style.cssText = 'top:80px;right:20px;z-index:9999;min-width:220px;';
        el.textContent = szMessage;
        document.body.appendChild(el);
        setTimeout(function () { el.remove(); }, 3500);
    }

    function escapeHtml(sz) {
        var div = document.createElement('div');
        div.textContent = sz == null ? '' : sz;
        return div.innerHTML;
    }

    window._drs = {
        saveSetting: saveSetting,
        addRecipient: addRecipient,
        toggleRecipient: toggleRecipient,
        deleteRecipient: deleteRecipient,
        testSend: testSend
    };
})();
