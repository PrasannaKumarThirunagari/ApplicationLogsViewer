using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XmlLogAnalyzer.Core.Interfaces;
using XmlLogAnalyzer.Core.Models;

namespace XmlLogAnalyzer.Core.Services;

/// <summary>
/// Loads text-log files via <see cref="ITextLogParser"/>, caches the full list in
/// <see cref="IMemoryCache"/> keyed by path + size + last-write, then applies query/sort/page.
///
/// Latest-first by default: entries are sorted descending by <c>Timestamp</c> (tie-break on Index).
/// </summary>
public sealed class TextLogQueryService : ITextLogQueryService
{
    private readonly ITextLogParser _parser;
    private readonly IPathValidator _validator;
    private readonly IMemoryCache _cache;
    private readonly AppSettings _settings;
    private readonly ILogger<TextLogQueryService> _logger;

    public TextLogQueryService(
        ITextLogParser parser,
        IPathValidator validator,
        IMemoryCache cache,
        IOptions<AppSettings> options,
        ILogger<TextLogQueryService> logger)
    {
        _parser = parser;
        _validator = validator;
        _cache = cache;
        _settings = options.Value;
        _logger = logger;
    }

    public IReadOnlyList<string> GetRoots() =>
        _settings.TextLogRoots.Where(Directory.Exists).ToList();

    public Task<IReadOnlyList<FileInfoDto>> GetFilesAsync(string folder, bool recursive, CancellationToken ct = default)
    {
        var safe = _validator.ValidateTextLogFolder(folder);
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        var files = new List<FileInfoDto>();
        foreach (var ext in _settings.TextLogExtensions)
        {
            ct.ThrowIfCancellationRequested();
            IEnumerable<string> matches;
            try { matches = Directory.EnumerateFiles(safe, "*" + ext, option); }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogDebug(ex, "Skipping inaccessible folder");
                continue;
            }
            foreach (var f in matches)
            {
                ct.ThrowIfCancellationRequested();
                var fi = new FileInfo(f);
                files.Add(new FileInfoDto
                {
                    Name = fi.Name,
                    FullPath = fi.FullName,
                    Size = fi.Length,
                    SizeDisplay = HumanSize(fi.Length),
                    LastModified = fi.LastWriteTime,
                    Extension = fi.Extension,
                });
            }
        }
        files.Sort((a, b) => b.LastModified.CompareTo(a.LastModified));
        return Task.FromResult<IReadOnlyList<FileInfoDto>>(files);
    }

    public async Task<TextLogQueryResult> QueryAsync(string filePath, TextLogQuery query, CancellationToken ct = default)
    {
        var loaded = await LoadAsync(filePath, ct);
        var filtered = ApplyFilters(loaded.Entries, query);
        var sorted = ApplySort(filtered, query);

        var pageSize = Math.Clamp(query.PageSize <= 0 ? 100 : query.PageSize, 1, _settings.MaxPageSize);
        var page = Math.Max(1, query.Page);
        var paged = sorted.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new TextLogQueryResult
        {
            Entries = paged,
            Total = sorted.Count,
            Page = page,
            PageSize = pageSize,
            Stats = ComputeStats(loaded.Entries),
            Truncated = loaded.Truncated,
            FileSize = loaded.FileSize,
            RawEntriesScanned = loaded.RawEntriesScanned,
        };
    }

    public async Task<TextLogStats> GetStatsAsync(string filePath, CancellationToken ct = default)
        => ComputeStats((await LoadAsync(filePath, ct)).Entries);

    public void Invalidate(string filePath)
    {
        var safe = _validator.ValidateTextLogFile(filePath);
        _cache.Remove(MakeKey(safe));
    }

    // -----------------------------------------------------------------
    /// <summary>Cached parse result + metadata.</summary>
    private sealed record LoadedFile(
        IReadOnlyList<TextLogEntry> Entries,
        bool Truncated,
        long FileSize,
        int RawEntriesScanned);

    /// <summary>
    /// Parse + cache. For files larger than <c>LargeFileThresholdBytes</c> we keep only the
    /// most recent <c>LargeFileTailEntries</c> entries via a circular ring — memory stays
    /// bounded even on 700 MB+ files at the cost of older entries being dropped.
    /// </summary>
    private async Task<LoadedFile> LoadAsync(string filePath, CancellationToken ct)
    {
        var safe = _validator.ValidateTextLogFile(filePath);
        var key = MakeKey(safe);
        if (_cache.TryGetValue<LoadedFile>(key, out var cached) && cached is not null)
            return cached;

        var fi = new FileInfo(safe);
        var isLarge = fi.Length > _settings.LargeFileThresholdBytes;
        var result = isLarge
            ? await StreamTailAsync(safe, fi.Length, ct)
            : await StreamFullAsync(safe, fi.Length, ct);

        _cache.Set(key, result, new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(_settings.CacheSlidingMinutes),
            Size = result.Entries.Count,
        });
        return result;
    }

    private async Task<LoadedFile> StreamFullAsync(string safe, long fileSize, CancellationToken ct)
    {
        var list = new List<TextLogEntry>();
        var scanned = 0;
        await foreach (var e in _parser.StreamAsync(safe, ct))
        {
            list.Add(e);
            scanned++;
        }
        return new LoadedFile(list, Truncated: false, FileSize: fileSize, RawEntriesScanned: scanned);
    }

    private async Task<LoadedFile> StreamTailAsync(string safe, long fileSize, CancellationToken ct)
    {
        // Bounded "tail" buffer — keep only the most recent N entries. Memory cost is
        // capped at LargeFileTailEntries × ~500 bytes regardless of file size.
        var cap = Math.Max(1, _settings.LargeFileTailEntries);
        var buf = new TextLogEntry[cap];
        var start = 0;
        var count = 0;
        var scanned = 0;
        var truncated = false;

        await foreach (var e in _parser.StreamAsync(safe, ct))
        {
            scanned++;
            if (count < cap)
            {
                buf[(start + count) % cap] = e;
                count++;
            }
            else
            {
                // Overwrite the oldest slot, advance start.
                buf[start] = e;
                start = (start + 1) % cap;
                truncated = true;
            }
        }

        // Flatten into a list in insertion (chronological) order.
        var list = new List<TextLogEntry>(count);
        for (int i = 0; i < count; i++)
            list.Add(buf[(start + i) % cap]);

        _logger.LogInformation(
            "Large text log {File} ({Size:N0} bytes) streamed; scanned {Scanned} entries, kept tail of {Kept} (truncated={Truncated})",
            safe, fileSize, scanned, count, truncated);

        return new LoadedFile(list, Truncated: truncated, FileSize: fileSize, RawEntriesScanned: scanned);
    }

    private static string MakeKey(string fullPath)
    {
        var info = new FileInfo(fullPath);
        return $"txtlog::{fullPath.ToLowerInvariant()}::{info.LastWriteTimeUtc.Ticks}::{info.Length}";
    }

    private static IEnumerable<TextLogEntry> ApplyFilters(IEnumerable<TextLogEntry> source, TextLogQuery q)
    {
        var query = source;

        if (!string.IsNullOrWhiteSpace(q.Severity))
            query = query.Where(e => string.Equals(e.SeverityLevel, q.Severity, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(q.Date))
            query = query.Where(e => string.Equals(e.Date, q.Date, StringComparison.OrdinalIgnoreCase));

        if (q.Hour is not null)
            query = query.Where(e => e.Hour == q.Hour);

        if (q.Seconds is not null)
            query = query.Where(e => e.Seconds == q.Seconds);

        if (q.FromDate is not null)
            query = query.Where(e => e.Timestamp is null || e.Timestamp >= q.FromDate);

        if (q.ToDate is not null)
            query = query.Where(e => e.Timestamp is null || e.Timestamp <= q.ToDate);

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            // Multi-keyword AND across tokens, case-insensitive, message + raw.
            var tokens = q.Search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            query = query.Where(e =>
            {
                foreach (var t in tokens)
                    if ((e.Message ?? "").IndexOf(t, StringComparison.OrdinalIgnoreCase) < 0 &&
                        (e.RawLine ?? "").IndexOf(t, StringComparison.OrdinalIgnoreCase) < 0)
                        return false;
                return true;
            });
        }

        return query;
    }

    private static List<TextLogEntry> ApplySort(IEnumerable<TextLogEntry> source, TextLogQuery q)
    {
        IOrderedEnumerable<TextLogEntry> ordered = q.SortBy.ToLowerInvariant() switch
        {
            "severity" => q.SortDescending
                            ? source.OrderByDescending(e => SeverityLevel.Rank(e.SeverityLevel))
                            : source.OrderBy(e => SeverityLevel.Rank(e.SeverityLevel)),
            "date"     => q.SortDescending
                            ? source.OrderByDescending(e => e.Date)
                            : source.OrderBy(e => e.Date),
            "seconds"  => q.SortDescending
                            ? source.OrderByDescending(e => e.Seconds ?? -1)
                            : source.OrderBy(e => e.Seconds ?? -1),
            _ /* timestamp */ => q.SortDescending
                            ? source.OrderByDescending(e => e.Timestamp ?? DateTimeOffset.MinValue)
                            : source.OrderBy(e => e.Timestamp ?? DateTimeOffset.MinValue),
        };

        // Tie-breakers: newest, then index.
        return ordered
            .ThenByDescending(e => e.Timestamp ?? DateTimeOffset.MinValue)
            .ThenByDescending(e => e.Index)
            .ToList();
    }

    private static TextLogStats ComputeStats(IReadOnlyCollection<TextLogEntry> entries)
    {
        var s = new TextLogStats { TotalEntries = entries.Count };

        foreach (var e in entries)
        {
            switch (SeverityLevel.Rank(e.SeverityLevel))
            {
                case 4: s.ErrorCount++;   break;
                case 3: s.WarningCount++; break;
                case 2: s.InfoCount++;    break;
                case 1: s.DebugCount++;   break;
            }

            if (e.Timestamp is { } t)
            {
                if (s.FirstEntryTime is null || t < s.FirstEntryTime) s.FirstEntryTime = t;
                if (s.LatestEntryTime is null || t > s.LatestEntryTime) s.LatestEntryTime = t;
            }

            if (!string.IsNullOrEmpty(e.Date))
            {
                s.ByDate.TryGetValue(e.Date!, out var c);
                s.ByDate[e.Date!] = c + 1;
            }
            if (!string.IsNullOrEmpty(e.SeverityLevel))
            {
                s.BySeverity.TryGetValue(e.SeverityLevel!, out var c);
                s.BySeverity[e.SeverityLevel!] = c + 1;
            }
        }
        return s;
    }

    private static string HumanSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes; int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return $"{v:0.##} {units[i]}";
    }
}
