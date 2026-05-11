using Microsoft.AspNetCore.Mvc;
using XmlLogAnalyzer.Core.Interfaces;
using XmlLogAnalyzer.Core.Models;

namespace XmlLogAnalyzer.Web.Controllers;

/// <summary>
/// API for the "Text Logs" tab — flat-text application logs in the form
/// <c>YYYY-MM-DD HH:MM:SS:fff:ttt [LEVEL] message</c>.
///
/// Roots are configured independently via <c>XmlLogAnalyzer.TextLogRoots</c>.
/// </summary>
[ApiController]
[Route("api/text-logs")]
public sealed class TextLogsController : ControllerBase
{
    private readonly ITextLogQueryService _svc;

    public TextLogsController(ITextLogQueryService svc) { _svc = svc; }

    /// <summary>Configured text-log root folders that exist on disk.</summary>
    [HttpGet("roots")]
    public IActionResult Roots() => Ok(new { roots = _svc.GetRoots() });

    /// <summary>Files under a folder, latest-modified first.</summary>
    [HttpGet("files")]
    public async Task<IActionResult> Files(
        [FromQuery] string path,
        [FromQuery] bool recursive = false,
        CancellationToken ct = default)
        => Ok(await _svc.GetFilesAsync(path, recursive, ct));

    /// <summary>Query (filter / sort / paginate) parsed text-log entries.</summary>
    [HttpPost("query")]
    public async Task<IActionResult> Query(
        [FromQuery] string path,
        [FromBody] TextLogQuery? query,
        CancellationToken ct = default)
        => Ok(await _svc.QueryAsync(path, query ?? new TextLogQuery(), ct));

    /// <summary>Dashboard-summary statistics for a single file.</summary>
    [HttpGet("stats")]
    public async Task<IActionResult> Stats([FromQuery] string path, CancellationToken ct = default)
        => Ok(await _svc.GetStatsAsync(path, ct));

    /// <summary>Drop the cached parse result so the next query re-reads from disk.</summary>
    [HttpPost("refresh")]
    public IActionResult Refresh([FromQuery] string path)
    {
        _svc.Invalidate(path);
        return Ok();
    }
}
