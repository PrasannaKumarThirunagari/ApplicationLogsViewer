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

    public long MaxFileSizeBytes { get; set; } = 1024L * 1024L * 1024L; // 1 GB

    public int CacheSlidingMinutes { get; set; } = 15;

    public int FavoritesMax { get; set; } = 50;
    public int RecentMax    { get; set; } = 25;

    /// <summary>Page size hard cap to prevent abuse.</summary>
    public int MaxPageSize { get; set; } = 5000;

    public bool RecursiveByDefault { get; set; } = true;
}
