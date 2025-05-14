using DLNAServer.Common;
using DLNAServer.Configuration;
using DLNAServer.Database;
using DLNAServer.Database.Interceptors;
using DLNAServer.Database.Repositories;
using DLNAServer.Database.Repositories.Interfaces;
using DLNAServer.Features.ApiBlocking;
using DLNAServer.Features.ApiBlocking.Interfaces;
using DLNAServer.Features.Cache;
using DLNAServer.Features.Cache.Interfaces;
using DLNAServer.Features.FileWatcher;
using DLNAServer.Features.FileWatcher.Interfaces;
using DLNAServer.Features.MediaContent;
using DLNAServer.Features.MediaContent.Interfaces;
using DLNAServer.Features.MediaProcessors;
using DLNAServer.Features.MediaProcessors.Interfaces;
using DLNAServer.Features.PhysicalFile;
using DLNAServer.Features.PhysicalFile.Interfaces;
using DLNAServer.Features.Subscriptions;
using DLNAServer.Features.Subscriptions.Interfaces;
using DLNAServer.FileServer;
using DLNAServer.Middleware;
using DLNAServer.SOAP;
using DLNAServer.SOAP.Constants;
using DLNAServer.SOAP.Endpoints;
using DLNAServer.SOAP.Endpoints.Interfaces;
using DLNAServer.SSDP;
using DLNAServer.Types.IP.Interfaces;
using DLNAServer.Types.UPNP;
using DLNAServer.Types.UPNP.Interfaces;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using SoapCore;
using System.Runtime;
using System.Xml;

namespace DLNAServer
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            var dlnaServerExit = false;

            ServerConfig.DlnaServerRestart = true;

            while (!dlnaServerExit)
            {
                if (ServerConfig.DlnaServerRestart)
                {
                    ServerConfig.DlnaServerRestart = false;

                    using (var app = CreateHostBuilder(args))
                    {
                        app.ConfigureWebHost();

                        var cancellationToken = app.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
                        await app.RunAsync(cancellationToken);

                        dlnaServerExit = (cancellationToken.IsCancellationRequested && !ServerConfig.DlnaServerRestart);
                    }
                    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                }

                if (ServerConfig.DlnaServerRestart)
                {
                    await Task.Delay(TimeSpanValues.TimeSecs30);
                }
            }
        }

        private static WebApplication CreateHostBuilder(string[] args)
        {
            WebApplicationBuilder builder;
            builder = WebApplication.CreateEmptyBuilder(new() { Args = args });
            {
                _ = builder.Services.AddSingleton<ServerConfig>(static (serviceProvider) => ServerConfig.Instance);

                _ = builder.Configuration
                        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                        .AddEnvironmentVariables();
                _ = builder.WebHost.UseKestrel(options: static (opt) =>
                {
                    opt.ListenAnyIP(
                        port: (int)ServerConfig.Instance.ServerPort,
                        configure: static (cfg) =>
                        {
                            // already set as default
                            //cfg.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2AndHttp3; 
                        });
                });
                _ = builder.Services.AddLogging(logging =>
                {
                    _ = logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
                    _ = logging.AddSimpleConsole();

                    if (ServerConfig.Instance.ServerDebugMode)
                    {
                        _ = logging.SetMinimumLevel(LogLevel.Trace);
                    }
                    // Serilog
                    var logLevels = builder.Configuration.GetSection("Logging:LogLevel").Get<Dictionary<string, string>>() ?? [];
                    var serilogConfig = new LoggerConfiguration()
                        .MinimumLevel.ControlledBy(new LoggingLevelSwitch(
                            ServerConfig.Instance.ServerDebugMode
                            ? LogEventLevel.Verbose
                            : builder.Configuration.GetValue("Logging:LogLevel:Default", defaultValue: LogEventLevel.Information)));
                    foreach (var (category, level) in logLevels)
                    {
                        if (Enum.TryParse(level, true, out LogEventLevel logEventLevel))
                        {
                            _ = serilogConfig.MinimumLevel.Override(category, logEventLevel);
                        }
                    }
                    _ = serilogConfig.WriteTo.File(
                            path: "logs/appLog.txt",
                            rollingInterval: RollingInterval.Day,
                            retainedFileTimeLimit: TimeSpanValues.TimeDays7,
                            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss:fff} {Level:u3}] [{SourceContext}] {Message}{NewLine}{Exception}");
                    _ = logging.AddSerilog(serilogConfig.CreateLogger());

                    _ = logging.Configure(static (options) =>
                    {
                        options.ActivityTrackingOptions =
                            ActivityTrackingOptions.SpanId |
                            ActivityTrackingOptions.TraceId |
                            ActivityTrackingOptions.ParentId;
                    });
                });
            }


            _ = builder.Services.AddControllers();
            _ = builder.Services.AddHttpContextAccessor();
            _ = builder.Services.AddResponseCompression(static (options) =>
            {
                options.Providers.Add<BrotliCompressionProvider>();
                options.Providers.Add<GzipCompressionProvider>();
                options.EnableForHttps = true;
            });

            _ = builder.Services.AddDbContextPool<DlnaDbContext>(
                optionsAction: (serviceProvider, options) =>
                {
                    var sqliteLogger = new LoggerConfiguration()
                        .WriteTo.File(
                                path: "logs/sqliteLog.txt",
                                rollingInterval: RollingInterval.Day,
                                retainedFileTimeLimit: TimeSpanValues.TimeDays7)
                        .CreateLogger();
                    _ = options
                        .UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"))
                        .AddInterceptors(
                        [
                            serviceProvider.GetRequiredService<SQLitePragmaInterceptor>(),
                            serviceProvider.GetRequiredService<PerformanceInterceptor>(),
                        ])
                        .ConfigureWarnings(static (b) => b.Log(
                            [
                                (RelationalEventId.ConnectionCreated, LogLevel.Debug),
                                (RelationalEventId.ConnectionDisposed, LogLevel.Debug),
                                (RelationalEventId.ConnectionOpened, LogLevel.Debug),
                                (RelationalEventId.ConnectionClosed, LogLevel.Debug),
                                (RelationalEventId.CommandExecuting, LogLevel.Debug),
                                (RelationalEventId.CommandExecuted, LogLevel.Debug)
                            ]))
                        .EnableSensitiveDataLogging(true)
                        .LogTo(message =>
                        {
                            if (ServerConfig.Instance.ServerLogAllDatabaseMessages)
                            {
                                sqliteLogger.Information(message);
                            }
                        }
                        , LogLevel.Debug);
                    // default internal configured have set IMemoryCahce.Size value as 10240
                    //options.UseMemoryCache(serviceProvider.GetRequiredService<IMemoryCache>()); 
                },
                poolSize: 64);
            _ = builder.Services.AddSingleton<SQLitePragmaInterceptor>();
            _ = builder.Services.AddSingleton<PerformanceInterceptor>(static (e) =>
                new PerformanceInterceptor(
                    serilogLogger: new LoggerConfiguration()
                        .WriteTo.File(
                            path: "logs/sqlitePerfLog.txt",
                            rollingInterval: RollingInterval.Month,
                            fileSizeLimitBytes: 32 * 1024 * 1024, // 32MB
                            retainedFileCountLimit: 5)
                        .CreateLogger(),
                    querySlowThreshold: TimeSpanValues.TimeMs50,
                    serverConfig: ServerConfig.Instance
                ));
            _ = builder.Services.AddScopedLazyService<IFileRepository, FileRepository>();
            _ = builder.Services.AddScopedLazyService<IDirectoryRepository, DirectoryRepository>();
            _ = builder.Services.AddScopedLazyService<IServerRepository, ServerRepository>();
            _ = builder.Services.AddScopedLazyService<IAudioMetadataRepository, AudioMetadataRepository>();
            _ = builder.Services.AddScopedLazyService<IVideoMetadataRepository, VideoMetadataRepository>();
            _ = builder.Services.AddScopedLazyService<ISubtitleMetadataRepository, SubtitleMetadataRepository>();
            _ = builder.Services.AddScopedLazyService<IThumbnailRepository, ThumbnailRepository>();
            _ = builder.Services.AddScopedLazyService<IThumbnailDataRepository, ThumbnailDataRepository>();

            _ = builder.Services.AddMemoryCache(static (option) =>
            {
                // Max half of memory or from config
                option.SizeLimit = Math.Min(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 2, (long)ServerConfig.Instance.MaxUseMemoryCacheInMBytes * (1024 * 1024));
                option.Clock = new Microsoft.Extensions.Internal.SystemClock();
                option.ExpirationScanFrequency = TimeSpanValues.TimeMs500;
                option.TrackStatistics = true;
                option.TrackLinkedCacheEntries = true;
            });
            _ = builder.Services.AddSoapCore<CustomEnvelopeMessage>();

            _ = builder.Services.AddSingleton<IApiBlockerService, ApiBlockerService>();
            _ = builder.Services.AddSingleton<ISubscriptionService, SubscriptionService>();

            _ = builder.Services.AddSingleton<IIP, Types.IP.IP>();
            _ = builder.Services.AddSingleton<IUPNPDevices, UPNPDevices>();

            _ = builder.Services.AddScopedLazyService<IContentDirectoryService, ContentDirectoryService>();
            _ = builder.Services.AddScopedLazyService<IAVTransportService, AVTransportService>();
            _ = builder.Services.AddScopedLazyService<IConnectionManagerService, ConnectionManagerService>();
            _ = builder.Services.AddScopedLazyService<IMediaReceiverRegistrarService, MediaReceiverRegistrarService>();

            _ = builder.Services.AddScopedLazyService<IMediaProcessingService, MediaProcessingService>();
            _ = builder.Services.AddScopedLazyService<IAudioProcessor, AudioProcessor>();
            _ = builder.Services.AddScopedLazyService<IImageProcessor, ImageProcessor>();
            _ = builder.Services.AddScopedLazyService<IVideoProcessor, VideoProcessor>();
            _ = builder.Services.AddScopedLazyService<IFFmpegService, FFmpegService>();

            _ = builder.Services.AddScopedLazyService<IContentExplorerManager, ContentExplorerManager>();
            _ = builder.Services.AddScopedLazyService<IFileMemoryCacheManager, FileMemoryCacheManager>();
            _ = builder.Services.AddSingleton<IFileWatcherHandler, FileWatcherHandler>();
            _ = builder.Services.AddScopedLazyService<IFileWatcherManager, FileWatcherManager>();
            _ = builder.Services.AddTransient<IFileService, FileService>();

            // Hosted services 
            {
                {
                    _ = builder.Services.AddHostedService<DlnaStartUpShutDownService>();
                }
                {
                    var sp = builder.Services.BuildServiceProvider();
                    var devices = sp.GetRequiredService<IUPNPDevices>();
                    var serverConfig = sp.GetRequiredService<ServerConfig>();
                    devices.InitializeAsync().Wait();
                    var ips = devices.AllUPNPDevices.GroupBy(static (dev) => dev.Endpoint);
                    foreach (var ip in ips)
                    {
                        _ = builder.Services.AddHostedService<SSDPNotifierService>(provider =>
                        new SSDPNotifierService(
                            provider.GetRequiredService<ILogger<SSDPNotifierService>>(),
                            provider.GetRequiredService<IServiceScopeFactory>(),
                            ip.Key,
                            serverConfig,
                            provider.GetRequiredService<IUPNPDevices>(),
                            provider.GetRequiredService<IIP>()
                            ));
                    }
                }
                {
                    _ = builder.Services.AddHostedService<SSDPListenerService>();
                }
                {
                    _ = builder.Services.AddHostedService<FileWatcherService>();
                }
            }

            var app = builder.Build();

            return app;
        }

        private static void ConfigureWebHost(this WebApplication app)
        {
            //// at local network without certificate
            //app.UseHttpsRedirection();
            //app.UseAuthorization();
            //
            //app.UseResponseCaching();
            //app.UseRouting(); 

            //use only for testing, taking too much memory with each streaming file 
            //app.UseMiddleware<PeekHeadersAndBodyMiddleware>();

            _ = app.UseMiddleware<BlockAllMiddleware>();
            _ = app.UseResponseCompression();

            _ = app.UseRouting();

            _ = ((IApplicationBuilder)app).UseSoapEndpoint<IContentDirectoryService, CustomEnvelopeMessage>(
                    options: SetSoapOptions(EndpointServices.ContentDirectoryServicePath));
            _ = ((IApplicationBuilder)app).UseSoapEndpoint<IConnectionManagerService, CustomEnvelopeMessage>(
                    options: SetSoapOptions(EndpointServices.ConnectionManagerServicePath));
            _ = ((IApplicationBuilder)app).UseSoapEndpoint<IAVTransportService, CustomEnvelopeMessage>(
                    options: SetSoapOptions(EndpointServices.AVTransportServicePath));
            _ = ((IApplicationBuilder)app).UseSoapEndpoint<IMediaReceiverRegistrarService, CustomEnvelopeMessage>(
                    options: SetSoapOptions(EndpointServices.MediaReceiverRegistrarServicePath));

            _ = app.MapControllers();
            _ = app.MapFallbackToController("NotFoundFallback", "Error");
        }

        private static Action<SoapCoreOptions> SetSoapOptions(string path)
        {
            return (soapCoreOptions) =>
            {
                soapCoreOptions.StandAloneAttribute = true;
                soapCoreOptions.Path = path;
                soapCoreOptions.EncoderOptions =
                [
                    new() { OverwriteResponseContentType = true, ReaderQuotas = XmlDictionaryReaderQuotas.Max }
                ];
                soapCoreOptions.HttpGetEnabled = true;
                soapCoreOptions.HttpPostEnabled = true;
                soapCoreOptions.HttpsGetEnabled = false; // at local network without certificate
                soapCoreOptions.HttpsPostEnabled = false;
                soapCoreOptions.SoapSerializer = SoapSerializer.XmlSerializer;
                soapCoreOptions.OmitXmlDeclaration = false;
                soapCoreOptions.IndentXml = false;
                soapCoreOptions.IndentWsdl = false;

                NameTable nt = new();
                XmlNamespaceManager prefix = new(nt);
                prefix.AddNamespace("SOAP-ENV", "http://schemas.xmlsoap.org/soap/envelope/");
                soapCoreOptions.XmlNamespacePrefixOverrides = prefix;
            };
        }

        private static IServiceCollection AddTransientLazyService<TInterface, TImplementation>(this IServiceCollection services)
            where TInterface : class
            where TImplementation : class, TInterface
        {
            _ = services.AddTransient<TInterface, TImplementation>();
            _ = services.AddTransientLazyService<TInterface>();
            return services;
        }
        private static IServiceCollection AddTransientLazyService<TInterface>(this IServiceCollection services)
            where TInterface : class
        {
            _ = services.AddTransient(static (provider) => new Lazy<TInterface>(provider.GetRequiredService<TInterface>()));
            return services;
        }

        private static IServiceCollection AddScopedLazyService<TInterface, TImplementation>(this IServiceCollection services)
            where TInterface : class
            where TImplementation : class, TInterface
        {
            _ = services.AddScoped<TInterface, TImplementation>();
            _ = services.AddScopedLazyService<TInterface>();
            return services;
        }
        private static IServiceCollection AddScopedLazyService<TInterface>(this IServiceCollection services)
            where TInterface : class
        {
            _ = services.AddScoped(static (provider) => new Lazy<TInterface>(provider.GetRequiredService<TInterface>()));
            return services;
        }
    }
}