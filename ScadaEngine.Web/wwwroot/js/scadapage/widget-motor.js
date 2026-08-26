// ============================================================
// scadapage/widget-motor.js — 馬達型設備（冷卻水塔 / 空調箱風扇 / 冰機）builder + 冰機設定溫度
// ============================================================
// 純搬移自 scadapage.js（plan 2026-08-06-scadapage-js-split）。
// 不包 IIFE、global scope，與 js/designer/ 拆分同模式；
// 共享狀態集中於 state.js。原始 4-space 縮排刻意保留，
// 以保證 template literal 內容 byte-level 不變（純搬移原則）。
// ============================================================

    // ============================================================
    // 馬達型設備（冷卻水塔 / 空調箱風扇 / 冰機）執行期
    // ============================================================
    // 圖形共用 MotorEquip（同 Designer），控制沿用 pump 的 _pumpStartStop /
    // _pumpFreqSet / _pumpAutoControl（generic by cid+title；EventLog 帶設備名）。

    // 從執行期 dataset 還原 MotorEquip 需要的 props（load 時由 ws.props 寫入 dataset）
    function _motorPropsFromEl(el) {
        var d = el.dataset;
        return {
            szTitle:        d.szTitle        || '',
            szSidRun:       d.sidRun         || '',
            szSidFault:     d.sidFault       || '',
            szSidMode:      d.sidMode        || '',
            szSidFreq:      d.sidFreq        || '',
            szSidLoad:      d.sidLoad        || '',
            szSidWaterTemp: d.sidWaterTemp   || '',
            szSidChwOut:    d.sidChwOut      || '',
            szCidStartStop: d.cidStartStop   || '',
            szCidFreqSet:   d.cidFreqSet     || '',
            szCidSetTemp:   d.cidSetTemp     || '',
            nFreqSetMin:    parseFloat(d.nFreqSetMin) || 0,
            nFreqSetMax:    parseFloat(d.nFreqSetMax) || 60,
            nFreqMax:       parseFloat(d.nFreqMax)    || 60,
            nLoadMax:       parseFloat(d.nLoadMax)    || 100,
            szRunColor:     d.szRunColor     || '#28a745',
            szStopColor:    d.szStopColor    || '#6c757d',
            szFaultColor:   d.szFaultColor   || '#dc3545',
            szManualColor:  d.szManualColor  || '#ffc107',
            szAutoColor:    d.szAutoColor    || '#0d6efd',
            szBgColor:      d.szBgColor      || 'transparent'
        };
    }

    // 組執行期 widget 內層 HTML（含 M 角標、冰機設定溫度覆蓋層、hover 標籤）
    // bBad=true：監控點斷線 — 圖照畫（停止灰）、上方紅字「斷線」，hover 仍浮標題與狀態
    function _buildMotorViewHtml(props, szType, szState, szPrimaryVal, szModeVal, szExtraTip, bBad) {
        var bVfd    = (szType !== 'chiller');
        var bHasBar = bVfd ? !!props.szSidFreq : !!props.szSidLoad;

        var szStateText = bBad ? '斷線' : szState === 'fault' ? '故障' : szState === 'run' ? '運轉中' : '停止';
        // 手自動點語意：冰機=遠端/現場（現場→控制箱面板深黃），其餘=自動/手動（機身模式色）
        var szModeText  = (szModeVal !== undefined && szModeVal !== '')
            ? (szModeVal == 1 || szModeVal === '1' || szModeVal === 'true'
                ? (szType === 'chiller' ? '遠端' : '自動')
                : (szType === 'chiller' ? '現場' : '手動')) : '';
        var szPrimaryText = '';
        if (szPrimaryVal !== undefined && szPrimaryVal !== '' && szPrimaryVal !== '--') {
            szPrimaryText = bVfd ? parseFloat(szPrimaryVal).toFixed(1) + ' Hz'
                                 : parseFloat(szPrimaryVal).toFixed(0) + ' %';
        }
        var szTitle = props.szTitle || (szType === 'chiller' ? '冰機' : szType === 'coolingTower' ? '冷卻水塔' : '空調箱風扇');

        // M 角標（cidStartStop=開關手動，一律右上；VFD 另有 cidFreqSet）
        // 冰機負載條在底部不佔右側 → 開關 M 恆右上；VFD 直條在右 → 有條時開關 M 移左上
        var szCidSS = props.szCidStartStop || '';
        var szCidFQ = props.szCidFreqSet || '';
        var szBadgeSS = '', szBadgeFQ = '';
        if (szCidSS) {
            szBadgeSS = (bVfd && bHasBar) ? _buildModeBadgeHtml(szCidSS, 'left:-4px;right:auto;') : _buildModeBadgeHtml(szCidSS);
        }
        if (bVfd && szCidFQ && bHasBar) szBadgeFQ = _buildModeBadgeHtml(szCidFQ);

        // 冰機設定溫度覆蓋層（右下角，雙擊編輯；顯示值取自 AO 手動快取）
        // 設定溫度切手動 → 值前顯示小 M（吃 cidSetTemp 的 isAuto，掛 scada-mode-badge
        // class + data-cid 讓既有 _toggleModeBadge 輪詢直接驅動；flex 排版避免 display:block 換行）
        var szOverlay = '';
        if (szType === 'chiller' && props.szCidSetTemp) {
            var cachedST = _aoManualValueMap[props.szCidSetTemp];
            var szTempVal = (cachedST && cachedST.value != null) ? (Math.round(cachedST.value * 10) / 10) : '--';
            var bSTManual = !!(cachedST && cachedST.isAuto === false);
            var bEditable = _canControlPage(scadaCurrentId);
            var szSTBadge = '<span class="scada-mode-badge" data-cid="' + escViewHtml(props.szCidSetTemp) + '"' +
                ' title="' + escViewHtml(t('scadapage.badge.manual_mode_tooltip')) + '"' +
                ' style="display:' + (bSTManual ? 'block' : 'none') + ';position:static;width:auto;height:auto;' +
                'border-radius:2px;padding:0 3px;line-height:12px;font-size:9px;background:#ffc107;color:#333;box-shadow:none;">M</span>';
            szOverlay = '<div class="chiller-settemp"' +
                (bEditable ? ' title="雙擊編輯設定溫度" style="cursor:pointer;' : ' style="') +
                'position:absolute;bottom:3px;right:4px;font-size:10px;color:#fff;background:rgba(33,37,41,.72);' +
                'padding:1px 6px;border-radius:4px;white-space:nowrap;z-index:11;' +
                'display:flex;align-items:center;gap:3px;">' + szSTBadge +
                '<span class="chiller-settemp-text">設定 ' + szTempVal + '°C</span></div>';
        }

        // 斷線紅字（上方置中，蓋在圖上；不擋 hover）
        var szBadLabel = bBad
            ? '<div style="position:absolute;top:2px;left:50%;transform:translateX(-50%);font-size:12px;' +
              'color:#dc3545;font-weight:700;background:rgba(255,255,255,.75);padding:0 6px;border-radius:3px;' +
              'white-space:nowrap;z-index:12;pointer-events:none;">斷線</div>'
            : '';

        var aTip = [escViewHtml(szTitle), szStateText];
        if (szModeText) aTip.push(szModeText);
        if (szPrimaryText) aTip.push(szPrimaryText);
        if (szExtraTip) aTip.push(szExtraTip);
        var szHover = '<div class="scada-hover-label" style="display:none;position:absolute;bottom:4px;left:50%;' +
            'transform:translateX(-50%);white-space:nowrap;background:rgba(33,37,41,.85);color:#fff;' +
            'font-size:11px;padding:3px 10px;border-radius:4px;pointer-events:none;z-index:10;">' +
            aTip.join(' — ') + '</div>';

        return MotorEquip.build({
            szType: szType, props: props, szState: szState,
            szPrimaryVal: szPrimaryVal, szModeVal: szModeVal, bInteractive: true,
            szBadgesHtml: szBadgeSS + szBadgeFQ, szOverlayHtml: szOverlay + szBadLabel, szHoverHtml: szHover
        });
    }

    // ── 冰機設定溫度：雙擊 → prompt（無上下限）→ 寫入 ──
    function _motorPromptSetTemp(el) {
        var szCid = el.dataset.cidSetTemp;
        if (!szCid) return;
        var szTitle = el.dataset.szTitle || '冰機';
        var cached = _aoManualValueMap[szCid];
        var szCur = (cached && cached.value != null) ? String(cached.value) : '';
        var szInput = prompt('輸入「' + szTitle + '」冰水設定溫度（°C）：', szCur);
        if (szInput === null) return;
        var fVal = parseFloat(szInput);
        if (isNaN(fVal)) { alert('請輸入有效數值'); return; }
        _motorSetTemp(szCid, szTitle, fVal);
    }

    async function _motorSetTemp(szCid, szTitle, fValue) {
        if (!confirm('確定要設定「' + szTitle + '」冰水溫度為 ' + fValue + ' °C？')) return;
        try {
            var resp = await fetch('/api/control/write', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ cid: szCid, value: fValue, actionType: 'ao_manual', displayName: szTitle })
            });
            var result = await resp.json();
            if (result.success) {
                _aoManualValueMap[szCid] = { value: fValue, isAuto: false };
                var szShow = '設定 ' + (Math.round(fValue * 10) / 10) + '°C';
                document.querySelectorAll('.scada-motor').forEach(function (el) {
                    if (el.dataset.cidSetTemp === szCid) {
                        // 只更新文字 span，避免蓋掉設定手動 M 角標
                        var st = el.querySelector('.chiller-settemp-text');
                        if (st) st.textContent = szShow;
                    }
                });
                _toggleModeBadge(szCid, false);
                showControlToast('已送出設定溫度：' + szTitle + ' → ' + fValue + ' °C');
            } else {
                alert('設定失敗：' + (result.error || '未知錯誤'));
            }
        } catch (err) {
            alert('設定請求失敗：' + err.message);
        }
    }
