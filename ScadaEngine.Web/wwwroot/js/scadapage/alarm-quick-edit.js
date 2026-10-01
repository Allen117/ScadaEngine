// ============================================================
// scadapage/alarm-quick-edit.js — 右鍵「警報設定…」就地彈窗（Admin / Engineer）
// ============================================================
// 不包 IIFE、global scope，與其餘 js/scadapage/ 模組同模式。
// 表單本體與存檔 / 刪除 / 驗證走共用 window.AlarmRuleForm（js/common/alarm-rule-form.js），
// 與 AlarmSetting 頁同一份 partial（_AlarmRuleForm.cshtml），本檔只負責：
// 依 SID 載入既有規則、前置點位候選清單、DI 標籤、存檔後刷新 ScadaPage 著色規則。
// ============================================================

    var _aqModal         = null;
    var _aqSid           = '';
    var _aqRuleId        = null;
    var _aqPointsPromise = null;   // 點位清單只灌一次（前置點位名稱反查 / 驗證；資料與選點器共用 PointPicker.load）

    function _aqEnsurePoints() {
        if (_aqPointsPromise) return _aqPointsPromise;
        _aqPointsPromise = window.PointPicker.load()
            .then(function (data) {
                window.AlarmRuleForm.setPoints((data.points || []).map(function (p) {
                    return { sid: p.szSid, name: p.szName, unit: p.szUnit };
                }));
            })
            .catch(function () { _aqPointsPromise = null; });   // 失敗下次重試
        return _aqPointsPromise;
    }

    // DI ON/OFF 標籤以本頁 Designer 設定為準（diPoint widget 或表格 DI cell）
    function _aqDiLabels(sid) {
        var szSel = '[data-sid="' + (window.CSS && CSS.escape ? CSS.escape(sid) : sid) + '"]';
        var el = document.querySelector('.scada-di-point' + szSel)
              || document.querySelector('td' + szSel + '[data-sz-on-label]');
        return {
            on:  (el && el.dataset.szOnLabel)  || 'ON',
            off: (el && el.dataset.szOffLabel) || 'OFF'
        };
    }

    async function _openAlarmQuickEdit(info) {
        var modalEl = document.getElementById('scadaAlarmModal');
        if (!window.AlarmRuleForm || !modalEl) return;
        _aqSid = info.sid;
        _aqRuleId = null;

        await _aqEnsurePoints();
        var rule = null;
        try {
            var resp = await fetch('/api/alarm-rules/by-sid/' + encodeURIComponent(info.sid));
            if (resp.ok) {
                var json = await resp.json();
                rule = window.AlarmRuleForm.fromModel(json.rule);
            }
        } catch (_) { /* 載入失敗視為無規則（新增） */ }

        window.AlarmRuleForm.setSelfSid(info.sid);
        if (rule) {
            window.AlarmRuleForm.fill(rule);
            _aqRuleId = rule.id;
        } else {
            window.AlarmRuleForm.reset();
        }
        var lbl = _aqDiLabels(info.sid);
        window.AlarmRuleForm.setDiLabels(lbl.on, lbl.off);

        document.getElementById('scadaAlarmModalTitle').textContent =
            t(rule ? 'scadapage.alarm_edit.title_edit' : 'scadapage.alarm_edit.title_add');
        document.getElementById('scadaAlarmPointLabel').textContent = info.name;
        document.getElementById('btnScadaAlarmDelete').style.display = rule ? '' : 'none';

        if (!_aqModal) _aqModal = new bootstrap.Modal(modalEl);
        _aqModal.show();
    }

    function _alarmQuickSave() {
        var dto = window.AlarmRuleForm.read(_aqSid, _aqRuleId);
        var szErr = window.AlarmRuleForm.validate(dto);
        if (szErr) { alert(szErr); return; }
        window.AlarmRuleForm.save(dto)
            .then(function (res) {
                if (!res.success) { alert(res.message || t('scadapage.alarm_edit.save_failed')); return; }
                _aqModal.hide();
                _loadAlarmRules();   // 著色規則即時更新
                showControlToast(t('scadapage.alarm_edit.saved'));
            })
            .catch(function (e) { alert(t('scadapage.alarm_edit.save_failed') + ': ' + e.message); });
    }

    function _alarmQuickDelete() {
        if (!_aqRuleId) return;
        if (!confirm(t('scadapage.alarm_edit.confirm_delete'))) return;
        window.AlarmRuleForm.remove(_aqRuleId)
            .then(function (res) {
                if (!res.success) { alert(res.message || t('scadapage.alarm_edit.delete_failed')); return; }
                _aqModal.hide();
                _loadAlarmRules();
                showControlToast(t('scadapage.alarm_edit.deleted'));
            })
            .catch(function (e) { alert(t('scadapage.alarm_edit.delete_failed') + ': ' + e.message); });
    }
