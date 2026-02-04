using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Agents.AI.Core;

/// <summary>
/// Minimal adapter to allow passing an <see cref="ILogger"/> where an <see cref="ILogger{TCategoryName}"/> is required.
/// </summary>
internal sealed class LoggerAdapter<T> : ILogger<T>
{
    private readonly ILogger _inner;

    public LoggerAdapter(ILogger inner) => _inner = inner ?? NullLogger.Instance;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        => _inner.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
        => _inner.Log(logLevel, eventId, state, exception, formatter);
}

