namespace XmlLogAnalyzer.Core.Models;

/// <summary>
/// Canonical severity ordering used everywhere in the app. Higher numeric value = more severe.
/// Used both for color coding in the UI and for "Latest Errors First" sorting.
/// </summary>
public static class SeverityLevel
{
    public const string Error   = "Error";
    public const string Warning = "Warning";
    public const string Info    = "Info";
    public const string Debug   = "Debug";

    public static int Rank(string? severity) => (severity ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "error"   or "fatal" or "critical" => 4,
        "warning" or "warn"                => 3,
        "info"    or "information"         => 2,
        "debug"   or "trace"  or "verbose" => 1,
        _ => 0,
    };

    public static string Normalize(string? severity)
    {
        return Rank(severity) switch
        {
            4 => Error,
            3 => Warning,
            2 => Info,
            1 => Debug,
            _ => severity ?? string.Empty,
        };
    }
}
