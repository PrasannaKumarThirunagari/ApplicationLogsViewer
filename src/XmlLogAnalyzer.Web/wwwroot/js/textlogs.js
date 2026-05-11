/* =====================================================================
   textlogs.js — driver for the "Text Logs" tab.
   Talks to /api/text-logs/*.  Client-side state only.
===================================================================== */
(function () {
    const state = {
        currentRoot: null,
        files: [],
        currentFile: null,
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
        $("textRootDropdown").addEventListener("change", e => loadFiles(e.target.value));
        $("textRecursive").addEventListener("change", () => state.currentRoot && loadFiles(state.currentRoot));
        $("btn-text-refresh").addEventListener("click", () => state.currentRoot && loadFiles(state.currentRoot));
        $("btn-text-close").addEventListener("click", closeViewer);
        $("btn-text-apply").addEventListener("click", () => { state.page = 1; loadGrid(); });
        $("btn-text-clear").addEventListener("click", clearFilters);
        $("btn-text-prev").addEventListener("click", () => { if (state.page > 1) { state.page--; loadGrid(); } });
        $("btn-text-next").addEventListener("click", () => { state.page++; loadGrid(); });
        $("textPageSize").addEventListener("change", e => { state.pageSize = +e.target.value; state.page = 1; loadGrid(); });
        $("textSearch").addEventListener("keydown", e => { if (e.key === "Enter") { state.page = 1; loadGrid(); } });
        $("btn-text-reload").addEventListener("click", async () => {
            if (!state.currentFile) return;
            await xla.api(`/api/text-logs/refresh?path=${encodeURIComponent(state.currentFile)}`, { method: "POST" });
            loadGrid();
        });
    }

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
        $("textCurrentFolder").textContent = folder;
        try {
            const recursive = $("textRecursive").checked;
            const files = await xla.api(`/api/text-logs/files?path=${encodeURIComponent(folder)}&recursive=${recursive}`);
            state.files = files;
            renderFiles();
        } catch (err) { xla.toast(err.message, "error"); }
    }

    function renderFiles() {
        const body = $("textFilesBody");
        body.innerHTML = "";
        if (state.files.length === 0) {
            body.innerHTML = `<tr><td colspan="4" class="text-secondary p-3">No files found.</td></tr>`;
            return;
        }
        state.files.forEach(f => {
            const tr = document.createElement("tr");
            tr.innerHTML = `
                <td class="text-truncate"><i class="bi bi-file-earmark-text"></i> ${xla.esc(f.name)}</td>
                <td class="text-end">${xla.esc(f.sizeDisplay || xla.fmtSize(f.size))}</td>
                <td>${xla.fmtTime(f.lastModified)}</td>
                <td class="text-end">
                  <button class="btn btn-sm btn-outline-primary"><i class="bi bi-eye"></i> Open</button>
                </td>`;
            tr.addEventListener("click", () => openFile(f.fullPath));
            body.appendChild(tr);
        });
    }

    async function openFile(path) {
        state.currentFile = path;
        $("textOpenedFile").textContent = path;
        $("textViewer").classList.remove("d-none");
        state.page = 1;
        clearFilters({ skipReload: true });
        await loadGrid();
    }

    function closeViewer() {
        state.currentFile = null;
        $("textViewer").classList.add("d-none");
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

    /** Visible banner when the server kept only the tail (most-recent) entries of a huge file. */
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
            `Showing the most recent ${kept.toLocaleString()} entries — earlier entries were dropped to keep memory bounded.`;
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
        $("textStatsLine").textContent =
            `${s.totalEntries.toLocaleString()} entries`;
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
        r.entries.forEach(e => {
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
            // Cell click → modal with the full content of that column.
            tr.addEventListener("click", (ev) => {
                const td = ev.target.closest("td[data-col]");
                if (!td) return;
                const col = td.dataset.col;
                const colMeta = COLUMNS.find(c => c.key === col);
                openTextCellModal((colMeta && colMeta.label) || col, td.dataset.full || "");
            });
            body.appendChild(tr);
        });

        const start = (r.page - 1) * r.pageSize + 1;
        const end = Math.min(r.total, r.page * r.pageSize);
        $("textPagerInfo").textContent = `${start.toLocaleString()}–${end.toLocaleString()} of ${r.total.toLocaleString()}`;
    }

    function clearFilters(opts) {
        ["textSearch","textDate","textHour","textSeconds"].forEach(id => $(id).value = "");
        $("textSeverity").value = "";
        state.page = 1;
        if (!(opts && opts.skipReload)) loadGrid();
    }

    /** Opens the #textCellModal and shows a column's full plain-text content. */
    function openTextCellModal(title, text) {
        const labelEl = $("textCellModalLabel");
        const bodyEl  = $("textCellModalBody");
        const modalEl = $("textCellModal");
        if (!bodyEl || !modalEl || !window.bootstrap) return;
        if (labelEl) labelEl.textContent = title || "Entry detail";
        bodyEl.textContent = (text && String(text).length > 0) ? String(text) : "(empty)";
        bootstrap.Modal.getOrCreateInstance(modalEl).show();
    }

    /** Strips HTML tags from a rendered cell value (e.g. severity badges) so the modal
        shows plain text rather than markup. */
    function extractPlain(value) {
        if (value == null) return "";
        const s = String(value);
        return s.indexOf("<") >= 0 ? s.replace(/<[^>]+>/g, "") : s;
    }
})();
