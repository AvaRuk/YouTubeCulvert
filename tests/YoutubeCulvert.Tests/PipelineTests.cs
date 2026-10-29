using System.Net;
using System.Text.Json;
using YoutubeCulvert.Caching;
using YoutubeCulvert.Configuration;
using YoutubeCulvert.Exceptions;
using YoutubeCulvert.InnerTube;
using YoutubeCulvert.InnerTube.Middleware;
using YoutubeCulvert.Services;
using YoutubeCulvert.Url;
using Xunit;

namespace YoutubeCulvert.Tests;

public sealed class UrlTests
{
    private readonly YoutubeUrlParser _parser = new();

    [Fact]
    public void Parses_watch_url_preserving_list_index_and_start()
    {
        Assert.True(_parser.TryParseVideoRef(
            "https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=PLabcdefghij&index=4&t=1m30s",
            out var video));

        Assert.NotNull(video);
        Assert.Equal("dQw4w9WgXcQ", video!.VideoId);
        Assert.Equal("PLabcdefghij", video.PlaylistId);
        Assert.Equal(4, video.Index);
        Assert.Equal(TimeSpan.FromSeconds(90), video.StartAt);
    }

    [Theory]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    public void Parses_video_ids(string input, string expected)
    {
        Assert.True(_parser.TryParseVideoId(input, out var videoId));
        Assert.Equal(expected, videoId);
    }

    [Fact]
    public void Parses_playlist_and_channel_references()
    {
        Assert.True(_parser.TryParsePlaylistId(
            "https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=PLabcdefghij",
            out var playlistId));
        Assert.Equal("PLabcdefghij", playlistId);

        Assert.True(_parser.TryParseChannel("https://www.youtube.com/@mrbeast", out var handle));
        Assert.Equal(new ChannelRef.ByHandle("mrbeast"), handle);

        Assert.True(_parser.TryParseChannel("https://www.youtube.com/channel/UC1234567890123456789012", out var byId));
        Assert.Equal(new ChannelRef.ById("UC1234567890123456789012"), byId);

        Assert.True(_parser.TryParseChannel("@mrbeast", out var shortHandle));
        Assert.Equal(new ChannelRef.ByHandle("mrbeast"), shortHandle);
    }

    [Fact]
    public void Builds_urls()
    {
        Assert.Equal(
            "https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=90s",
            _parser.BuildVideoUrl("dQw4w9WgXcQ", TimeSpan.FromSeconds(90)));

        Assert.Equal(
            "https://www.youtube.com/@mrbeast",
            _parser.BuildChannelUrl(new ChannelRef.ByHandle("mrbeast")));

        Assert.Equal(
            "https://www.youtube.com/channel/UC1234567890123456789012",
            _parser.BuildChannelUrl(new ChannelRef.ById("UC1234567890123456789012")));
    }

    [Fact]
    public void Throws_on_invalid_video_id()
    {
        Assert.Throws<ArgumentException>(() => _parser.ParseVideoIdOrThrow("not a video"));
    }
}

public sealed class RetryMiddlewareTests
{
    private static InnerTubeRequest Request(bool mutation) => new(
        "player",
        new JsonObject { ["videoId"] = "dQw4w9WgXcQ" },
        IsMutation: mutation);

    private static YoutubeClientOptions Options(int maxRetries = 2) => new()
    {
        MaxRetries = maxRetries,
        RetryDelay = TimeSpan.Zero,
        MaxDelay = TimeSpan.Zero
    };

    [Fact]
    public async Task Retries_idempotent_requests_with_exponential_backoff()
    {
        var middleware = new RetryMiddleware(Options());
        var calls = 0;

        var result = await middleware.InvokeAsync(
            Request(mutation: false),
            (_, _) =>
            {
                calls++;

                if (calls < 3)
                {
                    throw new YoutubeApiException(
                        HttpStatusCode.InternalServerError,
                        "player",
                        null,
                        "boom");
                }

                return Task.FromResult(Fixtures.Empty());
            },
            CancellationToken.None);

        Assert.Equal(3, calls);
        Assert.Equal(JsonValueKind.Object, result.ValueKind);
    }

    [Fact]
    public async Task Never_retries_mutations()
    {
        var middleware = new RetryMiddleware(Options());
        var calls = 0;

        await Assert.ThrowsAsync<YoutubeApiException>(() => middleware.InvokeAsync(
            Request(mutation: true),
            (_, _) =>
            {
                calls++;
                throw new YoutubeApiException(
                    HttpStatusCode.InternalServerError,
                    "like/like",
                    null,
                    "boom");
            },
            CancellationToken.None));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Gives_up_after_max_retries()
    {
        var middleware = new RetryMiddleware(Options(maxRetries: 2));
        var calls = 0;

        await Assert.ThrowsAsync<YoutubeApiException>(() => middleware.InvokeAsync(
            Request(mutation: false),
            (_, _) =>
            {
                calls++;
                throw new YoutubeApiException(
                    HttpStatusCode.BadGateway,
                    "player",
                    null,
                    "boom");
            },
            CancellationToken.None));

        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Does_not_retry_authentication_errors()
    {
        var middleware = new RetryMiddleware(Options());
        var calls = 0;

        await Assert.ThrowsAsync<YoutubeAuthenticationException>(() => middleware.InvokeAsync(
            Request(mutation: false),
            (_, _) =>
            {
                calls++;
                throw new YoutubeAuthenticationException("expired");
            },
            CancellationToken.None));

        Assert.Equal(1, calls);
    }
}

public sealed class CacheMiddlewareTests
{
    [Fact]
    public async Task Caches_successful_responses_by_key()
    {
        var cache = new MemoryYoutubeCache();
        var middleware = new CacheMiddleware(cache, new CacheTtls());
        var calls = 0;
        var request = new InnerTubeRequest(
            "player",
            new JsonObject(),
            CacheKey: CacheKeys.Video("dQw4w9WgXcQ", "ru", "RU"));

        var first = await middleware.InvokeAsync(
            request,
            (_, _) =>
            {
                calls++;
                return Task.FromResult(Fixtures.Parse("""{"value":1}"""));
            },
            CancellationToken.None);

        var second = await middleware.InvokeAsync(
            request,
            (_, _) =>
            {
                calls++;
                return Task.FromResult(Fixtures.Parse("""{"value":2}"""));
            },
            CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.Equal(1, first.GetProperty("value").GetInt32());
        Assert.Equal(1, second.GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task Caches_unavailable_videos_for_the_negative_ttl()
    {
        var cache = new MemoryYoutubeCache();
        var middleware = new CacheMiddleware(cache, new CacheTtls());
        var calls = 0;

        var request = new InnerTubeRequest(
            "player",
            new JsonObject(),
            CacheKey: CacheKeys.Streams("bad", "en", "US"));

        await Assert.ThrowsAsync<VideoUnavailableException>(() => middleware.InvokeAsync(
            request,
            (_, _) =>
            {
                calls++;
                throw new VideoUnavailableException(
                    "bad",
                    new PlayabilityStatus { Status = "ERROR", Reason = "Private video" });
            },
            CancellationToken.None));

        await Assert.ThrowsAsync<VideoUnavailableException>(() => middleware.InvokeAsync(
            request,
            (_, _) =>
            {
                calls++;
                return Task.FromResult(Fixtures.Empty());
            },
            CancellationToken.None));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Object_is_not_cached_without_cache_key()
    {
        var cache = new MemoryYoutubeCache();
        var middleware = new CacheMiddleware(cache, new CacheTtls());
        var calls = 0;
        var request = new InnerTubeRequest("search", new JsonObject());

        for (var index = 0; index < 2; index++)
        {
            await middleware.InvokeAsync(
                request,
                (_, _) =>
                {
                    calls++;
                    return Task.FromResult(Fixtures.Empty());
                },
                CancellationToken.None);
        }

        Assert.Equal(2, calls);
    }
}

public sealed class CacheKeyTests
{
    [Fact]
    public void Keys_include_language_and_region()
    {
        Assert.Equal("video:abc:ru:RU", CacheKeys.Video("abc", "ru", "RU"));
        Assert.Equal("streams:abc:en:US", CacheKeys.Streams("abc", "en", "US"));
        Assert.Equal(
            "transcript:abc:ru:False:ru:RU",
            CacheKeys.Transcript("abc", "ru", false, "ru", "RU"));
    }
}

public sealed class OptionsTests
{
    [Fact]
    public void Defaults_are_valid()
    {
        new YoutubeClientOptions().Validate();
    }

    [Theory]
    [InlineData("english")]
    [InlineData("")]
    public void Invalid_language_throws(string language)
    {
        var options = new YoutubeClientOptions { Language = language };
        Assert.Throws<YoutubeConfigurationException>(options.Validate);
    }

    [Fact]
    public void Invalid_audio_track_id_throws()
    {
        var options = new YoutubeClientOptions { PreferredAudioTrackId = "russian" };
        Assert.Throws<YoutubeConfigurationException>(options.Validate);
    }

    [Fact]
    public void Invalid_region_and_retries_throw()
    {
        Assert.Throws<YoutubeConfigurationException>(
            new YoutubeClientOptions { Region = "USA" }.Validate);

        Assert.Throws<YoutubeConfigurationException>(
            new YoutubeClientOptions { MaxRetries = -1 }.Validate);

        Assert.Throws<YoutubeConfigurationException>(
            new YoutubeClientOptions { Timeout = TimeSpan.Zero }.Validate);
    }
}

public sealed class ResultTests
{
    [Fact]
    public void Success_has_value_and_no_error()
    {
        var result = Result<int>.Ok(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_exposes_kind_and_message()
    {
        var result = Result<string>.Fail(new YoutubeError(
            YoutubeErrorKind.Unavailable,
            "private",
            404));

        Assert.False(result.IsSuccess);
        Assert.Equal(YoutubeErrorKind.Unavailable, result.Error!.Kind);
        Assert.Equal(404, result.Error.StatusCode);
    }
}
