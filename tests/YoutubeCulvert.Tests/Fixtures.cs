using System.Text.Json;

namespace YoutubeCulvert.Tests;

internal static class Fixtures
{
    public static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static JsonElement Empty() => Parse("{}");

    public const string PlayerWithAudioTracks = """
    {
      "videoDetails": {
        "videoId": "dQw4w9WgXcQ",
        "title": "Test video",
        "author": "Test channel",
        "channelId": "UC1234567890123456789012",
        "lengthSeconds": "100",
        "viewCount": "42",
        "keywords": ["a", "b"],
        "thumbnail": { "thumbnails": [ { "url": "https://i.ytimg.com/vi/dQw4w9WgXcQ/hq.jpg", "width": 480, "height": 360 } ] }
      },
      "playabilityStatus": { "status": "OK" },
      "streamingData": {
        "expiresInSeconds": "3600",
        "formats": [
          {
            "itag": 18,
            "url": "https://example.com/muxed.mp4",
            "mimeType": "video/mp4; codecs=\"avc1.42001E, mp4a.40.2\"",
            "qualityLabel": "360p",
            "width": 640,
            "height": 360,
            "contentLength": "1000"
          }
        ],
        "adaptiveFormats": [
          {
            "itag": 140,
            "url": "https://example.com/en-low.m4a",
            "mimeType": "audio/mp4; codecs=\"mp4a.40.2\"",
            "bitrate": 128000,
            "contentLength": "2000",
            "approxDurationMs": "100000",
            "audioTrack": { "audioTrackId": "en-US.4", "displayName": "English (US)", "audioIsDefault": true }
          },
          {
            "itag": 251,
            "url": "https://example.com/en-high.webm",
            "mimeType": "audio/webm; codecs=\"opus\"",
            "bitrate": 160000,
            "contentLength": "3000",
            "audioTrack": { "audioTrackId": "en-US.4", "displayName": "English (US)", "audioIsDefault": true }
          },
          {
            "itag": 140,
            "url": "https://example.com/ru.m4a",
            "mimeType": "audio/mp4; codecs=\"mp4a.40.2\"",
            "bitrate": 128000,
            "contentLength": "2100",
            "audioTrack": { "audioTrackId": "ru-RU.3", "displayName": "Russian", "audioIsDefault": false }
          },
          {
            "itag": 137,
            "url": "https://example.com/video.mp4",
            "mimeType": "video/mp4; codecs=\"avc1.640028\"",
            "qualityLabel": "1080p",
            "width": 1920,
            "height": 1080,
            "fps": 30,
            "bitrate": 4000000,
            "contentLength": "900000",
            "initRange": { "start": "0", "end": "100" },
            "indexRange": { "start": "101", "end": "200" }
          }
        ]
      },
      "captions": {
        "playerCaptionsTracklistRenderer": {
          "captionTracks": [
            { "languageCode": "en", "kind": "asr", "name": { "simpleText": "English (auto)" }, "isTranslatable": true, "baseUrl": "https://example.com/timedtext?v=abc&lang=en" }
          ],
          "translationLanguages": [ { "languageCode": "ru" } ]
        }
      }
    }
    """;
}
