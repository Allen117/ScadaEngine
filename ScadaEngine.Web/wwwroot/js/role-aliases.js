// ============================================================
// 電表角色別名表（共用模組）— 掛 window._roleAliases + window._roleMatch
// ============================================================
// 使用端：
//   - designer/picker.js（同設備點位角色比對，_findSameDevicePointForRole）
//   - designer/row-template.js（表格列範本自動帶入建議）
//   - energymeter.js（主要電表資訊點位自動比對）
// key   = canonical label (顯示用)
// value = aliases array (全 lower-case，比對時 candidate 也轉小寫)
//
// 比對規則（window._roleMatch.resolveRole）：把點位名正規化後以
// `- _ 空白` 切成 tokens，依「整名 → 開頭 token → 尾段 token」順序查別名，
// 任一命中即定。開頭優先於尾段是為了吃使用者「角色在前、相別在後」的命名
// （V_a / I_avg → 角色 V / A，而非把尾段 a / avg 當角色）；整名先查是為了
// 不拆散本身就是完整別名的複合詞（power_factor / real_power / l-n）。
// 設備前綴式（PM-1-V）開頭 pm 非角色 → 自然退回尾段 v，兩種命名同時成立。
//
// 載入順序：本檔須先於「呼叫 _roleMatch 的使用端」載入；resolveRole 內對
// picker.js 頂層 _normalizeHeaderText 的引用在**呼叫時**才解析（runtime），
// 故 picker.js 早於或晚於本檔載入皆可，只要呼叫發生在兩者都載入之後。
// ============================================================

(function () {
    'use strict';

    window._roleAliases = {
        'V':    ['v', 'u', 'voltage', 'volt', 'volts', 'vrms', 'vll', 'vln', 'l-l', 'l-n',
                 'vab', 'vbc', 'vca', 'van', 'vbn', 'vcn',
                 '電壓', '三相電壓', '線電壓', '相電壓'],
        'A':    ['a', 'i', 'amp', 'amps', 'ampere', 'amperes', 'current', 'irms',
                 'ia', 'ib', 'ic',
                 '電流', '三相電流'],
        'KW':   ['kw', 'p', 'w', 'mw', 'watt', 'watts', 'kilowatt', 'kilowatts',
                 'power', 'active', 'active_power', 'activepower',
                 'real_power', 'realpower', 'actpower',
                 '功率', '有效功率', '實功', '實功率', '主動功率'],
        'KVA':  ['kva', 's', 'apparent', 'apparent_power', 'apparentpower',
                 '視在功率'],
        'KVAr': ['kvar', 'kvars', 'q', 'var', 'vars',
                 'reactive', 'reactive_power', 'reactivepower',
                 '無效功率', '無功功率', '虛功', '虛功率'],
        'PF':   ['pf', 'powerfactor', 'power_factor', 'cosphi', 'cos_phi',
                 'cos', 'cosΦ', 'cos∅', 'λ', 'factor',
                 '功率因數'],
        'KWH':  ['kwh', 'kw_h', 'wh', 'mwh', 'kilowatthour', 'kilowatthours',
                 'energy', 'consumption', 'accumulated',
                 'kwh_total', 'kwhtotal', 'total_kwh',
                 '度數', '用電量', '電能', '累計用電', '累積電能', '總電能',
                 '用電度數', '總耗電'],
        'Hz':   ['hz', 'f', 'frequency', 'freq', 'cycle',
                 '頻率', '赫茲', '週期'],
        'KWHr': ['kwhr', 'kvarh', 'reactiveenergy', 'reactive_energy',
                 'kvarh_total', 'kvarhtotal',
                 '無功電能', '累計無功', '無功累計']
    };

    // ── 名稱正規化（優先沿用 picker.js 的 _normalizeHeaderText，缺席時自帶等價輕量版）──
    // 輕量版：trim → 全形轉半形（含全形空白）→ 去尾端括號單位 → 壓縮空白 → 小寫
    function _normalize(szRaw) {
        if (szRaw == null) return '';
        if (typeof _normalizeHeaderText === 'function') return _normalizeHeaderText(szRaw);
        let s = String(szRaw).trim();
        s = s.replace(/[！-～]/g, ch => String.fromCharCode(ch.charCodeAt(0) - 0xfee0))
             .replace(/　/g, ' ');
        let prev;
        do { prev = s; s = s.replace(/\s*\([^()]*\)\s*$/, ''); } while (s !== prev);
        return s.replace(/\s+/g, ' ').trim().toLowerCase();
    }

    // 正規化字串切 tokens（吃 - _ 空白）
    function _tokenize(szNorm) {
        return szNorm ? szNorm.split(/[-_\s]+/).filter(Boolean) : [];
    }

    // 單一 token（已 lower-case）→ canonical 角色 label 或 null（多命中取第一、警告）
    function _byAlias(szTok) {
        if (!szTok) return null;
        const hits = [];
        for (const role of Object.keys(window._roleAliases)) {
            if (window._roleAliases[role].indexOf(szTok) >= 0) hits.push(role);
        }
        if (hits.length === 0) return null;
        if (hits.length > 1) {
            console.warn('[role-match] multiple role hits for "' + szTok + '":', hits, '— picked', hits[0]);
        }
        return hits[0];
    }

    // 點位名 → canonical 角色 label 或 null（整名 → 開頭 token → 尾段 token）
    function resolveRole(szRaw) {
        const szNorm = _normalize(szRaw);
        if (!szNorm) return null;
        const toks = _tokenize(szNorm);
        let r = _byAlias(szNorm);                     // 整名（吃 power_factor / l-n 等複合別名）
        if (r) return r;
        if (toks.length > 0) { r = _byAlias(toks[0]); if (r) return r; }   // 開頭 token 優先
        if (toks.length > 1) { r = _byAlias(toks[toks.length - 1]); if (r) return r; } // 尾段回退
        return null;
    }

    // 點位名的「開頭 token」是否命中角色（供 row-template 判定風格 B）→ 角色 label 或 null
    function leadingRole(szRaw) {
        const toks = _tokenize(_normalize(szRaw));
        return toks.length > 0 ? _byAlias(toks[0]) : null;
    }

    window._roleMatch = {
        resolveRole: resolveRole,   // 整名 → 開頭 → 尾段
        leadingRole: leadingRole,   // 僅開頭 token
        byAlias:     _byAlias,      // 單 token
        normalize:   _normalize
    };
})();
