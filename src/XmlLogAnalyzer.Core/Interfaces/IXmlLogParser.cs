using XmlLogAnalyzer.Core.Models;

namespace XmlLogAnalyzer.Core.Interfaces;

public interface IXmlLogParser
{
    /// <summary>
    /// Streams a log file from disk and yields <see cref="LogEntry"/> instances.
    /// Implementations MUST:
    /// - Auto-wrap in a temporary &lt;Root&gt; if the file lacks a single root element.
    /// - Be hardened against XXE / DTD injection.
    /// - Skip and continue past malformed entries (record but don't crash).
    /// </summary>
    IAsyncEnumerable<LogEntry> StreamAsync(string filePath, CancellationToken ct = default);

    /// <summary>Parse a small XML string (raw editor input). Same auto-wrap rules.</summary>
    Task<IReadOnlyList<LogEntry>> ParseStringAsync(string xml, CancellationToken ct = default);
}
