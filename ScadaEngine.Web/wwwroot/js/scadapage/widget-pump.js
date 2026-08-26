// ============================================================
// scadapage/widget-pump.js — 水泵 SVG builder + Linear Gauge 拖拽控制
// ============================================================
// 純搬移自 scadapage.js（plan 2026-08-06-scadapage-js-split）。
// 不包 IIFE、global scope，與 js/designer/ 拆分同模式；
// 共享狀態集中於 state.js。原始 4-space 縮排刻意保留，
// 以保證 template literal 內容 byte-level 不變（純搬移原則）。
// 頂層僅註冊 document listener（拖拽），不呼叫跨檔函數，載入順序不敏感。
// ============================================================

    // ── 水泵 SVG ──
    function buildPumpViewHtml(props, szState, szFreqVal, szModeVal) {
        var szRunColor   = props.szRunColor   || '#28a745';
        var szStopColor  = props.szStopColor  || '#6c757d';
        var szFaultColor = props.szFaultColor || '#dc3545';
        var szCircleColor = szState === 'fault' ? szFaultColor
                          : szState === 'run'   ? szRunColor
                          : szStopColor;
        var szManualColor = props.szManualColor || '#ffc107';
        var szAutoColor   = props.szAutoColor   || '#0d6efd';
        var szBodyColor = '#555';
        if (props.szSidMode && szModeVal !== undefined && szModeVal !== '') {
            var bIsAuto = (szModeVal == 1 || szModeVal === '1' || szModeVal === true || szModeVal === 'true');
            szBodyColor = bIsAuto ? szAutoColor : szManualColor;
        }
        var szBg    = (props.szBgColor && props.szBgColor !== 'transparent') ? props.szBgColor : 'transparent';
        var szTitle = props.szTitle || '\u6c34\u6cf5';
        var szDir   = props.szOutletDir || 'right';

        var szTransform = szDir === 'left'  ? 'translate(120,0) scale(-1,1)'
                        : szDir === 'up'    ? 'rotate(-90,60,50)'
                        : '';

        var szSpinStyle = szState === 'run'
            ? ' style="transform-origin:60px 50px; animation: pump-spin 1.5s linear infinite;"'
            : '';

        var szStateText = szState === 'fault' ? '\u6545\u969c' : szState === 'run' ? '\u904b\u8f49\u4e2d' : '\u505c\u6b62';
        var szModeText = szModeVal !== undefined && szModeVal !== ''
            ? (szModeVal == 1 || szModeVal === '1' || szModeVal === 'true' ? '\u81ea\u52d5' : '\u624b\u52d5')
            : '';
        var szFreqText = szFreqVal !== undefined && szFreqVal !== '' && szFreqVal !== '--'
            ? parseFloat(szFreqVal).toFixed(1) + ' Hz' : '';

        var nFreqMax = parseFloat(props.nFreqMax) || 60;
        var bHasFreq = !!props.szSidFreq;
        var szViewBox = bHasFreq ? '0 0 170 110' : '0 0 120 110';
        var szContrastColor = (function () {
            var bg = szBg;
            if (!bg || bg === 'transparent') return '#333';
            var m = bg.match(/^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i);
            if (!m) return '#333';
            var lum = (parseInt(m[1], 16) * 299 + parseInt(m[2], 16) * 587 + parseInt(m[3], 16) * 114) / 1000;
            return lum > 128 ? '#333' : '#f0f0f0';
        })();
        var szGaugeHtml = '';
        if (bHasFreq) {
            var nBarTop = 22, nBarH = 68;
            var fFreq = (szFreqVal !== undefined && szFreqVal !== '' && szFreqVal !== '--')
                ? parseFloat(szFreqVal) : 0;
            var fRatio = Math.max(0, Math.min(1, fFreq / (nFreqMax || 1)));
            var nFillH = Math.round(nBarH * fRatio);
            var szGaugeColor = szState === 'fault' ? szFaultColor : '#17a2b8';
            var nFillY = nBarTop + nBarH - nFillH;
            szGaugeHtml =
                '<rect x="115" y="' + nBarTop + '" width="10" height="' + nBarH + '" rx="3" fill="#333" stroke="#555" stroke-width="1"/>' +
                '<rect class="pump-gauge-fill" x="115" y="' + nFillY + '" width="10" height="' + nFillH + '" rx="3" fill="' + szGaugeColor + '"/>' +
                '<text x="120" y="' + (nBarTop - 4) + '" text-anchor="middle" font-size="8" fill="' + szContrastColor + '">' + nFreqMax + '</text>' +
                '<text x="120" y="' + (nBarTop + nBarH + 10) + '" text-anchor="middle" font-size="8" fill="' + szContrastColor + '">0</text>' +
                (szFreqText ? '<text class="pump-gauge-text" x="130" y="' + (nFillY + nFillH / 2 + 3) + '" text-anchor="start" font-size="9" fill="' + szContrastColor + '" font-weight="600">' + szFreqText + '</text>' : '') +
                '<rect class="pump-gauge-handle" x="108" y="' + (nBarTop - 4) + '" width="24" height="' + (nBarH + 8) + '" fill="transparent" style="cursor:ns-resize;" />';
        }

        var szTooltipParts = [escViewHtml(szTitle), szStateText];
        if (szModeText) szTooltipParts.push(szModeText);
        if (szFreqText) szTooltipParts.push(szFreqText);

        // 兩個 M badge：泵本體（szCidStartStop）+ 變頻器（szCidFreqSet）各自獨立顯示
        // 有頻率 gauge 時：泵本體 badge 在左上、變頻器 badge 在右上；無 gauge 時：泵本體 badge 在右上
        var szCidSS = props.szCidStartStop || '';
        var szCidFQ = props.szCidFreqSet   || '';
        var szBadgeSS = '';
        var szBadgeFQ = '';
        if (szCidSS) {
            szBadgeSS = bHasFreq
                ? _buildModeBadgeHtml(szCidSS, 'left:-4px;right:auto;')
                : _buildModeBadgeHtml(szCidSS);
        }
        if (szCidFQ && bHasFreq) {
            szBadgeFQ = _buildModeBadgeHtml(szCidFQ);
        }

        return '<div style="position:relative;width:100%;height:100%;background:' + szBg + ';border-radius:4px;">' +
            '<svg viewBox="' + szViewBox + '" xmlns="http://www.w3.org/2000/svg" style="width:100%;height:100%;display:block;">' +
                '<rect x="38" y="86" width="44" height="6" rx="2" fill="#4a4a4a"/>' +
                '<rect x="38" y="92" width="44" height="3" rx="1" fill="#333"/>' +
                '<rect x="39" y="86" width="42" height="2" rx="1" fill="rgba(255,255,255,.12)"/>' +
                '<rect x="48" y="72" width="7" height="15" rx="1.5" fill="#4a4a4a" stroke="#3a3a3a" stroke-width=".5"/>' +
                '<rect x="65" y="72" width="7" height="15" rx="1.5" fill="#4a4a4a" stroke="#3a3a3a" stroke-width=".5"/>' +
                '<rect x="48" y="72" width="3.5" height="15" rx="1" fill="rgba(255,255,255,.08)"/>' +
                '<rect x="65" y="72" width="3.5" height="15" rx="1" fill="rgba(255,255,255,.08)"/>' +
                '<g transform="' + szTransform + '">' +
                    '<path d="M 60,22 L 106,22 L 106,37 L 86,37 A 30,30 0 1,1 60,22 Z" fill="rgba(0,0,0,.15)"/>' +
                    '<path d="M 60,20 L 105,20 L 105,35 L 86,35 A 30,30 0 1,1 60,20 Z" fill="' + szBodyColor + '" stroke="#3a3a3a" stroke-width="2" stroke-linejoin="round"/>' +
                    '<rect x="63" y="21" width="40" height="4" rx="1" fill="rgba(255,255,255,.15)"/>' +
                    '<rect x="103" y="18" width="4" height="19" rx="1" fill="' + szBodyColor + '" stroke="#3a3a3a" stroke-width="1"/>' +
                    '<line x1="104" y1="20" x2="106" y2="20" stroke="rgba(255,255,255,.2)" stroke-width=".8"/>' +
                    '<line x1="104" y1="35" x2="106" y2="35" stroke="rgba(0,0,0,.2)" stroke-width=".8"/>' +
                    '<g' + szSpinStyle + '>' +
                        '<circle cx="60" cy="50" r="12" fill="' + szCircleColor + '" stroke="#333" stroke-width="1.5"/>' +
                        '<path d="M 59,48 C 57,44 58.5,40.5 60,40 C 61.5,40.5 63,44 61,48 Z" fill="rgba(255,255,255,.4)"/>' +
                        '<path d="M 62.2,50.1 C 66.7,50.4 69,53.5 68.7,55 C 67.5,56 63.7,55.6 61.2,51.9 Z" fill="rgba(255,255,255,.4)"/>' +
                        '<path d="M 58.8,51.9 C 56.3,55.6 52.5,56 51.3,55 C 51,53.5 53.3,50.4 57.8,50.1 Z" fill="rgba(255,255,255,.4)"/>' +
                        '<circle cx="60" cy="50" r="3.5" fill="#3a3a3a" stroke="#2a2a2a" stroke-width="1"/>' +
                        '<circle cx="59" cy="49" r="1.2" fill="rgba(255,255,255,.25)"/>' +
                    '</g>' +
                '</g>' + szGaugeHtml +
            '</svg>' +
            szBadgeSS +
            szBadgeFQ +
            '<div class="scada-hover-label"' +
                 ' style="display:none;position:absolute;bottom:4px;left:50%;' +
                        'transform:translateX(-50%);white-space:nowrap;' +
                        'background:rgba(33,37,41,.85);color:#fff;' +
                        'font-size:11px;padding:3px 10px;border-radius:4px;' +
                        'pointer-events:none;z-index:10;">' +
                szTooltipParts.join(' \u2014 ') +
            '</div>' +
            '</div>';
    }

    // ── 水泵 Linear Gauge 拖拽控制 ──
    var _pumpGaugeDrag = null;
    var GAUGE_BAR_TOP = 22, GAUGE_BAR_H = 68;

    document.addEventListener('mousedown', function (e) {
        var handle = e.target.closest('.pump-gauge-handle');
        if (!handle) return;
        // 頻率條拖曳共用：水泵（.scada-pump）與 VFD 型馬達設備（.scada-motor）
        var pumpEl = handle.closest('.scada-pump, .scada-motor');
        if (!pumpEl) return;
        var szCid = pumpEl.dataset.cidFreqSet;
        if (!szCid) return;
        e.preventDefault();
        e.stopPropagation();
        var svgEl = pumpEl.querySelector('svg');
        var nFreqMax = parseFloat(pumpEl.dataset.nFreqMax) || 60;
        var nFqMin   = parseFloat(pumpEl.dataset.nFreqSetMin) || 0;
        var nFqMax   = parseFloat(pumpEl.dataset.nFreqSetMax) || 60;
        var szTitle  = pumpEl.dataset.szTitle || '\u6c34\u6cf5';
        var fInitFreq = _pumpGaugeYToFreq(e, svgEl, nFreqMax, nFqMin, nFqMax);
        _pumpGaugeDrag = { el: pumpEl, svgEl: svgEl, szCid: szCid, szTitle: szTitle, nFreqMax: nFreqMax, nFqMin: nFqMin, nFqMax: nFqMax, fCurFreq: fInitFreq };
        _pumpGaugeUpdateVisual(svgEl, fInitFreq, nFreqMax);
    });

    document.addEventListener('mousemove', function (e) {
        if (!_pumpGaugeDrag) return;
        e.preventDefault();
        var fFreq = _pumpGaugeYToFreq(e, _pumpGaugeDrag.svgEl, _pumpGaugeDrag.nFreqMax, _pumpGaugeDrag.nFqMin, _pumpGaugeDrag.nFqMax);
        _pumpGaugeDrag.fCurFreq = fFreq;
        _pumpGaugeUpdateVisual(_pumpGaugeDrag.svgEl, fFreq, _pumpGaugeDrag.nFreqMax);
    });

    document.addEventListener('mouseup', function (e) {
        if (!_pumpGaugeDrag) return;
        var szCid = _pumpGaugeDrag.szCid;
        var szTitle = _pumpGaugeDrag.szTitle;
        var fCurFreq = _pumpGaugeDrag.fCurFreq;
        _pumpGaugeDrag = null;
        var fRound = Math.round(fCurFreq * 10) / 10;
        (async function () {
            try {
                var resp = await fetch('/api/control/write', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ cid: szCid, value: fRound, actionType: 'pump_freq', displayName: szTitle })
                });
                var result = await resp.json();
                if (result.success) {
                    _aoManualValueMap[szCid] = { value: fRound, isAuto: false };
                    _toggleModeBadge(szCid, false);
                    showControlToast('\u5df2\u9001\u51fa\u983b\u7387\u8a2d\u5b9a\uff1a' + szTitle + ' \u2192 ' + fRound + ' Hz');
                } else {
                    alert('\u983b\u7387\u8a2d\u5b9a\u5931\u6557\uff1a' + (result.error || '\u672a\u77e5\u932f\u8aa4'));
                }
            } catch (err) {
                alert('\u983b\u7387\u8a2d\u5b9a\u8acb\u6c42\u5931\u6557\uff1a' + err.message);
            }
        })();
    });

    function _pumpGaugeYToFreq(e, svgEl, nFreqMax, nFqMin, nFqMax) {
        var rect = svgEl.getBoundingClientRect();
        var vb = svgEl.viewBox.baseVal;
        var scaleY = vb.height / rect.height;
        var svgY = (e.clientY - rect.top) * scaleY;
        var ratio = (GAUGE_BAR_TOP + GAUGE_BAR_H - svgY) / GAUGE_BAR_H;
        var fRaw = ratio * nFreqMax;
        return Math.max(nFqMin, Math.min(nFqMax, fRaw));
    }

    function _pumpGaugeUpdateVisual(svgEl, fFreq, nFreqMax) {
        var fRatio = Math.max(0, Math.min(1, fFreq / (nFreqMax || 1)));
        var nFillH = Math.round(GAUGE_BAR_H * fRatio);
        var nFillY = GAUGE_BAR_TOP + GAUGE_BAR_H - nFillH;
        var fillRect = svgEl.querySelector('.pump-gauge-fill');
        if (fillRect) {
            fillRect.setAttribute('y', nFillY);
            fillRect.setAttribute('height', nFillH);
        }
        var textEl = svgEl.querySelector('.pump-gauge-text');
        var szText = fFreq.toFixed(1) + ' Hz';
        if (textEl) {
            textEl.textContent = szText;
            textEl.setAttribute('y', nFillY + nFillH / 2 + 3);
        } else {
            var ns = 'http://www.w3.org/2000/svg';
            textEl = document.createElementNS(ns, 'text');
            textEl.classList.add('pump-gauge-text');
            textEl.setAttribute('x', '130');
            textEl.setAttribute('y', nFillY + nFillH / 2 + 3);
            textEl.setAttribute('text-anchor', 'start');
            textEl.setAttribute('font-size', '9');
            textEl.setAttribute('fill', '#333');
            textEl.setAttribute('font-weight', '600');
            textEl.textContent = szText;
            svgEl.appendChild(textEl);
        }
    }
