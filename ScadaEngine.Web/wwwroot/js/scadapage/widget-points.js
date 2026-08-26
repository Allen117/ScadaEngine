// ============================================================
// scadapage/widget-points.js — 點位型元件 HTML builder：控制鈕 / 即時值 / DI / AO / DO / 半圓儀表 + M 角標與高亮 helper
// ============================================================
// 純搬移自 scadapage.js（plan 2026-08-06-scadapage-js-split）。
// 不包 IIFE、global scope，與 js/designer/ 拆分同模式；
// 共享狀態集中於 state.js。原始 4-space 縮排刻意保留，
// 以保證 template literal 內容 byte-level 不變（純搬移原則）。
// ============================================================

    function escViewHtml(s) {
        return String(s)
            .replace(/&/g, '&amp;')
            .replace(/"/g, '&quot;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;');
    }

    // ── 控制按鈕 HTML ──
    function buildCtrlBtnHtml(props) {
        var szCid      = props.szCid       || '';
        var szBtnLabel = props.szBtnLabel  || '\u57f7\u884c';
        var szBtnColor = props.szBtnColor  || '#198754';
        var szLabel    = props.szPointName || props.szTitle || szCid;
        var szBg       = (props.szBgColor && props.szBgColor !== 'transparent')
                       ? props.szBgColor : 'transparent';
        var szTooltipText  = szCid
            ? escViewHtml(szLabel)
            : '<i class="fas fa-unlink" style="margin-right:4px;color:#dc3545;"></i>\u672a\u7d81\u5b9a CID';
        return '<div class="ctrl-btn-wrap" style="position:relative;width:100%;height:100%;' +
                    'display:flex;flex-direction:column;' +
                    'align-items:center;justify-content:center;' +
                    'background:' + szBg + ';container-type:size;">' +
                    '<button class="ctrl-btn-exec"' +
                            ' style="width:100%;height:100%;' +
                                   'display:inline-flex;align-items:center;justify-content:center;' +
                                   'gap:4px;border-radius:8px;' +
                                   'font-size:clamp(9px, 45cqh, 14px);' +
                                   'font-weight:600;border:none;' +
                                   'border-top:1px solid rgba(255,255,255,.25);' +
                                   'color:#fff;cursor:pointer;background:' + szBtnColor + ';' +
                                   'overflow:hidden;white-space:nowrap;"' +
                            (szCid ? '' : ' disabled') + '>' +
                        '<i class="fas ' + (props.szBtnIcon || 'fa-hand-pointer') + '" style="font-size:inherit;"></i>' +
                        escViewHtml(szBtnLabel) +
                    '</button>' +
                    '<div class="scada-hover-label"' +
                         ' style="display:none;position:absolute;bottom:0;left:50%;' +
                                'transform:translate(-50%,100%);white-space:nowrap;' +
                                'background:rgba(33,37,41,.9);color:#e0e0e0;' +
                                'font-size:11px;padding:2px 8px;border-radius:4px;' +
                                'pointer-events:none;z-index:25;margin-top:4px;">' +
                        szTooltipText +
                    '</div>' +
                '</div>';
    }

    async function onControlBtnClick(el) {
        var szCid      = el.dataset.cid   || '';
        var szTitle    = el.dataset.szTitle || '\u63a7\u5236';
        var fCtrlValue = parseFloat(el.dataset.fCtrlValue) || 1;
        if (!szCid) { alert('\u6b64\u6309\u9215\u5c1a\u672a\u7d81\u5b9a CID'); return; }
        if (!confirm('\u78ba\u5b9a\u8981\u57f7\u884c\u300c' + szTitle + '\u300d\u63a7\u5236\u6307\u4ee4\uff1f\uff08\u503c=' + fCtrlValue + '\uff09')) return;
        try {
            var resp = await fetch('/api/control/write', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ cid: szCid, value: fCtrlValue, actionType: 'button', displayName: szTitle })
            });
            var result = await resp.json();
            if (result.success) {
                showControlToast('\u5df2\u9001\u51fa\u63a7\u5236\u6307\u4ee4\uff1a' + szTitle);
            } else {
                alert('\u63a7\u5236\u5931\u6557\uff1a' + (result.error || '\u672a\u77e5\u932f\u8aa4'));
            }
        } catch (err) {
            alert('\u63a7\u5236\u8acb\u6c42\u5931\u6557\uff1a' + err.message);
        }
    }

    function showControlToast(szMsg) {
        var d = document.createElement('div');
        d.style.cssText = 'position:fixed;bottom:24px;right:24px;z-index:9999;min-width:220px;';
        d.innerHTML = '<div class="alert alert-success alert-dismissible fade show mb-0 shadow" style="font-size:13px;">' +
            '<i class="fas fa-check-circle me-1"></i>' + escViewHtml(szMsg) +
            '<button type="button" class="btn-close btn-sm" data-bs-dismiss="alert"></button>' +
            '</div>';
        document.body.appendChild(d);
        setTimeout(function () { d.querySelector('.alert')?.classList.remove('show'); setTimeout(function () { d.remove(); }, 300); }, 3000);
    }

    // ── 即時數值 View HTML ──
    function buildRealtimeValueViewHtml(props, szDisplayVal) {
        var nFs   = props.nFontSize   || 28;
        var szClr = props.szFontColor || '#212529';
        var szBg  = (props.szBgColor && props.szBgColor !== 'transparent') ? props.szBgColor : 'transparent';
        var szUnit = props.szUnit || '';
        var nUnitFs = Math.max(12, Math.round(nFs * 0.45));
        var szTitle = props.szTitle || '';
        var szTooltip = szTitle
            ? '<div class="scada-hover-label" style="display:none;position:absolute;top:100%;margin-top:4px;left:50%;' +
                    'transform:translateX(-50%);background:rgba(33,37,41,.9);color:#e0e0e0;' +
                    'font-size:11px;padding:2px 8px;border-radius:4px;white-space:nowrap;' +
                    'pointer-events:none;z-index:25;">' + escViewHtml(szTitle) + '</div>'
            : '';
        return '<div style="position:relative;width:100%;height:100%;display:flex;flex-direction:column;' +
                        'align-items:center;justify-content:center;background:' + szBg + ';border-radius:4px;">' +
                    '<div style="font-size:' + nFs + 'px;font-weight:700;color:' + szClr + ';' +
                                'font-family:\'Segoe UI\',sans-serif;line-height:1.2;">' +
                        escViewHtml(szDisplayVal) +
                        '<span style="font-size:' + nUnitFs + 'px;font-weight:400;color:#6c757d;margin-left:4px;">' + escViewHtml(szUnit) + '</span>' +
                    '</div>' +
                    szTooltip +
                '</div>';
    }

    // ── DI 點位 View HTML ──
    function buildDiPointViewHtml(props, bIsOn) {
        var szMode = props.szDisplayMode || 'indicator';
        var szBg   = (props.szBgColor && props.szBgColor !== 'transparent') ? props.szBgColor : 'transparent';
        var szScheduleName = props.szScheduleName || '';
        var szTitle = szScheduleName ? '' : (props.szTitle || '');
        var szTooltipInner = szScheduleName
            ? escViewHtml(t('scadapage.di.schedule_label')) + ': ' + escViewHtml(szScheduleName)
            : (szTitle ? escViewHtml(szTitle) : '');
        var szTooltip = szTooltipInner
            ? '<div class="scada-hover-label" style="display:none;position:absolute;top:100%;margin-top:4px;left:50%;' +
                    'transform:translateX(-50%);background:rgba(33,37,41,.85);color:#fff;' +
                    'font-size:11px;padding:2px 8px;border-radius:4px;white-space:nowrap;' +
                    'pointer-events:none;z-index:25;">' + szTooltipInner + '</div>'
            : '';

        var isAlarm = props.isAlarmEnabled &&
            ((props.szAlarmTrigger === 'ON' && bIsOn === true) ||
             (props.szAlarmTrigger === 'OFF' && bIsOn === false));
        var szAlarmColor = props.szAlarmColor || '#dc3545';

        var isOn = bIsOn === true;
        var szContentHtml = '';

        if (bIsOn === null) {
            szContentHtml = '<span style="font-size:13px;color:#adb5bd;">--</span>';
        } else if (szMode === 'text') {
            var nFs    = props.nFontSize || 24;
            var szColor = isAlarm ? szAlarmColor
                        : isOn ? (props.szOnColor || '#28a745') : (props.szOffColor || '#6c757d');
            var szLabel = isOn ? (props.szOnLabel || 'ON') : (props.szOffLabel || 'OFF');
            szContentHtml = '<span style="font-size:' + nFs + 'px;font-weight:700;color:' + szColor + ';' +
                                         'font-family:\'Segoe UI\',sans-serif;">' + escViewHtml(szLabel) + '</span>';
        } else {
            var nSize  = props.nIndicatorSize || 28;
            var szColor = isAlarm ? szAlarmColor
                        : isOn ? (props.szOnColor || '#28a745') : (props.szOffColor || '#6c757d');
            szContentHtml = '<span style="display:inline-block;width:' + nSize + 'px;height:' + nSize + 'px;border-radius:50%;' +
                                         'background:' + szColor + ';box-shadow:0 0 6px ' + szColor + ';' +
                                         (isAlarm ? 'animation:di-alarm-pulse 1s infinite;' : '') + '"></span>';
        }

        return '<div style="position:relative;width:100%;height:100%;display:flex;flex-direction:column;' +
                        'align-items:center;justify-content:center;background:' + szBg + ';border-radius:4px;gap:2px;">' +
                    szContentHtml +
                    szTooltip +
                '</div>';
    }

    // ── AO 點位 View HTML ──
    function buildAoPointViewHtml(props) {
        var szCid  = props.szCid || '';
        var szName = props.szDisplayName || props.szTitle || 'AO \u9ede\u4f4d';
        var nFs    = props.nFontSize  || 16;
        var szClr  = props.szFontColor || '#ffffff';
        var szBlock = props.szBlockColor
                    || (props.szBgColor && props.szBgColor !== 'transparent' ? props.szBgColor : null)
                    || '#0d6efd';

        var szTooltipText = szCid
            ? escViewHtml(props.szPointName || props.szTitle || szCid)
            : '<i class="fas fa-unlink" style="margin-right:4px;color:#dc3545;"></i>\u672a\u7d81\u5b9a CID';

        return '<div class="ao-point-body" style="position:relative;width:100%;height:100%;">' +
                    '<div class="ao-point-label-btn"' +
                         ' style="width:100%;height:100%;display:flex;align-items:center;justify-content:center;' +
                                'font-size:' + nFs + 'px;font-weight:600;color:' + szClr + ';' +
                                'text-align:center;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;' +
                                'border-radius:6px;background:' + szBlock + ';cursor:context-menu;">' +
                        escViewHtml(szName) +
                    '</div>' +
                    _buildModeBadgeHtml(szCid) +
                    '<div class="scada-hover-label"' +
                         ' style="display:none;position:absolute;top:100%;margin-top:4px;left:50%;' +
                                'transform:translateX(-50%);white-space:nowrap;' +
                                'background:rgba(33,37,41,.85);color:#fff;' +
                                'font-size:11px;padding:2px 8px;border-radius:4px;' +
                                'pointer-events:none;z-index:25;">' +
                        szTooltipText +
                    '</div>' +
                '</div>';
    }

    // ── 手動模式 M 角標 HTML（控制元件 AO/DO/Pump 共用）──
    // 初始顯示狀態由 _aoManualValueMap 預判（page load 時已從 /api/control/manual-values 載入）
    // 後續由 /api/realtime/latest polling 的 isAuto 欄位驅動 toggle
    function _buildModeBadgeHtml(szCid, szExtraStyle) {
        if (!szCid) return '';
        var cached = _aoManualValueMap[szCid];
        var bManual = !!(cached && cached.isAuto === false);
        var szDisplay = bManual ? 'block' : 'none';
        var szTitle = t('scadapage.badge.manual_mode_tooltip');
        return '<div class="scada-mode-badge" data-cid="' + escViewHtml(szCid) + '"' +
                    ' title="' + escViewHtml(szTitle) + '"' +
                    ' style="display:' + szDisplay + ';' + (szExtraStyle || '') + '">M</div>';
    }

    // ── 切換指定 CID 對應的 M 角標顯示（optimistic / polling 共用）──
    function _toggleModeBadge(szCid, isAuto) {
        if (!szCid) return;
        var nodes = document.querySelectorAll('.scada-mode-badge[data-cid="' + szCid + '"]');
        for (var i = 0; i < nodes.length; i++) {
            nodes[i].style.display = (isAuto === false) ? 'block' : 'none';
        }
    }

    // ── 控制狀態高亮輔助 ──
    function _applyActiveStyle(el) {
        el.style.boxShadow = 'inset 0 0 0 2px #0d6efd';
        el.style.borderRadius = '4px';
        el.style.background = '#f0f4ff';
    }

    // ── DO 點位 HTML ──
    function buildDoPointViewHtml(props) {
        var szCid      = props.szCid          || '';
        var szName     = props.szDisplayName   || props.szTitle || 'DO \u9ede\u4f4d';
        var nFs        = props.nFontSize       || 16;
        var szClr      = props.szFontColor     || '#212529';
        var szBlock    = props.szBlockColor
                       || (props.szBgColor && props.szBgColor !== 'transparent' ? props.szBgColor : null)
                       || '#0d6efd';
        var szTitle    = props.szPointName || props.szTitle || szCid;

        var szTooltipText = szCid
            ? escViewHtml(szTitle)
            : '<i class="fas fa-unlink" style="margin-right:4px;color:#dc3545;"></i>\u672a\u7d81\u5b9a CID';

        return '<div class="do-point-body" style="position:relative;width:100%;height:100%;">' +
                    '<div class="do-point-label-btn"' +
                         ' style="width:100%;height:100%;display:flex;align-items:center;justify-content:center;' +
                                'font-size:' + nFs + 'px;font-weight:600;color:' + szClr + ';' +
                                'text-align:center;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;' +
                                'border-radius:6px;background:' + szBlock + ';' +
                                'cursor:' + (szCid ? 'pointer' : 'default') + ';' +
                                'user-select:none;">' +
                        escViewHtml(szName) +
                    '</div>' +
                    _buildModeBadgeHtml(szCid) +
                    '<div class="scada-hover-label"' +
                         ' style="display:none;position:absolute;top:100%;margin-top:4px;left:50%;' +
                                'transform:translateX(-50%);white-space:nowrap;' +
                                'background:rgba(33,37,41,.85);color:#fff;' +
                                'font-size:11px;padding:2px 8px;border-radius:4px;' +
                                'pointer-events:none;z-index:25;">' +
                        szTooltipText +
                    '</div>' +
                '</div>';
    }

    // ── 半圓 SVG Gauge ──
    function buildGaugeHtml(props) {
        var cx = 100, cy = 110, r = 83;
        var fMin = props.fMin   || 0;
        var fMax = props.fMax   || 100;
        var szTitle = props.szTitle || '';
        var szTooltip = szTitle
            ? '<div class="scada-hover-label" style="display:none;position:absolute;bottom:4px;left:50%;' +
                    'transform:translateX(-50%);background:rgba(33,37,41,.85);color:#fff;' +
                    'font-size:11px;padding:3px 10px;border-radius:4px;white-space:nowrap;' +
                    'pointer-events:none;z-index:10;">' + escViewHtml(szTitle) + '</div>'
            : '';
        var szBgPath = 'M ' + (cx - r) + ' ' + cy + ' A ' + r + ' ' + r + ' 0 0 1 ' + (cx + r) + ' ' + cy;
        var szGaugeBg = (props.szBgColor && props.szBgColor !== 'transparent') ? props.szBgColor : 'transparent';

        if (props.isOffline) {
            return '<div style="position:relative;width:100%;height:100%;background:' + szGaugeBg + ';border-radius:4px;">' +
                '<svg viewBox="0 0 200 145" xmlns="http://www.w3.org/2000/svg" style="width:100%;height:100%;display:block;">' +
                    '<path d="' + szBgPath + '" fill="none" stroke="#e9ecef" stroke-width="15" stroke-linecap="round"/>' +
                    '<text x="100" y="96" text-anchor="middle" font-size="22" font-weight="700" fill="#dc3545" font-family="\'Segoe UI\',sans-serif">\u65b7\u7dda</text>' +
                    '<text x="100" y="113" text-anchor="middle" font-size="13" fill="#6c757d" font-family="\'Segoe UI\',sans-serif">' + (props.szUnit || '') + '</text>' +
                    '<text x="' + (cx - r) + '" y="' + (cy + 18) + '" text-anchor="middle" font-size="10" fill="#adb5bd">' + fMin + '</text>' +
                    '<text x="' + (cx + r) + '" y="' + (cy + 18) + '" text-anchor="middle" font-size="10" fill="#adb5bd">' + fMax + '</text>' +
                '</svg>' +
                szTooltip +
            '</div>';
        }

        var fVal = props.fValue || 0;
        var fRaw = (fVal - fMin) / ((fMax - fMin) || 1);
        var fPct = Math.max(0.001, Math.min(0.999, fRaw));
        var thetaEnd = (180 + fPct * 180) * Math.PI / 180;
        var ex = (cx + r * Math.cos(thetaEnd)).toFixed(2);
        var ey = (cy + r * Math.sin(thetaEnd)).toFixed(2);
        var szColor = props.szAlarmOverride || (props.szColor || '#00c0ff');
        var szValFill = props.szAlarmOverride || '#212529';
        var szArc = 'M ' + (cx - r) + ' ' + cy + ' A ' + r + ' ' + r + ' 0 0 1 ' + ex + ' ' + ey;
        return '<div style="position:relative;width:100%;height:100%;background:' + szGaugeBg + ';border-radius:4px;">' +
            '<svg viewBox="0 0 200 145" xmlns="http://www.w3.org/2000/svg" style="width:100%;height:100%;display:block;">' +
                '<path d="' + szBgPath + '" fill="none" stroke="#e9ecef" stroke-width="15" stroke-linecap="round"/>' +
                '<path d="' + szArc + '" fill="none" stroke="' + szColor + '" stroke-width="15" stroke-linecap="round"/>' +
                '<text x="100" y="96" text-anchor="middle" font-size="26" font-weight="700" fill="' + szValFill + '" font-family="\'Segoe UI\',sans-serif">' + Number(fVal).toFixed(1) + '</text>' +
                '<text x="100" y="113" text-anchor="middle" font-size="13" fill="#6c757d" font-family="\'Segoe UI\',sans-serif">' + (props.szUnit || '') + '</text>' +
                '<text x="' + (cx - r) + '" y="' + (cy + 18) + '" text-anchor="middle" font-size="10" fill="#adb5bd">' + fMin + '</text>' +
                '<text x="' + (cx + r) + '" y="' + (cy + 18) + '" text-anchor="middle" font-size="10" fill="#adb5bd">' + fMax + '</text>' +
            '</svg>' +
            szTooltip +
        '</div>';
    }
