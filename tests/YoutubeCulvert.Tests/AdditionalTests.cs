using System.Net;
using YoutubeCulvert.Caching;
using YoutubeCulvert.Exceptions;
using YoutubeCulvert.Internal;
using YoutubeCulvert.Parsing;
using Xunit;

namespace YoutubeCulvert.Tests;

public sealed class CommentParserTests
{
    private const string CommentsJson = """
    {
      "frameworkUpdates": {
        "entityBatchUpdate": {
          "mutations": [
            {
              "payload": {
                "commentEntityPayload": {
                  "properties": {
                    "commentId": "c1",
                    "content": { "content": "hello world" },
                    "publishedTime": "1 day ago"
                  },
                  "author": {
                    "displayName": "Bob",
                    "channelId": "UC1234567890123456789012",
                    "isCreator": true
                  },
                  "toolbar": {
                    "likeCountNotliked": "5",
                    "replyCount": "2"
                  }
                }
              }
            }
          ]
        }
      },
      "contents": {
        "commentThreadRenderer": {
          "commentViewModel": { "commentViewModel": { "commentId": "c1" } },
          "replies": {
            "continuationItems": [
              {
                "continuationItemRenderer": {
                  "button": {
                    "buttonRenderer": {
                      "command": { "continuationCommand": { "token": "REPLY" } }
                    }
                  }
                }
              }
            ]
          }
        }
      }
    }
    """;

    [Fact]
    public void Parses_new_ui_comments_with_replies_continuation()
    {
        var result = new CommentParser().Parse(Fixtures.Parse(CommentsJson));

        Assert.True(result.IsRecognized);
        var comment = Assert.Single(result.Items);

        Assert.Equal("c1", comment.Id);
        Assert.Equal("Bob", comment.Author);
        Assert.Equal("hello world", comment.Text);
        Assert.Equal(5L, comment.LikeCount);
        Assert.Equal(2, comment.ReplyCount);
        Assert.Equal("1 day ago", comment.PublishedTime);
        Assert.True(comment.IsHearted);
        Assert.Equal("REPLY", comment.RepliesContinuation);
    }

    [Fact]
    public void Rejects_unknown_comment_structure()
    {
        var result = new CommentParser().Parse(Fixtures.Empty());

        Assert.False(result.IsRecognized);
    }
}

public sealed class TimedTextParserTests
{
    [Fact]
    public void Parses_timedtext_events()
    {
        const string json = """
        {
          "events": [
            { "tStartMs": 1000, "dDurationMs": 2000, "segs": [ { "utf8": "Hello " }, { "utf8": "world" } ] },
            { "tStartMs": 3000, "dDurationMs": 1000, "segs": [ { "utf8": "again" } ] }
          ]
        }
        """;

        var result = TimedTextParser.Parse(json);

        Assert.True(result.IsRecognized);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("Hello world", result.Items[0].Text);
        Assert.Equal(TimeSpan.FromSeconds(1), result.Items[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(2), result.Items[0].Duration);
        Assert.Equal(TimeSpan.FromSeconds(3), result.Items[0].End);
    }

    [Fact]
    public void Reports_invalid_timedtext()
    {
        var result = TimedTextParser.Parse("not json");

        Assert.False(result.IsRecognized);
        Assert.Empty(result.Items);
    }
}

public sealed class ErrorClassifierTests
{
    [Fact]
    public void Maps_429_to_rate_limited()
    {
        var exception = new YoutubeApiException(
            HttpStatusCode.TooManyRequests,
            "timedtext",
            "{}",
            "too many");

        Assert.True(ErrorClassifier.TryMap(exception, out var error));
        Assert.Equal(YoutubeErrorKind.RateLimited, error.Kind);
        Assert.Equal(429, error.StatusCode);
    }

    [Fact]
    public void Maps_401_and_403_to_authentication_required()
    {
        Assert.True(ErrorClassifier.TryMap(
            new YoutubeApiException(HttpStatusCode.Unauthorized, "player", null, "unauthorized"),
            out var unauthorized));

        Assert.Equal(YoutubeErrorKind.AuthenticationRequired, unauthorized.Kind);

        Assert.True(ErrorClassifier.TryMap(
            new YoutubeApiException(HttpStatusCode.Forbidden, "like/like", null, "forbidden"),
            out var forbidden));

        Assert.Equal(YoutubeErrorKind.AuthenticationRequired, forbidden.Kind);
    }

    [Fact]
    public void Maps_unavailable_video()
    {
        var exception = new VideoUnavailableException(
            "badbadbadba",
            new PlayabilityStatus { Status = "ERROR", Reason = "private" });

        Assert.True(ErrorClassifier.TryMap(exception, out var error));
        Assert.Equal(YoutubeErrorKind.Unavailable, error.Kind);
    }

    [Fact]
    public void Does_not_map_programming_errors()
    {
        Assert.False(ErrorClassifier.TryMap(new ArgumentNullException("x"), out _));
    }
}

public sealed class MemoryYoutubeCacheTests
{
    [Fact]
    public async Task Expires_entries_after_ttl()
    {
        var time = new ManualTimeProvider();
        var cache = new MemoryYoutubeCache(time);

        await cache.SetAsync("key", "value", TimeSpan.FromMinutes(1));
        Assert.Equal("value", await cache.GetAsync<string>("key"));

        time.Advance(TimeSpan.FromMinutes(2));
        Assert.Null(await cache.GetAsync<string>("key"));
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }
}
