namespace XmlLogAnalyzer.Core.Models;

/// <summary>
/// A single line of a flat-text log file, e.g.
/// <c>2026-05-10 20:24:26:46:114  -4:00 [ERR] Could not sync settings</c>.
///
/// The parser captures both a "best-effort" parsed <see cref="Timestamp"/> as well as the
/// raw date/time fragments so the grid can display Date / Time / Seconds as separate columns
/// regardless of whether full parsing succeeded.
/// </summary>
public sealed class TextLogEntry
{
    /// <summary>0-based row index inside the source file (stable key).</summary>
    public int Index { get; set; }

    /// <summary>Best-effort parsed timestamp. May be null when the line couldn't be parsed.</summary>
    public DateTimeOffset? Timestamp { get; set; }

    /// <summary>Raw date string captured from the line (e.g. <c>2026-05-10</c>).</summary>
    public string? Date { get; set; }

    /// <summary>Raw time string captured from the line (e.g. <c>20:24:26:46:114</c>).</summary>
    public string? Time { get; set; }

    /// <summary>Hours fragment, if extractable from <see cref="Time"/>.</summary>
    public int? Hour { get; set; }

    /// <summary>Minutes fragment.</summary>
    public int? Minute { get; set; }

    /// <summary>Seconds fragment — exposed as its own column so users can group by it.</summary>
    public int? Seconds { get; set; }

    /// <summary>Milliseconds fragment (if any).</summary>
    public int? Milliseconds { get; set; }

    /// <summary>Trailing time fragment beyond ms (e.g. ticks / microseconds), as a raw string.</summary>
    public string? TimeFractionTail { get; set; }

    /// <summary>Timezone offset string captured from the line, e.g. <c>-4:00</c>.</summary>
    public string? TimezoneOffset { get; set; }

    /// <summary>Severity code as it appeared between brackets, e.g. <c>ERR</c>, <c>WRN</c>, <c>INF</c>.</summary>
    public string? SeverityCode { get; set; }

    /// <summary>Severity normalised to one of: Error / Warning / Info / Debug.</summary>
    public string? SeverityLevel { get; set; }

    /// <summary>Free-text message after the severity bracket.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Raw line verbatim — handy for copy/export and the Raw view.</summary>
    public string RawLine { get; set; } = string.Empty;
}
