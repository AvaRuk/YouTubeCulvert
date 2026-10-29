using System.Text.Json;
using YoutubeCulvert.Authentication;
using YoutubeCulvert.Caching;
using YoutubeCulvert.Datatypes;
using YoutubeCulvert.InnerTube;

namespace YoutubeCulvert.Testing;

/// <summary>Session-only <see cref="IAuthStore"/> for tests.</summary>
public sealed class InMemoryAuthStore : IAuthStore
{
    private readonly object _gate = new();
    private AuthState? _state;

    public AuthState? Current
    {
        get
        {
            lock (_gate)
                return _state;
        }
    }

    public Task<AuthState?> LoadAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
            return Task.FromResult(_state);
    }

    public Task SaveAsync(AuthState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ct.ThrowIfCancellationRequested();

        lock (_gate)
            _state = state;

        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
            _state = null;

        return Task.CompletedTask;
    }
}

/// <summary>Captures every <see cref="InnerTubeRequest"/> that reaches the pipeline.</summary>
public sealed class RecordingMiddleware : IInnerTubeMiddleware
{
    private readonly object _gate = new();
    private readonly List<InnerTubeRequest> _requests = [];

    /// <summary>When set, replaces the rest of the pipeline.</summary>
    public Func<InnerTubeRequest, Task<JsonElement>>? ResponseFactory { get; set; }

    public IReadOnlyList<InnerTubeRequest> Requests
    {
        get
        {
            lock (_gate)
                return _requests.ToArray();
        }
    }

    public async Task<JsonElement> InvokeAsync(
        InnerTubeRequest request,
        InnerTubeHandler next,
        CancellationToken ct)
    {
        lock (_gate)
            _requests.Add(request);

        return ResponseFactory is { } factory
            ? await factory(request).ConfigureAwait(false)
            : await next(request, ct).ConfigureAwait(false);
    }
}

/// <summary>In-memory <see cref="IYoutubeCache"/> that ignores TTLs.</summary>
public sealed class FakeYoutubeCache : IYoutubeCache
{
    private readonly Dictionary<string, object?> _entries = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(
                _entries.TryGetValue(key, out var value) && value is T typed
                    ? typed
                    : default);
        }
    }

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
    {
        lock (_gate)
            _entries[key] = value;

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        lock (_gate)
            _entries.Remove(key);

        return Task.CompletedTask;
    }
}
