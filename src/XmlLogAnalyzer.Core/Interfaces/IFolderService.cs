using XmlLogAnalyzer.Core.Models;

namespace XmlLogAnalyzer.Core.Interfaces;

public interface IFolderService
{
    Task<IReadOnlyList<FolderInfoDto>> GetTreeAsync(string root, bool recursive, CancellationToken ct = default);
    Task<IReadOnlyList<FileInfoDto>> GetFilesAsync(string folder, bool recursive, CancellationToken ct = default);
    IReadOnlyList<string> GetAllowedRoots();
}
