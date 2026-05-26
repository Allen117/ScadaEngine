// LogicFlow 左側樹狀選單 + 右側內容區切換
(function () {
    const S = window.__lfNS;

    // =========== 平坦 → 樹狀 ===========
    function buildTree(flat) {
        const map = {};
        const roots = [];
        for (const n of flat) {
            map[n.id] = {
                id: n.id, name: n.name, type: n.nodeType, isEnabled: n.isEnabled,
                sortOrder: n.sortOrder, parentId: n.parentId,
                children: [], expanded: S.expandedSet.has(n.id)
            };
        }
        for (const n of flat) {
            const node = map[n.id];
            if (n.parentId && map[n.parentId]) {
                map[n.parentId].children.push(node);
            } else {
                roots.push(node);
            }
        }
        const sortFn = (a, b) => a.sortOrder - b.sortOrder;
        function sortAll(nodes) { nodes.sort(sortFn); nodes.forEach(n => sortAll(n.children)); }
        sortAll(roots);
        return roots;
    }

    async function loadTree() {
        try {
            S.flatNodes = await S.apiFetch('/tree');
            S.treeData = buildTree(S.flatNodes);
            renderTree();
        } catch (e) {
            document.getElementById('treeContainer').innerHTML =
                `<p class="text-danger text-center small mt-5"><i class="fas fa-exclamation-triangle me-1"></i>${S.escHtml(S.t("logicflow.error.load_failed", { msg: e.message }))}</p>`;
        }
    }

    function renderTree() {
        const container = document.getElementById('treeContainer');
        if (S.treeData.length === 0) {
            container.innerHTML = '<p class="text-muted text-center small mt-5">' + S.escHtml(S.t('logicflow.tree.empty')) + '</p>';
            return;
        }
        container.innerHTML = buildNodes(S.treeData);
    }

    function buildNodes(nodes) {
        let html = '';
        for (const n of nodes) {
            const isFolder = n.type === 'folder';
            const isActive = n.id === S.selectedId ? ' active' : '';
            const isDisabled = !n.isEnabled ? ' disabled-node' : '';

            let icon, iconTitle;
            if (isFolder && n.isEnabled) {
                icon = n.expanded ? 'fas fa-folder-open text-warning' : 'fas fa-folder text-warning';
                iconTitle = S.t('logicflow.tree.folder');
            } else if (isFolder && !n.isEnabled) {
                icon = n.expanded ? 'fas fa-folder-open text-secondary' : 'fas fa-folder text-secondary';
                iconTitle = S.t('logicflow.tree.folder_disabled');
            } else if (n.isEnabled) {
                icon = 'fas fa-file-code text-info';
                iconTitle = S.t('logicflow.tree.logic_enabled');
            } else {
                icon = 'fas fa-file-code text-secondary';
                iconTitle = S.t('logicflow.tree.logic_disabled');
            }

            html += '<div class="tree-node">';
            html += `<div class="tree-item${isActive}${isDisabled}" data-id="${n.id}" onclick="window._lf.select(${n.id})" ondblclick="window._lf.toggle(${n.id})">`;

            if (isFolder) {
                html += `<span class="tree-toggle" onclick="event.stopPropagation(); window._lf.toggle(${n.id})">`;
                html += n.expanded ? '<i class="fas fa-caret-down"></i>' : '<i class="fas fa-caret-right"></i>';
                html += '</span>';
            } else {
                html += '<span style="width:16px;display:inline-block"></span>';
            }

            html += `<span class="node-icon" title="${iconTitle}"><i class="${icon}"></i></span>`;
            html += `<span class="node-label" title="${S.escHtml(n.name)}">${S.escHtml(n.name)}</span>`;

            // 操作按鈕
            html += '<span class="node-actions">';
            if (isFolder) {
                html += `<button title="${S.escHtml(S.t('logicflow.action.add_folder'))}" onclick="event.stopPropagation(); window._lf.addChild(${n.id},'folder')"><i class="fas fa-folder-plus"></i></button>`;
                html += `<button title="${S.escHtml(S.t('logicflow.action.add_logic'))}" onclick="event.stopPropagation(); window._lf.addChild(${n.id},'logic')"><i class="fas fa-file-circle-plus"></i></button>`;
            }
            if (n.isEnabled) {
                html += `<button title="${S.escHtml(S.t('logicflow.action.disable'))}" onclick="event.stopPropagation(); window._lf.toggleEnabled(${n.id},false)"><i class="fas fa-toggle-on text-success"></i></button>`;
            } else {
                html += `<button title="${S.escHtml(S.t('logicflow.action.enable'))}" onclick="event.stopPropagation(); window._lf.toggleEnabled(${n.id},true)"><i class="fas fa-toggle-off text-secondary"></i></button>`;
            }
            html += `<button title="${S.escHtml(S.t('logicflow.action.rename'))}" onclick="event.stopPropagation(); window._lf.rename(${n.id})"><i class="fas fa-pen"></i></button>`;
            html += `<button title="${S.escHtml(S.t('logicflow.action.delete'))}" onclick="event.stopPropagation(); window._lf.remove(${n.id})"><i class="fas fa-trash-alt text-danger"></i></button>`;
            html += '</span>';
            html += '</div>';

            if (isFolder && n.children.length > 0 && n.expanded) {
                html += '<div class="tree-children">' + buildNodes(n.children) + '</div>';
            }

            html += '</div>';
        }
        return html;
    }

    // =========== 操作 ===========
    function select(id) {
        S.selectedId = id;
        const node = S.findNode(id, S.treeData);
        renderTree();
        updateContent(node);
    }

    function toggle(id) {
        const node = S.findNode(id, S.treeData);
        if (!node || node.type !== 'folder') return;
        node.expanded = !node.expanded;
        if (node.expanded) S.expandedSet.add(id); else S.expandedSet.delete(id);
        renderTree();
    }

    async function addRoot(type) {
        const name = type === 'folder' ? S.t('logicflow.default.new_folder') : S.t('logicflow.default.new_logic');
        const sortOrder = S.treeData.length;
        try {
            const res = await S.apiFetch('/tree', {
                method: 'POST',
                body: JSON.stringify({ parentId: null, name, nodeType: type, sortOrder })
            });
            S.selectedId = res.id;
            await loadTree();
            updateContent(S.findNode(res.id, S.treeData));
        } catch (e) { alert(S.t('logicflow.error.add_failed', { msg: e.message })); }
    }

    async function addChild(parentId, type) {
        const parent = S.findNode(parentId, S.treeData);
        if (!parent || parent.type !== 'folder') return;
        S.expandedSet.add(parentId);
        const name = type === 'folder' ? '新資料夾' : '新邏輯';
        const sortOrder = parent.children.length;
        try {
            const res = await S.apiFetch('/tree', {
                method: 'POST',
                body: JSON.stringify({ parentId, name, nodeType: type, sortOrder })
            });
            S.selectedId = res.id;
            await loadTree();
            updateContent(S.findNode(res.id, S.treeData));
        } catch (e) { alert(S.t('logicflow.error.add_failed', { msg: e.message })); }
    }

    function rename(id) {
        S.renameTargetId = id;
        const node = S.findNode(id, S.treeData);
        if (!node) return;
        document.getElementById('renameInput').value = node.name;
        new bootstrap.Modal(document.getElementById('renameModal')).show();
        setTimeout(() => document.getElementById('renameInput').select(), 300);
    }

    async function confirmRename() {
        const val = document.getElementById('renameInput').value.trim();
        if (!val || S.renameTargetId == null) return;
        try {
            await S.apiFetch(`/tree/${S.renameTargetId}/rename`, {
                method: 'PUT',
                body: JSON.stringify({ name: val })
            });
            await loadTree();
            if (S.selectedId === S.renameTargetId) {
                updateContent(S.findNode(S.renameTargetId, S.treeData));
            }
        } catch (e) { alert(S.t('logicflow.error.rename_failed', { msg: e.message })); }
        bootstrap.Modal.getInstance(document.getElementById('renameModal'))?.hide();
        S.renameTargetId = null;
    }

    async function remove(id) {
        if (!confirm(S.t('logicflow.confirm.delete_item'))) return;
        try {
            await S.apiFetch(`/tree/${id}`, { method: 'DELETE' });
            if (S.selectedId === id) { S.selectedId = null; clearContent(); }
            await loadTree();
        } catch (e) { alert(S.t('logicflow.error.delete_failed', { msg: e.message })); }
    }

    async function toggleEnabled(id, isEnabled) {
        try {
            await S.apiFetch(`/tree/${id}/toggle`, {
                method: 'PUT',
                body: JSON.stringify({ isEnabled })
            });
            await loadTree();
            if (S.selectedId === id) {
                updateContent(S.findNode(id, S.treeData));
            }
        } catch (e) { alert(S.t('logicflow.error.toggle_failed', { msg: e.message })); }
    }

    // =========== 右側內容 ===========
    function updateContent(node) {
        const title = document.getElementById('contentTitle');
        const area = document.getElementById('contentArea');
        if (!node) { clearContent(); return; }

        if (node.type === 'folder') {
            const folderColor = node.isEnabled ? 'text-warning' : 'text-secondary';
            const folderBadge = node.isEnabled
                ? '<span class="badge bg-success ms-2">' + S.escHtml(S.t('logicflow.status.enabled')) + '</span>'
                : '<span class="badge bg-secondary ms-2">' + S.escHtml(S.t('logicflow.status.disabled')) + '</span>';
            title.innerHTML = `<i class="fas fa-folder-open ${folderColor} me-1"></i>${S.escHtml(node.name)}${folderBadge}`;
            const childCount = node.children ? node.children.length : 0;
            area.innerHTML = `
                <div class="text-center text-muted mt-5">
                    <i class="fas fa-folder-open fa-4x mb-3 d-block" style="opacity:.3"></i>
                    <p>${S.escHtml(S.t('logicflow.folder.label', { name: node.name }))}</p>
                    <p class="small">${S.escHtml(S.t('logicflow.folder.contains_items', { count: childCount }))}</p>
                    ${!node.isEnabled ? '<p class="small text-danger"><i class="fas fa-ban me-1"></i>' + S.escHtml(S.t('logicflow.folder.disabled_warning')) + '</p>' : ''}
                </div>`;
        } else {
            const statusColor = node.isEnabled ? 'text-info' : 'text-secondary';
            const statusBadge = node.isEnabled
                ? '<span class="badge bg-success ms-2">' + S.escHtml(S.t('logicflow.status.enabled')) + '</span>'
                : '<span class="badge bg-secondary ms-2">' + S.escHtml(S.t('logicflow.status.disabled')) + '</span>';
            title.innerHTML = `<i class="fas fa-file-code ${statusColor} me-1"></i>${S.escHtml(node.name)}${statusBadge}`;
            area.innerHTML = '<div id="diagramCanvas" class="diagram-canvas"></div>';
            // 只檢查自身邏輯是否啟用（不受上層資料夾影響）
            S._isLogicEnabled = !!node.isEnabled;
            S.initCanvas(node.id);
        }
    }

    function clearContent() {
        S.stopRealtimePolling();
        document.getElementById('contentTitle').innerHTML = '<i class="fas fa-hand-pointer me-1"></i>' + S.escHtml(S.t('logicflow.hint.select_logic'));
        document.getElementById('contentArea').innerHTML = `
            <div class="text-center text-muted mt-5">
                <i class="fas fa-project-diagram fa-4x mb-3 d-block" style="opacity:.3"></i>
                <p>${S.escHtml(S.t('logicflow.hint.select_to_edit'))}</p>
            </div>`;
    }

    // 暴露給其他模組
    S.loadTree = loadTree;
    S.renderTree = renderTree;
    S.select = select;
    S.toggle = toggle;
    S.addRoot = addRoot;
    S.addChild = addChild;
    S.rename = rename;
    S.confirmRename = confirmRename;
    S.remove = remove;
    S.toggleEnabled = toggleEnabled;
    S.updateContent = updateContent;
    S.clearContent = clearContent;
})();
