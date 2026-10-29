using Microsoft.Extensions.DependencyInjection;
using YoutubeCulvert.Datatypes;
using YoutubeCulvert.Exceptions;
using YoutubeCulvert.Testing;
using Xunit;

namespace YoutubeCulvert.Tests;

public sealed class AuthFlowTests
{
    private const string AccountMenuJson = """
    {
      "actions": [
        {
          "openPopupAction": {
            "popup": {
              "popupRenderer": {
                "activeAccountHeaderRenderer": {
                  "accountName": { "simpleText": "Alice" },
                  "channelHandle": "@alice",
                  "email": { "simpleText": "alice@example.com" },
                  "accountPhoto": {
                    "thumbnails": [
                      { "url": "https://example.com/avatar.jpg", "width": 88, "height": 88 }
                    ]
                  },
                  "navigationEndpoint": {
                    "browseEndpoint": { "browseId": "UC1234567890123456789012" }
                  }
                }
              }
            }
          }
        }
      ]
    }
    """;

    private static YoutubeClient CreateClient(FakeHttpHandler fake, InMemoryAuthStore store) =>
        new(new YoutubeClientOptions
        {
            HttpMessageHandler = fake,
            InitializeSessionOnFirstRequest = false,
            AuthStore = store
        });

    [Fact]
    public async Task Cookie_sign_in_loads_account_and_persists_state()
    {
        var fake = new FakeHttpHandler().Map("account/account_menu", AccountMenuJson);
        var store = new InMemoryAuthStore();

        await using var youtube = CreateClient(fake, store);

        var account = await youtube.Auth.SignInWithCookieHeaderAsync(
            "SAPISID=sapisid-value; SID=sid-value; HSID=hsid-value; LOGIN_INFO=login-value");

        Assert.Equal("Alice", account.Name);
        Assert.Equal("@alice", account.Handle);
        Assert.Equal("UC1234567890123456789012", account.ChannelId);
        Assert.True(youtube.Auth.IsAuthenticated);
        Assert.True(youtube.Auth.IsCookieSession);
        Assert.Equal("Alice", youtube.Auth.CurrentAccount?.Name);

        var persisted = await store.LoadAsync();
        Assert.NotNull(persisted);
        Assert.Equal("sapisid-value", persisted!.Sapisid);
        Assert.NotEmpty(persisted.Cookies);
        Assert.Equal("Alice", persisted.Account?.Name);

        Assert.Equal(1, fake.Count("account/account_menu"));
    }

    [Fact]
    public async Task Sign_out_clears_the_store_and_session()
    {
        var fake = new FakeHttpHandler().Map("account/account_menu", AccountMenuJson);
        var store = new InMemoryAuthStore();

        await using var youtube = CreateClient(fake, store);
        await youtube.Auth.SignInWithCookieHeaderAsync("SAPISID=sapisid-value; SID=sid-value");

        await youtube.Auth.SignOutAsync();

        Assert.False(youtube.Auth.IsAuthenticated);
        Assert.Null(youtube.Auth.CurrentAccount);
        Assert.Null(await store.LoadAsync());
    }

    [Fact]
    public async Task Account_actions_require_a_session()
    {
        var fake = new FakeHttpHandler();
        var store = new InMemoryAuthStore();

        await using var youtube = CreateClient(fake, store);

        await Assert.ThrowsAsync<YoutubeAuthenticationRequiredException>(
            () => youtube.Interactions.LikeAsync("dQw4w9WgXcQ", LikeState.Like));
    }

    [Fact]
    public async Task Account_actions_are_marked_as_mutations()
    {
        var fake = new FakeHttpHandler()
            .Map("account/account_menu", AccountMenuJson)
            .Map("like/like", """{"actions":[]}""");

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
        await youtube.Auth.SignInWithCookieHeaderAsync("SAPISID=sapisid-value; SID=sid-value");

        var result = await youtube.Interactions.LikeAsync("dQw4w9WgXcQ", LikeState.Like);

        Assert.True(result.Success);
        Assert.True(result.Raw.HasValue);

        var recording = scope.ServiceProvider.GetRequiredService<RecordingMiddleware>();
        var mutation = Assert.Single(recording.Requests, request => request.Endpoint == "like/like");
        Assert.True(mutation.IsMutation);
        Assert.Equal("dQw4w9WgXcQ", mutation.Payload["target"]!["videoId"]!.GetValue<string>());
    }
}
