// ============================================================
// common/alarm-rule-form.js — 警報規則表單共用邏輯（window.AlarmRuleForm）
// 搭配 Features/AlarmSetting/Views/_AlarmRuleForm.cshtml；AlarmSetting 頁與 ScadaPage 右鍵警報彈窗共用。
// 一頁一份表單（以 id 定位）。點位選擇不在本模組，由各頁自行處理後把 SID 傳進來。
//
// 規則物件（view shape，camelCase）：
//   { id, sid, isEnabled, isAlarmHigh, alarmHighValue, deadbandHigh, alarmHighSeverity,
//     isAlarmLow, alarmLowValue, deadbandLow, alarmLowSeverity,
//     isDiAlarm, diTriggerState, diAlarmSeverity, diOnLabel, diOffLabel, remarks,
//     isPrecondition, preSid, preOperator, preValue, preDelaySec,
//     recoveryNotifyLine, recoveryNotifyEmail, recoveryNotifySms, alarmDelaySec }
// /api/alarm-rules 回傳的 AlarmRuleModel（Hungarian）請先經 fromModel() 轉換。
// ============================================================
(function () {
    'use strict';

    function t(key, args) { return (window.i18n && window.i18n.t) ? window.i18n.t(key, args) : key; }
    function $(id) { return document.getElementById(id); }

    var _points = [];          // [{ sid, name, unit }] — 前置點位候選
    var _pointNameMap = {};    // sid → name
    var _szSelfSid = '';       // 本點 SID（前置點位不可選自己）

    // 需量 / 迴路用電虛擬點位：平時不進 Engine 警報評估器，不可作為前置條件
    function isVirtualSid(sid) {
        return /^(DMD|NRGD|NRGM|NRGP)-/i.test(sid || '');
    }

    /** 設定點位清單（points: [{ sid, name, unit }]）— 前置點位 SID → 名稱反查與驗證用 */
    function setPoints(points) {
        _points = (points || []).filter(function (p) { return p && p.sid && !isVirtualSid(p.sid); });
        _pointNameMap = {};
        _points.forEach(function (p) { _pointNameMap[p.sid] = p.name || p.sid; });
        onPreSidInput();
    }

    function pointName(sid) { return _pointNameMap[sid] || ''; }

    /** 設定本點 SID（選點器排除自己）；DI 標籤另由 setDiLabels 帶入 */
    function setSelfSid(sid) {
        _szSelfSid = sid || '';
    }

    /** 開共用選點器（比照 Designer 綁定點位）選前置點位；排除本點與 DMD-/NRG* 虛擬點 */
    function pickPrePoint() {
        if (!window.PointPicker) return;
        window.PointPicker.open({
            excludeSid: _szSelfSid,
            filter: function (sid) { return !isVirtualSid(sid); }
        }).then(function (p) {
            if (!p) return;
            if (!_pointNameMap[p.sid]) _pointNameMap[p.sid] = p.name;
            $('txtPreSid').value = p.sid;
            onPreSidInput();
        });
    }

    function setDiLabels(onLabel, offLabel) {
        $('txtDiOnLabel').value = onLabel || 'ON';
        $('txtDiOffLabel').value = offLabel || 'OFF';
    }

    // 前置點位顯示：只顯示點位名稱（不帶 SID）；未選 → 灰字提示；清單查無 → 紅字「找不到此點位」
    function onPreSidInput() {
        var el = $('arfPrePointName');
        if (!el) return;
        var sid = $('txtPreSid').value.trim();
        var name = sid ? pointName(sid) : '';
        var isMissing = !!sid && !name && _points.length > 0;
        el.textContent = !sid ? t('arf.pre_point.none') : (name || (isMissing ? t('arf.pre_point.not_found') : sid));
        el.classList.toggle('text-muted', !sid);
        el.classList.toggle('text-danger', isMissing);
    }

    function toggleSection(type) {
        var isChecked;
        var ids;
        if (type === 'high') {
            isChecked = $('chkAlarmHigh').checked;
            ids = ['secHighValue', 'secHighDeadband', 'secHighSeverity'];
        } else if (type === 'low') {
            isChecked = $('chkAlarmLow').checked;
            ids = ['secLowValue', 'secLowDeadband', 'secLowSeverity'];
        } else if (type === 'di') {
            isChecked = $('chkDiAlarm').checked;
            ids = ['secDiTrigger', 'secDiSeverity', 'secDiLabels'];
        } else {
            isChecked = $('chkPrecondition').checked;
            ids = ['secPrecondition'];
            var op = $('selPreOperator').value;
            $('secPreValue').style.display = (op === 'ON' || op === 'OFF') ? 'none' : '';
        }
        ids.forEach(function (id) {
            $(id).style.display = isChecked ? '' : 'none';
        });
    }

    function toggleAll() {
        toggleSection('high');
        toggleSection('low');
        toggleSection('di');
        toggleSection('pre');
    }

    // 恢復通知勾選可能不在頁面上（ScadaPage 彈窗隱藏）→ 以載入規則的值為準，存檔時原樣帶回
    var _recovery = { line: true, email: true, sms: true };
    function setChecked(id, v) { var el = $(id); if (el) el.checked = v; }
    function readChecked(id, fallback) { var el = $(id); return el ? el.checked : fallback; }

    /** 回到新增預設值 */
    function reset() {
        _recovery = { line: true, email: true, sms: true };
        $('chkAlarmHigh').checked = false;
        $('txtAlarmHighValue').value = '';
        $('txtDeadbandHigh').value = '0';
        $('selHighSeverity').value = '1';
        $('chkAlarmLow').checked = false;
        $('txtAlarmLowValue').value = '';
        $('txtDeadbandLow').value = '0';
        $('selLowSeverity').value = '1';
        $('chkDiAlarm').checked = false;
        $('selDiTrigger').value = 'ON';
        $('selDiSeverity').value = '1';
        setDiLabels('ON', 'OFF');
        $('txtAlarmDelaySec').value = '0';
        $('chkPrecondition').checked = false;
        $('txtPreSid').value = '';
        $('selPreOperator').value = 'GE';
        $('txtPreValue').value = '';
        $('txtPreDelaySec').value = '0';
        setChecked('chkRecoveryNotifyLine', true);
        setChecked('chkRecoveryNotifyEmail', true);
        setChecked('chkRecoveryNotifySms', true);
        $('txtRemarks').value = '';
        $('chkEnabled').checked = true;
        onPreSidInput();
        toggleAll();
    }

    function valOrEmpty(v) { return v != null ? v : ''; }

    /** 以規則物件（view shape）填表；DI 標籤不在此處理（以 Designer 設定為準，呼叫端 setDiLabels） */
    function fill(rule) {
        reset();
        if (!rule) return;
        $('chkAlarmHigh').checked = !!rule.isAlarmHigh;
        $('txtAlarmHighValue').value = valOrEmpty(rule.alarmHighValue);
        $('txtDeadbandHigh').value = rule.deadbandHigh != null ? rule.deadbandHigh : 0;
        $('selHighSeverity').value = rule.alarmHighSeverity != null ? rule.alarmHighSeverity : 1;
        $('chkAlarmLow').checked = !!rule.isAlarmLow;
        $('txtAlarmLowValue').value = valOrEmpty(rule.alarmLowValue);
        $('txtDeadbandLow').value = rule.deadbandLow != null ? rule.deadbandLow : 0;
        $('selLowSeverity').value = rule.alarmLowSeverity != null ? rule.alarmLowSeverity : 1;
        $('chkDiAlarm').checked = !!rule.isDiAlarm;
        $('selDiTrigger').value = rule.diTriggerState || 'ON';
        $('selDiSeverity').value = rule.diAlarmSeverity != null ? rule.diAlarmSeverity : 1;
        $('txtAlarmDelaySec').value = rule.alarmDelaySec || 0;
        $('chkPrecondition').checked = !!rule.isPrecondition;
        $('txtPreSid').value = rule.preSid || '';
        $('selPreOperator').value = rule.preOperator || 'GE';
        $('txtPreValue').value = valOrEmpty(rule.preValue);
        $('txtPreDelaySec').value = rule.preDelaySec || 0;
        _recovery = {
            line:  rule.recoveryNotifyLine !== false,
            email: rule.recoveryNotifyEmail !== false,
            sms:   rule.recoveryNotifySms !== false
        };
        setChecked('chkRecoveryNotifyLine', _recovery.line);
        setChecked('chkRecoveryNotifyEmail', _recovery.email);
        setChecked('chkRecoveryNotifySms', _recovery.sms);
        $('txtRemarks').value = rule.remarks || '';
        $('chkEnabled').checked = rule.isEnabled !== false;
        onPreSidInput();
        toggleAll();
    }

    function parseNum(id) {
        var v = parseFloat($(id).value);
        return isNaN(v) ? null : v;
    }
    function parseIntOr0(id) {
        var v = parseInt($(id).value, 10);
        return isNaN(v) ? 0 : v;
    }

    /** 讀表單 → 存檔 DTO（AlarmRuleSaveDto） */
    function read(sid, id) {
        var isHigh = $('chkAlarmHigh').checked;
        var isLow = $('chkAlarmLow').checked;
        var isDi = $('chkDiAlarm').checked;
        var isPre = $('chkPrecondition').checked;
        var preOp = $('selPreOperator').value;
        return {
            id: id ? parseInt(id, 10) : null,
            sid: sid,
            isEnabled: $('chkEnabled').checked,
            isAlarmHigh: isHigh,
            alarmHighValue: isHigh ? parseNum('txtAlarmHighValue') : null,
            deadbandHigh: isHigh ? (parseNum('txtDeadbandHigh') || 0) : 0,
            alarmHighSeverity: isHigh ? parseInt($('selHighSeverity').value, 10) : 1,
            isAlarmLow: isLow,
            alarmLowValue: isLow ? parseNum('txtAlarmLowValue') : null,
            deadbandLow: isLow ? (parseNum('txtDeadbandLow') || 0) : 0,
            alarmLowSeverity: isLow ? parseInt($('selLowSeverity').value, 10) : 1,
            isDiAlarm: isDi,
            diTriggerState: isDi ? $('selDiTrigger').value : null,
            diAlarmSeverity: isDi ? parseInt($('selDiSeverity').value, 10) : 1,
            diOnLabel: isDi ? ($('txtDiOnLabel').value.trim() || null) : null,
            diOffLabel: isDi ? ($('txtDiOffLabel').value.trim() || null) : null,
            remarks: $('txtRemarks').value.trim(),
            alarmDelaySec: parseIntOr0('txtAlarmDelaySec'),
            isPrecondition: isPre,
            preSid: $('txtPreSid').value.trim() || null,
            preOperator: preOp,
            preValue: (preOp === 'ON' || preOp === 'OFF') ? null : parseNum('txtPreValue'),
            preDelaySec: parseIntOr0('txtPreDelaySec'),
            recoveryNotifyLine: readChecked('chkRecoveryNotifyLine', _recovery.line),
            recoveryNotifyEmail: readChecked('chkRecoveryNotifyEmail', _recovery.email),
            recoveryNotifySms: readChecked('chkRecoveryNotifySms', _recovery.sms)
        };
    }

    /** 前端驗證（與 Web AlarmRuleService.ValidateRule 對齊），回傳錯誤訊息或 null */
    function validate(dto) {
        if (!dto.sid) return t('arf.error.select_point');
        if (dto.alarmDelaySec < 0 || dto.alarmDelaySec > 86400) return t('arf.error.delay_range');
        if (!dto.isPrecondition) return null;
        if (!dto.preSid) return t('arf.error.pre_sid_required');
        if (dto.preSid.toLowerCase() === dto.sid.toLowerCase()) return t('arf.error.pre_sid_self');
        if (isVirtualSid(dto.preSid)) return t('arf.error.pre_sid_virtual');
        if (_points.length > 0 && !pointName(dto.preSid)) return t('arf.error.pre_sid_unknown');
        if (dto.preOperator !== 'ON' && dto.preOperator !== 'OFF' && dto.preValue == null) return t('arf.error.pre_value_required');
        if (dto.preDelaySec < 0 || dto.preDelaySec > 86400) return t('arf.error.delay_range');
        return null;
    }

    /** POST /api/alarm-rules → Promise<{ success, message }>（403 轉成權限不足訊息） */
    function save(dto) {
        return fetch('/api/alarm-rules', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(dto)
        }).then(handleResponse);
    }

    /** DELETE /api/alarm-rules/{id} → Promise<{ success, message }> */
    function remove(id) {
        return fetch('/api/alarm-rules/' + id, { method: 'DELETE' }).then(handleResponse);
    }

    function handleResponse(r) {
        if (r.status === 403) return { success: false, message: t('arf.error.forbidden') };
        return r.json().catch(function () { return { success: false, message: 'HTTP ' + r.status }; });
    }

    /** AlarmRuleModel（/api/alarm-rules、by-sid 回傳，Hungarian camelCase）→ view shape */
    function fromModel(m) {
        if (!m) return null;
        return {
            id: m.nId, sid: m.szSID, pointName: m.szPointName || m.szSID,
            isEnabled: m.isEnabled,
            isAlarmHigh: m.isAlarmHigh, alarmHighValue: m.dAlarmHighValue,
            deadbandHigh: m.dDeadbandHigh, alarmHighSeverity: m.nAlarmHighSeverity,
            isAlarmLow: m.isAlarmLow, alarmLowValue: m.dAlarmLowValue,
            deadbandLow: m.dDeadbandLow, alarmLowSeverity: m.nAlarmLowSeverity,
            isDiAlarm: m.isDiAlarm, diTriggerState: m.szDiTriggerState,
            diAlarmSeverity: m.nDiAlarmSeverity,
            diOnLabel: m.szDiOnLabel || '', diOffLabel: m.szDiOffLabel || '',
            remarks: m.szRemarks || '',
            isPrecondition: m.isPrecondition, preSid: m.szPreSID, preOperator: m.szPreOperator,
            preValue: m.dPreValue, preDelaySec: m.nPreDelaySec,
            recoveryNotifyLine: m.isRecoveryNotifyLine, recoveryNotifyEmail: m.isRecoveryNotifyEmail,
            recoveryNotifySms: m.isRecoveryNotifySms, alarmDelaySec: m.nAlarmDelaySec
        };
    }

    var OP_SYMBOL = { GE: '≥', GT: '>', LE: '≤', LT: '<', EQ: '=', NE: '≠' };

    /**
     * 前置條件摘要（列表 / 彈窗用），例：「前置：泵浦1運轉 = ON 持續 30s」。無前置條件回空字串。
     * 回傳 { text, isMissing }：isMissing = 前置點位已不存在（警報會被永久遮蔽）。
     */
    function preconditionSummary(rule) {
        if (!rule || !rule.isPrecondition || !rule.preSid) return { text: '', isMissing: false };
        var name = pointName(rule.preSid);
        var isMissing = _points.length > 0 && !name;
        var cond = (rule.preOperator === 'ON' || rule.preOperator === 'OFF')
            ? '= ' + rule.preOperator
            : (OP_SYMBOL[rule.preOperator] || rule.preOperator) + ' ' + (rule.preValue != null ? rule.preValue : '');
        var text = t('arf.summary', {
            point: name || rule.preSid,
            cond: cond,
            sec: rule.preDelaySec || 0
        });
        return { text: text, isMissing: isMissing };
    }

    window.AlarmRuleForm = {
        setPoints: setPoints,
        setSelfSid: setSelfSid,
        setDiLabels: setDiLabels,
        pointName: pointName,
        isVirtualSid: isVirtualSid,
        reset: reset,
        fill: fill,
        read: read,
        validate: validate,
        save: save,
        remove: remove,
        fromModel: fromModel,
        toggleSection: toggleSection,
        onPreSidInput: onPreSidInput,
        pickPrePoint: pickPrePoint,
        preconditionSummary: preconditionSummary
    };
})();
