// SPDX-License-Identifier: AGPL-3.0-only
using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Askwerk.Tests.Api;

/// <summary>Captures every log record with its structured state and the trace id it was written under.</summary>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<LogRecord> Records { get; } = new();

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, Records);

    public void Dispose()
    {
    }

    internal sealed record LogRecord(string Category, LogLevel Level, string Message, IReadOnlyDictionary<string, object?> State, string? TraceId);

    private sealed class RecordingLogger(string category, ConcurrentQueue<LogRecord> records) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state as IEnumerable<KeyValuePair<string, object?>> ?? [];
            var stateDictionary = values.ToDictionary(pair => pair.Key, pair => pair.Value);
            var traceId = Activity.Current?.TraceId.ToString();

            records.Enqueue(new LogRecord(category, logLevel, formatter(state, exception), stateDictionary, traceId));
        }
    }
}
