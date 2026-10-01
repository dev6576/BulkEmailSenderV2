using System.Globalization;

namespace BulkEmailSender.Api.Logging;

/// <summary>Persists browser-side application failures in a separate local log.</summary>
public sealed class FrontendFileLogStore : IDisposable
{
    private readonly object _writeLock = new();
    private readonly StreamWriter _writer;

    public FrontendFileLogStore(string directory)
    {
        Directory.CreateDirectory(directory);
        LogFilePath = Path.Combine(directory, "bulk-email-sender-frontend.log");
        _writer = new StreamWriter(new FileStream(LogFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };
    }

    public string LogFilePath { get; }

    public void Write(string level, string category, string message)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
        var safeCategory = category.Replace("\r", " ").Replace("\n", " | ");
        var safeMessage = message.Replace("\r", " ").Replace("\n", " | ");
        lock (_writeLock)
            _writer.WriteLine($"{timestamp} [{level.ToUpperInvariant()}] [{safeCategory}] {safeMessage}");
    }

    public void Dispose() => _writer.Dispose();
}
