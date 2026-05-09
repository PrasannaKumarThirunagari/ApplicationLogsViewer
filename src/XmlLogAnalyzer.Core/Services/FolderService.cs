using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XmlLogAnalyzer.Core.Interfaces;
using XmlLogAnalyzer.Core.Models;

namespace XmlLogAnalyzer.Core.Services;

public sealed class FolderService : IFolderService
{
    private readonly IPathValidator _validator;
    private readonly AppSettings _settings;
    private readonly ILogger<FolderService> _logger;

    public FolderService(
        IPathValidator validator,
        IOptions<AppSettings> options,
        ILogger<FolderService> logger)
    {
        _validator = validator;
        _settings = options.Value;
        _logger = logger;
    }

    public IReadOnlyList<string> GetAllowedRoots() =>
        _settings.AllowedRoots.Where(Directory.Exists).ToList();

    public Task<IReadOnlyList<FolderInfoDto>> GetTreeAsync(string root, bool recursive, CancellationToken ct = default)
    {
        var safe = _validator.ValidateFolder(root);
        var list = new List<FolderInfoDto> { BuildNode(safe, recursive, depth: 0, ct) };
        return Task.FromResult<IReadOnlyList<FolderInfoDto>>(list);
    }

    public Task<IReadOnlyList<FileInfoDto>> GetFilesAsync(string folder, bool recursive, CancellationToken ct = default)
    {
        var safe = _validator.ValidateFolder(folder);
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        var files = new List<FileInfoDto>();
        try
        {
            // Enumerate directly with EnumerateFiles for streaming; one pass per allowed extension.
            foreach (var ext in _settings.AllowedExtensions)
            {
                ct.ThrowIfCancellationRequested();
                foreach (var f in EnumerateSafely(safe, "*" + ext, option))
                {
                    ct.ThrowIfCancellationRequested();
                    var fi = new FileInfo(f);
                    files.Add(new FileInfoDto
                    {
                        Name = fi.Name,
                        FullPath = fi.FullName,
                        Size = fi.Length,
                        SizeDisplay = HumanSize(fi.Length),
                        LastModified = fi.LastWriteTime,
                        Extension = fi.Extension,
                    });
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Access denied while enumerating {Folder}", safe);
        }

        // Same path can appear twice if AllowedExtensions repeats an extension case-variant
        // or the OS returns overlapping matches — keep one row per canonical full path.
        var unique = files
            .DistinctBy(f => Path.GetFullPath(f.FullPath), StringComparer.OrdinalIgnoreCase)
            .ToList();

        // "Latest file first": descending by LastModified by default.
        unique.Sort((a, b) => b.LastModified.CompareTo(a.LastModified));
        return Task.FromResult<IReadOnlyList<FileInfoDto>>(unique);
    }

    private FolderInfoDto BuildNode(string path, bool recursive, int depth, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var info = new DirectoryInfo(path);
        var node = new FolderInfoDto
        {
            Name = info.Name,
            FullPath = info.FullName,
            LastModified = info.LastWriteTime,
        };

        try
        {
            var subs = info.EnumerateDirectories().ToArray();
            node.HasChildren = subs.Length > 0;
            node.FileCount = info.EnumerateFiles().Count(f =>
                _settings.AllowedExtensions.Any(a => a.Equals(f.Extension, StringComparison.OrdinalIgnoreCase)));

            if (recursive && depth < 12) // safety: cap recursion depth
            {
                foreach (var s in subs)
                {
                    try { node.Children.Add(BuildNode(s.FullName, true, depth + 1, ct)); }
                    catch (UnauthorizedAccessException) { /* skip */ }
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogDebug(ex, "Skipping inaccessible folder {Path}", path);
        }

        return node;
    }

    /// <summary>
    /// Wraps Directory.EnumerateFiles so a single inaccessible subfolder doesn't abort the whole walk.
    /// </summary>
    private static IEnumerable<string> EnumerateSafely(string root, string pattern, SearchOption opt)
    {
        if (opt == SearchOption.TopDirectoryOnly)
        {
            IEnumerable<string> top;
            try { top = Directory.EnumerateFiles(root, pattern, SearchOption.TopDirectoryOnly); }
            catch (UnauthorizedAccessException) { yield break; }
            foreach (var f in top) yield return f;
            yield break;
        }

        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly); }
            catch (UnauthorizedAccessException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            foreach (var f in files) yield return f;

            IEnumerable<string> subs;
            try { subs = Directory.EnumerateDirectories(dir); }
            catch (UnauthorizedAccessException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            foreach (var s in subs) stack.Push(s);
        }
    }

    private static string HumanSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes; int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return $"{v:0.##} {units[i]}";
    }
}
