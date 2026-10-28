namespace SrbMinerMonitor.Core;

public sealed class MinerProcess : IMinerProcess, IDisposable
{
    private WindowsBatchProcess? _batch;
    private int? _activeApiPort;
    private string? _generatedBatch;
    public Guid OutputSession { get; private set; }
    public bool IsRunning => _batch?.IsRunning == true;
    public event Action<string>? Log;

    public async Task StartAsync(MinerSettings settings, CancellationToken ct)
    {
        if (settings.Validate() is string error) throw new ArgumentException(error);
        if (IsRunning) throw new InvalidOperationException("已有受管理的礦工程序。");
        LocalApiPort.EnsureAvailable(settings.ApiPort);
        _activeApiPort = settings.ApiPort;
        _batch?.Dispose(); _batch = null; DeleteGeneratedBatch();
        if (settings.UsePastedBatch)
        {
            _generatedBatch = await PastedBatch.CreateAsync(settings, ct);
            settings = settings with { UsePastedBatch = false, BatchFile = _generatedBatch };
        }
        OutputSession = Guid.NewGuid();
        var session = OutputSession;
        _batch = new WindowsBatchProcess();
        _batch.Log += message => { if (OutputSession == session) Log?.Invoke(message); };
        try { await _batch.StartAsync(settings, ct); }
        catch { _batch.Dispose(); _batch = null; DeleteGeneratedBatch(); throw; }
    }
    public async Task StopAsync(CancellationToken ct)
    {
        if (_batch is not null) await _batch.StopAsync(ct);
        if (_activeApiPort is int port) await LocalApiPort.WaitUntilReleasedAsync(port, TimeSpan.FromSeconds(3));
        DeleteGeneratedBatch();
    }
    private void DeleteGeneratedBatch()
    {
        if (_generatedBatch is null) return;
        try { File.Delete(_generatedBatch); _generatedBatch = null; }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    public void Dispose() { _batch?.Dispose(); DeleteGeneratedBatch(); }
}
