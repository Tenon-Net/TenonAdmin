using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>数据库初始化日志不得暴露连接串；同时保留排障所需的数据库类型。</summary>
public class DatabaseInitializerLoggingTests
{
    [Fact]
    public async Task Ready_log_contains_db_type_without_connection_string()
    {
        const string secretMarker = "TENON_TEST_CONNECTION_SECRET_9F3A";
        var tempDir = Path.Combine(Path.GetTempPath(), $"tenon-db-log-{secretMarker}-{Guid.NewGuid():N}");
        var connectionString = $"Data Source={Path.Combine(tempDir, "admin.db")}";
        var logs = new StructuredLogSink();

        using var host = new HostBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(logs);
            })
            .ConfigureServices(services => services.AddTenonAdminSqlSugar(
                new AdminDatabaseOptions
                {
                    DbType = "Sqlite",
                    ConnectionString = connectionString,
                    EnableCodeFirst = true,
                    EnableSeed = false,
                }))
            .Build();

        try
        {
            await host.StartAsync();

            var ready = Assert.Single(logs.Entries, entry =>
                entry.Formatted.StartsWith("TenonAdmin 数据库就绪:", StringComparison.Ordinal));

            Assert.Equal(LogLevel.Information, ready.Level);
            Assert.Equal("Sqlite", ready.State["DbType"]);
            Assert.DoesNotContain("Conn", ready.State.Keys);
            Assert.DoesNotContain(logs.Entries, entry =>
                entry.Formatted.Contains(secretMarker, StringComparison.Ordinal)
                || entry.State.Values.Any(value =>
                    value?.ToString()?.Contains(secretMarker, StringComparison.Ordinal) == true));
        }
        finally
        {
            await host.StopAsync();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Formatted,
        IReadOnlyDictionary<string, object?> State);

    /// <summary>同时捕获 ILogger 的格式化文本和结构化状态，防止敏感值藏在任一输出面。</summary>
    private sealed class StructuredLogSink : ILoggerProvider
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Capture(this);

        public void Dispose() { }

        private sealed class Capture(StructuredLogSink owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var structured = state is IEnumerable<KeyValuePair<string, object?>> values
                    ? values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                    : new Dictionary<string, object?>(StringComparer.Ordinal);
                owner.Entries.Enqueue(new LogEntry(logLevel, formatter(state, exception), structured));
            }
        }
    }
}
