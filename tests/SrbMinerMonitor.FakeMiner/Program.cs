using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;

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
Console.WriteLine($"FIXTURE_OUTPUT_REDIRECTED={Console.IsOutputRedirected}");
Console.WriteLine($"FIXTURE_CONSOLE_VISIBLE={Native.IsWindowVisible(Native.GetConsoleWindow())}");
Console.WriteLine($"FIXTURE_MINER_PID={Environment.ProcessId}");
for (var i = 0; i < 4; i++) Console.WriteLine("\u001b[32mGPU0[t0] share accepted [ 213ms ][pearlhash][0]\u001b[0m");
Console.Error.WriteLine("GPU0[t0] share rejected [ 10ms ][pearlhash][0]");
Console.WriteLine("ID Name Hashrate Power Eff Fan Temp CClk MClk A R HW");
Console.WriteLine("#0 Integration Fixture GPU 1.234 MH/s 100W 0.012 50% 60C 2000 9000 4 1 0");
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
            algorithms = new[] { new { name = "pearlhash", hashrate = new { now = 1234000, gpu = new { gpu0 = 1234000 } }, shares = new { accepted = args.Contains("--fixture-no-share-api") ? (int?)null : 4, rejected = args.Contains("--fixture-no-share-api") ? (int?)null : 1 } } }
        });
        var bytes = Encoding.UTF8.GetBytes(json);
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n"), lifetime.Token);
        await stream.WriteAsync(bytes, lifetime.Token);
    }
}
catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
return 0;

internal static class Native
{
    [DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool IsWindowVisible(IntPtr window);
}
