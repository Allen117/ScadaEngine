(function () {
    // ── 從 data 屬性讀取 server 資料 ─────────────────────────
    var _root = document.getElementById('accountSettingRoot');
    var _configurablePages = JSON.parse(_root.dataset.configurablePages || '[]');
    var _scadaDesignPages = JSON.parse(_root.dataset.scadaDesignPages || '[]');

    function t(key, args) {
        return (window.i18n && window.i18n.t) ? window.i18n.t(key, args) : key;
    }

    // ── 共用工具 ─────────────────────────────────────────────
    function showAlert(elId, msg, type) {
        var el = document.getElementById(elId);
        el.className = 'alert mt-3 mb-0 alert-' + type;
        el.textContent = msg;
        el.style.display = '';
    }
    function hideAlert(elId) {
        var el = document.getElementById(elId);
        el.style.display = 'none';
        el.textContent = '';
    }

    // ── 新增使用者 ───────────────────────────────────────────
    document.getElementById('btnTogglePassword').addEventListener('click', function () {
        var pwd = document.getElementById('inputPassword');
        var icon = this.querySelector('i');
        if (pwd.type === 'password') {
            pwd.type = 'text';
            icon.classList.replace('fa-eye', 'fa-eye-slash');
        } else {
            pwd.type = 'password';
            icon.classList.replace('fa-eye-slash', 'fa-eye');
        }
    });

    document.getElementById('createUserModal').addEventListener('hidden.bs.modal', function () {
        document.getElementById('inputUsername').value = '';
        document.getElementById('inputRealName').value = '';
        document.getElementById('inputPassword').value = '';
        document.getElementById('inputPasswordConfirm').value = '';
        document.getElementById('inputRole').value = 'User';
        document.getElementById('inputDepartment').value = '';
        document.getElementById('inputIsActive').checked = true;
        hideAlert('createUserAlert');
    });

    document.getElementById('btnCreateUser').addEventListener('click', async function () {
        var szUsername = document.getElementById('inputUsername').value.trim();
        var szRealName = document.getElementById('inputRealName').value.trim();
        var szPassword = document.getElementById('inputPassword').value;
        var szPasswordConfirm = document.getElementById('inputPasswordConfirm').value;
        var szRole = document.getElementById('inputRole').value;
        var szDepartment = document.getElementById('inputDepartment').value.trim();
        var isActive = document.getElementById('inputIsActive').checked;

        if (!szUsername) { showAlert('createUserAlert', t('account.alert.input_username'), 'warning'); return; }
        if (!szPassword) { showAlert('createUserAlert', t('account.alert.input_password'), 'warning'); return; }
        if (szPassword !== szPasswordConfirm) { showAlert('createUserAlert', t('account.alert.password_mismatch'), 'warning'); return; }

        this.disabled = true;
        this.innerHTML = '<i class="fas fa-spinner fa-spin me-1"></i>' + t('account.alert.saving');

        try {
            var resp = await fetch('/AccountSetting/Create', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    username: szUsername, realName: szRealName, password: szPassword,
                    role: szRole, department: szDepartment, isActive: isActive
                })
            });
            var result = await resp.json();
            if (result.success) {
                showAlert('createUserAlert', t('account.alert.create_success'), 'success');
                setTimeout(function () { location.reload(); }, 1000);
            } else {
                showAlert('createUserAlert', result.message || t('account.alert.create_failed'), 'danger');
            }
        } catch (ex) {
            showAlert('createUserAlert', t('account.alert.create_failed_with', { 0: ex.message }), 'danger');
        } finally {
            this.disabled = false;
            this.innerHTML = '<i class="fas fa-save me-1"></i>' + t('account.button.save');
        }
    });

    // ── 編輯使用者 ───────────────────────────────────────────
    function syncSelectAllPages() {
        var all = document.querySelectorAll('.perm-page-cb');
        var checked = document.querySelectorAll('.perm-page-cb:checked');
        var cb = document.getElementById('cbSelectAllPages');
        cb.checked = all.length > 0 && all.length === checked.length;
        cb.indeterminate = checked.length > 0 && checked.length < all.length;
    }

    function renderPermMainPages(permData) {
        var wrap = document.getElementById('permMainPages');
        wrap.innerHTML = '';
        _configurablePages.forEach(function (p) {
            var isChecked = permData.pages && permData.pages.indexOf(p.route) >= 0;
            var div = document.createElement('div');
            div.className = 'form-check form-check-inline';
            div.innerHTML = '<input class="form-check-input perm-page-cb" type="checkbox" value="' +
                p.route + '" id="perm_' + p.route.replace(/\//g, '_') + '"' +
                (isChecked ? ' checked' : '') + '>' +
                '<label class="form-check-label small" for="perm_' + p.route.replace(/\//g, '_') + '">' +
                p.name + '</label>';
            wrap.appendChild(div);
        });
        syncSelectAllPages();
    }

    function syncSelectAllScada() {
        var allView = document.querySelectorAll('.perm-scada-view');
        var checkedView = document.querySelectorAll('.perm-scada-view:checked');
        var cbView = document.getElementById('cbSelectAllView');
        if (cbView) {
            cbView.checked = allView.length > 0 && allView.length === checkedView.length;
            cbView.indeterminate = checkedView.length > 0 && checkedView.length < allView.length;
        }

        var allCtrl = document.querySelectorAll('.perm-scada-ctrl');
        var checkedCtrl = document.querySelectorAll('.perm-scada-ctrl:checked');
        var cbCtrl = document.getElementById('cbSelectAllCtrl');
        if (cbCtrl) {
            cbCtrl.checked = allCtrl.length > 0 && allCtrl.length === checkedCtrl.length;
            cbCtrl.indeterminate = checkedCtrl.length > 0 && checkedCtrl.length < allCtrl.length;
        }
    }

    function renderPermScadaPages(permData) {
        var wrap = document.getElementById('permScadaPages');
        if (!_scadaDesignPages || _scadaDesignPages.length === 0) {
            wrap.innerHTML = '<small class="text-muted">' + escapeText(t('account.permission.no_pages')) + '</small>';
            return;
        }
        var sp = (permData && permData.scadaPages) || {};
        var html = '<table class="table table-sm table-bordered mb-0"><thead><tr>' +
            '<th class="small">' + escapeText(t('account.permission.col_page')) + '</th>' +
            '<th class="small text-center" style="width:100px">' +
            '<input type="checkbox" class="form-check-input me-1" id="cbSelectAllView">' + escapeText(t('account.permission.col_can_view')) + '</th>' +
            '<th class="small text-center" style="width:100px">' +
            '<input type="checkbox" class="form-check-input me-1" id="cbSelectAllCtrl">' + escapeText(t('account.permission.col_can_control')) + '</th></tr></thead><tbody>';

        _scadaDesignPages.forEach(function (pg) {
            var perm = sp[pg.szPageSid] || { canView: false, canControl: false };
            var indent = pg.szParentPageSid ? 'padding-left:24px;' : '';
            html += '<tr><td class="small" style="' + indent + '">' +
                (pg.szParentPageSid ? '<i class="fas fa-level-up-alt fa-rotate-90 me-1 text-muted"></i>' : '') +
                pg.szPageName + '</td>' +
                '<td class="text-center"><input type="checkbox" class="form-check-input perm-scada-view" data-pagesid="' +
                pg.szPageSid + '"' + (perm.canView ? ' checked' : '') + '></td>' +
                '<td class="text-center"><input type="checkbox" class="form-check-input perm-scada-ctrl" data-pagesid="' +
                pg.szPageSid + '"' + (perm.canControl ? ' checked' : '') + '></td></tr>';
        });

        html += '</tbody></table>';
        wrap.innerHTML = html;
        syncSelectAllScada();
    }

    function escapeText(s) {
        if (s == null) return '';
        return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

    function collectPermissionJson() {
        var pages = [];
        document.querySelectorAll('.perm-page-cb:checked').forEach(function (cb) {
            pages.push(cb.value);
        });

        var scadaPages = {};
        document.querySelectorAll('.perm-scada-view').forEach(function (cb) {
            var sid = cb.dataset.pagesid;
            if (!scadaPages[sid]) scadaPages[sid] = { canView: false, canControl: false };
            scadaPages[sid].canView = cb.checked;
        });
        document.querySelectorAll('.perm-scada-ctrl').forEach(function (cb) {
            var sid = cb.dataset.pagesid;
            if (!scadaPages[sid]) scadaPages[sid] = { canView: false, canControl: false };
            scadaPages[sid].canControl = cb.checked;
        });

        return JSON.stringify({ pages: pages, scadaPages: scadaPages });
    }

    // ── 全選 checkbox 事件 ──────────────────────────────────
    // 主功能頁面全選
    document.getElementById('cbSelectAllPages').addEventListener('change', function () {
        var isChecked = this.checked;
        document.querySelectorAll('.perm-page-cb').forEach(function (cb) { cb.checked = isChecked; });
    });

    // 個別主功能頁面 checkbox 變更 → 同步全選狀態
    document.getElementById('permMainPages').addEventListener('change', function (e) {
        if (e.target.classList.contains('perm-page-cb')) syncSelectAllPages();
    });

    // ScadaPage 可檢視/可控制全選（因表格是動態渲染，用事件委派）
    document.getElementById('permScadaPages').addEventListener('change', function (e) {
        if (e.target.id === 'cbSelectAllView') {
            var isChecked = e.target.checked;
            document.querySelectorAll('.perm-scada-view').forEach(function (cb) { cb.checked = isChecked; });
        } else if (e.target.id === 'cbSelectAllCtrl') {
            var isChecked = e.target.checked;
            document.querySelectorAll('.perm-scada-ctrl').forEach(function (cb) { cb.checked = isChecked; });
        } else if (e.target.classList.contains('perm-scada-view') || e.target.classList.contains('perm-scada-ctrl')) {
            syncSelectAllScada();
        }
    });

    // 角色切換時顯示/隱藏權限面板
    document.getElementById('editRole').addEventListener('change', function () {
        document.getElementById('permissionSection').style.display =
            this.value === 'User' ? '' : 'none';
    });

    // 編輯按鈕事件委派
    document.addEventListener('click', function (e) {
        var btn = e.target.closest('.btn-edit');
        if (!btn) return;

        var ds = btn.dataset;
        document.getElementById('editUserID').value = ds.userid;
        document.getElementById('editUsername').value = ds.username;
        document.getElementById('editRealName').value = ds.realname || '';
        document.getElementById('editRole').value = ds.role;
        document.getElementById('editDepartment').value = ds.department || '';
        document.getElementById('editIsActive').checked = ds.isactive === 'true';
        hideAlert('editUserAlert');

        var isUser = ds.role === 'User';
        document.getElementById('permissionSection').style.display = isUser ? '' : 'none';

        // 載入權限設定
        if (isUser) {
            loadAndRenderPermissions(ds.userid);
        } else {
            renderPermMainPages({ pages: [] });
            renderPermScadaPages({});
        }

        new bootstrap.Modal(document.getElementById('editUserModal')).show();
    });

    async function loadAndRenderPermissions(nUserID) {
        try {
            var resp = await fetch('/AccountSetting/GetPermissions/' + nUserID);
            var data = await resp.json();
            var permData = JSON.parse(data.permissionJson || '{}');
            renderPermMainPages(permData);
            renderPermScadaPages(permData);
        } catch (_) {
            renderPermMainPages({ pages: [] });
            renderPermScadaPages({});
        }
    }

    document.getElementById('btnSaveEdit').addEventListener('click', async function () {
        var nUserID = parseInt(document.getElementById('editUserID').value);
        var szRole = document.getElementById('editRole').value;
        var szRealName = document.getElementById('editRealName').value.trim();
        var szDepartment = document.getElementById('editDepartment').value.trim();
        var isActive = document.getElementById('editIsActive').checked;

        var payload = {
            userID: nUserID,
            realName: szRealName,
            role: szRole,
            department: szDepartment,
            isActive: isActive,
            permissionJson: szRole === 'User' ? collectPermissionJson() : null
        };

        this.disabled = true;
        this.innerHTML = '<i class="fas fa-spinner fa-spin me-1"></i>' + t('account.alert.saving');

        try {
            var resp = await fetch('/AccountSetting/Update', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });
            var result = await resp.json();
            if (result.success) {
                showAlert('editUserAlert', t('account.alert.update_success'), 'success');
                setTimeout(function () { location.reload(); }, 1000);
            } else {
                showAlert('editUserAlert', result.message || t('account.alert.update_failed'), 'danger');
            }
        } catch (ex) {
            showAlert('editUserAlert', t('account.alert.update_failed_with', { 0: ex.message }), 'danger');
        } finally {
            this.disabled = false;
            this.innerHTML = '<i class="fas fa-save me-1"></i>' + t('account.button.save_changes');
        }
    });

    // ── 刪除使用者 ───────────────────────────────────────────
    document.addEventListener('click', function (e) {
        var btn = e.target.closest('.btn-delete');
        if (!btn) return;

        document.getElementById('deleteUserID').value = btn.dataset.userid;
        document.getElementById('deleteUsername').textContent = btn.dataset.username;
        // 用 i18n 樣板拆 prefix/suffix 包住 username strong
        var msg = t('account.delete.confirm_msg', { 0: 'USERNAME' });
        var parts = msg.split('USERNAME');
        document.getElementById('deleteUserMsgPrefix').textContent = parts[0] || '';
        document.getElementById('deleteUserMsgSuffix').textContent = parts[1] || '';
        new bootstrap.Modal(document.getElementById('deleteUserModal')).show();
    });

    document.getElementById('btnConfirmDelete').addEventListener('click', async function () {
        var nUserID = parseInt(document.getElementById('deleteUserID').value);

        this.disabled = true;
        try {
            var resp = await fetch('/AccountSetting/Delete', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ userID: nUserID })
            });
            var result = await resp.json();
            if (result.success) {
                location.reload();
            } else {
                alert(result.message || t('account.alert.delete_failed'));
            }
        } catch (ex) {
            alert(t('account.alert.delete_failed_with', { 0: ex.message }));
        } finally {
            this.disabled = false;
        }
    });
})();
