using DlsiteClient.Core.Api;
using DlsiteClient.Core.Services;
using DlsiteClient.Core.Storage;
using DlsiteClient.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DlsiteClient.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDlsiteCore(this IServiceCollection services, IPathConfig paths)
    {
        services.AddSingleton(paths);
        services.AddSingleton(sp =>
        {
            var db = new LibraryDatabase(
                Path.Combine(paths.AppDataRoot, "library.db"),
                sp.GetRequiredService<ILogger<LibraryDatabase>>());
            db.Initialize();
            return db;
        });
        services.AddSingleton<SettingsService>();
        services.AddSingleton<DlsiteApiClient>();
        services.AddSingleton<AuthSession>();
        services.AddSingleton<LibraryService>();
        services.AddSingleton<ArchiveExtractor>();
        services.AddSingleton<DownloadQueueService>();
        services.AddSingleton<LauncherService>();

        services.AddHttpClient("dlsite", (sp, client) =>
            {
                client.Timeout = TimeSpan.FromMinutes(5);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(DlsiteApiClient.UserAgent);
            })
            .ConfigurePrimaryHttpMessageHandler(sp =>
                new SettingsAwareProxyHandler(sp.GetRequiredService<SettingsService>()));

        return services;
    }
}
