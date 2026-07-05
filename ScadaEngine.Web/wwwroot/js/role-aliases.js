// ============================================================
// 電表角色別名表（共用模組）— 掛 window._roleAliases
// ============================================================
// 使用端：
//   - designer/row-template.js（表格列範本自動帶入建議）
//   - energymeter.js（主要電表資訊點位自動比對）
// key   = canonical label (顯示用)
// value = aliases array (全 lower-case，比對時 candidate 也轉小寫)
// 載入順序：必須先於使用端 <script> 載入。
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
})();
