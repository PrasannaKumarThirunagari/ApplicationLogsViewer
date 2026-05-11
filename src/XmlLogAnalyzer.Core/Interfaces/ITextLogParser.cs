using XmlLogAnalyzer.Core.Models;

namespace XmlLogAnalyzer.Core.Interfaces;

public interface ITextLogParser
{
    /// <summary>
    /// Streams a plain-text log file from disk and yields one <see cref="TextLogEntry"/> per
    /// logical entry. A "logical entry" is one timestamp-prefixed line plus any continuation
    /// lines that follow it (for example, multi-line stack traces).
    ///
    /// Implementations must:
    /// * Open the file with <c>FileShare.ReadWrite | Delete</c> so the producer can keep writing.
    /// * Be safe for huge files (constant memory).
    /// * Never throw on a single malformed line — treat it as continuation of the previous entry,
    ///   or, if none, emit a synthetic Info entry with the raw line.
    /// </summary>
    IAsyncEnumerable<TextLogEntry> StreamAsync(string filePath, CancellationToken ct = default);
}
