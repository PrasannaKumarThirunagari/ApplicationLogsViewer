namespace XmlLogAnalyzer.Core.Models;

/// <summary>
/// Strongly-typed configuration bound from <c>appsettings.json</c> -&gt; <c>XmlLogAnalyzer</c>.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// Allowed root folders. Any path the user requests must live underneath one of these.
    /// Acts as the path-traversal guard.
    /// </summary>
    public List<string> AllowedRoots { get; set; } = new();

    /// <summary>Hard-block these system paths even if a parent is allowed.</summary>
    public List<string> ForbiddenPaths { get; set; } = new()
    {
        @"C:\Windows", @"C:\Program Files", @"C:\Program Files (x86)",
        @"C:\ProgramData", @"C:\$Recycle.Bin", @"/etc", @"/usr", @"/var/log"
    };

    public List<string> AllowedExtensions { get; set; } = new() { ".xml", ".log", ".txt" };

    /// <summary>
    /// Independent root folders for the "Text Logs" feature (plain-text application logs
    /// in the form <c>YYYY-MM-DD HH:MM:SS.fff [LEVEL] message</c>). Treated as their own
    /// allow-list so XML-log roots stay separate.
    /// </summary>
    public List<string> TextLogRoots { get; set; } = new();

    /// <summary>Allowed extensions for the text-log feature.</summary>
    public List<string> TextLogExtensions { get; set; } = new() { ".log", ".txt" };

    /// <summary>
    /// Files at or above this size on the text-log side are streamed line-by-line into
    /// chunks rather than buffered whole. (Default 1 MB matches the user spec.)
    /// </summary>
    public long TextLogChunkThresholdBytes { get; set; } = 1024L * 1024L;

    /// <summary>
    /// Files at or above this size use a bounded "tail" cache instead of loading the full
    /// parsed entry list into memory. Anything below this threshold is fully buffered.
    /// 64 MB by default — comfortably handles "normal" logs while keeping 700 MB-plus files
    /// loadable on commodity hardware.
    /// </summary>
    public long LargeFileThresholdBytes { get; set; } = 64L * 1024L * 1024L;

    /// <summary>
    /// When a file exceeds <see cref="LargeFileThresholdBytes"/> the parser keeps only the
    /// most-recent N entries in memory (latest-first is the dominant use case). Older entries
    /// are streamed past the buffer and discarded. The total raw line count is still reported
    /// to the UI so the user sees how much was truncated.
    /// </summary>
    public int LargeFileTailEntries { get; set; } = 100_000;

    public long MaxFileSizeBytes { get; set; } = 1024L * 1024L * 1024L; // 1 GB

    public int CacheSlidingMinutes { get; set; } = 15;

    public int FavoritesMax { get; set; } = 50;
    public int RecentMax    { get; set; } = 25;

    /// <summary>Page size hard cap to prevent abuse.</summary>
    public int MaxPageSize { get; set; } = 5000;

    public bool RecursiveByDefault { get; set; } = true;
}
