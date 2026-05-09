using Microsoft.AspNetCore.Mvc;
using XmlLogAnalyzer.Core.Interfaces;

namespace XmlLogAnalyzer.Web.Controllers;

[ApiController]
[Route("api/folders")]
public sealed class FoldersController : ControllerBase
{
    private readonly IFolderService _folders;
    private readonly IUserPreferencesService _prefs;
    private readonly ILogQueryService _logs;

    public FoldersController(IFolderService folders, IUserPreferencesService prefs, ILogQueryService logs)
    {
        _folders = folders;
        _prefs = prefs;
        _logs = logs;
    }

    /// <summary>List configured root folders + favourites + recent.</summary>
    [HttpGet("roots")]
    public IActionResult Roots() => Ok(new
    {
        roots      = _folders.GetAllowedRoots(),
        favorites  = _prefs.GetFavorites(),
        recent     = _prefs.GetRecent(),
    });

    /// <summary>Tree for a single root, optionally recursive.</summary>
    [HttpGet("tree")]
    public async Task<IActionResult> Tree([FromQuery] string path, [FromQuery] bool recursive = true, CancellationToken ct = default)
    {
        var tree = await _folders.GetTreeAsync(path, recursive, ct);
        return Ok(tree);
    }

    /// <summary>List of files in a folder, sorted "latest first" by default.</summary>
    [HttpGet("files")]
    public async Task<IActionResult> Files([FromQuery] string path, [FromQuery] bool recursive = false, CancellationToken ct = default)
    {
        var files = await _folders.GetFilesAsync(path, recursive, ct);
        return Ok(files);
    }

    /// <summary>
    /// Clears server-side parsed-log cache for every recognized file in this folder (non-recursive),
    /// matching the main file list. Clients should re-fetch <c>/api/folders/files</c> after this.
    /// </summary>
    [HttpPost("cache/refresh")]
    public async Task<IActionResult> RefreshFolderCache([FromQuery] string path, CancellationToken ct = default)
    {
        await _logs.InvalidateFolderContentsAsync(path, ct);
        return Ok();
    }

    [HttpPost("favorites/add")]
    public IActionResult AddFavorite([FromBody] PathDto dto)
    {
        _prefs.AddFavorite(dto.Path);
        return Ok();
    }

    [HttpPost("favorites/remove")]
    public IActionResult RemoveFavorite([FromBody] PathDto dto)
    {
        _prefs.RemoveFavorite(dto.Path);
        return Ok();
    }

    /// <summary>Clears the recently-viewed file list (sidebar).</summary>
    [HttpPost("recent/clear")]
    public IActionResult ClearRecent()
    {
        _prefs.ClearRecent();
        return Ok();
    }

    public sealed record PathDto(string Path);
}
