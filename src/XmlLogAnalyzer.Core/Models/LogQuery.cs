namespace XmlLogAnalyzer.Core.Models;

/// <summary>
/// Query parameters used by the log grid: pagination + filtering + sorting.
/// Everything is optional; sensible defaults apply.
/// </summary>
public sealed class LogQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;

    public string? Search { get; set; }
    public string? Severity { get; set; }
    public string? MachineName { get; set; }
    public string? ProcessId { get; set; }
    public string? Operation { get; set; }
    public string? Keyword { get; set; }
    public DateTimeOffset? FromDate { get; set; }
    public DateTimeOffset? ToDate { get; set; }

    /// <summary>Field to sort on. Default = Time.</summary>
    public string SortBy { get; set; } = "Time";
    /// <summary>true = descending. Default = true (latest first).</summary>
    public bool SortDescending { get; set; } = true;
}

public sealed class LogQueryResult
{
    public required IReadOnlyList<LogEntry> Entries { get; set; }
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public LogStats Stats { get; set; } = new();
}

public sealed class LogStats
{
    public int TotalEntries { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public int InfoCount { get; set; }
    public int DebugCount { get; set; }
    public DateTimeOffset? LatestErrorTime { get; set; }
    public DateTimeOffset? LatestEntryTime { get; set; }
    public Dictionary<string, int> ByMachine   { get; set; } = new();
    public Dictionary<string, int> ByOperation { get; set; } = new();
    public Dictionary<string, int> ExceptionFrequency { get; set; } = new();
}
