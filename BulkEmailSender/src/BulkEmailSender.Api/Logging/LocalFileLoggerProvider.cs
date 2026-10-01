using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace BulkEmailSender.Api.Logging;

/// <summary>Writes structured, timestamped application logs beside the SQLite database.</summary>
public sealed class LocalFileLoggerProvider : ILoggerProvider
{
    private readonly object _writeLock = new();
    private readonly StreamWriter _writer;
    private bool _disposed;

    public LocalFileLoggerProvider(string connectionString)
    {
        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        if (string.IsNullOrWhiteSpace(dataSource) || dataSource == ":memory:")
            throw new InvalidOperationException("A file-backed SQLite database is required to locate the application log file.");

        var databasePath = Path.GetFullPath(dataSource);
        var directory = Path.GetDirectoryName(databasePath)!;
        Directory.CreateDirectory(directory);
        LogFilePath = Path.Combine(directory, "bulk-email-sender.log");
        _writer = new StreamWriter(new FileStream(LogFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };
    }

    public string LogFilePath { get; }
    public string LogDirectory => Path.GetDirectoryName(LogFilePath)!;

    public ILogger CreateLogger(string categoryName) => new LocalFileLogger(this, categoryName);

    internal void Write<TState>(string category, LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (_disposed) return;
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
        var message = formatter(state, exception);
        var line = $"{timestamp} [{level.ToString().ToUpperInvariant()}] [{category}] EventId={eventId.Id} {message}";
        if (exception is not null) line += Environment.NewLine + exception;

        lock (_writeLock)
        {
            if (!_disposed)
                _writer.WriteLine(line);
        }
    }

    public void Dispose()
    {
        lock (_writeLock)
        {
            if (_disposed) return;
            _disposed = true;
            _writer.Dispose();
        }
    }

    private sealed class LocalFileLogger(LocalFileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) owner.Write(category, logLevel, eventId, state, exception, formatter);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
