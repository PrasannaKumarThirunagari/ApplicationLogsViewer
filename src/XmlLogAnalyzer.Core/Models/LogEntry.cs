using System.Collections.Generic;

namespace XmlLogAnalyzer.Core.Models;

/// <summary>
/// Strongly-typed representation of a single &lt;LogData&gt; entry parsed from an XML log file.
/// Unknown / additional XML elements are preserved in <see cref="ExtraFields"/> so the
/// dynamic grid view can still render them.
/// </summary>
public sealed class LogEntry
{
    /// <summary>Zero-based index of this entry in the original file (used as a stable key).</summary>
    public int Index { get; set; }

    public string? ConversationId { get; set; }
    public string? SeverityLevel { get; set; }
    public DateTimeOffset? Time { get; set; }
    public string? MachineName { get; set; }
    public string? ComponentName { get; set; }
    public string? TypeName { get; set; }
    public string? Operation { get; set; }
    public string? LogMessage { get; set; }
    public string? StackTrace { get; set; }
    public string? ProcessId { get; set; }
    public string? ManagedThreadId { get; set; }
    public string? AppDomainID { get; set; }
    public string? AppDomainName { get; set; }
    public string? StkFrame { get; set; }

    /// <summary>Original XML for the entry — used by the Raw / Pretty XML view.</summary>
    public string? RawXml { get; set; }

    /// <summary>Anything that wasn't a recognised column.</summary>
    public IDictionary<string, string?> ExtraFields { get; set; } = new Dictionary<string, string?>();
}
