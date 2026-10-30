using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace SrbMinerMonitor.Core;

// One entry per actual accepted stdout line. API counters never synthesize a share record.
public sealed record MiningLogEntry(
    DateTimeOffset Timestamp, Guid Session, string ShareText, DateTimeOffset? ShareTimestamp,
    double? HashrateHps = null, DateTimeOffset? HashrateTimestamp = null,
    double? PoolDifficulty = null, DateTimeOffset? PoolDifficultyTimestamp = null);

public sealed class MiningLogWriter : IAsyncDisposable
{
    private sealed record Work(MiningLogEntry? Entry, string? Directory = null, TaskCompletionSource? Barrier = null);
    private readonly Channel<Work> _queue = Channel.CreateBounded<Work>(new BoundedChannelOptions(2048)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly Task _worker;
    private readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private string? _error;
    private int _completed;
    private string _directory;
    public string DirectoryPath => Volatile.Read(ref _directory);
    public void SetDirectory(string directory)
    {
        Volatile.Write(ref _directory, Path.GetFullPath(directory));
        Volatile.Write(ref _error, null);
    }
    public string? Error => Volatile.Read(ref _error);
    public MiningLogWriter(string directory)
    {
        _directory = Path.GetFullPath(directory);
        _worker = Task.Run(Run);
    }
    public bool TryWrite(MiningLogEntry entry)
    {
        if (Volatile.Read(ref _completed) != 0) return false;
        if (_queue.Writer.TryWrite(new(entry, DirectoryPath))) return true;
        Volatile.Write(ref _error, "寫入佇列已滿，部分紀錄未保存。");
        return false;
    }
    public async Task FlushAsync()
    {
        if (Volatile.Read(ref _completed) != 0) { await _worker.ConfigureAwait(false); return; }
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try { await _queue.Writer.WriteAsync(new(null, Barrier: barrier)).ConfigureAwait(false); }
        catch (ChannelClosedException) { await _worker.ConfigureAwait(false); return; }
        await barrier.Task.ConfigureAwait(false);
    }
    private async Task Run()
    {
        await foreach (var work in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (work.Barrier is { } barrier) { barrier.TrySetResult(); continue; }
            try
            {
                var entry = work.Entry!;
                Directory.CreateDirectory(work.Directory!);
                var day = entry.Timestamp.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var path = Path.Combine(work.Directory!, $"mining-{day}-{Environment.ProcessId}.log");
                var line = JsonSerializer.Serialize(entry, _json) + Environment.NewLine;
                await File.AppendAllTextAsync(path, line, new UTF8Encoding(false)).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
            { Volatile.Write(ref _error, "無法完整保存挖礦紀錄：" + e.GetType().Name); }
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0) _queue.Writer.TryComplete();
        await _worker.ConfigureAwait(false);
    }
}
