using Microsoft.Extensions.DependencyInjection.Extensions;
using YoutubeCulvert;
using YoutubeCulvert.Authentication;
using YoutubeCulvert.Caching;
using YoutubeCulvert.Configuration;
using YoutubeCulvert.DependencyInjection;
using YoutubeCulvert.Downloads;
using YoutubeCulvert.Handle;
using YoutubeCulvert.Http;
using YoutubeCulvert.InnerTube;
using YoutubeCulvert.InnerTube.Endpoints;
using YoutubeCulvert.Logging;
using YoutubeCulvert.Parsing;
using YoutubeCulvert.Services;
using YoutubeCulvert.Url;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registration helpers for <see cref="IYoutubeClient"/>.</summary>
public static class YoutubeCulvertServiceCollectionExtensions
{
    /// <summary>
    /// Registers the client, its services, endpoints, parsers and the default
    /// middleware pipeline.
    /// </summary>
    public static IYoutubeCulvertBuilder AddYoutubeCulvert(
        this IServiceCollection services,
        Action<YoutubeClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new YoutubeClientOptions();
        configure?.Invoke(options);

        var builder = new YoutubeCulvertBuilder(services);

        services.TryAddSingleton(options);
        services.TryAddSingleton(options.CacheTtls);
        services.TryAddSingleton<IYoutubeCache>(NullYoutubeCache.Instance);
        services.TryAddSingleton<IYoutubeLogger>(NullLogger.Instance);
        services.TryAddSingleton<IYoutubeUrlParser, YoutubeUrlParser>();

        services.TryAddSingleton<YoutubeCulvert.InnerTube.Middleware.LoggingMiddleware>();
        services.TryAddSingleton<YoutubeCulvert.InnerTube.Middleware.CacheMiddleware>();
        services.TryAddSingleton<YoutubeCulvert.InnerTube.Middleware.AuthSigningMiddleware>();
        services.TryAddSingleton<YoutubeCulvert.InnerTube.Middleware.RetryMiddleware>();
        services.TryAddSingleton<YoutubeCulvert.InnerTube.Middleware.RateLimitMiddleware>();
        services.TryAddSingleton<YoutubeCulvert.InnerTube.Middleware.ErrorClassificationMiddleware>();

        services.TryAddSingleton<IAuthStore>(_ => options.AuthStore ?? FileAuthStore.CreateDefault());

        services.TryAddSingleton(sp => new AuthStateManager(
            sp.GetRequiredService<IAuthStore>(),
            new Uri(options.NormalizedBaseUrl)));

        services.TryAddSingleton<IAuthStateManager>(sp => sp.GetRequiredService<AuthStateManager>());

        services.TryAddSingleton(sp => new HttpTransport(
            options,
            sp.GetRequiredService<AuthStateManager>().Cookies));

        services.TryAddSingleton<IClientProfileProvider>(_ =>
            options.ClientProfileProvider ?? new AutoClientProfileProvider());

        services.TryAddSingleton<IRequestSigner>(sp => new RequestSigner(
            sp.GetRequiredService<AuthStateManager>(),
            options.NormalizedBaseUrl));

        services.TryAddSingleton(sp => new InnerTubeTransport(
            sp.GetRequiredService<HttpTransport>(),
            sp.GetRequiredService<AuthStateManager>(),
            options,
            sp.GetRequiredService<IClientProfileProvider>(),
            sp.GetRequiredService<IRequestSigner>(),
            builder.Middlewares
                .Select(registration => (IInnerTubeMiddleware)(sp.GetService(registration.Type)
                    ?? ActivatorUtilities.CreateInstance(sp, registration.Type)))
                .ToArray()));

        services.TryAddSingleton<IInnerTubeTransport>(sp => sp.GetRequiredService<InnerTubeTransport>());
        services.TryAddSingleton<YoutubeCulvert.Http.IHttpTransport>(sp => sp.GetRequiredService<HttpTransport>());

        RegisterEndpoints(services);
        RegisterParsers(services);

        services.TryAddSingleton(sp => new ChannelHandleResolver(
            sp.GetRequiredService<ResolveUrlEndpoint>(),
            sp.GetRequiredService<HttpTransport>(),
            sp.GetRequiredService<IYoutubeCache>(),
            sp.GetRequiredService<CacheTtls>(),
            options));

        RegisterAuthentication(services, options);
        RegisterServices(services, options);
        RegisterDownloader(services, options);

        services.TryAddScoped<IYoutubeClient>(sp =>
        {
            var state = sp.GetRequiredService<AuthStateManager>();
            var auth = sp.GetRequiredService<IAuthenticationService>();
            var http = sp.GetRequiredService<HttpTransport>();

            var context = new YoutubeClientContext
            {
                Options = options,
                Search = sp.GetRequiredService<ISearchService>(),
                Videos = sp.GetRequiredService<IVideosService>(),
                Transcripts = sp.GetRequiredService<ITranscriptsService>(),
                Comments = sp.GetRequiredService<ICommentsService>(),
                Channels = sp.GetRequiredService<IChannelsService>(),
                Playlists = sp.GetRequiredService<IPlaylistsService>(),
                Home = sp.GetRequiredService<IHomeService>(),
                AccountFeed = sp.GetRequiredService<IAccountFeedService>(),
                Interactions = sp.GetRequiredService<IInteractionsService>(),
                Pages = sp.GetRequiredService<IPageService>(),
                Downloader = sp.GetRequiredService<IYoutubeDownloader>(),
                Auth = auth,
                Urls = sp.GetRequiredService<IYoutubeUrlParser>(),
                Transport = sp.GetRequiredService<IInnerTubeTransport>(),
                InitializeSessionAsync = async ct =>
                {
                    await http.InitializeAsync(ct).ConfigureAwait(false);
                    await auth.InitializeAsync(ct).ConfigureAwait(false);
                },
                SaveStateAsync = ct => state.SaveAsync(ct)
            };

            return new YoutubeClient(context);
        });

        return builder;
    }

    /// <summary>Replaces the cache with <typeparamref name="TCache"/> and configures TTLs.</summary>
    public static IYoutubeCulvertBuilder AddYoutubeCache<TCache>(
        this IYoutubeCulvertBuilder builder,
        Action<YoutubeCacheOptions>? configure = null)
        where TCache : class, IYoutubeCache
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new YoutubeCacheOptions();
        configure?.Invoke(options);

        builder.Services.AddSingleton<IYoutubeCache, TCache>();
        builder.Services.AddSingleton(options.Ttls);

        return builder;
    }

    /// <summary>Adds a custom middleware at the requested pipeline position.</summary>
    public static IYoutubeCulvertBuilder AddYoutubeMiddleware<T>(
        this IYoutubeCulvertBuilder builder,
        MiddlewarePosition position = MiddlewarePosition.End)
        where T : class, IInnerTubeMiddleware
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton<T>();
        return builder.AddMiddleware(typeof(T), position);
    }

    /// <summary>Registers a custom typed endpoint.</summary>
    public static IYoutubeCulvertBuilder AddInnerTubeEndpoint<T>(this IYoutubeCulvertBuilder builder)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton<T>();
        return builder;
    }

    private static void RegisterEndpoints(IServiceCollection services)
    {
        services.TryAddSingleton<PlayerEndpoint>();
        services.TryAddSingleton<BrowseEndpoint>();
        services.TryAddSingleton<SearchEndpoint>();
        services.TryAddSingleton<NextEndpoint>();
        services.TryAddSingleton<GuideEndpoint>();
        services.TryAddSingleton<ResolveUrlEndpoint>();
        services.TryAddSingleton<AccountEndpoint>();
    }

    private static void RegisterParsers(IServiceCollection services)
    {
        services.TryAddSingleton<VideoParser>();
        services.TryAddSingleton<StreamManifestParser>();
        services.TryAddSingleton<CaptionTrackParser>();
        services.TryAddSingleton<ChapterParser>();
        services.TryAddSingleton<CommentParser>();
        services.TryAddSingleton<ChannelParser>();
        services.TryAddSingleton<PlaylistParser>();
        services.TryAddSingleton<PlaylistVideoParser>();
        services.TryAddSingleton<SearchParser>();
        services.TryAddSingleton<FeedParser>();
        services.TryAddSingleton<FeedChannelParser>();
        services.TryAddSingleton<FeedPlaylistParser>();
        services.TryAddSingleton<PlaylistTileParser>();
        services.TryAddSingleton<NotificationParser>();
        services.TryAddSingleton<TimedTextParser>();
        services.TryAddSingleton<TranscriptPanelParser>();
        services.TryAddSingleton<AccountParser>();
    }

    private static void RegisterAuthentication(IServiceCollection services, YoutubeClientOptions options)
    {
        services.TryAddSingleton<ICookieAuthProvider>(sp => new CookieAuthProvider(
            sp.GetRequiredService<AuthStateManager>(),
            sp.GetRequiredService<AccountEndpoint>()));

        services.TryAddSingleton<IOAuthProvider>(sp => new OAuthProvider(
            options.OAuth,
            sp.GetRequiredService<HttpTransport>(),
            sp.GetRequiredService<AccountEndpoint>(),
            sp.GetRequiredService<AuthStateManager>()));

        services.TryAddSingleton<IAuthenticationService>(sp => new AuthService(
            sp.GetRequiredService<AuthStateManager>(),
            sp.GetRequiredService<ICookieAuthProvider>(),
            sp.GetRequiredService<IOAuthProvider>(),
            sp.GetRequiredService<AccountEndpoint>(),
            sp.GetRequiredService<HttpTransport>()));
    }

    private static void RegisterServices(IServiceCollection services, YoutubeClientOptions options)
    {
        services.TryAddScoped<ISearchService>(sp => new SearchService(
            sp.GetRequiredService<SearchEndpoint>(),
            sp.GetRequiredService<SearchParser>(),
            options,
            sp.GetRequiredService<YoutubeCulvert.Http.IHttpTransport>()));

        services.TryAddScoped<IVideosService>(sp => new VideosService(
            sp.GetRequiredService<PlayerEndpoint>(),
            sp.GetRequiredService<NextEndpoint>(),
            sp.GetRequiredService<VideoParser>(),
            sp.GetRequiredService<StreamManifestParser>(),
            sp.GetRequiredService<CaptionTrackParser>(),
            sp.GetRequiredService<ChapterParser>(),
            sp.GetRequiredService<IYoutubeUrlParser>(),
            options));

        services.TryAddScoped<ITranscriptsService>(sp => new TranscriptsService(
            sp.GetRequiredService<PlayerEndpoint>(),
            sp.GetRequiredService<NextEndpoint>(),
            sp.GetRequiredService<CaptionTrackParser>(),
            sp.GetRequiredService<TranscriptPanelParser>(),
            sp.GetRequiredService<YoutubeCulvert.Http.IHttpTransport>(),
            sp.GetRequiredService<IYoutubeCache>(),
            sp.GetRequiredService<CacheTtls>(),
            sp.GetRequiredService<IYoutubeUrlParser>(),
            options));

        services.TryAddScoped<ICommentsService>(sp => new CommentsService(
            sp.GetRequiredService<NextEndpoint>(),
            sp.GetRequiredService<CommentParser>(),
            sp.GetRequiredService<IYoutubeUrlParser>()));

        services.TryAddScoped<IChannelsService>(sp => new ChannelsService(
            sp.GetRequiredService<BrowseEndpoint>(),
            sp.GetRequiredService<ChannelHandleResolver>(),
            sp.GetRequiredService<ChannelParser>(),
            sp.GetRequiredService<FeedParser>(),
            sp.GetRequiredService<FeedPlaylistParser>(),
            sp.GetRequiredService<IYoutubeUrlParser>()));

        services.TryAddScoped<IPlaylistsService>(sp => new PlaylistsService(
            sp.GetRequiredService<BrowseEndpoint>(),
            sp.GetRequiredService<PlaylistParser>(),
            sp.GetRequiredService<PlaylistVideoParser>(),
            sp.GetRequiredService<IYoutubeUrlParser>()));

        services.TryAddScoped<IHomeService>(sp => new HomeService(
            sp.GetRequiredService<BrowseEndpoint>(),
            sp.GetRequiredService<FeedParser>()));

        services.TryAddScoped<IAccountFeedService>(sp => new AccountFeedService(
            sp.GetRequiredService<BrowseEndpoint>(),
            sp.GetRequiredService<AccountEndpoint>(),
            sp.GetRequiredService<FeedParser>(),
            sp.GetRequiredService<FeedChannelParser>(),
            sp.GetRequiredService<PlaylistTileParser>(),
            sp.GetRequiredService<NotificationParser>(),
            sp.GetRequiredService<IAuthenticationService>()));

        services.TryAddScoped<IInteractionsService>(sp => new InteractionsService(
            sp.GetRequiredService<AccountEndpoint>(),
            sp.GetRequiredService<ChannelHandleResolver>(),
            sp.GetRequiredService<IAuthenticationService>(),
            sp.GetRequiredService<IYoutubeUrlParser>()));

        services.TryAddScoped<IPageService>(sp => new PageService(
            sp.GetRequiredService<YoutubeCulvert.Http.IHttpTransport>(),
            sp.GetRequiredService<IYoutubeUrlParser>()));
    }

    private static void RegisterDownloader(IServiceCollection services, YoutubeClientOptions options)
    {
        services.TryAddSingleton<IDownloadStrategy>(_ =>
            new RangeDownloadStrategy(options.HttpMessageHandler, options.UserAgent));

        services.TryAddSingleton<ICompanionFileManager>(_ => new CompanionFileManager());

        services.TryAddTransient<IStreamResolver>(sp =>
            new StreamResolver(sp.GetRequiredService<IVideosService>()));

        services.TryAddTransient<IDownloadCoordinator>(sp => new DownloadCoordinator(
            sp.GetRequiredService<IStreamResolver>(),
            sp.GetRequiredService<IDownloadStrategy>(),
            sp.GetRequiredService<ICompanionFileManager>(),
            sp.GetRequiredService<IVideosService>()));

        services.TryAddScoped<IYoutubeDownloader>(sp => new YoutubeDownloader(
            sp.GetRequiredService<IDownloadCoordinator>(),
            sp.GetRequiredService<IDownloadStrategy>(),
            sp.GetRequiredService<IVideosService>(),
            options));
    }
}
