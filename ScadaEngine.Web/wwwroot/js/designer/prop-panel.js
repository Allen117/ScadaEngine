// ============================================================
// Designer — 屬性面板
// ============================================================
// 內容：renderPropPanel（533 行，包 9 種 widget 屬性 UI）、文字屬性面板、
// DI/AI 標籤同步、setProp/setPos/setSize、表格 cell 屬性面板、
// 重選點位後建立 widget 的 createXxxWithPoint、bg-transparent 切換。
// 依賴：state.js / widget-defs.js / widget-core.js / picker.js。
// 注意：本檔的 HTML template 內含大量 oninput="setProp(...)"、
// onclick="rerouteXxxPoint()" 字面 global 函式名，picker.js 內也會呼叫
// 本檔的 createXxxWithPoint — 循環依賴靠 global hoisting 解，
// 切勿改成 const xxx = function() {} 形式。
// ============================================================

// ============================================================
// 屬性面板
// ============================================================
function renderPropPanel(el) {
    const szType = el.dataset.type;
    const props  = el.widgetProps;

    // 文字 Widget 使用獨立屬性面板
    if (szType === 'text') {
        document.getElementById('propBody').innerHTML = buildTextPropHtml(el, props);
        return;
    }

    // 共用欄位
    const szUnboundLabel = `<span style="color:#888;font-size:11px;">${escHtml(t('designer.widget.unbound'))}</span>`;
    let szHtml = `
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.title'))}</label>
            <input type="text" value="${escHtml(props.szTitle)}"
                   oninput="setProp('szTitle', this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.pos_x'))}</label>
            <input type="number" id="pX" value="${parseInt(el.style.left)}" step="20"
                   oninput="setPos('left', +this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.pos_y'))}</label>
            <input type="number" id="pY" value="${parseInt(el.style.top)}" step="20"
                   oninput="setPos('top', +this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.width'))}</label>
            <input type="number" id="pW" value="${el.offsetWidth}" step="20"
                   oninput="setSize('width', +this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.height'))}</label>
            <input type="number" id="pH" value="${el.offsetHeight}" step="20"
                   oninput="setSize('height', +this.value)">
        </div>
        <hr class="prop-divider">
    `;

    if (szType === 'table') {
        szHtml += `
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.rows'))}</label>
            <input type="number" value="${props.nRows}" min="1" max="20"
                   oninput="setProp('nRows', +this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.cols'))}</label>
            <input type="number" value="${props.nCols}" min="1"
                   oninput="setProp('nCols', +this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.header_color'))}</label>
            <input type="color" value="${props.szHeaderColor}"
                   oninput="setProp('szHeaderColor', this.value)">
        </div>`;
    } else if (szType === 'gauge') {
        const szSidLabel = props.szPointName
            ? `<span style="font-size:12px;color:#c8c8c8;">${escHtml(props.szPointName)}</span>`
            : szUnboundLabel;
        const isBgTransparent = !props.szBgColor || props.szBgColor === 'transparent';
        const szBgColorVal    = isBgTransparent ? '#ffffff' : props.szBgColor;
        szHtml += `
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.bound_point'))}</label>
            <div style="display:flex;align-items:center;gap:6px;flex-wrap:wrap;">
                ${szSidLabel}
                <button class="btn btn-outline-warning btn-sm py-0 px-2" style="font-size:11px;"
                        onclick="rerouteGaugePoint()">
                    <i class="fas fa-exchange-alt me-1"></i>${escHtml(t('designer.prop.reselect'))}
                </button>
            </div>
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.current_value'))}</label>
            <input type="number" value="${props.fValue}" step="0.1"
                   oninput="setProp('fValue', +this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.min_value'))}</label>
            <input type="number" value="${props.fMin}"
                   oninput="setProp('fMin', +this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.max_value'))}</label>
            <input type="number" value="${props.fMax}"
                   oninput="setProp('fMax', +this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.unit_from_point'))}</label>
            <input type="text" value="${escHtml(props.szUnit)}" readonly
                   style="background:#f0f0f0;color:#666;cursor:not-allowed;">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.main_color_normal'))}</label>
            <input type="color" value="${props.szColor}"
                   oninput="setProp('szColor', this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.high_color'))}</label>
            <input type="color" value="${props.szHighColor || '#dc3545'}"
                   oninput="setProp('szHighColor', this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.low_color'))}</label>
            <input type="color" value="${props.szLowColor || '#fd7e14'}"
                   oninput="setProp('szLowColor', this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.background_color'))}</label>
            <label style="display:inline-flex;align-items:center;gap:3px;font-size:11px;color:#9d9d9d;
                          font-weight:normal;text-transform:none;letter-spacing:0;cursor:pointer;margin-top:4px;white-space:nowrap;">
                <input type="checkbox" style="cursor:pointer;width:auto;"
                       ${isBgTransparent ? 'checked' : ''}
                       onchange="onGaugeBgTransparentChange(this.checked)">
                ${escHtml(t('designer.prop.transparent_bg'))}
            </label>
            <input type="color" id="gaugeBgPicker" value="${szBgColorVal}"
                   style="width:100%;margin-top:4px;${isBgTransparent ? 'display:none;' : ''}"
                   oninput="setProp('szBgColor', this.value)">
        </div>
        `;
    } else if (szType === 'controlBtn') {
        const szCidLabel = props.szPointName
            ? `<span style="font-size:12px;color:#c8c8c8;">${escHtml(props.szPointName)}</span>`
            : szUnboundLabel;
        szHtml += `
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.bound_cid'))}</label>
            <div style="display:flex;align-items:center;gap:6px;flex-wrap:wrap;">
                ${szCidLabel}
                <button class="btn btn-outline-success btn-sm py-0 px-2" style="font-size:11px;"
                        onclick="rerouteControlBtnPoint()">
                    <i class="fas fa-exchange-alt me-1"></i>${escHtml(t('designer.prop.reselect'))}
                </button>
            </div>
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.btn_label'))}</label>
            <input type="text" value="${escHtml(props.szBtnLabel || t('designer.default.controlBtn_label'))}"
                   oninput="setProp('szBtnLabel', this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.btn_icon'))}</label>
            <select style="width:100%;padding:4px 6px;border:1px solid #555;border-radius:4px;
                           background:#2b2b2b;color:#e0e0e0;font-size:12px;"
                    onchange="setProp('szBtnIcon', this.value)">
                ${[
                    { v: 'fa-hand-pointer', tk: 'designer.prop.btn_icon.finger' },
                    { v: 'fa-play',         tk: 'designer.prop.btn_icon.play' },
                    { v: 'fa-power-off',    tk: 'designer.prop.btn_icon.power' },
                    { v: 'fa-paper-plane',  tk: 'designer.prop.btn_icon.send' },
                    { v: 'fa-bolt',         tk: 'designer.prop.btn_icon.bolt' }
                ].map(o => `<option value="${o.v}" ${(props.szBtnIcon || 'fa-hand-pointer') === o.v ? 'selected' : ''}>${escHtml(t(o.tk))}</option>`).join('')}
            </select>
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.ctrl_value'))}</label>
            <input type="number" value="${props.fCtrlValue ?? 1}" step="any"
                   oninput="setProp('fCtrlValue', +this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.btn_color'))}</label>
            <input type="color" value="${props.szBtnColor || '#198754'}"
                   oninput="setProp('szBtnColor', this.value)">
        </div>`;
    } else if (szType === 'realtimeValue') {
        const szSidLabel = props.szPointName
            ? `<span style="font-size:12px;color:#c8c8c8;">${escHtml(props.szPointName)}</span>`
            : szUnboundLabel;
        const isBgTransparent = !props.szBgColor || props.szBgColor === 'transparent';
        const szBgColorVal    = isBgTransparent ? '#ffffff' : props.szBgColor;
        szHtml += `
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.bound_sid'))}</label>
            <div style="display:flex;align-items:center;gap:6px;flex-wrap:wrap;">
                ${szSidLabel}
                <button class="btn btn-outline-danger btn-sm py-0 px-2" style="font-size:11px;"
                        onclick="rerouteRealtimeValuePoint()">
                    <i class="fas fa-exchange-alt me-1"></i>${escHtml(t('designer.prop.reselect'))}
                </button>
            </div>
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.font_size'))}</label>
            <input type="number" value="${props.nFontSize || 28}" min="12" max="120" step="2"
                   oninput="setProp('nFontSize', +this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.font_color'))}</label>
            <input type="color" value="${props.szFontColor || '#212529'}"
                   oninput="setProp('szFontColor', this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.unit'))}</label>
            <input type="text" value="${escHtml(props.szUnit || '')}"
                   oninput="setProp('szUnit', this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.high_color'))}</label>
            <input type="color" value="${props.szHighColor || '#dc3545'}"
                   oninput="setProp('szHighColor', this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.low_color'))}</label>
            <input type="color" value="${props.szLowColor || '#fd7e14'}"
                   oninput="setProp('szLowColor', this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.background_color'))}</label>
            <label style="display:inline-flex;align-items:center;gap:3px;font-size:11px;color:#9d9d9d;
                          font-weight:normal;text-transform:none;letter-spacing:0;cursor:pointer;margin-top:4px;white-space:nowrap;">
                <input type="checkbox" style="cursor:pointer;width:auto;"
                       ${isBgTransparent ? 'checked' : ''}
                       onchange="onRtValBgTransparentChange(this.checked)">
                ${escHtml(t('designer.prop.transparent_bg'))}
            </label>
            <input type="color" id="rtValBgPicker" value="${szBgColorVal}"
                   style="width:100%;margin-top:4px;${isBgTransparent ? 'display:none;' : ''}"
                   oninput="setProp('szBgColor', this.value)">
        </div>
        `;
    } else if (szType === 'diPoint') {
        const bSchedule = props.nScheduleId != null;
        let szBindRow;
        if (bSchedule) {
            szBindRow = `
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.source_schedule'))}</label>
            <div style="display:flex;align-items:center;gap:6px;flex-wrap:wrap;">
                <span style="font-size:12px;color:#7eb6ff;"><i class="fas fa-calendar-alt me-1"></i>${escHtml(props.szScheduleName || '')}</span>
                <button class="btn btn-outline-success btn-sm py-0 px-2" style="font-size:11px;"
                        onclick="rerouteDiPointPoint()">
                    <i class="fas fa-exchange-alt me-1"></i>${escHtml(t('designer.prop.reselect'))}
                </button>
            </div>
        </div>`;
        } else {
            const szSidLabel = props.szPointName
                ? `<span style="font-size:12px;color:#c8c8c8;">${escHtml(props.szPointName)}</span>`
                : szUnboundLabel;
            szBindRow = `
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.bound_sid'))}</label>
            <div style="display:flex;align-items:center;gap:6px;flex-wrap:wrap;">
                ${szSidLabel}
                <button class="btn btn-outline-success btn-sm py-0 px-2" style="font-size:11px;"
                        onclick="rerouteDiPointPoint()">
                    <i class="fas fa-exchange-alt me-1"></i>${escHtml(t('designer.prop.reselect'))}
                </button>
            </div>
        </div>`;
        }
        const isBgTransparent = !props.szBgColor || props.szBgColor === 'transparent';
        const szBgColorVal    = isBgTransparent ? '#ffffff' : props.szBgColor;
        const szMode = props.szDisplayMode || 'indicator';
        szHtml += szBindRow;
        szHtml += `
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.display_mode'))}</label>
            <select onchange="setProp('szDisplayMode', this.value)"
                    style="width:100%;background:#3c3c3c;border:1px solid #555;color:#d4d4d4;
                           padding:4px 6px;font-size:12px;border-radius:3px;">
                <option value="indicator" ${szMode === 'indicator' ? 'selected' : ''}>${escHtml(t('designer.prop.display_mode.indicator'))}</option>
                <option value="text"      ${szMode === 'text'      ? 'selected' : ''}>${escHtml(t('designer.prop.display_mode.text'))}</option>
            </select>
        </div>
        <hr class="prop-divider">
        ${szMode === 'indicator' ? `
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.indicator_size'))}</label>
            <input type="number" value="${props.nIndicatorSize || 28}" min="12" max="80" step="2"
                   oninput="setProp('nIndicatorSize', +this.value)">
        </div>` : `
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.font_size'))}</label>
            <input type="number" value="${props.nFontSize || 24}" min="10" max="120" step="1"
                   oninput="setProp('nFontSize', +this.value)">
        </div>`}
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.on_color'))}</label>
            <input type="color" value="${props.szOnColor || '#28a745'}"
                   oninput="setProp('szOnColor', this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.off_color'))}</label>
            <input type="color" value="${props.szOffColor || '#6c757d'}"
                   oninput="setProp('szOffColor', this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.on_label'))}</label>
            <input type="text" value="${escHtml(props.szOnLabel || 'ON')}"
                   oninput="setProp('szOnLabel', this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.off_label'))}</label>
            <input type="text" value="${escHtml(props.szOffLabel || 'OFF')}"
                   oninput="setProp('szOffLabel', this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t(szMode === 'text' ? 'designer.prop.alarm_text_color' : 'designer.prop.alarm_color'))}</label>
            <input type="color" value="${props.szAlarmColor || '#dc3545'}"
                   oninput="setProp('szAlarmColor', this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.background_color'))}</label>
            <label style="display:inline-flex;align-items:center;gap:3px;font-size:11px;color:#9d9d9d;
                          font-weight:normal;text-transform:none;letter-spacing:0;cursor:pointer;margin-top:4px;white-space:nowrap;">
                <input type="checkbox" style="cursor:pointer;width:auto;"
                       ${isBgTransparent ? 'checked' : ''}
                       onchange="onDiPointBgTransparentChange(this.checked)">
                ${escHtml(t('designer.prop.transparent_bg'))}
            </label>
            <input type="color" id="diPointBgPicker" value="${szBgColorVal}"
                   style="width:100%;margin-top:4px;${isBgTransparent ? 'display:none;' : ''}"
                   oninput="setProp('szBgColor', this.value)">
        </div>
        `;
    } else if (szType === 'aoPoint') {
        const szCidLabel = props.szPointName
            ? `<span style="font-size:12px;color:#c8c8c8;">${escHtml(props.szPointName)}</span>`
            : szUnboundLabel;
        szHtml += `
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.bound_cid'))}</label>
            <div style="display:flex;align-items:center;gap:6px;flex-wrap:wrap;">
                ${szCidLabel}
                <button class="btn btn-outline-info btn-sm py-0 px-2" style="font-size:11px;"
                        onclick="rerouteAoPointPoint()">
                    <i class="fas fa-exchange-alt me-1"></i>${escHtml(t('designer.prop.reselect'))}
                </button>
            </div>
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.display_name'))}</label>
            <input type="text" value="${escHtml(props.szDisplayName || props.szTitle || t('designer.default.aoPoint_title'))}"
                   oninput="setProp('szDisplayName', this.value)">
        </div>
        <hr class="prop-divider">
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.default_write_value'))}</label>
            <input type="number" value="${props.fWriteValue ?? 0}" step="${props.fStep ?? 1}"
                   oninput="setProp('fWriteValue', +this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.min_value'))}</label>
            <input type="number" value="${props.fMin ?? 0}" step="any"
                   oninput="setProp('fMin', +this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.max_value'))}</label>
            <input type="number" value="${props.fMax ?? 100}" step="any"
                   oninput="setProp('fMax', +this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.step_value'))}</label>
            <input type="number" value="${props.fStep ?? 1}" min="0.001" step="any"
                   oninput="setProp('fStep', +this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.decimal_places'))}</label>
            <input type="number" value="${props.nDecimalPlaces ?? 2}" min="0" max="6" step="1"
                   oninput="setProp('nDecimalPlaces', +this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.unit'))}</label>
            <input type="text" value="${escHtml(props.szUnit || '')}"
                   oninput="setProp('szUnit', this.value)">
        </div>
        <hr class="prop-divider">
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.menu_manual'))}</label>
            <input type="text" value="${escHtml(props.szMenuManualLabel ?? '')}"
                   placeholder="${escHtml(t('designer.prop.menu_blank_hint'))}"
                   oninput="setProp('szMenuManualLabel', this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.menu_auto'))}</label>
            <input type="text" value="${escHtml(props.szMenuAutoLabel ?? '')}"
                   placeholder="${escHtml(t('designer.prop.menu_blank_hint'))}"
                   oninput="setProp('szMenuAutoLabel', this.value)">
        </div>
        <hr class="prop-divider">
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.font_size'))}</label>
            <input type="number" value="${props.nFontSize || 16}" min="10" max="60" step="1"
                   oninput="setProp('nFontSize', +this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.font_color'))}</label>
            <input type="color" value="${props.szFontColor || '#ffffff'}"
                   oninput="setProp('szFontColor', this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.block_color'))}</label>
            <input type="color" value="${props.szBlockColor || '#0d6efd'}"
                   oninput="setProp('szBlockColor', this.value)">
        </div>`;
    } else if (szType === 'doPoint') {
        const szCidLabel = props.szPointName
            ? `<span style="font-size:12px;color:#c8c8c8;">${escHtml(props.szPointName)}</span>`
            : szUnboundLabel;
        szHtml += `
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.bound_cid'))}</label>
            <div style="display:flex;align-items:center;gap:6px;flex-wrap:wrap;">
                ${szCidLabel}
                <button class="btn btn-outline-warning btn-sm py-0 px-2" style="font-size:11px;"
                        onclick="rerouteDoPointPoint()">
                    <i class="fas fa-exchange-alt me-1"></i>${escHtml(t('designer.prop.reselect'))}
                </button>
            </div>
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.display_name'))}</label>
            <input type="text" value="${escHtml(props.szDisplayName || props.szTitle || t('designer.default.doPoint_title'))}"
                   oninput="setProp('szDisplayName', this.value)">
        </div>
        <hr class="prop-divider">
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.on_write_value'))}</label>
            <input type="number" value="${props.nOnValue ?? 1}" step="any"
                   oninput="setProp('nOnValue', +this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.off_write_value'))}</label>
            <input type="number" value="${props.nOffValue ?? 0}" step="any"
                   oninput="setProp('nOffValue', +this.value)">
        </div>
        <hr class="prop-divider">
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.menu_on'))}</label>
            <input type="text" value="${escHtml(props.szMenuOnLabel ?? '')}"
                   placeholder="${escHtml(t('designer.prop.menu_blank_hint'))}"
                   oninput="setProp('szMenuOnLabel', this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.menu_off'))}</label>
            <input type="text" value="${escHtml(props.szMenuOffLabel ?? '')}"
                   placeholder="${escHtml(t('designer.prop.menu_blank_hint'))}"
                   oninput="setProp('szMenuOffLabel', this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.menu_auto'))}</label>
            <input type="text" value="${escHtml(props.szMenuAutoLabel ?? '')}"
                   placeholder="${escHtml(t('designer.prop.menu_blank_hint'))}"
                   oninput="setProp('szMenuAutoLabel', this.value)">
        </div>
        <hr class="prop-divider">
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.font_size'))}</label>
            <input type="number" value="${props.nFontSize || 16}" min="10" max="60" step="1"
                   oninput="setProp('nFontSize', +this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.font_color'))}</label>
            <input type="color" value="${props.szFontColor || '#212529'}"
                   oninput="setProp('szFontColor', this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.block_color'))}</label>
            <input type="color" value="${props.szBlockColor || '#0d6efd'}"
                   oninput="setProp('szBlockColor', this.value)">
        </div>`;
    } else if (szType === 'pump') {
        const isBgTransparent = !props.szBgColor || props.szBgColor === 'transparent';
        const szBgColorVal    = isBgTransparent ? '#ffffff' : props.szBgColor;

        // 產生綁定欄位 HTML 的輔助函式
        function pumpBindRow(szLabel, szSidKey, szNameKey, szBtnClass) {
            const szName = props[szNameKey];
            const szDisp = szName
                ? `<span style="font-size:12px;color:#c8c8c8;">${escHtml(szName)}</span>`
                : szUnboundLabel;
            return `<div class="prop-group">
                <label>${escHtml(szLabel)}</label>
                <div style="display:flex;align-items:center;gap:6px;flex-wrap:wrap;">
                    ${szDisp}
                    <button class="btn ${szBtnClass} btn-sm py-0 px-2" style="font-size:11px;"
                            onclick="reroutePumpBinding('${szSidKey}','${szNameKey}')">
                        <i class="fas fa-exchange-alt me-1"></i>${escHtml(t('designer.prop.reselect'))}
                    </button>
                </div>
            </div>`;
        }

        szHtml += `
        <div style="font-size:11px;color:#aaa;margin-bottom:4px;letter-spacing:1px;">${escHtml(t('designer.prop.pump.sid_monitor'))}</div>
        ${pumpBindRow(t('designer.prop.pump.run_status'),   'szSidRun',   'szRunName',   'btn-outline-info')}
        ${pumpBindRow(t('designer.prop.pump.fault_status'), 'szSidFault', 'szFaultName', 'btn-outline-danger')}
        ${pumpBindRow(t('designer.prop.pump.mode_status'),  'szSidMode',  'szModeName',  'btn-outline-warning')}
        ${pumpBindRow(t('designer.prop.pump.frequency'),    'szSidFreq',  'szFreqName',  'btn-outline-info')}
        <hr class="prop-divider">
        <div style="font-size:11px;color:#aaa;margin-bottom:4px;letter-spacing:1px;">${escHtml(t('designer.prop.pump.cid_control'))}</div>
        ${pumpBindRow(t('designer.prop.pump.start_stop'), 'szCidStartStop', 'szStartStopName', 'btn-outline-success')}
        ${pumpBindRow(t('designer.prop.pump.freq_set'),   'szCidFreqSet',   'szFreqSetName',   'btn-outline-success')}
        <hr class="prop-divider">
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.pump.outlet_dir'))}</label>
            <select style="width:100%;padding:4px 6px;border:1px solid #555;border-radius:4px;
                           background:#2b2b2b;color:#e0e0e0;font-size:12px;"
                    onchange="setProp('szOutletDir', this.value)">
                <option value="right" ${(props.szOutletDir || 'right') === 'right' ? 'selected' : ''}>${escHtml(t('designer.prop.pump.outlet_right'))}</option>
                <option value="left"  ${props.szOutletDir === 'left'  ? 'selected' : ''}>${escHtml(t('designer.prop.pump.outlet_left'))}</option>
                <option value="up"    ${props.szOutletDir === 'up'    ? 'selected' : ''}>${escHtml(t('designer.prop.pump.outlet_up'))}</option>
            </select>
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.pump.run_color'))}</label>
            <input type="color" value="${props.szRunColor || '#28a745'}"
                   oninput="setProp('szRunColor', this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.pump.stop_color'))}</label>
            <input type="color" value="${props.szStopColor || '#6c757d'}"
                   oninput="setProp('szStopColor', this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.pump.fault_color'))}</label>
            <input type="color" value="${props.szFaultColor || '#dc3545'}"
                   oninput="setProp('szFaultColor', this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.pump.manual_color'))}</label>
            <input type="color" value="${props.szManualColor || '#ffc107'}"
                   oninput="setProp('szManualColor', this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.pump.auto_color'))}</label>
            <input type="color" value="${props.szAutoColor || '#0d6efd'}"
                   oninput="setProp('szAutoColor', this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.background_color'))}</label>
            <label style="display:inline-flex;align-items:center;gap:3px;font-size:11px;color:#9d9d9d;
                          font-weight:normal;text-transform:none;letter-spacing:0;cursor:pointer;margin-top:4px;white-space:nowrap;">
                <input type="checkbox" style="cursor:pointer;width:auto;"
                       ${isBgTransparent ? 'checked' : ''}
                       onchange="onPumpBgTransparentChange(this.checked)">
                ${escHtml(t('designer.prop.transparent_bg'))}
            </label>
            <input type="color" id="pumpBgPicker" value="${szBgColorVal}"
                   style="width:100%;margin-top:4px;${isBgTransparent ? 'display:none;' : ''}"
                   oninput="setProp('szBgColor', this.value)">
        </div>`;
    }

    document.getElementById('propBody').innerHTML = szHtml;
}

// ============================================================
// 文字 Widget 屬性面板 HTML
// ============================================================
function buildTextPropHtml(el, props) {
    const szFontFamilyOpts = [
        { val: 'inherit',                             label: t('designer.prop.text.font.default') },
        { val: "'Microsoft JhengHei', sans-serif",    label: t('designer.prop.text.font.msjh') },
        { val: "'Noto Sans TC', sans-serif",          label: 'Noto Sans TC' },
        { val: "'Arial', sans-serif",                 label: 'Arial' },
        { val: "'Times New Roman', serif",            label: 'Times New Roman' },
        { val: "'Courier New', monospace",            label: 'Courier New' },
    ].map(o => `<option value="${escHtml(o.val)}" ${props.szFontFamily === o.val ? 'selected' : ''}>${escHtml(o.label)}</option>`).join('');

    const isBgTransparent = props.szBgColor === 'transparent';
    const szBgColorVal    = isBgTransparent ? '#ffffff' : props.szBgColor;

    return `
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.text_content'))}</label>
            <textarea rows="3" style="width:100%;background:#3c3c3c;border:1px solid #555;
                      color:#d4d4d4;padding:4px 6px;font-size:12px;border-radius:3px;
                      outline:none;resize:vertical;box-sizing:border-box;"
                      oninput="setProp('szText', this.value)">${escHtml(props.szText)}</textarea>
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.pos_x'))}</label>
            <input type="number" id="pX" value="${parseInt(el.style.left)}" step="20"
                   oninput="setPos('left', +this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.pos_y'))}</label>
            <input type="number" id="pY" value="${parseInt(el.style.top)}" step="20"
                   oninput="setPos('top', +this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.width'))}</label>
            <input type="number" id="pW" value="${el.offsetWidth}" step="20"
                   oninput="setSize('width', +this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.height'))}</label>
            <input type="number" id="pH" value="${el.offsetHeight}" step="20"
                   oninput="setSize('height', +this.value)">
        </div>
        <hr class="prop-divider">
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.font_size'))}</label>
            <input type="number" value="${props.nFontSize}" min="8" max="200" step="2"
                   oninput="setProp('nFontSize', +this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.font_color'))}</label>
            <input type="color" value="${props.szFontColor}"
                   oninput="setProp('szFontColor', this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.font_family'))}</label>
            <select oninput="setProp('szFontFamily', this.value)">${szFontFamilyOpts}</select>
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.text.bold'))}</label>
            <select oninput="setProp('szFontWeight', this.value)">
                <option value="normal" ${props.szFontWeight === 'normal' ? 'selected' : ''}>${escHtml(t('designer.prop.text.bold.normal'))}</option>
                <option value="bold"   ${props.szFontWeight === 'bold'   ? 'selected' : ''}>${escHtml(t('designer.prop.text.bold.bold'))}</option>
            </select>
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.text.italic'))}</label>
            <select onchange="setProp('isItalic', this.value === 'true')">
                <option value="false" ${!props.isItalic ? 'selected' : ''}>${escHtml(t('designer.prop.text.italic.no'))}</option>
                <option value="true"  ${props.isItalic  ? 'selected' : ''}>${escHtml(t('designer.prop.text.italic.yes'))}</option>
            </select>
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.background_color'))}</label>
            <label style="display:inline-flex;align-items:center;gap:3px;font-size:11px;color:#9d9d9d;
                          font-weight:normal;text-transform:none;letter-spacing:0;cursor:pointer;margin-top:4px;white-space:nowrap;">
                <input type="checkbox" style="cursor:pointer;width:auto;"
                       ${isBgTransparent ? 'checked' : ''}
                       onchange="onTextBgTransparentChange(this.checked)">
                ${escHtml(t('designer.prop.transparent_bg'))}
            </label>
            <input type="color" id="txtBgColorPicker" value="${szBgColorVal}"
                   style="width:100%;margin-top:4px;${isBgTransparent ? 'display:none;' : ''}"
                   oninput="setProp('szBgColor', this.value)">
        </div>
    `;
}

function onTextBgTransparentChange(isChecked) {
    if (isChecked) {
        setProp('szBgColor', 'transparent');
        const picker = document.getElementById('txtBgColorPicker');
        if (picker) picker.style.display = 'none';
    } else {
        const picker = document.getElementById('txtBgColorPicker');
        const szColor = picker ? picker.value : '#ffffff';
        setProp('szBgColor', szColor);
        if (picker) picker.style.display = '';
    }
}

// 從單一 widget state (props) 中檢查是否有同 SID 的 DI 標籤
function _checkDiLabelsInProps(szType, props, szSid) {
    if (szType === 'diPoint' && props.szSid === szSid) {
        if (props.szOnLabel !== 'ON' || props.szOffLabel !== 'OFF') {
            return { szOnLabel: props.szOnLabel, szOffLabel: props.szOffLabel };
        }
    }
    if (szType === 'table' && props.arrCells) {
        for (const row of props.arrCells) {
            for (const cell of row) {
                if (cell && cell.szSid === szSid && cell.szPointType === 'DI') {
                    if (cell.szOnLabel !== 'ON' || cell.szOffLabel !== 'OFF') {
                        return { szOnLabel: cell.szOnLabel, szOffLabel: cell.szOffLabel };
                    }
                }
            }
        }
    }
    return null;
}

// 在所有頁面（當前畫布 + arrPageTree 其他頁面）中，找出同 SID 的 DI ON/OFF 標籤
function _findDiLabelsForSid(szSid) {
    if (!szSid) return null;
    // 1. 搜尋當前畫布 DOM
    for (const el of canvas.querySelectorAll('.canvas-widget')) {
        if (!el.widgetProps) continue;
        const found = _checkDiLabelsInProps(el.dataset.type, el.widgetProps, szSid);
        if (found) return found;
    }
    // 2. 搜尋其他頁面的 arrWidgetState（遞迴遍歷 arrPageTree）
    function searchTree(pages) {
        for (const page of pages) {
            if (page.szId !== szCurrentPageId && page.arrWidgetState) {
                for (const ws of page.arrWidgetState) {
                    const found = _checkDiLabelsInProps(ws.szType, ws.props || {}, szSid);
                    if (found) return found;
                }
            }
            if (page.arrChildren) {
                const found = searchTree(page.arrChildren);
                if (found) return found;
            }
        }
        return null;
    }
    return searchTree(arrPageTree);
}

// 同步 ON/OFF 標籤到所有頁面中綁定同一個 SID 的 DI widget 與 table cell
function _syncDiLabelsToSid(szSid, szOnLabel, szOffLabel, elExclude) {
    if (!szSid) return;
    // 1. 同步當前畫布 DOM
    for (const el of canvas.querySelectorAll('.canvas-widget')) {
        if (!el.widgetProps) continue;
        if (el === elExclude && !el.widgetProps.arrCells) continue;
        const p = el.widgetProps;
        if (el.dataset.type === 'diPoint' && p.szSid === szSid && el !== elExclude) {
            p.szOnLabel  = szOnLabel;
            p.szOffLabel = szOffLabel;
            renderWidget(el);
        }
        if (el.dataset.type === 'table' && p.arrCells) {
            let isChanged = false;
            for (const row of p.arrCells) {
                for (const cell of row) {
                    if (cell && cell.szSid === szSid && cell.szPointType === 'DI') {
                        cell.szOnLabel  = szOnLabel;
                        cell.szOffLabel = szOffLabel;
                        isChanged = true;
                    }
                }
            }
            if (isChanged) renderWidget(el);
        }
    }
    // 2. 同步其他頁面的 arrWidgetState
    function syncTree(pages) {
        for (const page of pages) {
            if (page.szId !== szCurrentPageId && page.arrWidgetState) {
                for (const ws of page.arrWidgetState) {
                    const props = ws.props;
                    if (!props) continue;
                    if (ws.szType === 'diPoint' && props.szSid === szSid) {
                        props.szOnLabel  = szOnLabel;
                        props.szOffLabel = szOffLabel;
                    }
                    if (ws.szType === 'table' && props.arrCells) {
                        for (const row of props.arrCells) {
                            for (const cell of row) {
                                if (cell && cell.szSid === szSid && cell.szPointType === 'DI') {
                                    cell.szOnLabel  = szOnLabel;
                                    cell.szOffLabel = szOffLabel;
                                }
                            }
                        }
                    }
                }
            }
            if (page.arrChildren) syncTree(page.arrChildren);
        }
    }
    syncTree(arrPageTree);
}

// AI 點位上限/下限顏色 → 同步所有同 SID 的 AI Widget（realtimeValue + table AI cell）
function _syncAiColorsToSid(szSid, szHighColor, szLowColor, elExclude) {
    if (!szSid) return;
    const arrSyncTypes = ['realtimeValue', 'gauge'];
    // 1. 同步當前畫布 DOM
    for (const el of canvas.querySelectorAll('.canvas-widget')) {
        if (!el.widgetProps) continue;
        const p = el.widgetProps;
        if (arrSyncTypes.includes(el.dataset.type) && p.szSid === szSid && el !== elExclude) {
            p.szHighColor = szHighColor;
            p.szLowColor  = szLowColor;
            renderWidget(el);
        }
        if (el.dataset.type === 'table' && p.arrCells) {
            let isChanged = false;
            for (const row of p.arrCells) {
                for (const cell of row) {
                    if (cell && cell.szSid === szSid && (cell.szPointType || 'AI') === 'AI') {
                        cell.szHighColor = szHighColor;
                        cell.szLowColor  = szLowColor;
                        isChanged = true;
                    }
                }
            }
            if (isChanged) renderWidget(el);
        }
    }
    // 2. 同步其他頁面的 arrWidgetState
    function syncTree(pages) {
        for (const page of pages) {
            if (page.szId !== szCurrentPageId && page.arrWidgetState) {
                for (const ws of page.arrWidgetState) {
                    const props = ws.props;
                    if (!props) continue;
                    if (arrSyncTypes.includes(ws.szType) && props.szSid === szSid) {
                        props.szHighColor = szHighColor;
                        props.szLowColor  = szLowColor;
                    }
                    if (ws.szType === 'table' && props.arrCells) {
                        for (const row of props.arrCells) {
                            for (const cell of row) {
                                if (cell && cell.szSid === szSid && (cell.szPointType || 'AI') === 'AI') {
                                    cell.szHighColor = szHighColor;
                                    cell.szLowColor  = szLowColor;
                                }
                            }
                        }
                    }
                }
            }
            if (page.arrChildren) syncTree(page.arrChildren);
        }
    }
    syncTree(arrPageTree);
}

// DI 點位警報顏色 → 同步所有同 SID 的 DI Widget（diPoint + table DI cell）
function _syncDiAlarmColorToSid(szSid, szAlarmColor, elExclude) {
    if (!szSid) return;
    // 1. 同步當前畫布 DOM
    for (const el of canvas.querySelectorAll('.canvas-widget')) {
        if (!el.widgetProps) continue;
        const p = el.widgetProps;
        if (el.dataset.type === 'diPoint' && p.szSid === szSid && el !== elExclude) {
            p.szAlarmColor = szAlarmColor;
            renderWidget(el);
        }
        if (el.dataset.type === 'table' && p.arrCells) {
            let isChanged = false;
            for (const row of p.arrCells) {
                for (const cell of row) {
                    if (cell && cell.szSid === szSid && cell.szPointType === 'DI') {
                        cell.szAlarmColor = szAlarmColor;
                        isChanged = true;
                    }
                }
            }
            if (isChanged) renderWidget(el);
        }
    }
    // 2. 同步其他頁面的 arrWidgetState
    function syncTree(pages) {
        for (const page of pages) {
            if (page.szId !== szCurrentPageId && page.arrWidgetState) {
                for (const ws of page.arrWidgetState) {
                    const props = ws.props;
                    if (!props) continue;
                    if (ws.szType === 'diPoint' && props.szSid === szSid) {
                        props.szAlarmColor = szAlarmColor;
                    }
                    if (ws.szType === 'table' && props.arrCells) {
                        for (const row of props.arrCells) {
                            for (const cell of row) {
                                if (cell && cell.szSid === szSid && cell.szPointType === 'DI') {
                                    cell.szAlarmColor = szAlarmColor;
                                }
                            }
                        }
                    }
                }
            }
            if (page.arrChildren) syncTree(page.arrChildren);
        }
    }
    syncTree(arrPageTree);
}

// 更新 prop → 重新渲染 widget 內容
function setProp(szKey, val) {
    if (!selectedEl) return;
    selectedEl.widgetProps[szKey] = val;
    // 當列數/欄數改變時同步 arrCells
    if (selectedEl.dataset.type === 'table' && (szKey === 'nRows' || szKey === 'nCols')) {
        syncArrCellsSize(selectedEl.widgetProps);
        nSelectedCellRow = -1;
        nSelectedCellCol = -1;
    }
    // DI ON/OFF 標籤修改時，同步到所有同 SID 的 DI Widget
    if (selectedEl.dataset.type === 'diPoint' && (szKey === 'szOnLabel' || szKey === 'szOffLabel')) {
        const p = selectedEl.widgetProps;
        _syncDiLabelsToSid(p.szSid, p.szOnLabel, p.szOffLabel, selectedEl);
    }
    // AI/Gauge 上限/下限顏色修改時，同步到所有同 SID 的 Widget
    if ((selectedEl.dataset.type === 'realtimeValue' || selectedEl.dataset.type === 'gauge') && (szKey === 'szHighColor' || szKey === 'szLowColor')) {
        const p = selectedEl.widgetProps;
        _syncAiColorsToSid(p.szSid, p.szHighColor, p.szLowColor, selectedEl);
    }
    // DI 警報顏色修改時，同步到所有同 SID 的 DI Widget
    if (selectedEl.dataset.type === 'diPoint' && szKey === 'szAlarmColor') {
        const p = selectedEl.widgetProps;
        _syncDiAlarmColorToSid(p.szSid, p.szAlarmColor, selectedEl);
    }
    renderWidget(selectedEl);
    // DI 顯示模式切換時重新渲染屬性面板（更新標籤文字）
    if (selectedEl.dataset.type === 'diPoint' && szKey === 'szDisplayMode') {
        renderPropPanel(selectedEl);
    }
}

function setPos(szSide, nVal) {
    if (!selectedEl) return;
    selectedEl.style[szSide] = Math.max(0, nVal) + 'px';
}

function setSize(szSide, nVal) {
    if (!selectedEl) return;
    const def = WIDGET_DEFS[selectedEl.dataset.type];
    const nMin = szSide === 'width' ? (def?.nMinW || 40) : (def?.nMinH || 30);
    selectedEl.style[szSide] = Math.max(nMin, nVal) + 'px';
}

// ============================================================
// 表格儲存格選取與屬性面板
// ============================================================
let nSelectedCellRow = -1;
let nSelectedCellCol = -1;

function onTableCellClick(widgetEl, nRow, nCol) {
    selectWidget(widgetEl);
    nSelectedCellRow = nRow;
    nSelectedCellCol = nCol;
    // 高亮選中 cell
    widgetEl.querySelectorAll('.w-table td, .w-table th').forEach(c => c.classList.remove('selected-cell'));
    const sel = widgetEl.querySelector(`.w-table [data-row="${nRow}"][data-col="${nCol}"]`);
    if (sel) sel.classList.add('selected-cell');
    renderTableCellPropPanel(widgetEl, nRow, nCol);
}

function renderTableCellPropPanel(el, nRow, nCol) {
    const props = el.widgetProps;
    initArrCells(props);
    if (nRow >= props.arrCells.length || nCol >= props.arrCells[nRow].length) return;
    const cell = props.arrCells[nRow][nCol];
    const isHeader = (nRow === 0);

    let szHtml = `
        <div style="font-size:11px;color:#0d6efd;margin-bottom:8px;cursor:pointer;"
             onclick="renderPropPanel(selectedEl)">
            <i class="fas fa-arrow-left me-1"></i>${escHtml(t('designer.prop.cell_back'))}
        </div>
        <div style="font-size:10px;color:#888;margin-bottom:6px;">
            ${escHtml(t(isHeader ? 'designer.prop.cell_header_row' : 'designer.prop.cell_data_row'))} [${nRow}, ${nCol}]
        </div>
        <hr class="prop-divider">
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.text_content'))}</label>
            <input type="text" value="${escHtml(cell.szText || '')}"
                   oninput="setCellProp('szText', this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.font_size'))}</label>
            <input type="number" value="${cell.nFontSize || 12}" min="8" max="24"
                   oninput="setCellProp('nFontSize', +this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.text_color'))}</label>
            <input type="color" value="${cell.szFontColor || (isHeader ? '#ffffff' : '#444444')}"
                   oninput="setCellProp('szFontColor', this.value)">
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.weight'))}</label>
            <select onchange="setCellProp('szFontWeight', this.value)">
                <option value="normal" ${(cell.szFontWeight||'normal')==='normal'?'selected':''}>${escHtml(t('designer.prop.weight.normal'))}</option>
                <option value="bold" ${cell.szFontWeight==='bold'?'selected':''}>${escHtml(t('designer.prop.weight.bold'))}</option>
                <option value="500" ${cell.szFontWeight==='500'?'selected':''}>${escHtml(t('designer.prop.weight.medium'))}</option>
            </select>
        </div>
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.alignment'))}</label>
            <select onchange="setCellProp('szAlign', this.value)">
                <option value="left" ${(cell.szAlign||'left')==='left'?'selected':''}>${escHtml(t('designer.prop.align.left'))}</option>
                <option value="center" ${cell.szAlign==='center'?'selected':''}>${escHtml(t('designer.prop.align.center'))}</option>
                <option value="right" ${cell.szAlign==='right'?'selected':''}>${escHtml(t('designer.prop.align.right'))}</option>
            </select>
        </div>`;

    if (!isHeader) {
        // 綁定點位
        const szSidLabel = cell.szSid
            ? `<span style="font-size:11px;color:#c8c8c8;">${escHtml(cell.szPointName || cell.szSid)}</span>`
            : `<span style="color:#888;font-size:11px;">${escHtml(t('designer.widget.unbound'))}</span>`;
        szHtml += `
        <hr class="prop-divider">
        <div class="prop-group">
            <label>${escHtml(t('designer.prop.bound_point'))}</label>
            <div style="display:flex;align-items:center;gap:6px;flex-wrap:wrap;">
                ${szSidLabel}
                <button class="btn btn-outline-warning btn-sm py-0 px-2" style="font-size:11px;"
                        onclick="openCellPointPicker(${nRow}, ${nCol})">
                    <i class="fas fa-exchange-alt me-1"></i>${escHtml(t(cell.szSid ? 'designer.prop.reselect' : 'designer.prop.bind'))}
                </button>
                ${cell.szSid ? `<button class="btn btn-outline-danger btn-sm py-0 px-2" style="font-size:11px;"
                        onclick="clearCellSid(${nRow}, ${nCol})">
                    <i class="fas fa-unlink me-1"></i>${escHtml(t('designer.prop.clear'))}
                </button>` : ''}
            </div>
        </div>`;
        // 首欄以外才顯示點位屬性
        if (nCol > 0) {
            const szPT = cell.szPointType || 'AI';
            szHtml += `
            <div class="prop-group">
                <label>${escHtml(t('designer.prop.point_type'))}</label>
                <select onchange="setCellProp('szPointType', this.value)">
                    <option value="AI" ${szPT === 'AI' ? 'selected' : ''}>${escHtml(t('designer.prop.point_type.ai'))}</option>
                    <option value="DI" ${szPT === 'DI' ? 'selected' : ''}>${escHtml(t('designer.prop.point_type.di'))}</option>
                </select>
            </div>`;
            if (szPT === 'AI') {
                szHtml += `
                <div class="prop-group">
                    <label>${escHtml(t('designer.prop.col_decimals'))}</label>
                    <input type="number" value="${props.arrColDecimals[nCol] ?? ''}" min="0" max="6"
                           placeholder="${escHtml(t('designer.prop.col_decimals_placeholder'))}"
                           oninput="setColDecimals(${nCol}, this.value)">
                </div>
                <div class="prop-group">
                    <label>${escHtml(t('designer.prop.high_color'))}</label>
                    <input type="color" value="${cell.szHighColor || '#dc3545'}"
                           oninput="setCellProp('szHighColor', this.value)">
                </div>
                <div class="prop-group">
                    <label>${escHtml(t('designer.prop.low_color'))}</label>
                    <input type="color" value="${cell.szLowColor || '#fd7e14'}"
                           oninput="setCellProp('szLowColor', this.value)">
                </div>
                `;
            } else if (szPT === 'DI') {
                szHtml += `
                <div class="prop-group">
                    <label>${escHtml(t('designer.prop.on_label'))}</label>
                    <input type="text" value="${escHtml(cell.szOnLabel || 'ON')}"
                           oninput="setCellProp('szOnLabel', this.value)">
                </div>
                <div class="prop-group">
                    <label>${escHtml(t('designer.prop.off_label'))}</label>
                    <input type="text" value="${escHtml(cell.szOffLabel || 'OFF')}"
                           oninput="setCellProp('szOffLabel', this.value)">
                </div>
                <div class="prop-group">
                    <label>${escHtml(t('designer.prop.alarm_text_color'))}</label>
                    <input type="color" value="${cell.szAlarmColor || '#dc3545'}"
                           oninput="setCellProp('szAlarmColor', this.value)">
                </div>
                `;
            }
        }
    }

    document.getElementById('propBody').innerHTML = szHtml;
}

function setCellProp(szKey, val) {
    if (!selectedEl || nSelectedCellRow < 0) return;
    const props = selectedEl.widgetProps;
    if (!props.arrCells || nSelectedCellRow >= props.arrCells.length) return;
    // 字體大小、對齊、粗細 → 整欄同步修改（標題列與資料列各自獨立）
    const arrColWideKeys = ['nFontSize', 'szAlign', 'szFontWeight', 'szPointType'];
    if (arrColWideKeys.includes(szKey)) {
        const nCol = nSelectedCellCol;
        const isHeader = nSelectedCellRow === 0;
        for (let ri = 0; ri < props.arrCells.length; ri++) {
            // 標題列只改標題列，資料列只改資料列
            if (isHeader ? ri === 0 : ri > 0) {
                if (props.arrCells[ri][nCol]) {
                    props.arrCells[ri][nCol][szKey] = val;
                }
            }
        }
    } else {
        props.arrCells[nSelectedCellRow][nSelectedCellCol][szKey] = val;
    }
    // 表格 DI Cell ON/OFF 標籤修改時，同步到所有同 SID 的 DI Widget
    if ((szKey === 'szOnLabel' || szKey === 'szOffLabel')) {
        const cell = props.arrCells[nSelectedCellRow]?.[nSelectedCellCol];
        if (cell && cell.szSid && cell.szPointType === 'DI') {
            _syncDiLabelsToSid(cell.szSid, cell.szOnLabel, cell.szOffLabel, null);
        }
    }
    // 表格 AI Cell 上限/下限顏色修改時，同步到所有同 SID 的 AI Widget
    if ((szKey === 'szHighColor' || szKey === 'szLowColor')) {
        const cell = props.arrCells[nSelectedCellRow]?.[nSelectedCellCol];
        if (cell && cell.szSid && (cell.szPointType || 'AI') === 'AI') {
            _syncAiColorsToSid(cell.szSid, cell.szHighColor, cell.szLowColor, null);
        }
    }
    // 表格 DI Cell 警報顏色修改時，同步到所有同 SID 的 DI Widget
    if (szKey === 'szAlarmColor') {
        const cell = props.arrCells[nSelectedCellRow]?.[nSelectedCellCol];
        if (cell && cell.szSid && cell.szPointType === 'DI') {
            _syncDiAlarmColorToSid(cell.szSid, cell.szAlarmColor, null);
        }
    }
    renderWidget(selectedEl);
    // 重新高亮
    const sel = selectedEl.querySelector(`.w-table [data-row="${nSelectedCellRow}"][data-col="${nSelectedCellCol}"]`);
    if (sel) sel.classList.add('selected-cell');
    // 切換點位屬性或警報開關時重新渲染屬性面板
    const arrReRenderKeys = ['szPointType'];
    if (arrReRenderKeys.includes(szKey)) {
        renderTableCellPropPanel(selectedEl, nSelectedCellRow, nSelectedCellCol);
    }
}

function setColDecimals(nCol, val) {
    if (!selectedEl) return;
    const props = selectedEl.widgetProps;
    initArrCells(props);
    props.arrColDecimals[nCol] = val === '' ? null : Math.max(0, Math.min(6, parseInt(val)));
    // 不需重新渲染，小數位數僅影響 ScadaPage 即時顯示
}

function clearCellSid(nRow, nCol) {
    if (!selectedEl) return;
    const cell = selectedEl.widgetProps.arrCells[nRow][nCol];
    cell.szSid = '';
    cell.szPointName = '';
    renderWidget(selectedEl);
    const sel = selectedEl.querySelector(`.w-table [data-row="${nRow}"][data-col="${nCol}"]`);
    if (sel) sel.classList.add('selected-cell');
    renderTableCellPropPanel(selectedEl, nRow, nCol);
}

// ============================================================
// 依點位建立 widget（picker 確認後呼叫）
// ============================================================
function createGaugeWithPoint(point, x, y) {
    const def  = WIDGET_DEFS['gauge'];
    const szId = 'w' + (++nWidgetCounter);
    const szFullName = point.szDeviceLabel ? point.szDeviceLabel + ' / ' + point.szName : point.szName;

    const el = document.createElement('div');
    el.id           = szId;
    el.className    = 'canvas-widget';
    el.dataset.type = 'gauge';
    el.style.left   = x + 'px';
    el.style.top    = y + 'px';
    el.style.width  = def.nDefaultW + 'px';
    el.style.height = def.nDefaultH + 'px';
    const fNewMin = point.fMin ?? 0;
    const fNewMax = point.fMax ?? 100;
    el.widgetProps  = {
        ...getWidgetDefaultProps(el.dataset.type),
        szSid:       point.szSid,
        szPointName: szFullName,
        szTitle:     szFullName,
        szUnit:  point.szUnit || '',
        fMin:    fNewMin,
        fMax:    fNewMax,
        fValue:  (fNewMin + fNewMax) / 2
    };

    renderWidget(el);
    canvas.appendChild(el);
    selectWidget(el);
}

function createControlBtnWithPoint(point, x, y) {
    const def  = WIDGET_DEFS['controlBtn'];
    const szId = 'w' + (++nWidgetCounter);
    const szFullName = point.szDeviceLabel ? point.szDeviceLabel + ' / ' + point.szName : point.szName;

    const el = document.createElement('div');
    el.id           = szId;
    el.className    = 'canvas-widget';
    el.dataset.type = 'controlBtn';
    el.style.left   = x + 'px';
    el.style.top    = y + 'px';
    el.style.width  = def.nDefaultW + 'px';
    el.style.height = def.nDefaultH + 'px';
    el.widgetProps  = {
        ...getWidgetDefaultProps(el.dataset.type),
        szCid:       point.szSid,
        szPointName: szFullName,
        szTitle:     szFullName
    };

    renderWidget(el);
    canvas.appendChild(el);
    selectWidget(el);
}

function createRealtimeValueWithPoint(point, x, y) {
    const def  = WIDGET_DEFS['realtimeValue'];
    const szId = 'w' + (++nWidgetCounter);
    const szFullName = point.szDeviceLabel ? point.szDeviceLabel + ' / ' + point.szName : point.szName;

    const el = document.createElement('div');
    el.id           = szId;
    el.className    = 'canvas-widget';
    el.dataset.type = 'realtimeValue';
    el.style.left   = x + 'px';
    el.style.top    = y + 'px';
    el.style.width  = def.nDefaultW + 'px';
    el.style.height = def.nDefaultH + 'px';
    el.widgetProps  = {
        ...getWidgetDefaultProps(el.dataset.type),
        szSid:       point.szSid,
        szPointName: szFullName,
        szTitle:     szFullName,
        szUnit:      point.szUnit || ''
    };

    renderWidget(el);
    canvas.appendChild(el);
    selectWidget(el);
}

function createDiPointWithPoint(point, x, y) {
    const def  = WIDGET_DEFS['diPoint'];
    const szId = 'w' + (++nWidgetCounter);
    const szFullName = point.szDeviceLabel ? point.szDeviceLabel + ' / ' + point.szName : point.szName;

    const el = document.createElement('div');
    el.id           = szId;
    el.className    = 'canvas-widget';
    el.dataset.type = 'diPoint';
    el.style.left   = x + 'px';
    el.style.top    = y + 'px';
    el.style.width  = def.nDefaultW + 'px';
    el.style.height = def.nDefaultH + 'px';
    el.widgetProps  = {
        ...getWidgetDefaultProps(el.dataset.type),
        szSid:       point.szSid,
        szPointName: szFullName,
        szTitle:     szFullName
    };
    // 繼承同 SID 已存在的 DI ON/OFF 標籤
    const diLabels = _findDiLabelsForSid(point.szSid);
    if (diLabels) {
        el.widgetProps.szOnLabel  = diLabels.szOnLabel;
        el.widgetProps.szOffLabel = diLabels.szOffLabel;
    }

    renderWidget(el);
    canvas.appendChild(el);
    selectWidget(el);
}

function createDiPointWithSchedule(nScheduleId, szScheduleName, x, y) {
    const def  = WIDGET_DEFS['diPoint'];
    const szId = 'w' + (++nWidgetCounter);

    const el = document.createElement('div');
    el.id           = szId;
    el.className    = 'canvas-widget';
    el.dataset.type = 'diPoint';
    el.style.left   = x + 'px';
    el.style.top    = y + 'px';
    el.style.width  = def.nDefaultW + 'px';
    el.style.height = def.nDefaultH + 'px';
    el.widgetProps  = {
        ...getWidgetDefaultProps(el.dataset.type),
        szSid:          '',
        szPointName:    '',
        nScheduleId:    nScheduleId,
        szScheduleName: szScheduleName || '',
        szTitle:        szScheduleName || ''
    };

    renderWidget(el);
    canvas.appendChild(el);
    selectWidget(el);
}

function createAoPointWithPoint(point, x, y) {
    const def  = WIDGET_DEFS['aoPoint'];
    const szId = 'w' + (++nWidgetCounter);
    const szFullName = point.szDeviceLabel ? point.szDeviceLabel + ' / ' + point.szName : point.szName;

    const el = document.createElement('div');
    el.id           = szId;
    el.className    = 'canvas-widget';
    el.dataset.type = 'aoPoint';
    el.style.left   = x + 'px';
    el.style.top    = y + 'px';
    el.style.width  = def.nDefaultW + 'px';
    el.style.height = def.nDefaultH + 'px';
    el.widgetProps  = {
        ...getWidgetDefaultProps(el.dataset.type),
        szCid:         point.szSid,
        szPointName:   szFullName,
        szTitle:       szFullName,
        szDisplayName: szFullName,
        szUnit:        point.szUnit || '',
        fMin:          point.fMin ?? 0,
        fMax:          point.fMax ?? 100
    };

    renderWidget(el);
    canvas.appendChild(el);
    selectWidget(el);
}

function createDoPointWithPoint(point, x, y) {
    const def  = WIDGET_DEFS['doPoint'];
    const szId = 'w' + (++nWidgetCounter);
    const szFullName = point.szDeviceLabel ? point.szDeviceLabel + ' / ' + point.szName : point.szName;

    const el = document.createElement('div');
    el.id           = szId;
    el.className    = 'canvas-widget';
    el.dataset.type = 'doPoint';
    el.style.left   = x + 'px';
    el.style.top    = y + 'px';
    el.style.width  = def.nDefaultW + 'px';
    el.style.height = def.nDefaultH + 'px';
    el.widgetProps  = {
        ...getWidgetDefaultProps(el.dataset.type),
        szCid:         point.szSid,
        szPointName:   szFullName,
        szTitle:       szFullName,
        szDisplayName: szFullName
    };

    renderWidget(el);
    canvas.appendChild(el);
    selectWidget(el);
}

// ============================================================
// 背景透明切換（gauge / controlBtn / realtimeValue / diPoint / aoPoint / doPoint / pump）
// ============================================================
function onGaugeBgTransparentChange(isChecked) {
    if (isChecked) {
        setProp('szBgColor', 'transparent');
        const picker = document.getElementById('gaugeBgPicker');
        if (picker) picker.style.display = 'none';
    } else {
        const picker = document.getElementById('gaugeBgPicker');
        const szColor = picker ? picker.value : '#ffffff';
        setProp('szBgColor', szColor);
        if (picker) picker.style.display = '';
    }
}

function onCtrlBtnBgTransparentChange(isChecked) {
    if (isChecked) {
        setProp('szBgColor', 'transparent');
        const picker = document.getElementById('ctrlBtnBgPicker');
        if (picker) picker.style.display = 'none';
    } else {
        const picker = document.getElementById('ctrlBtnBgPicker');
        const szColor = picker ? picker.value : '#ffffff';
        setProp('szBgColor', szColor);
        if (picker) picker.style.display = '';
    }
}

function onRtValBgTransparentChange(isChecked) {
    if (isChecked) {
        setProp('szBgColor', 'transparent');
        const picker = document.getElementById('rtValBgPicker');
        if (picker) picker.style.display = 'none';
    } else {
        const picker = document.getElementById('rtValBgPicker');
        const szColor = picker ? picker.value : '#ffffff';
        setProp('szBgColor', szColor);
        if (picker) picker.style.display = '';
    }
}

function onDiPointBgTransparentChange(isChecked) {
    if (isChecked) {
        setProp('szBgColor', 'transparent');
        const picker = document.getElementById('diPointBgPicker');
        if (picker) picker.style.display = 'none';
    } else {
        const picker = document.getElementById('diPointBgPicker');
        const szColor = picker ? picker.value : '#ffffff';
        setProp('szBgColor', szColor);
        if (picker) picker.style.display = '';
    }
}

function onAoPointBgTransparentChange(isChecked) {
    if (isChecked) {
        setProp('szBgColor', 'transparent');
        const picker = document.getElementById('aoPointBgPicker');
        if (picker) picker.style.display = 'none';
    } else {
        const picker = document.getElementById('aoPointBgPicker');
        const szColor = picker ? picker.value : '#ffffff';
        setProp('szBgColor', szColor);
        if (picker) picker.style.display = '';
    }
}

function onDoPointBgTransparentChange(isChecked) {
    if (isChecked) {
        setProp('szBgColor', 'transparent');
        const picker = document.getElementById('doPointBgPicker');
        if (picker) picker.style.display = 'none';
    } else {
        const picker = document.getElementById('doPointBgPicker');
        const szColor = picker ? picker.value : '#ffffff';
        setProp('szBgColor', szColor);
        if (picker) picker.style.display = '';
    }
}

function onPumpBgTransparentChange(isChecked) {
    if (isChecked) {
        setProp('szBgColor', 'transparent');
        const picker = document.getElementById('pumpBgPicker');
        if (picker) picker.style.display = 'none';
    } else {
        const picker = document.getElementById('pumpBgPicker');
        const szColor = picker ? picker.value : '#ffffff';
        setProp('szBgColor', szColor);
        if (picker) picker.style.display = '';
    }
}
