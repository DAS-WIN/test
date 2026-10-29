namespace SrbMinerMonitor.Core;

public sealed class MinerProcess : IMinerProcess, IDisposable
{
    private readonly MinerAdapterRegistry _registry;
    public MinerProcess(MinerAdapterRegistry? registry = null) => _registry = registry ?? MinerAdapterRegistry.Default;
    private WindowsBatchProcess? _batch;
    private int? _activeApiPort;
    private string? _generatedBatch;
    public Guid OutputSession { get; private set; }
    public bool IsRunning => _batch?.IsRunning == true;
    public event Action<string>? Log;

    public async Task StartAsync(MinerSettings settings, CancellationToken ct)
    {
        var plan = _registry.Get(settings.MinerId).PrepareLaunch(settings);
        if (IsRunning) throw new InvalidOperationException("已有受管理的礦工程序。");
        if (plan.ApiPort is int port) LocalApiPort.EnsureAvailable(port);
        _activeApiPort = plan.ApiPort;
        _batch?.Dispose(); _batch = null; DeleteGeneratedBatch();
        if (plan.BatchBytes is not null)
        {
            _generatedBatch = Path.Combine(plan.WorkingDirectory, "Miner-Monitor-" + Guid.NewGuid().ToString("N") + ".bat");
            var created = false;
            try
            {
                await using var stream = new FileStream(_generatedBatch, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true);
                created = true;
                await stream.WriteAsync(plan.BatchBytes, ct);
            }
            catch { if (created) DeleteGeneratedBatch(); else _generatedBatch = null; throw; }
        }
        settings = settings with { UsePastedBatch = false, BatchFile = _generatedBatch ?? plan.BatchFile, BatchDirectory = plan.WorkingDirectory };
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
