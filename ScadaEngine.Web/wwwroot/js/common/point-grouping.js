// 點位分群解析（前端共用單一真相）
// ============================================================
// 第一性原理：一個 SID 落在哪台設備 / 站號內子設備，是「純查表 + 算術」，
// 不該在十幾個頁面各自 split(',') + nId*65536 + mid*256 硬算一遍。
// 這段運算原本被複製 5+ 份且 fallback 寫法已走偏（有的退 d.szName、
// 有的退 mid 數字、DB/CALC 預設字串各頁不同），此檔把「算」的部分收斂為
// 一份，"顯示用的預設標籤/i18n" 仍由各頁自理（各頁 i18n key 不同）。
//
// SID 格式：{CoordinatorId*65536 + ModbusId*256 + 1}-S{N}
//   → 數字前綴 = CoordinatorId*65536 + ModbusId*256 + 1，可逆
//   計算點位 SID 以 'CALC-' 開頭；DB 來源 'DB{n}-S{n}'；OPC 'OPC{n}-S{n}'
//
// 主入口：window.PointGrouping
//   parseCoord(d)            → 吸收 Hungarian / camelCase 兩種欄位命名，
//                              回傳 { id, modbusIds:[], deviceNames:[], name }
//   getSidPrefix(sid)        → 數字前綴（非 modbus 點位回 -1）
//   isCalcSid / isDbSid / isOpcSid(sid)
//   isMultiId(coord)         → 該 Coordinator 是否多站號
//   coordContainsSid(sid,c)  → sid 是否屬於此 Coordinator（不分子站號）
//   subOfSid(sid, coord)     → 多站號時回 { mid, idx, subName }；單站號 / 找不到回 null
//                              subName = deviceNames[idx]（缺就 ''），fallback 由呼叫端決定
//   subRangeBase(id, mid)    → id*65536 + mid*256（點位範圍篩選用）
//
// ⚠️ deviceNames 刻意「不 filter 空字串」以保持與 modbusIds 的索引對齊
//   （現場 DeviceName 逗號數可能與 ModbusID 不對齊，見 plan 決策 5 / 風險段）
// ============================================================
(function () {
    'use strict';

    // 吸收兩種欄位命名：
    //   Designer/API 回傳 Hungarian（nId / szModbusID / szDeviceName / szName）
    //   Razor 注入 coord 為 camelCase（id / modbusId / deviceName / name）
    function parseCoord(d) {
        if (!d) return { id: -1, modbusIds: [], deviceNames: [], name: '' };
        var id = (d.nId != null) ? d.nId : ((d.id != null) ? d.id : -1);
        var rawMid = (d.szModbusID != null) ? d.szModbusID : ((d.modbusId != null) ? d.modbusId : '');
        var rawName = (d.szDeviceName != null) ? d.szDeviceName : ((d.deviceName != null) ? d.deviceName : '');
        var name = (d.szName != null) ? d.szName : ((d.name != null) ? d.name : '');
        return {
            id: id,
            modbusIds: String(rawMid).split(',').map(function (s) { return s.trim(); }).filter(Boolean),
            // 刻意不 filter：保留索引對齊，deviceNames[idx] 才對得上 modbusIds[idx]
            deviceNames: String(rawName).split(',').map(function (s) { return s.trim(); }),
            name: name
        };
    }

    function getSidPrefix(sid) {
        if (!sid) return -1;
        var m = String(sid).match(/^(\d+)-S\d+$/);
        return m ? parseInt(m[1], 10) : -1;
    }

    function isCalcSid(sid) { return !!sid && String(sid).indexOf('CALC-') === 0; }
    function isDbSid(sid) { return !!sid && /^DB\d+-S\d+$/.test(sid); }
    function isOpcSid(sid) { return !!sid && /^OPC\d+-S\d+$/.test(sid); }

    function isMultiId(coord) {
        return parseCoord(coord).modbusIds.length > 1;
    }

    function subRangeBase(id, mid) {
        return id * 65536 + parseInt(mid, 10) * 256;
    }

    // sid 是否屬於此 Coordinator（含其所有子站號，不細分）
    function coordContainsSid(sid, coord) {
        if (isCalcSid(sid) || isDbSid(sid) || isOpcSid(sid)) return false;
        var c = parseCoord(coord);
        var pfx = getSidPrefix(sid);
        return pfx >= c.id * 65536 && pfx < (c.id + 1) * 65536;
    }

    // 多站號時定位所屬子站號：回 { mid, idx, subName }；單站號或找不到回 null。
    // subName 為 deviceNames[idx]（缺就空字串）——「取不到子名要退什麼」由呼叫端決定
    //（點位標籤慣例退 coord.name、設備清單列慣例退 mid 數字，兩者刻意保留差異）
    function subOfSid(sid, coord) {
        var c = parseCoord(coord);
        if (c.modbusIds.length <= 1) return null;
        var pfx = getSidPrefix(sid);
        for (var j = 0; j < c.modbusIds.length; j++) {
            var mid = parseInt(c.modbusIds[j], 10);
            var base = c.id * 65536 + mid * 256;
            if (pfx >= base && pfx < base + 256) {
                return {
                    mid: mid,
                    idx: j,
                    subName: (j < c.deviceNames.length && c.deviceNames[j]) ? c.deviceNames[j] : ''
                };
            }
        }
        return null;
    }

    // ── 階段 C：站號內子設備分群（Tag.Device → ModbusPoints.DeviceGroup）─────────
    // 點位物件吸收 Hungarian（szSid/szDeviceGroup）與 camelCase（sid/deviceGroup）兩種命名

    function pointSid(p) {
        if (!p) return '';
        return (p.szSid != null) ? p.szSid
             : (p.sid != null) ? p.sid
             : (p.szSID != null) ? p.szSID : '';
    }
    // 取點位的 DeviceGroup（去空白，未分群回 ''）
    function getDeviceGroup(p) {
        if (!p) return '';
        var v = (p.szDeviceGroup != null) ? p.szDeviceGroup
              : (p.deviceGroup != null) ? p.deviceGroup : '';
        return v ? String(v).trim() : '';
    }

    // 四級 fallback 鏈（plan 決策 5）：算出點位的設備標籤
    //   1) Tag.Device（DeviceGroup） → 2) 多站號的站號→DeviceName → 3) Coordinator 名
    // （第 4 級「未分群」是設備清單的桶，非點位標籤 — 點位一定落在某 Coordinator，故 level 3 必可解）
    function pointDeviceLabel(sid, deviceGroup, coord) {
        var dg = deviceGroup ? String(deviceGroup).trim() : '';
        if (dg) return dg;                                  // level 1
        var c = parseCoord(coord);
        if (c.modbusIds.length > 1) {                       // level 2
            var sub = subOfSid(sid, coord);
            return sub ? (sub.subName || c.name) : c.name;
        }
        return c.name;                                      // level 3
    }

    // 單站號 Coordinator 內的 Device 分群盤點（供設備清單決定是否展開）。
    // 多站號回 null —— 站號 / Device 互斥（決策 4），多站號一律走站號子選單、不走 Device。
    function coordDeviceGroups(coord, points) {
        var c = parseCoord(coord);
        if (c.modbusIds.length > 1) return null;
        var countByGroup = {};
        var hasUngrouped = false;
        (points || []).forEach(function (p) {
            var sid = pointSid(p);
            if (!coordContainsSid(sid, coord)) return;
            var dg = getDeviceGroup(p);
            if (dg) countByGroup[dg] = (countByGroup[dg] || 0) + 1;
            else hasUngrouped = true;
        });
        return { names: Object.keys(countByGroup).sort(), hasUngrouped: hasUngrouped, countByGroup: countByGroup };
    }
    // 便捷：此 Coordinator 是否有 Device 分群（單站號 + 至少一個 Device）→ 設備清單展開條件
    function hasDeviceGroups(coord, points) {
        var g = coordDeviceGroups(coord, points);
        return !!(g && g.names.length > 0);
    }

    window.PointGrouping = {
        parseCoord: parseCoord,
        getSidPrefix: getSidPrefix,
        isCalcSid: isCalcSid,
        isDbSid: isDbSid,
        isOpcSid: isOpcSid,
        isMultiId: isMultiId,
        subRangeBase: subRangeBase,
        coordContainsSid: coordContainsSid,
        subOfSid: subOfSid,
        // 階段 C
        pointSid: pointSid,
        getDeviceGroup: getDeviceGroup,
        pointDeviceLabel: pointDeviceLabel,
        coordDeviceGroups: coordDeviceGroups,
        hasDeviceGroups: hasDeviceGroups
    };
})();
