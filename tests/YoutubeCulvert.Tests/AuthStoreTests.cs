using YoutubeCulvert.Authentication;
using Xunit;

namespace YoutubeCulvert.Tests;

public sealed class FileAuthStoreTests
{
    [Fact]
    public async Task Round_trips_state_without_persisting_raw_json()
    {
        var path = Path.Combine(Path.GetTempPath(), $"youtubeculvert-{Guid.NewGuid():N}.json");
        var store = new FileAuthStore(path);

        try
        {
            var state = new AuthState
            {
                AccessToken = "token",
                RefreshToken = "refresh",
                Sapisid = "sapisid",
                VisitorData = "visitor",
                Cookies =
                [
                    new AuthCookie { Name = "SID", Value = "value", Domain = ".youtube.com" }
                ],
                Account = new Account
                {
                    Name = "Test",
                    ChannelId = "UC1234567890123456789012",
                    Handle = "@test",
                    Raw = Fixtures.Parse("""{"activeAccountHeaderRenderer":{"title":"Test"}}""")
                }
            };

            await store.SaveAsync(state);

            var raw = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("activeAccountHeaderRenderer", raw);

            var loaded = await store.LoadAsync();

            Assert.NotNull(loaded);
            Assert.Equal("token", loaded!.AccessToken);
            Assert.Equal("sapisid", loaded.Sapisid);
            Assert.Equal("visitor", loaded.VisitorData);
            Assert.Equal("Test", loaded.Account?.Name);
            Assert.Equal("UC1234567890123456789012", loaded.Account?.ChannelId);
            Assert.Single(loaded.Cookies);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task Clear_removes_the_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"youtubeculvert-{Guid.NewGuid():N}.json");
        var store = new FileAuthStore(path);

        await store.SaveAsync(new AuthState { AccessToken = "token" });
        Assert.True(File.Exists(path));

        await store.ClearAsync();
        Assert.False(File.Exists(path));
        Assert.Null(await store.LoadAsync());
    }
}
