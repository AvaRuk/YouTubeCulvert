using System.Diagnostics;
using System.Text.Json;
using YoutubeCulvert.Exceptions;
using YoutubeCulvert.InnerTube;

namespace YoutubeCulvert.Telemetry;

/// <summary>Emits an <see cref="Activity"/> around every InnerTube call.</summary>
public sealed class TelemetryMiddleware : IInnerTubeMiddleware
{
    private readonly YoutubeTelemetry _telemetry;
    private readonly TelemetryOptions _options;

    public TelemetryMiddleware(YoutubeTelemetry telemetry, TelemetryOptions options)
    {
        _telemetry = telemetry;
        _options = options;
    }

    public async Task<JsonElement> InvokeAsync(
        InnerTubeRequest request,
        InnerTubeHandler next,
        CancellationToken ct)
    {
        if (!_options.EmitActivity || !_options.ShouldSample())
            return await next(request, ct).ConfigureAwait(false);

        using var activity = _telemetry.Source.StartActivity(
            $"innertube.{request.Endpoint}",
            ActivityKind.Client);

        activity?.SetTag("youtube.endpoint", request.Endpoint);
        activity?.SetTag("youtube.profile", request.Profile?.ClientName);
        activity?.SetTag("youtube.mutation", request.IsMutation);

        var started = Stopwatch.GetTimestamp();

        try
        {
            var result = await next(request, ct).ConfigureAwait(false);

            activity?.SetTag("youtube.status", "ok");
            activity?.SetTag(
                "youtube.elapsed_ms",
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);

            activity?.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch (Exception exception)
        {
            activity?.SetTag(
                "youtube.status",
                (exception as YoutubeApiException)?.StatusCodeValue.ToString() ?? "error");

            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            throw;
        }
    }
}
