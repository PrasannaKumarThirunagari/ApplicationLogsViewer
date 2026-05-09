using XmlLogAnalyzer.Core.Models;

namespace XmlLogAnalyzer.Core.Interfaces;

public interface ILogQueryService
{
    /// <summary>
    /// Loads (or returns from cache) the full set of log entries for a file, then
    /// applies the supplied query (filter / search / sort / paginate) and computes stats.
    /// </summary>
    Task<LogQueryResult> QueryAsync(string filePath, LogQuery query, CancellationToken ct = default);

    /// <summary>Returns the raw XML for a single log entry by index — used by the Raw / Pretty / Tree views.</summary>
    Task<string?> GetEntryRawXmlAsync(string filePath, int index, CancellationToken ct = default);

    /// <summary>Computes only the dashboard summary (no paging, no entry list).</summary>
    Task<LogStats> GetStatsAsync(string filePath, CancellationToken ct = default);

    /// <summary>Removes the cached entries for a file (used by the "refresh" button).</summary>
    void Invalidate(string filePath);

    /// <summary>
    /// Drops in-memory parse caches for every matching log file in this folder (non-recursive),
    /// so the next open re-reads from disk.
    /// </summary>
    Task InvalidateFolderContentsAsync(string folderPath, CancellationToken ct = default);
}
