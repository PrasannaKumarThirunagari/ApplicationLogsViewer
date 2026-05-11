namespace XmlLogAnalyzer.Core.Models;

/// <summary>Filter / sort / page parameters for the text-log grid.</summary>
public sealed class TextLogQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;

    public string? Search { get; set; }
    public string? Severity { get; set; }
    public string? Date { get; set; }           // exact date match, e.g. "2026-05-10"
    public int?    Seconds { get; set; }        // exact seconds match
    public int?    Hour { get; set; }           // exact hour match
    public DateTimeOffset? FromDate { get; set; }
    public DateTimeOffset? ToDate { get; set; }

    /// <summary>Field to sort on. Default = Timestamp.</summary>
    public string SortBy { get; set; } = "Timestamp";
    /// <summary>true = descending. Default = true (latest first).</summary>
    public bool SortDescending { get; set; } = true;
}

public sealed class TextLogQueryResult
{
    public required IReadOnlyList<TextLogEntry> Entries { get; set; }
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public TextLogStats Stats { get; set; } = new();

    /// <summary>
    /// True when the source file was too large to hold entirely in memory — only the most
    /// recent <c>LargeFileTailEntries</c> are available for filter/sort.
    /// </summary>
    public bool Truncated { get; set; }

    /// <summary>Raw file size in bytes (so the UI can show "loaded last X of Y MB").</summary>
    public long FileSize { get; set; }

    /// <summary>Total number of log entries observed during the parse, even if truncated.</summary>
    public int RawEntriesScanned { get; set; }
}

public sealed class TextLogStats
{
    public int TotalEntries { get; set; }
    public int ErrorCount   { get; set; }
    public int WarningCount { get; set; }
    public int InfoCount    { get; set; }
    public int DebugCount   { get; set; }
    public DateTimeOffset? FirstEntryTime { get; set; }
    public DateTimeOffset? LatestEntryTime { get; set; }
    public Dictionary<string, int> ByDate    { get; set; } = new();
    public Dictionary<string, int> BySeverity{ get; set; } = new();
}
