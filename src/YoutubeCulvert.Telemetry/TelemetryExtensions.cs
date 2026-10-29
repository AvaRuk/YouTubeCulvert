using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using YoutubeCulvert.DependencyInjection;
using YoutubeCulvert.InnerTube;
using YoutubeCulvert.Logging;
using YoutubeCulvert.Telemetry;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the telemetry adapter and its middleware.</summary>
public static class YoutubeCulvertTelemetryExtensions
{
    /// <summary>
    /// Adds <see cref="LoggerAdapter"/> as the <see cref="IYoutubeLogger"/> and
    /// puts <see cref="TelemetryMiddleware"/> at the start of the pipeline.
    /// </summary>
    public static IYoutubeCulvertBuilder AddYoutubeTelemetry(
        this IYoutubeCulvertBuilder builder,
        Action<TelemetryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new TelemetryOptions();
        configure?.Invoke(options);

        builder.Services.AddSingleton(options);
        builder.Services.TryAddSingleton<YoutubeTelemetry>();
        builder.Services.TryAddSingleton<TelemetryMiddleware>();

        builder.Services.AddSingleton<IYoutubeLogger>(sp => new LoggerAdapter(
            sp.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance));

        builder.AddMiddleware(typeof(TelemetryMiddleware), MiddlewarePosition.Start);

        return builder;
    }
}
