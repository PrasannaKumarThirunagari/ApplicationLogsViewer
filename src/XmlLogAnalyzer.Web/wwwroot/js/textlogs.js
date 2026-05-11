/* =====================================================================
   textlogs.js — driver for the "Text Logs" tab.

   Layout: left sidebar holds the file picker (root selector + search +
   scrollable list). Right pane holds the viewer (filters + grid). When
   no file is open, a placeholder card sits in the right pane.

   Talks to /api/text-logs/*.  Pure client state.
===================================================================== */
(function () {
    const state = {
        currentRoot: null,
        files: [],            // raw FileInfoDto[] from server
        currentFile: null,    // selected file's fullPath
        page: 1,
        pageSize: 100,
        sortBy: "Timestamp",
        sortDescending: true,
        latest: null,
    };

    const $ = (id) => document.getElementById(id);

    document.addEventListener("DOMContentLoaded", init);

    async function init() {
        bindHandlers();
        await loadRoots();
    }

    function bindHandlers() {
        // Sidebar
        $("textRootDropdown").addEventListener("change", e => loadFiles(e.target.value));
        $("textRecursive").addEventListener("change", () => state.currentRoot && loadFiles(state.currentRoot));
        $("btn-text-refresh").addEventListener("click", () => state.currentRoot && loadFiles(state.currentRoot));
        $("textFileFilter").addEventListener("input", filterFiles);
        $("btn-text-filter-clear").addEventListener("click", () => {
            $("textFileFilter").value = "";
            filterFiles();
        });

        // Viewer
        $("btn-text-close").addEventListener("click", closeViewer);
        $("btn-text-apply").addEventListener("click", () => { state.page = 1; loadGrid(); });
        $("btn-text-clear").addEventListener("click", clearFilters);
        $("btn-text-prev").addEventListener("click", () => { if (state.page > 1) { state.page--; loadGrid(); } });
        $("btn-text-next").addEventListener("click", () => { state.page++; loadGrid(); });
        $("textPageSize").addEventListener("change", e => { state.pageSize = +e.target.value; state.page = 1; loadGrid(); });
        $("textSearch").addEventListener("keydown", e => { if (e.key === "Enter") { state.page = 1; loadGrid(); } });
        $("btn-text-reload").addEventListener("click", async () => {
            if (!state.currentFile) return;
            try {
                await xla.api(`/api/text-logs/refresh?path=${encodeURIComponent(state.currentFile)}`, { method: "POST" });
                await loadGrid();
            } catch (err) { xla.toast(err.message, "error"); }
        });
    }

    // ====== Roots / files ======
    async function loadRoots() {
        try {
            const data = await xla.api("/api/text-logs/roots");
            const dd = $("textRootDropdown");
            dd.innerHTML = "";
            (data.roots || []).forEach(r => {
                const o = document.createElement("option");
                o.value = r; o.textContent = r;
                dd.appendChild(o);
            });
            if (dd.value) await loadFiles(dd.value);
            else xla.toast("No TextLogRoots configured in appsettings.json", "warning");
        } catch (err) { xla.toast(err.message, "error"); }
    }

    async function loadFiles(folder) {
        state.currentRoot = folder;
        try {
            const recursive = $("textRecursive").checked;
            const files = await xla.api(`/api/text-logs/files?path=${encodeURIComponent(folder)}&recursive=${recursive}`);
            state.files = Array.isArray(files) ? files : [];
            renderFiles();
        } catch (err) {
            state.files = [];
            renderFiles();
            xla.toast(err.message, "error");
        }
    }

    /** Render the file list in the sidebar (always sorted latest-first by the server). */
    function renderFiles() {
        const ul = $("textFilesList");
        ul.innerHTML = "";
        $("textFilesCount").textContent = state.files.length;

        if (state.files.length === 0) {
            const li = document.createElement("li");
            li.className = "textlog-file-empty text-secondary small";
            li.textContent = "No files in this folder.";
            ul.appendChild(li);
            return;
        }
        state.files.forEach(f => {
            const li = document.createElement("li");
            li.dataset.fullpath = f.fullPath;
            li.dataset.name = (f.name || "").toLowerCase();
            li.dataset.path = (f.fullPath || "").toLowerCase();
            if (state.currentFile && state.currentFile === f.fullPath) li.classList.add("is-active");
            li.innerHTML = `
                <div class="file-name" title="${xla.esc(f.fullPath)}">
                    <i class="bi bi-file-earmark-text"></i> ${xla.esc(f.name)}
                </div>
                <div class="file-meta text-secondary">
                    <span>${xla.esc(f.sizeDisplay || xla.fmtSize(f.size))}</span>
                    <span>·</span>
                    <span>${xla.fmtTime(f.lastModified)}</span>
                </div>`;
            li.addEventListener("click", () => openFile(f.fullPath));
            ul.appendChild(li);
        });

        filterFiles(); // apply current search box value if any
    }

    /** Client-side filter on file name / path. */
    function filterFiles() {
        const term = ($("textFileFilter").value || "").trim().toLowerCase();
        const items = $("textFilesList").querySelectorAll("li[data-fullpath]");
        let shown = 0;
        items.forEach(li => {
            const hit = !term ||
                li.dataset.name.includes(term) ||
                li.dataset.path.includes(term);
            li.classList.toggle("is-hidden", !hit);
            if (hit) shown++;
        });
        $("textFilesCount").textContent = term ? `${shown}/${items.length}` : items.length;
    }

    // ====== Viewer ======
    async function openFile(path) {
        state.currentFile = path;
        $("textOpenedFile").textContent = path;

        // Mark active in sidebar
        $("textFilesList").querySelectorAll("li").forEach(li => {
            li.classList.toggle("is-active", li.dataset.fullpath === path);
        });

        // Swap placeholder for viewer.
        $("textPlaceholder").classList.add("d-none");
        $("textViewer").classList.remove("d-none");

        state.page = 1;
        clearFilters({ skipReload: true });
        await loadGrid();
    }

    function closeViewer() {
        state.currentFile = null;
        $("textViewer").classList.add("d-none");
        $("textPlaceholder").classList.remove("d-none");
        $("textFilesList").querySelectorAll("li.is-active").forEach(li => li.classList.remove("is-active"));
    }

    function readQuery() {
        return {
            page: state.page,
            pageSize: state.pageSize,
            search: $("textSearch").value || null,
            severity: $("textSeverity").value || null,
            date: $("textDate").value || null,
            hour: numOrNull($("textHour").value),
            seconds: numOrNull($("textSeconds").value),
            sortBy: state.sortBy,
            sortDescending: state.sortDescending,
        };
    }

    function numOrNull(v) { return v === "" || v == null ? null : Number(v); }

    async function loadGrid() {
        if (!state.currentFile) return;
        try {
            const r = await xla.api(`/api/text-logs/query?path=${encodeURIComponent(state.currentFile)}`, {
                method: "POST", body: readQuery()
            });
            state.latest = r;
            renderStats(r.stats);
            renderTruncationBanner(r);
            renderGrid(r);
        } catch (err) { xla.toast(err.message, "error"); }
    }

    function renderTruncationBanner(r) {
        const banner = $("textTruncBanner");
        const text   = $("textTruncText");
        if (!banner || !text) return;
        if (!r.truncated) { banner.classList.add("d-none"); return; }
        const mb = (r.fileSize / (1024 * 1024)).toFixed(1);
        const kept = (r.stats && r.stats.totalEntries) || 0;
        const scanned = r.rawEntriesScanned || kept;
        text.textContent =
            `Large file (${mb} MB, ${scanned.toLocaleString()} entries scanned). ` +
            `Showing the most recent ${kept.toLocaleString()} entries — earlier entries dropped to keep memory bounded.`;
        banner.classList.remove("d-none");
    }

    function renderStats(s) {
        $("textStatsStrip").innerHTML = `
            <span class="stat-chip">Total <strong>${s.totalEntries.toLocaleString()}</strong></span>
            <span class="stat-chip error">Error <strong>${s.errorCount}</strong></span>
            <span class="stat-chip warning">Warning <strong>${s.warningCount}</strong></span>
            <span class="stat-chip info">Info <strong>${s.infoCount}</strong></span>
            <span class="stat-chip debug">Debug <strong>${s.debugCount}</strong></span>
            ${s.latestEntryTime ? `<span class="stat-chip">Latest <strong>${xla.fmtTime(s.latestEntryTime)}</strong></span>` : ""}`;
        $("textStatsLine").textContent = `${s.totalEntries.toLocaleString()} entries`;
    }

    /* Columns: Date, Time, Seconds (as its own column), Severity, Message + raw. */
    const COLUMNS = [
        { key: "Date",     label: "Date",     get: e => xla.esc(e.date) },
        { key: "Time",     label: "Time",     get: e => xla.esc(e.time) },
        { key: "Seconds",  label: "Seconds",  get: e => e.seconds ?? "" },
        { key: "Severity", label: "Severity", get: e => xla.sevBadge(e.severityLevel) },
        { key: "Message",  label: "Message",  get: (e, term) => xla.highlight(e.message || "", term) },
        { key: "Timezone", label: "TZ",       get: e => xla.esc(e.timezoneOffset) },
        { key: "Code",     label: "Code",     get: e => xla.esc(e.severityCode) },
    ];

    function renderGrid(r) {
        const head = $("textGridHead");
        const body = $("textGridBody");
        const term = $("textSearch").value || "";

        head.innerHTML = "<tr>" + COLUMNS.map(c => `
            <th data-key="${c.key}">${c.label}${state.sortBy === c.key ? (state.sortDescending ? " ▼" : " ▲") : ""}</th>`).join("") + "</tr>";
        head.querySelectorAll("th").forEach(th => th.addEventListener("click", () => {
            const k = th.dataset.key;
            if (state.sortBy === k) state.sortDescending = !state.sortDescending;
            else { state.sortBy = k; state.sortDescending = true; }
            state.page = 1;
            loadGrid();
        }));

        body.innerHTML = "";
        if ((r.entries || []).length === 0) {
            body.innerHTML = `<tr><td colspan="${COLUMNS.length}" class="text-center text-secondary py-4">
                No entries match the current filters.</td></tr>`;
        }
        (r.entries || []).forEach(e => {
            const tr = document.createElement("tr");
            const sev = (e.severityLevel || "").trim();
            if (["Error","Warning","Info","Debug"].includes(sev)) {
                tr.classList.add("sev-row", "sev-row-" + sev);
            }
            tr.innerHTML = COLUMNS.map(c => {
                const cell = c.get(e, term);
                const plain = c.key === "Message"
                    ? String(e.message ?? "")
                    : extractPlain(c.get(e));
                return `<td data-col="${c.key}" data-full="${xla.esc(plain)}" title="${xla.esc(plain)}">${cell ?? ""}</td>`;
            }).join("");
            tr.addEventListener("click", (ev) => {
                const td = ev.target.closest("td[data-col]");
                if (!td) return;
                const col = td.dataset.col;
                const colMeta = COLUMNS.find(c => c.key === col);
                openTextCellModal((colMeta && colMeta.label) || col, td.dataset.full || "");
            });
            body.appendChild(tr);
        });

        const total = r.total ?? 0;
        if (total === 0) {
            $("textPagerInfo").textContent = "0 entries";
        } else {
            const start = (r.page - 1) * r.pageSize + 1;
            const end = Math.min(total, r.page * r.pageSize);
            $("textPagerInfo").textContent = `${start.toLocaleString()}–${end.toLocaleString()} of ${total.toLocaleString()}`;
        }
    }

    function clearFilters(opts) {
        ["textSearch","textDate","textHour","textSeconds"].forEach(id => $(id).value = "");
        $("textSeverity").value = "";
        state.page = 1;
        if (!(opts && opts.skipReload)) loadGrid();
    }

    function openTextCellModal(title, text) {
        const labelEl = $("textCellModalLabel");
        const bodyEl  = $("textCellModalBody");
        const modalEl = $("textCellModal");
        if (!bodyEl || !modalEl || !window.bootstrap) return;
        if (labelEl) labelEl.textContent = title || "Entry detail";
        bodyEl.textContent = (text && String(text).length > 0) ? String(text) : "(empty)";
        bootstrap.Modal.getOrCreateInstance(modalEl).show();
    }

    function extractPlain(value) {
        if (value == null) return "";
        const s = String(value);
        return s.indexOf("<") >= 0 ? s.replace(/<[^>]+>/g, "") : s;
    }
})();
