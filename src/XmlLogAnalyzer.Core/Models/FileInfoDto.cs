namespace XmlLogAnalyzer.Core.Models;

public sealed class FileInfoDto
{
    public required string Name { get; set; }
    public required string FullPath { get; set; }
    public long Size { get; set; }
    public string SizeDisplay { get; set; } = string.Empty;
    public DateTimeOffset LastModified { get; set; }
    public string Extension { get; set; } = string.Empty;
    /// <summary>Optional - populated by an "analyze" request, otherwise null.</summary>
    public int? TotalEntries { get; set; }
    public int? ErrorCount   { get; set; }
    public int? WarningCount { get; set; }
    public int? InfoCount    { get; set; }
}

public sealed class FolderInfoDto
{
    public required string Name { get; set; }
    public required string FullPath { get; set; }
    public bool HasChildren { get; set; }
    public int FileCount { get; set; }
    public DateTimeOffset LastModified { get; set; }
    public List<FolderInfoDto> Children { get; set; } = new();
}
