using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core.Tests.TestKit;

internal sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

internal sealed class CollectingLogger : ILogger
{
    public ConcurrentQueue<LogEntry> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Enqueue(new LogEntry(logLevel, formatter(state, exception), exception));
    }
}

