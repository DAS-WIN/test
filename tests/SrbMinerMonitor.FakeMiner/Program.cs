using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

// Test transport/probe fixture only. This program performs no hashing and connects to no pool.
var probeMode = Environment.GetEnvironmentVariable("SRBM_FAKE_PROBE_MODE");
if (args.Contains("--list-algorithms") || args.Contains("--list-devices"))
{
    if (probeMode == "empty") return 0;
    if (probeMode == "fail") { Console.Error.WriteLine("fixture: driver unavailable"); return 7; }
    if (probeMode == "slow") { Console.WriteLine("fixture: partial output before timeout"); await Task.Delay(TimeSpan.FromSeconds(30)); return 0; }
    if (args.Contains("--list-algorithms")) Console.WriteLine("Supported algorithms:\n0: pearlhash  0.85%\n1: kawpow  1.00%\n2: pearlhash  0.85%");
    else Console.WriteLine("GPU0: [BUS_ID 1] NVIDIA Integration Fixture\nGPU2: AMD Integration Fixture");
    return 0;
}
if (!args.Contains("--api-enable")) { Console.Error.WriteLine("Integration fixture requires --api-enable; no mining is performed."); return 2; }
var index = Array.IndexOf(args, "--api-port");
var port = index >= 0 && index + 1 < args.Length ? int.Parse(args[index + 1]) : 21550;
using var lifetime = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; lifetime.Cancel(); };
using var listener = new TcpListener(IPAddress.Loopback, port);
listener.Start();
Console.WriteLine("Integration fixture API ready. No mining is performed.");
var started = DateTimeOffset.UtcNow;
long accepted = 0;
try
{
    while (!lifetime.IsCancellationRequested)
    {
        using var client = await listener.AcceptTcpClientAsync(lifetime.Token);
        var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
        if (await reader.ReadLineAsync(lifetime.Token) is not { Length: > 0 }) continue;
        while (await reader.ReadLineAsync(lifetime.Token) is { Length: > 0 }) { }
        var json = JsonSerializer.Serialize(new
        {
            miner_version = "integration-fixture", mining_time = (DateTimeOffset.UtcNow - started).TotalSeconds,
            gpu_devices = new[] { new { device_id = 0, bus_id = 1, name = "Integration Fixture GPU" } },
            algorithms = new[] { new { name = "pearlhash", hashrate = new { now = 1234000, gpu = new { gpu0 = 1234000 } }, shares = new { accepted = ++accepted, rejected = 0 } } }
        });
        var bytes = Encoding.UTF8.GetBytes(json);
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n"), lifetime.Token);
        await stream.WriteAsync(bytes, lifetime.Token);
    }
}
catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
return 0;
