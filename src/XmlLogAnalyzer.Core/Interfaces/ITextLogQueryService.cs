using XmlLogAnalyzer.Core.Models;

namespace XmlLogAnalyzer.Core.Interfaces;

public interface ITextLogQueryService
{
    /// <summary>The configured text-log roots, filtered to ones that actually exist on disk.</summary>
    IReadOnlyList<string> GetRoots();

    /// <summary>List of text-log files under a folder, sorted newest-first.</summary>
    Task<IReadOnlyList<FileInfoDto>> GetFilesAsync(string folder, bool recursive, CancellationToken ct = default);

    /// <summary>Loads (or returns from cache) parsed entries and applies the query.</summary>
    Task<TextLogQueryResult> QueryAsync(string filePath, TextLogQuery query, CancellationToken ct = default);

    /// <summary>Dashboard-only summary stats for a file.</summary>
    Task<TextLogStats> GetStatsAsync(string filePath, CancellationToken ct = default);

    /// <summary>Invalidate cached parse results for a file (used by the Refresh button).</summary>
    void Invalidate(string filePath);
}
