using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;

namespace Aethernet.Plugin.Services;

/// <summary>Adapter so Microsoft.Extensions.Logging writes through Dalamud's plugin log.</summary>
public sealed class DalamudLoggerProvider : ILoggerProvider
{
    private readonly IPluginLog _log;
    public DalamudLoggerProvider(IPluginLog log) { _log = log; }
    public ILogger CreateLogger(string categoryName) => new DalamudLogger(_log, categoryName);
    public void Dispose() {}

    private sealed class DalamudLogger : ILogger
    {
        private readonly IPluginLog _log; private readonly string _cat;
        public DalamudLogger(IPluginLog log, string cat) { _log = log; _cat = cat; }
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Debug;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> formatter)
        {
            var msg = $"[{_cat}] {formatter(state, ex)}";
            switch (level)
            {
                case LogLevel.Trace:        _log.Verbose(msg);                                  break;
                case LogLevel.Debug:        _log.Debug(msg);                                    break;
                case LogLevel.Information:  _log.Information(msg);                              break;
                case LogLevel.Warning:      _log.Warning(msg);                                  break;
                case LogLevel.Error:        if (ex is not null) _log.Error(ex, msg); else _log.Error(msg); break;
                case LogLevel.Critical:     if (ex is not null) _log.Fatal(ex, msg); else _log.Fatal(msg); break;
            }
        }
        private sealed class NullScope : IDisposable { public static readonly NullScope Instance = new(); public void Dispose() {} }
    }
}
