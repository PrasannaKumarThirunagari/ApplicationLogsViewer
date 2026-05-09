using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using XmlLogAnalyzer.Core.Interfaces;
using XmlLogAnalyzer.Core.Models;
using XmlLogAnalyzer.Core.Services;

namespace XmlLogAnalyzer.Web.Controllers;

[ApiController]
[Route("api/logs")]
public sealed class LogsController : ControllerBase
{
    private const int MaxPasteUtf8Bytes = 16 * 1024 * 1024;

    private readonly ILogQueryService _query;
    private readonly IUserPreferencesService _prefs;
    private readonly IXmlLogParser _parser;
    private readonly AppSettings _settings;

    public LogsController(
        ILogQueryService query,
        IUserPreferencesService prefs,
        IXmlLogParser parser,
        IOptions<AppSettings> options)
    {
        _query = query;
        _prefs = prefs;
        _parser = parser;
        _settings = options.Value;
    }

    /// <summary>
    /// The grid endpoint. Accepts filter / sort / page parameters and returns paged entries +
    /// dashboard stats. The first call against a file is a "cold" parse; subsequent calls hit
    /// the in-memory cache.
    /// </summary>
    [HttpPost("query")]
    public async Task<IActionResult> Query([FromQuery] string path, [FromBody] LogQuery? query, CancellationToken ct = default)
    {
        var q = query ?? new LogQuery();
        _prefs.TouchRecent(path);
        var result = await _query.QueryAsync(path, q, ct);
        return Ok(result);
    }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats([FromQuery] string path, CancellationToken ct = default)
    {
        return Ok(await _query.GetStatsAsync(path, ct));
    }

    [HttpGet("entry/raw")]
    public async Task<IActionResult> Raw([FromQuery] string path, [FromQuery] int index, CancellationToken ct = default)
    {
        var xml = await _query.GetEntryRawXmlAsync(path, index, ct);
        return xml is null ? NotFound() : Content(xml, "application/xml");
    }

    [HttpGet("entry/pretty")]
    public async Task<IActionResult> Pretty([FromQuery] string path, [FromQuery] int index, CancellationToken ct = default)
    {
        var xml = await _query.GetEntryRawXmlAsync(path, index, ct);
        return xml is null ? NotFound() : Content(XmlConverter.Pretty(xml), "application/xml");
    }

    [HttpGet("entry/json")]
    public async Task<IActionResult> EntryJson([FromQuery] string path, [FromQuery] int index, CancellationToken ct = default)
    {
        var xml = await _query.GetEntryRawXmlAsync(path, index, ct);
        return xml is null ? NotFound() : Content(XmlConverter.ToJson(xml), "application/json");
    }

    /// <summary>
    /// Parse arbitrary XML from the request body (same rules as file parsing; no path validation).
    /// Response is capped at <see cref="AppSettings.MaxPageSize"/> entries for safety.
    /// </summary>
    [HttpPost("parse")]
    public async Task<IActionResult> Parse([FromBody] ParseLogRequest body, CancellationToken ct = default)
    {
        var xml = body.Xml ?? "";
        if (string.IsNullOrWhiteSpace(xml))
            return BadRequest(new { error = "Request body must include non-empty XML." });

        if (Encoding.UTF8.GetByteCount(xml) > MaxPasteUtf8Bytes)
            return BadRequest(new { error = $"XML payload exceeds maximum size ({MaxPasteUtf8Bytes / (1024 * 1024)} MB UTF-8)." });

        var all = await _parser.ParseStringAsync(xml, ct);
        var total = all.Count;
        var cap = Math.Max(1, _settings.MaxPageSize);

        var response = new ParseLogResponse
        {
            Entries = total <= cap ? all : all.Take(cap).ToList(),
            Total = total,
            Truncated = total > cap,
        };
        return Ok(response);
    }

    [HttpPost("refresh")]
    public IActionResult Refresh([FromQuery] string path)
    {
        _query.Invalidate(path);
        return Ok();
    }

    /// <summary>Export the current filter selection to CSV for download.</summary>
    [HttpPost("export/csv")]
    public async Task<IActionResult> ExportCsv([FromQuery] string path, [FromBody] LogQuery? query, CancellationToken ct = default)
    {
        var q = query ?? new LogQuery();
        // Pull all matched entries — bypass paging.
        q.Page = 1; q.PageSize = int.MaxValue;
        var r = await _query.QueryAsync(path, q, ct);
        var bytes = CsvExporter.ToCsv(r.Entries);
        var name = $"{Path.GetFileNameWithoutExtension(path)}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        return File(bytes, "text/csv", name);
    }
}

/// <summary>JSON body for <c>POST /api/logs/parse</c>.</summary>
public sealed class ParseLogRequest
{
    public string? Xml { get; set; }
}

/// <summary>Result of pasted-XML parsing.</summary>
public sealed class ParseLogResponse
{
    public IReadOnlyList<LogEntry> Entries { get; init; } = Array.Empty<LogEntry>();
    public int Total { get; init; }
    public bool Truncated { get; init; }
}
