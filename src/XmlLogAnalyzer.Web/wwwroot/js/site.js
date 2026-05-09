/* =====================================================================
   site.js — common utilities used by every page.
   - Theme toggle (persisted in localStorage)
   - Tiny fetch wrapper that surfaces the JSON error envelope
   - Toast helper (Bootstrap)
   - HTML escaper + highlight helper
===================================================================== */

(function () {
    const THEME_KEY = "xla.theme";

    function applyTheme(theme) {
        document.documentElement.setAttribute("data-theme", theme);
        localStorage.setItem(THEME_KEY, theme);
    }

    const saved = localStorage.getItem(THEME_KEY) || "dark";
    applyTheme(saved);

    document.addEventListener("DOMContentLoaded", () => {
        const btn = document.getElementById("themeToggle");
        if (btn) {
            btn.addEventListener("click", () => {
                const current = document.documentElement.getAttribute("data-theme");
                applyTheme(current === "dark" ? "light" : "dark");
            });
        }
    });
})();

window.xla = window.xla || {};

xla.api = async function (url, opts = {}) {
    const headers = { "Accept": "application/json", ...(opts.headers || {}) };
    if (opts.body && !(opts.body instanceof FormData)) {
        headers["Content-Type"] = "application/json";
        if (typeof opts.body !== "string") opts.body = JSON.stringify(opts.body);
    }
    const res = await fetch(url, { ...opts, headers });
    if (!res.ok) {
        let payload = null;
        try { payload = await res.json(); } catch { /* not json */ }
        const msg = payload?.error || `HTTP ${res.status}`;
        const err = new Error(msg);
        err.status = res.status;
        err.payload = payload;
        throw err;
    }
    const ct = res.headers.get("content-type") || "";
    if (ct.includes("application/json")) return res.json();
    if (ct.includes("xml") || ct.includes("text")) return res.text();
    return res;
};

xla.toast = function (message, kind = "info") {
    const host = document.getElementById("toast-host");
    if (!host) { console.log("[toast]", message); return; }
    const el = document.createElement("div");
    el.className = `toast text-bg-${kind === "error" ? "danger" : kind} border-0`;
    el.setAttribute("role", "alert");
    el.innerHTML = `<div class="d-flex"><div class="toast-body">${xla.esc(message)}</div>
        <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast"></button></div>`;
    host.appendChild(el);
    const t = new bootstrap.Toast(el, { delay: 4000 });
    t.show();
    el.addEventListener("hidden.bs.toast", () => el.remove());
};

xla.esc = function (s) {
    if (s === null || s === undefined) return "";
    return String(s)
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#39;");
};

xla.highlight = function (text, terms) {
    if (!text) return "";
    let out = xla.esc(text);
    if (!terms) return out;
    const tokens = terms.split(/\s+/).filter(Boolean);
    for (const t of tokens) {
        const re = new RegExp(`(${t.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")})`, "gi");
        out = out.replace(re, '<mark class="hl">$1</mark>');
    }
    return out;
};

xla.fmtTime = function (iso) {
    if (!iso) return "";
    const d = new Date(iso);
    if (isNaN(d)) return iso;
    return d.toLocaleString();
};

xla.fmtSize = function (bytes) {
    if (bytes == null) return "";
    const u = ["B","KB","MB","GB","TB"];
    let i = 0; let v = bytes;
    while (v >= 1024 && i < u.length - 1) { v /= 1024; i++; }
    return `${v.toFixed(2)} ${u[i]}`;
};

/* Severity color helper */
xla.sevBadge = function (sev) {
    const cls = ["Error","Warning","Info","Debug"].includes(sev) ? sev : "Info";
    return `<span class="severity-badge ${cls}">${xla.esc(sev || "")}</span>`;
};
