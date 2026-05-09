/* =====================================================================
   explorer.js — driver for the main Explorer page (Index.cshtml).
   Talks to /api/folders and /api/logs, manages all client state.
===================================================================== */

(function () {
    const state = {
        currentFolder: null,
        files: [],
        currentFile: null,
        viewMode: "grid",
        page: 1,
        pageSize: 100,
        sortBy: "Time",
        sortDescending: true,
        latestResult: null,
        selectedIndex: null,
    };

    const $ = (id) => document.getElementById(id);

    document.addEventListener("DOMContentLoaded", init);

    async function init() {
        bindStaticHandlers();
        await loadRoots();
    }

    function pathsEqual(a, b) {
        if (!a || !b) return false;
        return String(a).replace(/\\/g, "/").toLowerCase() === String(b).replace(/\\/g, "/").toLowerCase();
    }

    function updateFolderReloadEnabled() {
        const btn = $("btn-reload-folder");
        if (btn) btn.disabled = !state.currentFolder;
    }

    function updateClearRecentButton(recent) {
        const btn = $("btn-clear-recent");
        if (!btn) return;
        const n = recent && recent.length ? recent.length : 0;
        btn.disabled = n === 0;
        btn.title = n ? "Clear all recently viewed files" : "No recent files";
    }

    async function clearRecentPaths() {
        try {
            await xla.api("/api/folders/recent/clear", { method: "POST" });
            await loadRoots();
            xla.toast("Recently viewed list cleared.", "success");
        } catch (err) {
            xla.toast(err.message, "error");
        }
    }

    function bindStaticHandlers() {
        $("btn-refresh-roots").addEventListener("click", loadRoots);
        $("btn-reload-folder").addEventListener("click", reloadFolderFromDisk);
        $("btn-clear-recent").addEventListener("click", clearRecentPaths);
        $("rootDropdown").addEventListener("change", (e) => loadTree(e.target.value));
        $("recursiveTree").addEventListener("change", () => {
            if (state.currentFolder) loadFiles(state.currentFolder);
        });
        $("folderFilter").addEventListener("input", filterTree);

        $("btn-close-viewer").addEventListener("click", closeViewer);
        document.querySelectorAll(".view-tabs .nav-link").forEach(a => {
            a.addEventListener("click", (e) => {
                e.preventDefault();
                document.querySelectorAll(".view-tabs .nav-link").forEach(x => x.classList.remove("active"));
                a.classList.add("active");
                showView(a.dataset.view);
            });
        });

        $("btn-apply-filters").addEventListener("click", () => { state.page = 1; loadGrid(); });
        $("btn-clear-filters").addEventListener("click", () => clearFilters());
        $("btn-prev-page").addEventListener("click", () => { if (state.page > 1) { state.page--; loadGrid(); } });
        $("btn-next-page").addEventListener("click", () => { state.page++; loadGrid(); });
        $("pageSize").addEventListener("change", (e) => { state.pageSize = +e.target.value; state.page = 1; loadGrid(); });
        $("globalSearch").addEventListener("keydown", (e) => { if (e.key === "Enter") { state.page = 1; loadGrid(); } });
        $("btn-reload-log").addEventListener("click", reloadOpenFileFromDisk);
        $("btn-reload-file-header").addEventListener("click", reloadOpenFileFromDisk);

        // keyboard shortcuts — use capture so we run before Bootstrap removes .modal.show on Escape
        document.addEventListener("keydown", (e) => {
            if (e.key !== "Escape") {
                if (e.key === "/" && !["INPUT", "TEXTAREA", "SELECT"].includes(document.activeElement.tagName)) {
                    e.preventDefault();
                    $("globalSearch").focus();
                }
                return;
            }
            // While any Bootstrap modal is open, only dismiss the modal (never close the log viewer).
            if (
                document.body.classList.contains("modal-open")
                || document.querySelector(".modal.show")
                || e.target && typeof e.target.closest === "function" && e.target.closest(".modal.show")
            ) {
                return;
            }
            if (!$("logViewer").classList.contains("d-none")) {
                e.preventDefault();
                closeViewer();
            }
        }, true);
    }

    // ---------- Folders ----------
    async function loadRoots() {
        try {
            const data = await xla.api("/api/folders/roots");
            const dd = $("rootDropdown");
            dd.innerHTML = "";
            (data.roots || []).forEach(r => {
                const o = document.createElement("option");
                o.value = r; o.textContent = r;
                dd.appendChild(o);
            });
            renderPathList("favList", data.favorites || [], (p) => onSelectFolder(p));
            renderPathList("recentList", data.recent || [], (p) => onSelectFile(p));
            updateClearRecentButton(data.recent);

            if (dd.value) await loadTree(dd.value);
            updateFolderReloadEnabled();
        } catch (err) {
            xla.toast(err.message, "error");
        }
    }

    function renderPathList(id, items, onClick) {
        const ul = $(id);
        ul.innerHTML = "";
        if (!items.length) {
            ul.innerHTML = `<li class="text-secondary">— empty —</li>`;
            return;
        }
        items.forEach(p => {
            const li = document.createElement("li");
            li.title = p; li.textContent = p;
            li.addEventListener("click", () => onClick(p));
            ul.appendChild(li);
        });
    }

    async function loadTree(root) {
        state.selectedRoot = root;
        try {
            const tree = await xla.api(`/api/folders/tree?path=${encodeURIComponent(root)}&recursive=true`);
            renderTree(tree);
        } catch (err) {
            xla.toast(err.message, "error");
        }
    }

    function renderTree(nodes) {
        const host = $("folderTree");
        host.innerHTML = "";
        nodes.forEach(n => host.appendChild(renderNode(n, 0)));
    }

    function renderNode(node, depth) {
        const wrap = document.createElement("div");
        wrap.className = "folder-node-wrap";

        const row = document.createElement("div");
        row.className = "folder-node";
        row.style.paddingLeft = (depth * 14) + "px";
        row.dataset.fullpath = node.fullPath;

        const hasSubfolders = !!(node.children && node.children.length > 0);
        const nFiles = node.fileCount || 0;
        const hasFiles = nFiles > 0;
        const expandable = hasSubfolders || hasFiles;

        const caretSym = expandable ? "▸" : "·";
        row.innerHTML = `<span class="caret caret-btn" role="${expandable ? "button" : "presentation"}" tabindex="${expandable ? "0" : "-1"}" aria-expanded="false" title="${expandable ? "Expand or collapse" : ""}">${caretSym}</span><i class="bi bi-folder text-warning"></i> <span class="folder-node-label">${xla.esc(node.name)}</span>
            <small class="text-secondary ms-1">(${nFiles})</small>`;

        row.addEventListener("click", (e) => {
            if (e.target.closest(".caret-btn")) return;
            onSelectFolder(node.fullPath);
        });

        const childWrap = document.createElement("div");
        childWrap.className = "folder-children d-none";

        const fileSlot = document.createElement("div");
        fileSlot.className = "folder-tree-files";
        childWrap.appendChild(fileSlot);

        if (hasSubfolders) {
            node.children.forEach(ch => childWrap.appendChild(renderNode(ch, depth + 1)));
        }

        if (expandable) {
            const caretEl = row.querySelector(".caret-btn");
            const toggle = async (e) => {
                if (e) { e.stopPropagation(); e.preventDefault(); }
                const hidden = childWrap.classList.contains("d-none");
                if (hidden) {
                    childWrap.classList.remove("d-none");
                    caretEl.textContent = "▾";
                    caretEl.setAttribute("aria-expanded", "true");
                    if (!fileSlot.dataset.loaded) {
                        await loadFilesIntoTreeSlot(node.fullPath, fileSlot);
                        fileSlot.dataset.loaded = "1";
                    }
                } else {
                    childWrap.classList.add("d-none");
                    caretEl.textContent = "▸";
                    caretEl.setAttribute("aria-expanded", "false");
                }
            };
            caretEl.addEventListener("click", toggle);
            caretEl.addEventListener("keydown", (e) => {
                if (e.key === "Enter" || e.key === " ") {
                    toggle(e);
                }
            });
        }

        wrap.appendChild(row);
        if (expandable) {
            wrap.appendChild(childWrap);
        }
        return wrap;
    }

    /** Log files in this folder (non-recursive), shown under the tree row when expanded. */
    async function loadFilesIntoTreeSlot(folderPath, slot) {
        slot.innerHTML = `<div class="folder-tree-file-loading small text-secondary px-2 py-1">Loading…</div>`;
        try {
            const files = await xla.api(`/api/folders/files?path=${encodeURIComponent(folderPath)}&recursive=false`);
            slot.innerHTML = "";
            if (!files.length) {
                slot.innerHTML = `<div class="folder-tree-file-empty small text-secondary px-2 py-1">No log files in this folder</div>`;
                return;
            }
            files.forEach(f => {
                const div = document.createElement("div");
                div.className = "folder-tree-file";
                div.dataset.fullpath = f.fullPath;
                div.innerHTML = `<i class="bi bi-file-earmark-code"></i> ${xla.esc(f.name)}`;
                div.title = f.fullPath;
                div.addEventListener("click", (e) => {
                    e.stopPropagation();
                    onSelectFile(f.fullPath);
                });
                slot.appendChild(div);
            });
        } catch (err) {
            slot.innerHTML = `<div class="small text-danger px-2 py-1">${xla.esc(err.message)}</div>`;
        }
    }

    function filterTree() {
        const host = $("folderTree");
        if (!host) return;
        const term = ($("folderFilter").value || "").toLowerCase().trim();
        const wraps = host.querySelectorAll(".folder-node-wrap");
        if (!term) {
            wraps.forEach(w => { w.style.display = ""; });
            return;
        }
        wraps.forEach(w => { w.style.display = "none"; });
        wraps.forEach(wrap => {
            const block = wrap.innerText.toLowerCase();
            const row = wrap.querySelector(":scope > .folder-node");
            const path = (row && row.dataset.fullpath) ? row.dataset.fullpath.toLowerCase() : "";
            if (block.includes(term) || path.includes(term)) {
                let el = wrap;
                while (el && el !== host) {
                    if (el.classList && el.classList.contains("folder-node-wrap")) el.style.display = "";
                    el = el.parentElement;
                }
            }
        });
    }

    // ---------- Files (state for cache reload / folder refresh; list UI is in sidebar tree) ----------
    async function onSelectFolder(path) {
        state.currentFolder = path;
        updateFolderReloadEnabled();
        await loadFiles(path);
    }

    /** Clears parse cache for every log file in the current folder, re-lists files, optionally refreshes the open grid. */
    async function reloadFolderFromDisk() {
        if (!state.currentFolder) {
            xla.toast("Select a folder in the tree first.", "warning");
            return;
        }
        const btn = $("btn-reload-folder");
        btn.disabled = true;
        const icon = btn.querySelector(".bi-arrow-clockwise");
        if (icon) icon.classList.add("spin-icon");
        try {
            await xla.api("/api/folders/cache/refresh?path=" + encodeURIComponent(state.currentFolder), { method: "POST" });
            await loadFiles(state.currentFolder);
            document.querySelectorAll(".folder-tree-files[data-loaded]").forEach((el) => {
                delete el.dataset.loaded;
                el.innerHTML = "";
            });
            const viewerOpen = !$("logViewer").classList.contains("d-none");
            if (viewerOpen && state.currentFile && state.files.some((f) => pathsEqual(f.fullPath, state.currentFile))) {
                await loadGrid();
                if (state.selectedIndex != null && state.viewMode !== "grid") await loadEntryView(state.viewMode);
            }
            xla.toast("Folder caches updated.", "success");
        } catch (err) {
            xla.toast(err.message, "error");
        } finally {
            if (icon) icon.classList.remove("spin-icon");
            updateFolderReloadEnabled();
        }
    }

    /** Invalidate cache for the open log file and repaint grid + XML panes. */
    async function reloadOpenFileFromDisk() {
        if (!state.currentFile) return;
        const btns = [$("btn-reload-log"), $("btn-reload-file-header")].filter(Boolean);
        btns.forEach((b) => { b.disabled = true; });
        btns.forEach((b) => {
            const i = b.querySelector(".bi-arrow-clockwise");
            if (i) i.classList.add("spin-icon");
        });
        try {
            await xla.api("/api/logs/refresh?path=" + encodeURIComponent(state.currentFile), { method: "POST" });
            await loadGrid();
            if (state.selectedIndex != null && state.viewMode !== "grid") await loadEntryView(state.viewMode);
            xla.toast("Log file reloaded from disk.", "success");
        } catch (err) {
            xla.toast(err.message, "error");
        } finally {
            btns.forEach((b) => { b.disabled = false; });
            btns.forEach((b) => {
                const i = b.querySelector(".bi-arrow-clockwise");
                if (i) i.classList.remove("spin-icon");
            });
        }
    }

    async function loadFiles(path) {
        try {
            const rec = $("recursiveTree").checked;
            const files = await xla.api(`/api/folders/files?path=${encodeURIComponent(path)}&recursive=${rec}`);
            state.files = files;
        } catch (err) { xla.toast(err.message, "error"); }
    }

    function setMainLogOpen(open) {
        $("logViewer").classList.toggle("d-none", !open);
        const empty = $("explorerEmpty");
        if (empty) empty.classList.toggle("d-none", open);
    }

    // ---------- Log Viewer ----------
    async function onSelectFile(path) {
        state.currentFile = path;
        $("openedFile").textContent = path;
        setMainLogOpen(true);
        state.page = 1;
        clearFilters({ skipLoad: true });
        await loadGrid();
    }

    function closeViewer() {
        setMainLogOpen(false);
        state.currentFile = null;
    }

    function showView(name) {
        state.viewMode = name;
        ["grid","raw","pretty","tree","json"].forEach(v => {
            $("view-" + v).classList.toggle("d-none", v !== name);
        });
        if (state.selectedIndex != null && name !== "grid") loadEntryView(name);
    }

    function readQueryFromUI() {
        const fd = $("fromDate").value, td = $("toDate").value;
        return {
            page: state.page,
            pageSize: state.pageSize,
            search: $("globalSearch").value || null,
            severity: $("severityFilter").value || null,
            fromDate: fd ? new Date(fd).toISOString() : null,
            toDate: td ? new Date(td).toISOString() : null,
            sortBy: state.sortBy,
            sortDescending: state.sortDescending,
        };
    }

    async function loadGrid() {
        if (!state.currentFile) return;
        try {
            const q = readQueryFromUI();
            const r = await xla.api(`/api/logs/query?path=${encodeURIComponent(state.currentFile)}`, {
                method: "POST", body: q
            });
            state.latestResult = r;
            renderStats(r.stats);
            renderGrid(r);
        } catch (err) {
            xla.toast(err.message, "error");
        }
    }

    function renderStats(s) {
        $("statsStrip").innerHTML = `
            <span class="stat-chip">Total <strong>${s.totalEntries.toLocaleString()}</strong></span>
            <span class="stat-chip error">Error <strong>${s.errorCount}</strong></span>
            <span class="stat-chip warning">Warning <strong>${s.warningCount}</strong></span>
            <span class="stat-chip info">Info <strong>${s.infoCount}</strong></span>
            <span class="stat-chip debug">Debug <strong>${s.debugCount}</strong></span>
            ${s.latestErrorTime ? `<span class="stat-chip">Latest error <strong>${xla.fmtTime(s.latestErrorTime)}</strong></span>` : ""}
            ${s.latestEntryTime ? `<span class="stat-chip">Latest entry <strong>${xla.fmtTime(s.latestEntryTime)}</strong></span>` : ""}
        `;
    }

    /* Column order: Time, Severity, Operation, Machine, **Message** (right after Machine),
       then secondary identifiers. */
    const COLUMNS = [
        { key: "Time",            label: "Time",     get: e => xla.fmtTime(e.time) },
        { key: "SeverityLevel",   label: "Severity", get: e => xla.sevBadge(e.severityLevel) },
        { key: "Operation",       label: "Operation",get: e => xla.esc(e.operation) },
        { key: "MachineName",     label: "Machine",  get: e => xla.esc(e.machineName) },
        { key: "LogMessage",      label: "Message",  get: (e, term) => xla.highlight(e.logMessage || "", term) },
        { key: "ProcessId",       label: "PID",      get: e => xla.esc(e.processId) },
        { key: "ManagedThreadId", label: "TID",      get: e => xla.esc(e.managedThreadId) },
        { key: "TypeName",        label: "Type",     get: e => xla.esc(e.typeName) },
        { key: "AppDomainName",   label: "AppDomain",get: e => xla.esc(e.appDomainName) },
        { key: "ConversationId",  label: "Conv. Id", get: e => xla.esc(e.conversationId) },
    ];

    function renderGrid(r) {
        const head = $("gridHead");
        const body = $("gridBody");
        const term = $("globalSearch").value || "";

        head.innerHTML = "<tr>" + COLUMNS.map(c => `
            <th data-key="${c.key}">${c.label}
                ${state.sortBy === c.key ? (state.sortDescending ? " ▼" : " ▲") : ""}
            </th>`).join("") + "</tr>";

        head.querySelectorAll("th").forEach(th => th.addEventListener("click", () => {
            const k = th.dataset.key;
            if (state.sortBy === k) state.sortDescending = !state.sortDescending;
            else { state.sortBy = k; state.sortDescending = true; }
            state.page = 1;
            loadGrid();
        }));

        body.innerHTML = "";
        const entries = r.entries || [];
        if (entries.length === 0) {
            const tr = document.createElement("tr");
            tr.innerHTML = `<td colspan="${COLUMNS.length}" class="text-center text-secondary py-4">
                No entries match the current filters. Try clearing filters or changing the date range.</td>`;
            body.appendChild(tr);
        }
        entries.forEach(e => {
            const tr = document.createElement("tr");
            tr.dataset.index = e.index;
            // Row-level tint based on severity, so users can spot Errors at a glance.
            const sev = (e.severityLevel || "").trim();
            if (["Error","Warning","Info","Debug"].includes(sev)) {
                tr.classList.add("sev-row", "sev-row-" + sev);
            }
            tr.innerHTML = COLUMNS.map(c => {
                const cell = c.get(e, term);
                const titlePlain = c.key === "LogMessage"
                    ? String(e.logMessage ?? "")
                    : (c.get(e) ? String(c.get(e)).replace(/<[^>]+>/g, "") : "");
                return `<td data-col="${c.key}" title="${xla.esc(titlePlain)}">${cell ?? ""}</td>`;
            }).join("");
            tr.addEventListener("click", () => selectRow(e.index, tr));
            const msgTd = tr.querySelector('td[data-col="LogMessage"]');
            if (msgTd) {
                msgTd.classList.add("log-grid-msg-cell");
                msgTd.addEventListener("click", (ev) => {
                    ev.stopPropagation();
                    selectRow(e.index, tr);
                    openMessageModal(e.logMessage || "");
                });
            }
            body.appendChild(tr);
        });

        const total = r.total ?? 0;
        if (total === 0) {
            $("pagerInfo").textContent = "0 entries";
        } else {
            const start = (r.page - 1) * r.pageSize + 1;
            const end = Math.min(total, r.page * r.pageSize);
            $("pagerInfo").textContent = `${start.toLocaleString()}–${end.toLocaleString()} of ${total.toLocaleString()}`;
        }
    }

    function openMessageModal(text) {
        const bodyEl = $("messageModalBody");
        const modalEl = $("messageModal");
        if (!bodyEl || !modalEl || !window.bootstrap) return;
        bodyEl.textContent = text && text.trim() ? text : "(empty message)";
        bootstrap.Modal.getOrCreateInstance(modalEl).show();
    }

    function selectRow(idx, tr) {
        document.querySelectorAll("#gridBody tr.row-selected").forEach(x => x.classList.remove("row-selected"));
        tr.classList.add("row-selected");
        state.selectedIndex = idx;
        if (state.viewMode !== "grid") loadEntryView(state.viewMode);
    }

    async function loadEntryView(mode) {
        if (state.selectedIndex == null) return;
        const path = encodeURIComponent(state.currentFile);
        const idx  = state.selectedIndex;
        try {
            if (mode === "raw") {
                const xml = await xla.api(`/api/logs/entry/raw?path=${path}&index=${idx}`);
                $("rawPane").textContent = xml;
            } else if (mode === "pretty") {
                const xml = await xla.api(`/api/logs/entry/pretty?path=${path}&index=${idx}`);
                $("prettyPane").textContent = xml;
            } else if (mode === "json") {
                const json = await xla.api(`/api/logs/entry/json?path=${path}&index=${idx}`);
                $("jsonPane").textContent = json;
            } else if (mode === "tree") {
                const xml = await xla.api(`/api/logs/entry/raw?path=${path}&index=${idx}`);
                $("treePane").innerHTML = renderXmlTree(xml);
            }
        } catch (err) { xla.toast(err.message, "error"); }
    }

    function renderXmlTree(xml) {
        try {
            const doc = new DOMParser().parseFromString(xml, "application/xml");
            if (doc.querySelector("parsererror")) return "<em>Invalid XML</em>";
            return walkXml(doc.documentElement, 0);
        } catch { return "<em>Could not render</em>"; }
    }
    function walkXml(node, depth) {
        if (node.nodeType !== 1) return "";
        let out = `<div style="padding-left:${depth*16}px">`;
        out += `<span class="text-info">&lt;${xla.esc(node.localName)}&gt;</span>`;
        if (node.children.length === 0 && node.textContent) {
            out += ` <span>${xla.esc(node.textContent.trim())}</span>`;
        }
        out += `</div>`;
        for (const c of node.children) out += walkXml(c, depth + 1);
        return out;
    }

    function clearFilters(opts) {
        const skipLoad = opts && opts.skipLoad;
        ["globalSearch","fromDate","toDate"].forEach(id => $(id).value = "");
        $("severityFilter").value = "";
        state.page = 1;
        if (!skipLoad) loadGrid();
    }
})();
