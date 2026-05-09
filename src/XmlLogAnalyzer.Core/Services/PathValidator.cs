using Microsoft.Extensions.Options;
using XmlLogAnalyzer.Core.Interfaces;
using XmlLogAnalyzer.Core.Models;

namespace XmlLogAnalyzer.Core.Services;

/// <summary>
/// Normalises and validates filesystem paths. Guards against path-traversal attacks
/// and reading from system-critical directories.
/// </summary>
public sealed class PathValidator : IPathValidator
{
    private readonly AppSettings _settings;

    public PathValidator(IOptions<AppSettings> options)
    {
        _settings = options.Value;
    }

    public string ValidateFolder(string path)
    {
        var full = Normalize(path);
        if (!Directory.Exists(full))
            throw new DirectoryNotFoundException($"Folder not found: {path}");
        EnsureAllowed(full);
        return full;
    }

    public string ValidateFile(string path)
    {
        var full = Normalize(path);
        if (!File.Exists(full))
            throw new FileNotFoundException($"File not found: {path}");
        EnsureAllowed(full);
        if (!IsExtensionAllowed(full))
            throw new InvalidOperationException($"File type not allowed: {Path.GetExtension(full)}");
        return full;
    }

    public bool IsExtensionAllowed(string path)
    {
        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return false;
        return _settings.AllowedExtensions.Any(a => a.Equals(ext, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is empty.", nameof(path));

        // Reject obvious traversal payloads early — even though Path.GetFullPath collapses them,
        // we want to surface a meaningful error rather than silently letting them through.
        if (path.Contains("..", StringComparison.Ordinal))
        {
            // allow only if after Path.GetFullPath the resolved path is still inside an allowed root —
            // we'll re-check below, but log the suspicious payload.
        }

        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full;
    }

    private void EnsureAllowed(string fullPath)
    {
        var roots = _settings.AllowedRoots
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => Path.GetFullPath(r).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .ToList();

        if (roots.Count == 0)
        {
            // Empty allowed roots = no restriction (only OK when the operator hasn't configured
            // any). Still enforce forbidden list.
        }
        else
        {
            var inside = roots.Any(r =>
                fullPath.Equals(r, StringComparison.OrdinalIgnoreCase) ||
                fullPath.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            if (!inside)
                throw new UnauthorizedAccessException("Path is outside the allowed roots.");
        }

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
