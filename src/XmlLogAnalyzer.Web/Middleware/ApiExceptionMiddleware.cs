using System.Net;
using System.Text.Json;

namespace XmlLogAnalyzer.Web.Middleware;

/// <summary>
/// Centralised exception handler for /api/* — returns a JSON envelope and the right
/// HTTP status code for known exception types. Non-API requests fall through.
/// </summary>
public sealed class ApiExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiExceptionMiddleware> _logger;

    public ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        try { await _next(ctx); }
        catch (Exception ex) when (ctx.Request.Path.StartsWithSegments("/api"))
        {
            _logger.LogError(ex, "Unhandled API exception on {Path}", ctx.Request.Path);

            var (status, msg) = ex switch
            {
                FileNotFoundException        => (HttpStatusCode.NotFound, ex.Message),
                DirectoryNotFoundException   => (HttpStatusCode.NotFound, ex.Message),
                UnauthorizedAccessException  => (HttpStatusCode.Forbidden, ex.Message),
                ArgumentException            => (HttpStatusCode.BadRequest, ex.Message),
                InvalidOperationException    => (HttpStatusCode.BadRequest, ex.Message),
                IOException                  => (HttpStatusCode.Conflict, ex.Message),
                _                            => (HttpStatusCode.InternalServerError, "An unexpected error occurred."),
            };

            ctx.Response.Clear();
            ctx.Response.StatusCode  = (int)status;
            ctx.Response.ContentType = "application/json";

            var payload = JsonSerializer.Serialize(new
            {
                error = msg,
                status = (int)status,
                path = ctx.Request.Path.Value,
                traceId = ctx.TraceIdentifier
            });
            await ctx.Response.WriteAsync(payload);
        }
    }
}
