using YoutubeCulvert.Datatypes;
using YoutubeCulvert.Parsing;
using Xunit;

namespace YoutubeCulvert.Tests;

public sealed class StreamManifestTests
{
    private static StreamManifest ParseManifest(string json)
    {
        var result = new StreamManifestParser().Parse(Fixtures.Parse(json));
        Assert.True(result.IsRecognized, result.Diagnostics.Reason);
        return Assert.Single(result.Items);
    }

    [Fact]
    public void Parses_muxed_adaptive_and_audio_track_metadata()
    {
        var manifest = ParseManifest(Fixtures.PlayerWithAudioTracks);

        Assert.Equal(5, manifest.Streams.Count);
        Assert.Single(manifest.Muxed);
        Assert.Single(manifest.VideoOnly);
        Assert.Equal(3, manifest.AudioOnly.Count);

        var adaptiveVideo = manifest.VideoOnly[0];
        Assert.Equal(1080, adaptiveVideo.Height);
        Assert.Equal(new ByteRange(0, 100), adaptiveVideo.InitRange);
        Assert.Equal(new ByteRange(101, 200), adaptiveVideo.IndexRange);

        var audio = manifest.AudioOnly[0];
        Assert.Equal("en-US.4", audio.AudioTrackId);
        Assert.Equal("English (US)", audio.AudioTrackName);
        Assert.True(audio.AudioIsDefault);
        Assert.Equal("en-US", audio.LanguageCode);
    }

    [Fact]
    public void Groups_streams_by_audio_track()
    {
        var manifest = ParseManifest(Fixtures.PlayerWithAudioTracks);
        var tracks = manifest.GetAudioTracks();

        Assert.Equal(2, tracks.Count);

        var english = tracks.Single(track => track.Id == "en-US.4");
        Assert.Equal(2, english.StreamCount);
        Assert.True(english.IsDefault);
        Assert.True(english.IsOriginal);
        Assert.Equal("en-US", english.LanguageCode);

        var russian = tracks.Single(track => track.Id == "ru-RU.3");
        Assert.Equal(1, russian.StreamCount);
        Assert.False(russian.IsDefault);
        Assert.Equal("ru-RU", russian.LanguageCode);

        Assert.Equal(2, manifest.GetAudioStreams("en-US.4").Count);
        Assert.Equal(160000L, manifest.GetBestAudioStream("en-US.4")!.Bitrate);
        Assert.Equal("en-US.4", manifest.GetDefaultAudioTrack()!.Id);
    }

    [Fact]
    public void Selects_best_streams_and_by_quality_and_itag()
    {
        var manifest = ParseManifest(Fixtures.PlayerWithAudioTracks);

        Assert.Equal(1080, manifest.GetBestVideoStream()!.Height);
        Assert.Equal(360, manifest.GetBestMuxedStream(720)!.Height);
        Assert.Null(manifest.GetBestMuxedStream(240));
        Assert.Equal("1080p", manifest.GetStreamByQuality("1080p")!.QualityLabel);
        Assert.Equal(18, manifest.GetStreamByItag(18)!.Itag);
        Assert.Equal("en-US.4", manifest.GetDefaultAudioTrack()!.Id);
    }

    [Fact]
    public void Streams_without_audio_track_form_one_default_track()
    {
        const string json = """
        {
          "streamingData": {
            "adaptiveFormats": [
              { "itag": 140, "url": "https://example.com/a.m4a", "mimeType": "audio/mp4; codecs=\"mp4a.40.2\"", "bitrate": 128000 },
              { "itag": 251, "url": "https://example.com/a.webm", "mimeType": "audio/webm; codecs=\"opus\"", "bitrate": 160000 }
            ]
          }
        }
        """;

        var manifest = ParseManifest(json);
        var tracks = manifest.GetAudioTracks();

        var track = Assert.Single(tracks);
        Assert.Equal(StreamManifest.UnnamedAudioTrackId, track.Id);
        Assert.Equal("Default", track.DisplayName);
        Assert.True(track.IsDefault);
        Assert.Equal(2, track.StreamCount);
        Assert.Equal(2, manifest.GetAudioStreams(StreamManifest.UnnamedAudioTrackId).Count);
        Assert.NotNull(manifest.GetBestOriginalAudioStream());
    }

    [Fact]
    public void Reports_unrecognized_manifest_instead_of_empty_list()
    {
        var result = new StreamManifestParser().Parse(Fixtures.Empty());

        Assert.False(result.IsRecognized);
        Assert.Empty(result.Items);
        Assert.NotNull(result.Diagnostics.Reason);
    }

    [Fact]
    public void Parses_signature_cipher_into_cipher_and_url()
    {
        const string json = """
        {
          "streamingData": {
            "adaptiveFormats": [
              {
                "itag": 140,
                "signatureCipher": "s=ABCDEF&sp=sig&url=https%3A%2F%2Fexample.com%2Fa.m4a",
                "mimeType": "audio/mp4; codecs=\"mp4a.40.2\""
              }
            ]
          }
        }
        """;

        var manifest = ParseManifest(json);
        var stream = Assert.Single(manifest.Streams);

        Assert.Equal("https://example.com/a.m4a", stream.Url);
        Assert.NotNull(stream.Cipher);
        Assert.Equal("ABCDEF", stream.Cipher!.Signature);
        Assert.Equal("sig", stream.Cipher.SignatureParameter);
    }
}

public sealed class VideoParserTests
{
    [Fact]
    public void Parses_video_details_and_caption_tracks()
    {
        var result = new VideoParser().Parse(new PlayerParseInput(
            Fixtures.Parse(Fixtures.PlayerWithAudioTracks),
            "dQw4w9WgXcQ"));

        Assert.True(result.IsRecognized);
        var video = Assert.Single(result.Items);

        Assert.Equal("Test video", video.Title);
        Assert.Equal("Test channel", video.ChannelTitle);
        Assert.Equal(TimeSpan.FromSeconds(100), video.Duration);
        Assert.Equal(42L, video.ViewCount);
        Assert.False(video.IsLive);
        Assert.True(video.PlayabilityStatus.IsOk);

        var captions = new CaptionTrackParser().Parse(Fixtures.Parse(Fixtures.PlayerWithAudioTracks));
        var track = Assert.Single(captions.Items);
        Assert.True(track.IsAutoGenerated);
        Assert.True(track.IsTranslatable);
    }

    [Fact]
    public void Reports_unrecognized_player_response()
    {
        var result = new VideoParser().Parse(new PlayerParseInput(Fixtures.Empty(), "abc"));

        Assert.False(result.IsRecognized);
        Assert.Contains("videoDetails", result.Diagnostics.Reason);
    }

    [Fact]
    public void Search_parser_rejects_unknown_structure()
    {
        var result = new SearchParser().Parse(new SearchParseInput(Fixtures.Empty(), SearchType.All));

        Assert.False(result.IsRecognized);
    }

    [Fact]
    public void Search_parser_parses_video_renderers_and_continuation()
    {
        const string json = """
        {
          "estimatedResults": "1000",
          "contents": {
            "twoColumnSearchResultsRenderer": {
              "primaryContents": {
                "sectionListRenderer": {
                  "contents": [
                    {
                      "itemSectionRenderer": {
                        "contents": [
                          {
                            "videoRenderer": {
                              "videoId": "dQw4w9WgXcQ",
                              "title": { "runs": [ { "text": "First" } ] },
                              "lengthText": { "simpleText": "3:32" },
                              "viewCountText": { "simpleText": "1.2M views" },
                              "ownerText": { "runs": [ { "text": "Chan", "navigationEndpoint": { "browseEndpoint": { "browseId": "UC1234567890123456789012" } } } ] }
                            }
                          }
                        ],
                        "continuations": [ { "nextContinuationData": { "continuation": "TOKEN" } } ]
                      }
                    }
                  ]
                }
              }
            }
          }
        }
        """;

        var result = new SearchParser().Parse(new SearchParseInput(Fixtures.Parse(json), SearchType.All));

        Assert.True(result.IsRecognized);
        var item = Assert.Single(result.Items);
        Assert.Equal(SearchResultKind.Video, item.Kind);
        Assert.Equal("dQw4w9WgXcQ", item.Video!.Id);
        Assert.Equal("First", item.Video.Title);
        Assert.Equal(TimeSpan.FromSeconds(212), item.Video.Duration!.Value);
        Assert.Equal(1_200_000L, item.Video.ViewCount);
        Assert.Equal("TOKEN", result.Continuation);
    }
}
