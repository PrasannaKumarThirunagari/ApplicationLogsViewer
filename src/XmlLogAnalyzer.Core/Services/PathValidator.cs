using Microsoft.Extensions.Options;
using XmlLogAnalyzer.Core.Interfaces;
using XmlLogAnalyzer.Core.Models;

namespace XmlLogAnalyzer.Core.Services;

/// <summary>
/// Normalises and validates filesystem paths. Guards against path-traversal attacks
/// and reading from system-critical directories.
///
/// Two independent root-lists are enforced:
///  * <c>AllowedRoots</c>       — for XML log files.
///  * <c>TextLogRoots</c>       — for plain-text logs (the "Text Logs" tab).
/// Each kind uses its own allow-list so the two features can point at entirely different
/// directory trees without leaking access between them.
/// </summary>
public sealed class PathValidator : IPathValidator
{
    private readonly AppSettings _settings;

    public PathValidator(IOptions<AppSettings> options)
    {
        _settings = options.Value;
    }

    // ------------------- XML log paths -------------------
    public string ValidateFolder(string path)
    {
        var full = Normalize(path);
        if (!Directory.Exists(full))
            throw new DirectoryNotFoundException($"Folder not found: {path}");
        EnsureAllowed(full, _settings.AllowedRoots, requireConfig: false);
        return full;
    }

    public string ValidateFile(string path)
    {
        var full = Normalize(path);
        if (!File.Exists(full))
            throw new FileNotFoundException($"File not found: {path}");
        EnsureAllowed(full, _settings.AllowedRoots, requireConfig: false);
        if (!IsExtensionAllowed(full))
            throw new InvalidOperationException($"File type not allowed: {Path.GetExtension(full)}");
        return full;
    }

    public bool IsExtensionAllowed(string path) =>
        HasExt(path, _settings.AllowedExtensions);

    // ------------------- Text log paths -------------------
    public string ValidateTextLogFolder(string path)
    {
        var full = Normalize(path);
        if (!Directory.Exists(full))
            throw new DirectoryNotFoundException($"Folder not found: {path}");
        EnsureAllowed(full, _settings.TextLogRoots, requireConfig: true);
        return full;
    }

    public string ValidateTextLogFile(string path)
    {
        var full = Normalize(path);
        if (!File.Exists(full))
            throw new FileNotFoundException($"File not found: {path}");
        EnsureAllowed(full, _settings.TextLogRoots, requireConfig: true);
        if (!IsTextLogExtensionAllowed(full))
            throw new InvalidOperationException($"File type not allowed: {Path.GetExtension(full)}");
        return full;
    }

    public bool IsTextLogExtensionAllowed(string path) =>
        HasExt(path, _settings.TextLogExtensions);

    // ------------------- Helpers -------------------
    private static bool HasExt(string path, IReadOnlyCollection<string> allowed)
    {
        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return false;
        return allowed.Any(a => a.Equals(ext, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is empty.", nameof(path));

        // Reject obvious traversal payloads early — Path.GetFullPath collapses them, but
        // we want to enforce the allow-list check below regardless.
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full;
    }

    private void EnsureAllowed(string fullPath, IReadOnlyCollection<string> rootsCfg, bool requireConfig)
    {
        var roots = rootsCfg
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => Path.GetFullPath(r).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .ToList();

        if (roots.Count > 0)
        {
            var inside = roots.Any(r =>
                fullPath.Equals(r, StringComparison.OrdinalIgnoreCase) ||
                fullPath.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            if (!inside)
                throw new UnauthorizedAccessException("Path is outside the allowed roots.");
        }
        else if (requireConfig)
        {
            // Text-log side: refuse if no roots are configured.
            throw new UnauthorizedAccessException(
                "No text-log roots configured. Set XmlLogAnalyzer.TextLogRoots in appsettings.");
        }
        // else (XML side, empty config) — fall through; still enforce ForbiddenPaths below.

        foreach (var f in _settings.ForbiddenPaths)
        {
            if (string.IsNullOrWhiteSpace(f)) continue;
            var forbidden = Path.GetFullPath(f).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (fullPath.Equals(forbidden, StringComparison.OrdinalIgnoreCase) ||
                fullPath.StartsWith(forbidden + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("Access to system-critical paths is denied.");
            }
        }
    }
}
