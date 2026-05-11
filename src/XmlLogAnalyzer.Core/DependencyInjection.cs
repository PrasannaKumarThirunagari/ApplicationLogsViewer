using Microsoft.Extensions.DependencyInjection;
using XmlLogAnalyzer.Core.Interfaces;
using XmlLogAnalyzer.Core.Services;

namespace XmlLogAnalyzer.Core;

public static class DependencyInjection
{
    public static IServiceCollection AddXmlLogAnalyzerCore(this IServiceCollection services)
    {
        services.AddMemoryCache(opt => opt.SizeLimit = 200_000); // ~200k entries cap across all files
        services.AddSingleton<IPathValidator, PathValidator>();
        services.AddSingleton<IFolderService, FolderService>();
        services.AddSingleton<IXmlLogParser, XmlLogParser>();
        services.AddSingleton<ILogQueryService, LogQueryService>();
        services.AddSingleton<IUserPreferencesService, UserPreferencesService>();

        // Text-log feature (plain-text application logs)
        services.AddSingleton<ITextLogParser, TextLogParser>();
        services.AddSingleton<ITextLogQueryService, TextLogQueryService>();
        return services;
    }
}
