# YouTubeCulvert

.NET library for YouTube **without the official Data API**. Every request goes
through InnerTube — the same internal API the YouTube web client uses. No API
keys, no third-party services.

Features: search, videos and streams, **multi-language audio tracks** (dubs and
translations), subtitles, chapters, channels, playlists, comments and replies,
feeds (trending, subscriptions, history, watch later, notifications), account
actions (likes, subscriptions, comments, playlist management) and downloads.

## Requirements

- .NET 10
- Core has no external dependencies (BCL only)
- `Nullable` enabled; JSON is parsed manually, so the core is trim/AOT friendly

## Packages

| Package | Purpose | Dependencies |
| --- | --- | --- |
| **YoutubeCulvert.Abstractions** | interfaces, DTOs, exceptions, options, `Result<T>`, `ParseResult<T>`, `YoutubeError` | BCL only |
| **YoutubeCulvert** (Core) | InnerTube, parsers, services, downloader, auth, cache | Abstractions |
| **YoutubeCulvert.DependencyInjection** | `AddYoutubeCulvert(...)` and builders | Core, `Microsoft.Extensions.DependencyInjection.Abstractions` |
| **YoutubeCulvert.Telemetry** | `ILogger` / `ActivitySource` adapter | Core, DI, `Microsoft.Extensions.Logging.Abstractions` |
| **YoutubeCulvert.Testing** | `FakeHttpHandler`, `InMemoryAuthStore`, `RecordingMiddleware`, `FakeYoutubeCache` | Core |

## Quick start

### Dependency injection

```csharp
using Microsoft.Extensions.DependencyInjection;
using YoutubeCulvert;

var services = new ServiceCollection();

services.AddYoutubeCulvert(o =>
{
    o.Language = "ru";
    o.Region   = "RU";
})
.AddYoutubeCache<MemoryYoutubeCache>()
.AddYoutubeMiddleware<LoggingMiddleware>()
.AddYoutubeMiddleware<RetryMiddleware>()
.AddYoutubeTelemetry();

await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();

var youtube = scope.ServiceProvider.GetRequiredService<IYoutubeClient>();
await youtube.InitializeAsync();

var video = await youtube.Videos.GetAsync("dQw4w9WgXcQ");
Console.WriteLine($"{video.Title} — {video.Duration}");
```

### Manual construction

```csharp
using YoutubeCulvert;

var options = new YoutubeClientOptions
{
    Language = "ru",
    Region   = "RU"
};

await using var youtube = new YoutubeClient(options);
await youtube.InitializeAsync();

var video = await youtube.Videos.GetAsync("dQw4w9WgXcQ");
```

Anonymous access is enough for search, videos, streams, subtitles, comments,
channels, playlists and trending. Personal feeds and account actions require a
signed-in session.

## Authentication

Cookie session (recommended for account actions):

```csharp
await youtube.Auth.SignInWithCookieHeaderAsync(
    "SAPISID=...; __Secure-3PAPISID=...; SID=...; HSID=...; SSID=...; LOGIN_INFO=...");
```

OAuth2 device-code flow:

```csharp
var challenge = await youtube.Auth.StartDeviceCodeFlowAsync();
Console.WriteLine($"Open {challenge.VerificationUrl} and enter {challenge.UserCode}");

var account = await youtube.Auth.CompleteDeviceCodeFlowAsync(challenge);
```

The state is persisted through `IAuthStore` (by default
`%LOCALAPPDATA%\YoutubeCulvert\auth.json`, plaintext — replace the store for
production).

## Streams and audio tracks

```csharp
var streams = await youtube.Videos.GetStreamsAsync("dQw4w9WgXcQ");

var muxed  = streams.GetBestMuxedStream(maxHeight: 720);
var video  = streams.GetBestVideoStream(maxHeight: 1080);
var audio  = streams.GetBestAudioStream();           // default track
var russian = streams.GetBestAudioStream("ru-RU.3"); // specific track
var original = streams.GetBestOriginalAudioStream();
var byItag = streams.GetStreamByItag(18);
```

YouTube returns multi-language audio in `streamingData.adaptiveFormats[].audioTrack`.
Several streams may share one `itag` but have different tracks:

```csharp
var tracks = await youtube.Videos.GetAudioTracksAsync("dQw4w9WgXcQ");

foreach (var track in tracks)
    Console.WriteLine($"{track.Id} | {track.DisplayName} | default={track.IsDefault} | streams={track.StreamCount}");
```

`AudioTrack` exposes `Id`, `DisplayName`, `LanguageCode`, `IsDefault`,
`IsOriginal`, `StreamCount`, `Raw`.

## Downloading

```csharp
var result = await youtube.DownloadAsync(
    "dQw4w9WgXcQ", "video.mp4", maxHeight: 720,
    progress: new Progress<DownloadProgress>(p =>
        Console.WriteLine($"{p.Percent:P0}  {p.Bytes}/{p.Total}")));

await youtube.DownloadAudioAsync("dQw4w9WgXcQ", "ru.m4a", audioTrackId: "ru-RU.3");

var all = await youtube.Downloader.DownloadAllAudioTracksAsync("dQw4w9WgXcQ", "audio/");
```

Partial files are resumed with HTTP Range. When YouTube offers no muxed stream,
the video and audio files are saved separately (`CompanionAudioPath` /
`CompanionVideoPath`); the library does not bundle ffmpeg:

```bash
ffmpeg -i video.mp4 -i video.audio.webm -c copy out.mp4
```

## Expected errors: `Result<T>`

`Get*Async` throws; `Try*Async` returns `Result<T>` for expected situations
(unavailable, auth required, rate limited, parse failed) and never throws for
them:

```csharp
var result = await youtube.Videos.TryGetAsync("dQw4w9WgXcQ");

switch (result.Error?.Kind)
{
    case YoutubeErrorKind.Unavailable:
        Console.WriteLine($"Unavailable: {result.Error.Message}");
        break;
    case null:
        Console.WriteLine(result.Value!.Title);
        break;
}
```

## Pagination

`ContinueAsync(continuation)` remains available; every paginated service also
implements `IAsyncEnumerable<T>`:

```csharp
await foreach (var comment in youtube.Comments.EnumerateAsync(
    "dQw4w9WgXcQ", new PaginationOptions(MaxItems: 500), ct))
{
    Console.WriteLine($"{comment.Author}: {comment.Text}");
}
```

## Caching

The default is `NullYoutubeCache` (no cache). Enable it in DI:

```csharp
services.AddYoutubeCache<MemoryYoutubeCache>(o =>
{
    o.Ttls.Video          = TimeSpan.FromMinutes(10);
    o.Ttls.Streams        = TimeSpan.FromMinutes(5);
    o.Ttls.ChannelResolve = TimeSpan.FromHours(24);
});
```

Keys include `hl`/`gl`, so regions never mix. Unavailable videos are cached for
`Ttls.Negative`.

## Architecture

```
Services/       application: search, videos, comments, channels, feeds, actions
Parsing/        IResponseParser<TIn,TOut> -> ParseResult<T> with ParseDiagnostics
Downloader/     StreamResolver + RangeDownloadStrategy + CompanionFileManager + Coordinator
InnerTube/      IInnerTubeTransport, typed endpoints, middleware pipeline, profile selection
Http/           HttpTransport (thin HttpClient layer, no retries)
Auth/           AuthStateManager, CookieAuthProvider, OAuthProvider, RequestSigner
Cache/          IYoutubeCache implementations
Url/            IYoutubeUrlParser + static YoutubeUrl facade
```

Default pipeline:

```
Telemetry -> Logging -> Cache -> AuthSigning -> Retry -> RateLimit -> ErrorClassification -> Transport
```

- Mutations set `IsMutation = true` and are never retried.
- `AuthSigningMiddleware` chooses WEB + `SAPISIDHASH` when SAPISID is present,
  otherwise TVHTML5 + bearer.
- `CacheMiddleware` uses `InnerTubeRequest.CacheKey` and `CacheTtls`.
- Profile selection is data (`InnerTubeClientProfile`), not code paths.

## Testing

```csharp
var fake = new FakeHttpHandler()
    .Map("/youtubei/v1/player", playerJson);

var youtube = new YoutubeClient(new YoutubeClientOptions
{
    HttpMessageHandler = fake,
    AuthStore = new InMemoryAuthStore()
});
```

`RecordingMiddleware` captures every `InnerTubeRequest`; parsers, endpoints,
middleware, auth providers and the cache can all be tested in isolation.

## Build and test

```bash
dotnet build YouTubeCulvert.slnx
dotnet test YouTubeCulvert.slnx
dotnet pack -c Release
```

## Version 2 layout

The library was restructured into five packages. Migration notes:

- Types moved from one assembly to `YoutubeCulvert.Abstractions` / `YoutubeCulvert`.
- `VideoInformation` is now `Video`; DTOs implement `IExtensible` (`Raw`,
  `Get<T>(path)`).
- `IProgress<double>` in download methods is now `IProgress<DownloadProgress>`.
- Options are configured through settable properties
  (`AddYoutubeCulvert(o => o.Language = "ru")`) and validated by
  `InitializeAsync` (invalid values throw `YoutubeConfigurationException`).
- `ParseResult<T>` and `ParseDiagnostics` are public.
