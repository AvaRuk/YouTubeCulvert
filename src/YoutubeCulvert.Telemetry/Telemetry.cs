using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging;
using YoutubeCulvert.Logging;

namespace YoutubeCulvert.Telemetry;

/// <summary>Options of the telemetry package.</summary>
public sealed class TelemetryOptions
{
    /// <summary>Emit an <see cref="Activity"/> for every InnerTube call.</summary>
    public bool EmitActivity { get; set; } = true;

    /// <summary>Fraction of calls that emit an activity (0..1).</summary>
    public double SampleRate { get; set; } = 1.0;

    internal bool ShouldSample() =>
        SampleRate >= 1.0 || (SampleRate > 0 && Random.Shared.NextDouble() < SampleRate);
}

/// <summary>Owns the <c>ActivitySource("YoutubeCulvert")</c>.</summary>
public sealed class YoutubeTelemetry : IDisposable
{
    public const string SourceName = "YoutubeCulvert";

    public YoutubeTelemetry()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
        Source = new ActivitySource(SourceName, version);
    }

    public ActivitySource Source { get; }

    public void Dispose() => Source.Dispose();
}

/// <summary>Adapts <see cref="IYoutubeLogger"/> to <see cref="ILogger"/>.</summary>
public sealed class LoggerAdapter : IYoutubeLogger
{
    private readonly ILogger _logger;

    public LoggerAdapter(ILoggerFactory factory) : this(factory.CreateLogger("YoutubeCulvert"))
    {
    }

    public LoggerAdapter(ILogger logger) => _logger = logger;

    public bool IsEnabled(YoutubeLogLevel level) => _logger.IsEnabled(Map(level));

    public void Log(
        YoutubeLogLevel level,
        string eventName,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? fields = null)
    {
        var logLevel = Map(level);

        if (!_logger.IsEnabled(logLevel))
            return;

        _logger.Log(
            logLevel,
            new EventId(0, eventName),
            new LogState(message, eventName, fields),
            exception,
            static (state, _) => state.Message);
    }

    private static Microsoft.Extensions.Logging.LogLevel Map(YoutubeLogLevel level) => level switch
    {
        YoutubeLogLevel.Trace => Microsoft.Extensions.Logging.LogLevel.Trace,
        YoutubeLogLevel.Debug => Microsoft.Extensions.Logging.LogLevel.Debug,
        YoutubeLogLevel.Information => Microsoft.Extensions.Logging.LogLevel.Information,
        YoutubeLogLevel.Warning => Microsoft.Extensions.Logging.LogLevel.Warning,
        _ => Microsoft.Extensions.Logging.LogLevel.Error
    };

    private sealed class LogState : IReadOnlyList<KeyValuePair<string, object?>>
    {
        private readonly IReadOnlyDictionary<string, object?>? _fields;

        public LogState(
            string message,
            string eventName,
            IReadOnlyDictionary<string, object?>? fields)
        {
            Message = message;
            EventName = eventName;
            _fields = fields;
        }

        public string Message { get; }

        public string EventName { get; }

        public int Count => 2 + (_fields?.Count ?? 0);

        public KeyValuePair<string, object?> this[int index]
        {
            get
            {
                if (index == 0)
                    return new KeyValuePair<string, object?>("Event", EventName);

                if (index == 1)
                    return new KeyValuePair<string, object?>("Message", Message);

                if (_fields is null)
                    throw new ArgumentOutOfRangeException(nameof(index));

                var position = index - 2;

                foreach (var pair in _fields)
                {
                    if (position-- == 0)
                        return pair;
                }

                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            yield return new KeyValuePair<string, object?>("Event", EventName);
            yield return new KeyValuePair<string, object?>("Message", Message);

            if (_fields is null)
                yield break;

            foreach (var pair in _fields)
                yield return pair;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => Message;
    }
}
