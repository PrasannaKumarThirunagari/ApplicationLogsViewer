using Microsoft.AspNetCore.HttpOverrides;
using XmlLogAnalyzer.Core;
using XmlLogAnalyzer.Core.Models;
using XmlLogAnalyzer.Web.Middleware;

var builder = WebApplication.CreateBuilder(args);

// ---------- Configuration ----------
builder.Services.Configure<AppSettings>(builder.Configuration.GetSection("XmlLogAnalyzer"));

// ---------- Core services ----------
builder.Services.AddXmlLogAnalyzerCore();

// ---------- ASP.NET Core ----------
builder.Services.AddRazorPages();
builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        // Tighter JSON output — no nulls, camelCase by default.
        o.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "XmlLogAnalyzer API",
        Version = "v1",
        Description = "Lightweight ASP.NET Core API for reading and analyzing XML log files."
    });
});

builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
    o.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
});

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowedToAllowWildcardSubdomains().AllowAnyOrigin()));

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

// Larger request body for raw-XML parsing endpoint.
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 256L * 1024L * 1024L;
    o.ValueLengthLimit         = int.MaxValue;
});

var app = builder.Build();

// ---------- Pipeline ----------
app.UseForwardedHeaders();
app.UseResponseCompression();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Custom JSON-aware error middleware (kicks in for /api/* failures).
app.UseMiddleware<ApiExceptionMiddleware>();

app.UseStaticFiles();
app.UseRouting();
app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapRazorPages();
app.MapControllers();

app.Run();
