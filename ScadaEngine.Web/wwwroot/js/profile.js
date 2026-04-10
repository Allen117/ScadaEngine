(function () {
    var permData = (typeof _permissionJson === 'string')
        ? JSON.parse(_permissionJson)
        : (_permissionJson || {});

    // ── 主功能頁面（唯讀） ─────────────────────────────────
    var wrapMain = document.getElementById('profileMainPages');
    if (wrapMain) {
        wrapMain.innerHTML = '';
        _configurablePages.forEach(function (p) {
            var isChecked = permData.pages && permData.pages.indexOf(p.route) >= 0;
            var div = document.createElement('div');
            div.className = 'form-check form-check-inline';
            div.innerHTML = '<input class="form-check-input" type="checkbox"' +
                (isChecked ? ' checked' : '') + ' disabled>' +
                '<label class="form-check-label small">' + p.name + '</label>';
            wrapMain.appendChild(div);
        });
    }

    // ── ScadaPage 子頁面（唯讀） ──────────────────────────
    var wrapScada = document.getElementById('profileScadaPages');
    if (wrapScada) {
        if (!_scadaDesignPages || _scadaDesignPages.length === 0) {
            wrapScada.innerHTML = '<small class="text-muted">\u5c1a\u7121\u5df2\u767c\u4f48\u7684\u756b\u9762\u8a2d\u8a08</small>';
            return;
        }

        var sp = (permData && permData.scadaPages) || {};
        var html = '<table class="table table-sm table-bordered mb-0"><thead><tr>' +
            '<th class="small">\u9801\u9762</th>' +
            '<th class="small text-center" style="width:100px">\u53ef\u6aa2\u8996</th>' +
            '<th class="small text-center" style="width:100px">\u53ef\u63a7\u5236</th></tr></thead><tbody>';

        _scadaDesignPages.forEach(function (pg) {
            var perm = sp[pg.szPageSid] || { canView: false, canControl: false };
            var indent = pg.szParentPageSid ? 'padding-left:24px;' : '';
            html += '<tr><td class="small" style="' + indent + '">' +
                (pg.szParentPageSid ? '<i class="fas fa-level-up-alt fa-rotate-90 me-1 text-muted"></i>' : '') +
                pg.szPageName + '</td>' +
                '<td class="text-center"><input type="checkbox" class="form-check-input"' +
                (perm.canView ? ' checked' : '') + ' disabled></td>' +
                '<td class="text-center"><input type="checkbox" class="form-check-input"' +
                (perm.canControl ? ' checked' : '') + ' disabled></td></tr>';
        });

        html += '</tbody></table>';
        wrapScada.innerHTML = html;
    }
})();
