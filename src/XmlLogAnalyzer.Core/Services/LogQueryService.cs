using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XmlLogAnalyzer.Core.Interfaces;
using XmlLogAnalyzer.Core.Models;

namespace XmlLogAnalyzer.Core.Services;

/// <summary>
/// Loads a log file once (streamed from disk via <see cref="IXmlLogParser"/>), caches the
/// resulting in-memory list keyed by file path + last-write timestamp, then applies query
/// (filter / search / sort / paginate) on subsequent calls.
/// </summary>
public sealed class LogQueryService : ILogQueryService
{
    private readonly IXmlLogParser _parser;
    private readonly IPathValidator _validator;
    private readonly IFolderService _folders;
    private readonly IMemoryCache _cache;
    private readonly AppSettings _settings;
    private readonly ILogger<LogQueryService> _logger;

    public LogQueryService(
        IXmlLogParser parser,
        IPathValidator validator,
        IFolderService folders,
        IMemoryCache cache,
        IOptions<AppSettings> options,
        ILogger<LogQueryService> logger)
    {
        _parser = parser;
        _validator = validator;
        _folders = folders;
        _cache = cache;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<LogQueryResult> QueryAsync(string filePath, LogQuery query, CancellationToken ct = default)
    {
        var entries = await LoadAsync(filePath, ct);
        var filtered = ApplyFilters(entries, query);
        var sorted = ApplySort(filtered, query);

        var pageSize = Math.Clamp(query.PageSize <= 0 ? 100 : query.PageSize, 1, _settings.MaxPageSize);
        var page = Math.Max(1, query.Page);

        var paged = sorted.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new LogQueryResult
        {
            Entries = paged,
            Total = sorted.Count,
            Page = page,
            PageSize = pageSize,
            Stats = ComputeStats(entries),
        };
    }

    public async Task<string?> GetEntryRawXmlAsync(string filePath, int index, CancellationToken ct = default)
    {
        var entries = await LoadAsync(filePath, ct);
        return entries.FirstOrDefault(e => e.Index == index)?.RawXml;
    }

    public async Task<LogStats> GetStatsAsync(string filePath, CancellationToken ct = default)
    {
        var entries = await LoadAsync(filePath, ct);
        return ComputeStats(entries);
    }

    public void Invalidate(string filePath)
    {
        var safe = _validator.ValidateFile(filePath);
        var key = MakeKey(safe);
        _cache.Remove(key);
    }

    /// <inheritdoc />
    public async Task InvalidateFolderContentsAsync(string folderPath, CancellationToken ct = default)
    {
        var files = await _folders.GetFilesAsync(folderPath, recursive: false, ct).ConfigureAwait(false);
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            Invalidate(f.FullPath);
        }
    }

    // -----------------------------------------------------------------

    private async Task<List<LogEntry>> LoadAsync(string filePath, CancellationToken ct)
    {
        var safe = _validator.ValidateFile(filePath);
        var key = MakeKey(safe);

        if (_cache.TryGetValue<List<LogEntry>>(key, out var cached) && cached is not null)
            return cached;

        var list = new List<LogEntry>();
        await foreach (var e in _parser.StreamAsync(safe, ct))
            list.Add(e);

        _cache.Set(key, list, new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(_settings.CacheSlidingMinutes),
            // Rough estimate — let MemoryCache use the count for size-based eviction.
            Size = list.Count,
        });

        return list;
    }

    private static string MakeKey(string fullPath)
    {
        var info = new FileInfo(fullPath);
        return $"log::{fullPath.ToLowerInvariant()}::{info.LastWriteTimeUtc.Ticks}::{info.Length}";
    }

    private static IEnumerable<LogEntry> ApplyFilters(IEnumerable<LogEntry> source, LogQuery q)
    {
        var query = source;

        if (!string.IsNullOrWhiteSpace(q.Severity))
            query = query.Where(e => string.Equals(e.SeverityLevel, q.Severity, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(q.MachineName))
            query = query.Where(e => string.Equals(e.MachineName, q.MachineName, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(q.ProcessId))
            query = query.Where(e => string.Equals(e.ProcessId, q.ProcessId, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(q.Operation))
            query = query.Where(e => (e.Operation ?? "").Contains(q.Operation, StringComparison.OrdinalIgnoreCase));

        if (q.FromDate is not null)
            query = query.Where(e => e.Time is null || e.Time >= q.FromDate);

        if (q.ToDate is not null)
            query = query.Where(e => e.Time is null || e.Time <= q.ToDate);

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            // Multi-keyword: split on whitespace; AND across tokens.
            var tokens = q.Search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            query = query.Where(e =>
            {
                var bag = $"{e.LogMessage} {e.StackTrace} {e.Operation} {e.MachineName} {e.ConversationId} {e.TypeName} {e.ComponentName}";
                foreach (var t in tokens)
                    if (bag.IndexOf(t, StringComparison.OrdinalIgnoreCase) < 0) return false;
                return true;
            });
        }

        if (!string.IsNullOrWhiteSpace(q.Keyword))
        {
            query = query.Where(e =>
                (e.LogMessage ?? "").Contains(q.Keyword, StringComparison.OrdinalIgnoreCase) ||
                (e.StackTrace ?? "").Contains(q.Keyword, StringComparison.OrdinalIgnoreCase));
        }

        return query;
    }

    private static List<LogEntry> ApplySort(IEnumerable<LogEntry> source, LogQuery q)
    {
        IOrderedEnumerable<LogEntry> ordered = q.SortBy.ToLowerInvariant() switch
        {
            "severity"      => q.SortDescending
                                ? source.OrderByDescending(e => SeverityLevel.Rank(e.SeverityLevel))
                                : source.OrderBy(e => SeverityLevel.Rank(e.SeverityLevel)),
            "machine"       => q.SortDescending
                                ? source.OrderByDescending(e => e.MachineName)
                                : source.OrderBy(e => e.MachineName),
            "operation"     => q.SortDescending
                                ? source.OrderByDescending(e => e.Operation)
                                : source.OrderBy(e => e.Operation),
            "processid"     => q.SortDescending
                                ? source.OrderByDescending(e => e.ProcessId)
                                : source.OrderBy(e => e.ProcessId),
            _ /* time */    => q.SortDescending
                                ? source.OrderByDescending(e => e.Time ?? DateTimeOffset.MinValue)
                                : source.OrderBy(e => e.Time ?? DateTimeOffset.MinValue),
        };

        // Tie-breakers: newest time first, then highest severity first, then index.
        ordered = ordered
            .ThenByDescending(e => e.Time ?? DateTimeOffset.MinValue)
            .ThenByDescending(e => SeverityLevel.Rank(e.SeverityLevel))
            .ThenByDescending(e => e.Index);

        return ordered.ToList();
    }

    private static LogStats ComputeStats(IReadOnlyCollection<LogEntry> entries)
    {
        var stats = new LogStats { TotalEntries = entries.Count };

        foreach (var e in entries)
        {
            switch (SeverityLevel.Rank(e.SeverityLevel))
            {
                case 4: stats.ErrorCount++;   break;
                case 3: stats.WarningCount++; break;
                case 2: stats.InfoCount++;    break;
                case 1: stats.DebugCount++;   break;
            }

            if (e.Time is { } t)
            {
                if (stats.LatestEntryTime is null || t > stats.LatestEntryTime) stats.LatestEntryTime = t;
                if (SeverityLevel.Rank(e.SeverityLevel) == 4 &&
                    (stats.LatestErrorTime is null || t > stats.LatestErrorTime))
                    stats.LatestErrorTime = t;
            }

            if (!string.IsNullOrWhiteSpace(e.MachineName))
            {
                stats.ByMachine.TryGetValue(e.MachineName!, out var mc);
                stats.ByMachine[e.MachineName!] = mc + 1;
            }

            if (!string.IsNullOrWhiteSpace(e.Operation))
            {
                stats.ByOperation.TryGetValue(e.Operation!, out var oc);
                stats.ByOperation[e.Operation!] = oc + 1;
            }

            // Rough exception classifier: first line of LogMessage up to ':'
            if (!string.IsNullOrWhiteSpace(e.LogMessage))
            {
                var msg = e.LogMessage!;
                var bucket = msg.Length > 120 ? msg[..120] : msg;
                stats.ExceptionFrequency.TryGetValue(bucket, out var c);
                stats.ExceptionFrequency[bucket] = c + 1;
            }
        }

        return stats;
    }
}
