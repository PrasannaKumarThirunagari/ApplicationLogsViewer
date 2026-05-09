/* dashboard.js — analytics view for a single log file */
(function () {
    document.addEventListener("DOMContentLoaded", () => {
        document.getElementById("btn-load-dash").addEventListener("click", load);
    });

    async function load() {
        const path = document.getElementById("dashPath").value.trim();
        if (!path) return;
        try {
            const stats = await xla.api(`/api/logs/stats?path=${encodeURIComponent(path)}`);
            renderCards(stats);
            renderBars("byMachine", stats.byMachine);
            renderBars("byException", stats.exceptionFrequency);
        } catch (err) {
            xla.toast(err.message, "error");
        }
    }

    function card(title, value, kind) {
        return `<div class="col-6 col-lg-3">
            <div class="card panel-card text-${kind || "info"}">
                <div class="card-body">
                    <div class="small text-secondary">${xla.esc(title)}</div>
                    <div class="display-6">${value}</div>
                </div>
            </div>
        </div>`;
    }

    function renderCards(s) {
        document.getElementById("dashCards").innerHTML =
            card("Total entries", s.totalEntries.toLocaleString()) +
            card("Errors",   s.errorCount,   "error") +
            card("Warnings", s.warningCount, "warning") +
            card("Info",     s.infoCount,    "info");
    }

    function renderBars(id, dict) {
        const host = document.getElementById(id);
        if (!dict || Object.keys(dict).length === 0) {
            host.innerHTML = `<div class="text-secondary small">No data</div>`;
            return;
        }
        const entries = Object.entries(dict).sort((a,b) => b[1] - a[1]).slice(0, 12);
        const max = entries[0][1];
        host.classList.add("barlist");
        host.innerHTML = entries.map(([k, v]) =>
            `<div class="bar-row">
                <div class="text-truncate" title="${xla.esc(k)}">${xla.esc(k)}</div>
                <div class="bar-bg"><div class="bar-fill" style="width:${(v/max*100).toFixed(1)}%"></div></div>
                <div class="text-end">${v}</div>
            </div>`).join("");
    }
})();
