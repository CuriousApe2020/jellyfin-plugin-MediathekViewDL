using System;
using System.IO;
using System.Net.Http;
using Jellyfin.Plugin.MediathekViewDL.Api;
using Jellyfin.Plugin.MediathekViewDL.Api.External;
using Jellyfin.Plugin.MediathekViewDL.Configuration;
using Jellyfin.Plugin.MediathekViewDL.Data;
using Jellyfin.Plugin.MediathekViewDL.Services;
using Jellyfin.Plugin.MediathekViewDL.Services.Adoption;
using Jellyfin.Plugin.MediathekViewDL.Services.Downloading;
using Jellyfin.Plugin.MediathekViewDL.Services.Downloading.Clients;
using Jellyfin.Plugin.MediathekViewDL.Services.Downloading.Handlers;
using Jellyfin.Plugin.MediathekViewDL.Services.Downloading.Queue;
using Jellyfin.Plugin.MediathekViewDL.Services.Library;
using Jellyfin.Plugin.MediathekViewDL.Services.Media;
using Jellyfin.Plugin.MediathekViewDL.Services.Metadata;
using Jellyfin.Plugin.MediathekViewDL.Services.Subscriptions;
using MediaBrowser.Controller;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.MediathekViewDL
{
    /// <summary>
    /// Registers plugin services.
    /// </summary>
    public class ServiceRegistrator : IPluginServiceRegistrator
    {
        /// <inheritdoc />
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            // Register a named client for FileDownloader
            serviceCollection.AddHttpClient("FileDownloaderClient");

            // Register the typed client for API. Named explicitly (rather than the parameterless
            // overload, which derives the HttpClientFactory registration name from just the simple
            // type name "IMediathekViewApiClient") so this doesn't collide with the upstream plugin's
            // identically-named-but-different-assembly interface when both are installed side by
            // side: AddHttpClient<TClient,...>() reserves that name for the TClient *type*, and two
            // distinct assemblies both owning a type called "IMediathekViewApiClient" trips
            // HttpClientFactory's "name already bound to a different type" check, throwing during
            // plugin service registration for whichever plugin registers second.
            serviceCollection.AddHttpClient<IMediathekViewApiClient, MediathekViewApiClient>("MediathekViewDLFork.MediathekViewApiClient");

            // Register a typed client per broadcaster-specific original-version language resolver
            // (each needs its own HttpClient, not IHttpClientFactory), all discoverable together as
            // IBroadcasterOriginalVersionLanguageResolver so the composite below can dispatch by URL.
            // Explicitly named for the same cross-plugin collision reason as above.
            serviceCollection.AddHttpClient<IBroadcasterOriginalVersionLanguageResolver, ArdOriginalVersionLanguageResolver>("MediathekViewDLFork.ArdOriginalVersionLanguageResolver");
            serviceCollection.AddHttpClient<IBroadcasterOriginalVersionLanguageResolver, ArteOriginalVersionLanguageResolver>("MediathekViewDLFork.ArteOriginalVersionLanguageResolver");
            serviceCollection.AddTransient<IOriginalVersionLanguageResolver, OriginalVersionLanguageResolver>();

            // Database
            serviceCollection.AddDbContext<MediathekViewDlDbContext>(options =>
            {
                var dbPath = Path.Combine(Plugin.Instance!.DataFolderPath, "mediathek-dl.db");
                var dbDir = Path.GetDirectoryName(dbPath);
                if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
                {
                    Directory.CreateDirectory(dbDir);
                }

                options.UseSqlite($"Data Source={dbPath}");
            });

            serviceCollection.AddSingleton<DatabaseMigrator>();
            serviceCollection.AddHostedService<MigrationHostedService>();
            serviceCollection.AddSingleton<IDownloadHistoryRepository, DbDownloadHistoryRepository>();

            serviceCollection.AddSingleton<IConfigurationProvider, PluginConfigurationProvider>();
            serviceCollection.AddSingleton<IQueryParser, QueryParser>();
            serviceCollection.AddSingleton<ILanguageDetectionService, LanguageDetectionService>();
            serviceCollection.AddSingleton<IVideoParser, VideoParser>();
            serviceCollection.AddSingleton<IFileNameBuilderService, FileNameBuilderService>();
            serviceCollection.AddSingleton<ILocalMediaScanner, LocalMediaScanner>();
            serviceCollection.AddTransient<ITempMetadataCache, TempMetadataCache>();
            // IMediathekViewApiClient is already registered via AddHttpClient above
            serviceCollection.AddTransient<IFFmpegService, FFmpegService>();
            serviceCollection.AddTransient<IFileDownloader, FileDownloader>();
            serviceCollection.AddTransient<ISubscriptionProcessor, SubscriptionProcessor>();
            serviceCollection.AddTransient<IFileAdoptionService, FileAdoptionService>();

            // Live TV
            serviceCollection.AddSingleton<ITunerHost, LiveTv.ZappTunerHost>();
            serviceCollection.AddSingleton<IListingsProvider, LiveTv.ZappListingsProvider>();

            // Register Download Handlers
            serviceCollection.AddTransient<IDownloadHandler, FFmpegDownloadHandler>();
            serviceCollection.AddTransient<IDownloadHandler, SubtitleDownloadHandler>();
            serviceCollection.AddTransient<IDownloadHandler, AudioExtractionHandler>();
            serviceCollection.AddTransient<IDownloadHandler, StreamingUrlHandler>();

            serviceCollection.AddTransient<IDownloadManager, DownloadManager>();
            serviceCollection.AddSingleton<IDownloadQueueManager, DownloadQueueManager>();
            serviceCollection.AddSingleton<IStrmValidationService, StrmValidationService>();
            serviceCollection.AddTransient<INfoService, NfoService>();
        }
    }
}
