# XML Log Analyzer

A lightweight, high-performance ASP.NET Core (**see `global.json` — currently .NET 10**) web application for reading and analyzing XML log files (e.g. application error logs containing `<LogData>` entries).

The app intelligently handles XML files **with or without** a single root element, validates and sanitizes input, and renders huge log files in a fast, sortable, filterable, dark-mode-friendly grid.

## Tech stack

| Layer        | Choice                                                |
|--------------|-------------------------------------------------------|
| Backend      | ASP.NET Core Web API + Razor Pages (pinned to SDK in `global.json`) |
| Frontend     | Razor Pages, Bootstrap 5, vanilla JS (no npm, no SPA) |
| Persistence  | None required (file-based prefs in `App_Data`)        |
| Architecture | Clean architecture — `Core` library + `Web` host      |

No npm. No Node.js. No Angular/React. Just `dotnet`.

## Solution layout

```
Application Error Log Reader/
├── XmlLogAnalyzer.sln
├── README.md
├── samples/                          # sample XML log files
└── src/
    ├── XmlLogAnalyzer.Core/          # models, services, parser, validation
    │   ├── Interfaces/
    │   ├── Models/
    │   ├── Services/
    │   └── DependencyInjection.cs
    └── XmlLogAnalyzer.Web/           # ASP.NET Core host
        ├── Controllers/              # /api/folders, /api/logs
        ├── Middleware/               # ApiExceptionMiddleware
        ├── Pages/                    # Razor pages (Index, Dashboard, Paste, RawXml → home redirect)
        ├── Properties/launchSettings.json
        ├── wwwroot/                  # site.css + JS + vendored Bootstrap
        ├── appsettings.json
        ├── Program.cs
        └── web.config                # IIS hosting
```

## Features

### Folder & file explorer (Tab 1)
- Configured root folders, dropdown + tree view with expand/collapse
- Recursive enumeration toggle
- Folder filter, refresh button
- Favorites and recents (persisted to `App_Data/preferences.json`)
- File table with name / size / last modified / entry counts
- File search, sort, filter
- **Latest file first** by default (sorted descending by `LastModified`)

### XML parsing & transformation
- Streaming `XmlReader`-based parser — constant memory for huge files
- **Auto-wraps a synthetic `<Root>`** when the file lacks a single root element
- Hardened against XXE, billion-laughs, and DTD injection
- Skips and logs malformed entries instead of crashing
- Strongly-typed `LogEntry` model with all known fields and an `ExtraFields` map for unknown elements
- Severity normalization (Error / Warning / Info / Debug)

### Log viewer
- Resizable grid with sticky header
- Sorting (click any column), pagination, virtual-friendly with chunked pages
- Dynamic column generation
- **Latest errors first** by default (descending by Time then Severity)
- Filters: severity, machine, processId, operation, date range, multi-keyword global search
- Inline highlighting of search terms
- Color-coded severity badges
- Row → detail views: Raw XML / Pretty XML / Tree / JSON

### Dashboard
- Total / error / warning / info / debug counts
- Latest error timestamp
- Top machines and top exception messages

### Export & utilities
- CSV export of the current filtered selection
- "Paste XML" page — parse arbitrary XML pasted into a textarea (still auto-wraps)
- File comparison-friendly favorites
- Refresh button to invalidate the in-memory cache

### UI/UX
- Dark mode (default) and light mode — toggle persists in `localStorage`
- Sticky filter row, sticky table headers
- Responsive layout
- Keyboard shortcut: `/` focuses global search
- Toast notifications for errors / success

### Performance
- Async file reads (`useAsync: true`)
- Streamed XML parsing (`IAsyncEnumerable<LogEntry>`)
- Files opened with `FileShare.ReadWrite | Delete` so live logs can be read while the producer is appending
- `IMemoryCache` of parsed entries keyed by path + size + last write — second open is instant
- Configurable `MaxFileSizeBytes`, `MaxPageSize`, sliding cache expiration
- `gzip` / `brotli` response compression on API and static files

### Security & validation
- Path-traversal guard via `IPathValidator` — every filesystem path is normalised through `Path.GetFullPath` and checked against `AllowedRoots` and `ForbiddenPaths`
- Rejects unsupported extensions
- Disables DTD processing entirely (`DtdProcessing.Prohibit`)
- Caps entity expansion (`MaxCharactersFromEntities = 1024`)
- Server-side HTML escaping in JSON; client-side via `xla.esc`
- IIS adds `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy` headers
- Centralised `ApiExceptionMiddleware` returns sanitised JSON envelopes (no stack traces leaked)

## Configuration

Edit `src/XmlLogAnalyzer.Web/appsettings.json`:

```json
"XmlLogAnalyzer": {
  "AllowedRoots": [ "C:\\Logs", "C:\\Temp", "C:\\Users" ],
  "ForbiddenPaths": [ "C:\\Windows", "C:\\Program Files", "..." ],
  "AllowedExtensions": [ ".xml", ".log", ".txt" ],
  "MaxFileSizeBytes": 1073741824,
  "CacheSlidingMinutes": 15,
  "FavoritesMax": 50,
  "RecentMax": 25,
  "MaxPageSize": 5000,
  "RecursiveByDefault": true
}
```

## Run locally

Prerequisite: [.NET 10 SDK (x64)](https://dotnet.microsoft.com/download/dotnet/10.0). The
`global.json` in the solution root pins the project to .NET 10 and `<Platforms>x64</Platforms>`
in both csproj files locks the build to x64.

```pwsh
# from solution root — IMPORTANT: clean first if you previously built against .NET 8
dotnet clean
dotnet restore
dotnet build -c Release

# (optional) populate vendored Bootstrap — see lib/README.txt for one-liner
# the app falls back to a CDN if the files aren't present.

dotnet run --project src/XmlLogAnalyzer.Web -c Release
```

> **Got "You must install or update .NET to run this application … framework version '8.0.0'"?**
> That message comes from a stale `XmlLogAnalyzer.Web.exe` in `bin/Debug/net8.0/` left over
> from an earlier build. Run `dotnet clean` and rebuild — the new output goes to `bin/x64/Debug/net10.0/`.

Then open <https://localhost:5001>. Swagger UI at <https://localhost:5001/swagger>.

You can drop the included sample logs (`samples/*.xml`) into one of the `AllowedRoots` and open them from the explorer.

## Deploy to IIS

1. Install the [ASP.NET Core Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/10.0) that matches **your app’s runtime** (pinned in `global.json`) on the target server.
2. Publish the Web project:
   ```pwsh
   dotnet publish src/XmlLogAnalyzer.Web -c Release -o C:\Inetpub\XmlLogAnalyzer
   ```
3. In IIS Manager, add a new website pointing at `C:\Inetpub\XmlLogAnalyzer`.
4. Set the application pool to **No Managed Code** (the host bundle handles .NET).
5. Grant the application pool identity **Read** access to your log directories (`AllowedRoots`).
6. Optional: enable HTTPS in IIS bindings; the included `web.config` adds standard security headers.

The bundled `web.config` is already wired for in-process hosting via `AspNetCoreModuleV2`.

## API surface (selected)

| Method | Route                          | Purpose                                |
|--------|--------------------------------|----------------------------------------|
| GET    | `/api/folders/roots`           | Allowed roots + favorites + recent     |
| GET    | `/api/folders/tree`            | Folder tree (recursive option)         |
| GET    | `/api/folders/files`           | XML/TXT files, latest-first            |
| POST   | `/api/folders/favorites/add`   | Add a favorite                         |
| POST   | `/api/folders/favorites/remove`| Remove a favorite                      |
| POST   | `/api/logs/query?path=...`     | Filter / sort / paginate a log file    |
| GET    | `/api/logs/stats?path=...`     | Dashboard summary                      |
| GET    | `/api/logs/entry/raw`          | Raw XML for a single entry             |
| GET    | `/api/logs/entry/pretty`       | Pretty-printed XML                     |
| GET    | `/api/logs/entry/json`         | JSON conversion                        |
| POST   | `/api/logs/refresh`            | Invalidate cache for a file            |
| POST   | `/api/logs/export/csv`         | Export filtered rows to CSV            |
| POST   | `/api/logs/parse`              | Parse pasted XML (body `{ "xml": "..." }`, capped by `MaxPageSize`) |

## Performance recommendations

- Keep individual log files under ~500 MB for snappy first-open. The parser streams, but
  the in-memory filter/sort cache is a `List<LogEntry>` and 5M entries is a lot of GC pressure.
- For "tail-style" live logs, leave the file alone — the parser opens with `FileShare.ReadWrite | Delete`
  and re-parses on click of **Refresh**.
- If you need cross-process invalidation (multiple web nodes), wire up `IMemoryCache` to a distributed cache.
- Tune `MaxPageSize` and the page-size dropdown in `Pages/Index.cshtml` for your typical workload.
- For very large folders (>10k files), turn off the recursive tree toggle.

## Coding standards used

- Clean architecture (`Core` is independent of ASP.NET; `Web` depends on `Core`)
- SOLID — services depend on interfaces, registered in `DependencyInjection.cs`
- Async/await throughout; cancellation tokens on every long-running call
- Strongly typed config bound from `appsettings.json`
- Centralised exception handling via middleware
- No `unsafe`, no reflection in hot paths
- Bootstrap-only CSS, no compiler / preprocessor / npm

## License

Provided as-is for internal use. Adapt freely.
