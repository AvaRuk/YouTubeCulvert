using Microsoft.Extensions.DependencyInjection;
using YoutubeCulvert.Configuration;
using YoutubeCulvert.InnerTube;
using Middlewares = YoutubeCulvert.InnerTube.Middleware;

namespace YoutubeCulvert.DependencyInjection;

/// <summary>A middleware registration with its insertion position.</summary>
public sealed record MiddlewareRegistration(Type Type, MiddlewarePosition Position);

/// <summary>Builder returned by <c>AddYoutubeCulvert</c>.</summary>
public interface IYoutubeCulvertBuilder
{
    IServiceCollection Services { get; }

    IReadOnlyList<MiddlewareRegistration> Middlewares { get; }

    IYoutubeCulvertBuilder AddMiddleware(Type middlewareType, MiddlewarePosition position);
}

/// <summary>Default builder implementation.</summary>
public sealed class YoutubeCulvertBuilder : IYoutubeCulvertBuilder
{
    private readonly List<MiddlewareRegistration> _middlewares =
    [
        new(typeof(Middlewares.LoggingMiddleware), MiddlewarePosition.Start),
        new(typeof(Middlewares.CacheMiddleware), MiddlewarePosition.BeforeCache),
        new(typeof(Middlewares.AuthSigningMiddleware), MiddlewarePosition.AfterCache),
        new(typeof(Middlewares.RetryMiddleware), MiddlewarePosition.End),
        new(typeof(Middlewares.RateLimitMiddleware), MiddlewarePosition.End),
        new(typeof(Middlewares.ErrorClassificationMiddleware), MiddlewarePosition.End)
    ];

    public YoutubeCulvertBuilder(IServiceCollection services) => Services = services;

    public IServiceCollection Services { get; }

    public IReadOnlyList<MiddlewareRegistration> Middlewares => _middlewares;

    public IYoutubeCulvertBuilder AddMiddleware(Type middlewareType, MiddlewarePosition position)
    {
        ArgumentNullException.ThrowIfNull(middlewareType);

        if (!typeof(IInnerTubeMiddleware).IsAssignableFrom(middlewareType))
        {
            throw new ArgumentException(
                $"{middlewareType} must implement {nameof(IInnerTubeMiddleware)}.",
                nameof(middlewareType));
        }

        var index = position switch
        {
            MiddlewarePosition.Start => 0,
            MiddlewarePosition.BeforeCache => IndexOf(typeof(Middlewares.CacheMiddleware)),
            MiddlewarePosition.AfterCache => IndexOf(typeof(Middlewares.CacheMiddleware)) + 1,
            MiddlewarePosition.BeforeAuth => IndexOf(typeof(Middlewares.AuthSigningMiddleware)),
            MiddlewarePosition.AfterAuth => IndexOf(typeof(Middlewares.AuthSigningMiddleware)) + 1,
            _ => _middlewares.Count
        };

        _middlewares.Insert(Math.Clamp(index, 0, _middlewares.Count), new MiddlewareRegistration(middlewareType, position));
        return this;
    }

    private int IndexOf(Type type)
    {
        for (var index = 0; index < _middlewares.Count; index++)
        {
            if (_middlewares[index].Type == type)
                return index;
        }

        return _middlewares.Count;
    }
}

/// <summary>Options accepted by <c>AddYoutubeCache</c>.</summary>
public sealed class YoutubeCacheOptions
{
    public CacheTtls Ttls { get; set; } = new();
}
