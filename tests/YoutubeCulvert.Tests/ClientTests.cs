using Microsoft.Extensions.DependencyInjection;
using YoutubeCulvert.Caching;
using YoutubeCulvert.Datatypes;
using YoutubeCulvert.Exceptions;
using YoutubeCulvert.Testing;
using Xunit;

namespace YoutubeCulvert.Tests;

public sealed class YoutubeClientTests
{
    private const string UnavailablePlayer = """
    {
      "videoDetails": { "videoId": "badbadbadba", "title": "Gone" },
      "playabilityStatus": { "status": "ERROR", "reason": "This video is private" }
    }
    """;

    private static YoutubeClient CreateClient(FakeHttpHandler handler, bool useCache = false) =>
        new(new YoutubeClientOptions
        {
            HttpMessageHandler = handler,
            InitializeSessionOnFirstRequest = false,
            AuthStore = new InMemoryAuthStore(),
            Cache = useCache ? new MemoryYoutubeCache() : null
        });

    [Fact]
    public async Task Loads_video_streams_audio_tracks_and_captions()
    {
        var fake = new FakeHttpHandler().Map("youtubei/v1/player", Fixtures.PlayerWithAudioTracks);
        await using var youtube = CreateClient(fake);

        await youtube.InitializeAsync();

        var video = await youtube.Videos.GetAsync("dQw4w9WgXcQ");
        Assert.Equal("Test video", video.Title);
        Assert.Equal("dQw4w9WgXcQ", video.Id);

        var streams = await youtube.Videos.GetStreamsAsync("dQw4w9WgXcQ");
        Assert.NotEmpty(streams.Streams);

        var tracks = await youtube.Videos.GetAudioTracksAsync("dQw4w9WgXcQ");
        Assert.Equal(2, tracks.Count);

        var captions = await youtube.Videos.GetCaptionTracksAsync("dQw4w9WgXcQ");
        Assert.Single(captions);
    }

    [Fact]
    public async Task Respects_the_configured_cache()
    {
        var fake = new FakeHttpHandler().Map("youtubei/v1/player", Fixtures.PlayerWithAudioTracks);
        await using var youtube = CreateClient(fake, useCache: true);

        await youtube.Videos.GetAsync("dQw4w9WgXcQ");
        await youtube.Videos.GetAsync("dQw4w9WgXcQ");

        Assert.Equal(1, fake.Count("youtubei/v1/player"));
    }

    [Fact]
    public async Task Unavailable_video_throws_and_TryGet_returns_error()
    {
        var fake = new FakeHttpHandler().Map("youtubei/v1/player", UnavailablePlayer);
        await using var youtube = CreateClient(fake);

        await Assert.ThrowsAsync<VideoUnavailableException>(
            () => youtube.Videos.GetAsync("badbadbadba"));

        var result = await youtube.Videos.TryGetAsync("badbadbadba");

        Assert.False(result.IsSuccess);
        Assert.Equal(YoutubeErrorKind.Unavailable, result.Error!.Kind);
        Assert.Contains("private", result.Error.Message);
    }

    [Fact]
    public async Task RawAsync_calls_the_requested_endpoint()
    {
        var fake = new FakeHttpHandler().Map("youtubei/v1/guide", """{"ok":true}""");
        await using var youtube = CreateClient(fake);

        var raw = await youtube.RawAsync("guide", new JsonObject());

        Assert.True(raw.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Calls_after_dispose_throw()
    {
        var fake = new FakeHttpHandler().Map("youtubei/v1/player", Fixtures.PlayerWithAudioTracks);
        var youtube = CreateClient(fake);

        await youtube.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => youtube.InitializeAsync());
    }
}

public sealed class DependencyInjectionTests
{
    [Fact]
    public async Task Resolves_the_client_with_cache_and_telemetry()
    {
        var fake = new FakeHttpHandler().Map("youtubei/v1/player", Fixtures.PlayerWithAudioTracks);

        var services = new ServiceCollection();

        services
            .AddYoutubeCulvert(options =>
            {
                options.HttpMessageHandler = fake;
                options.InitializeSessionOnFirstRequest = false;
                options.AuthStore = new InMemoryAuthStore();
            })
            .AddYoutubeCache<MemoryYoutubeCache>()
            .AddYoutubeTelemetry(telemetry => telemetry.SampleRate = 1.0);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var youtube = scope.ServiceProvider.GetRequiredService<IYoutubeClient>();
        await youtube.InitializeAsync();

        var video = await youtube.Videos.GetAsync("dQw4w9WgXcQ");
        Assert.Equal("Test video", video.Title);

        var again = await youtube.Videos.GetAsync("dQw4w9WgXcQ");
        Assert.Equal("Test video", again.Title);

        Assert.Equal(1, fake.Count("youtubei/v1/player"));
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IYoutubeCache>());
    }

    [Fact]
    public async Task Registers_custom_middleware_and_endpoints()
    {
        var fake = new FakeHttpHandler().Map("youtubei/v1/player", Fixtures.PlayerWithAudioTracks);
        var services = new ServiceCollection();

        services
            .AddYoutubeCulvert(options =>
            {
                options.HttpMessageHandler = fake;
                options.InitializeSessionOnFirstRequest = false;
                options.AuthStore = new InMemoryAuthStore();
            })
            .AddYoutubeMiddleware<RecordingMiddleware>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var youtube = scope.ServiceProvider.GetRequiredService<IYoutubeClient>();
        await youtube.Videos.GetAsync("dQw4w9WgXcQ");

        var recording = scope.ServiceProvider.GetRequiredService<RecordingMiddleware>();
        Assert.Contains(recording.Requests, request => request.Endpoint == "player");

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<YoutubeCulvert.InnerTube.Endpoints.PlayerEndpoint>());
    }
}
