// 排程即時評估（前端共用）
// 從 TimeSchedules 的規則判斷某個排程「現在是否導通」。
// 由 LogicFlow（A/B 接點排程模式）與 ScadaPage（DI 點位綁定排程）共用，
// 避免兩份判斷邏輯走偏（plan 決策 3）。
//
// 主入口：window.ScheduleEval.evalScheduleNow(scheduleId, nodeType, schedules)
//   - scheduleId: TimeSchedules.Id
//   - nodeType:   'contact_no'（active→true）或 'contact_nc'（active→false）
//   - schedules:  排程陣列（呼叫端負責 fetch /api/schedules 後傳入）
//   回傳 boolean 或 null（找不到 / 未啟用）
(function () {
    function _schCheckDaysOfWeek(now, daysStr) {
        if (!daysStr) return false;
        var jsDay = now.getDay();
        var isoDay = jsDay === 0 ? 7 : jsDay;
        return daysStr.split(',').some(function (d) { return parseInt(d) === isoDay; });
    }

    function _schCheckDaysOfMonth(now, daysStr) {
        if (!daysStr) return false;
        var dom = now.getDate();
        return daysStr.split(',').some(function (d) { return parseInt(d) === dom; });
    }

    function _schCheckTimeWindow(now, startStr, endStr) {
        if (!startStr || !endStr) return false;
        var nowMin = now.getHours() * 60 + now.getMinutes();
        var sp = startStr.split(':'), ep = endStr.split(':');
        var startMin = parseInt(sp[0]) * 60 + parseInt(sp[1]);
        var endMin = parseInt(ep[0]) * 60 + parseInt(ep[1]);
        if (endMin <= startMin) return nowMin >= startMin || nowMin < endMin;
        return nowMin >= startMin && nowMin < endMin;
    }

    function _schCheckWeekCycle(now, s) {
        if (!s.dtAnchorDateTime && !s.anchorDateTime) return false;
        if (!s.nRunLength && !s.runLength) return false;
        if (!s.nRestLength && !s.restLength) return false;
        var anchor = new Date(s.dtAnchorDateTime || s.anchorDateTime);
        var runLen = s.nRunLength || s.runLength;
        var restLen = s.nRestLength || s.restLength;
        var elapsedMs = now.getTime() - anchor.getTime();
        if (elapsedMs < 0) return false;
        var totalCycle = runLen + restLen;
        var elapsedWeeks = Math.floor(elapsedMs / (7 * 24 * 60 * 60000));
        return (elapsedWeeks % totalCycle) < runLen;
    }

    function _schCheckMonthCycle(now, s) {
        if (!s.dtAnchorDateTime && !s.anchorDateTime) return false;
        if (!s.nRunLength && !s.runLength) return false;
        if (!s.nRestLength && !s.restLength) return false;
        var anchor = new Date(s.dtAnchorDateTime || s.anchorDateTime);
        var runLen = s.nRunLength || s.runLength;
        var restLen = s.nRestLength || s.restLength;
        var totalMonths = (now.getFullYear() - anchor.getFullYear()) * 12 + (now.getMonth() - anchor.getMonth());
        if (totalMonths < 0) return false;
        var totalCycle = runLen + restLen;
        return (totalMonths % totalCycle) < runLen;
    }

    function evalScheduleNow(scheduleId, nodeType, schedules) {
        if (!schedules || !schedules.length) return null;
        var s = schedules.find(function (x) { return x.nId === scheduleId; });
        if (!s || !s.isEnabled) return null;
        var now = new Date();
        var dayMatch = false;
        if (s.nRecurrenceType === 0) {
            dayMatch = _schCheckDaysOfWeek(now, s.szDaysOfWeek);
        } else if (s.nRecurrenceType === 1) {
            dayMatch = _schCheckWeekCycle(now, s) && _schCheckDaysOfWeek(now, s.szDaysOfWeek);
        } else if (s.nRecurrenceType === 2) {
            dayMatch = _schCheckDaysOfMonth(now, s.szDaysOfMonth);
        } else if (s.nRecurrenceType === 3) {
            dayMatch = _schCheckMonthCycle(now, s) && _schCheckDaysOfMonth(now, s.szDaysOfMonth);
        }
        var timeMatch = _schCheckTimeWindow(now, s.szStartTime, s.szEndTime);
        var isActive = dayMatch && timeMatch;
        return nodeType === 'contact_no' ? isActive : !isActive;
    }

    window.ScheduleEval = { evalScheduleNow: evalScheduleNow };
})();
