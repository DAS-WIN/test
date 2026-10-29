using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;

// Test transport/probe fixture only. This program performs no hashing and connects to no pool.
if (args.Any(a => a.StartsWith("--query-gpu=", StringComparison.Ordinal)))
{
    var counterPath = Path.Combine(AppContext.BaseDirectory, "fixture-sensor-counter.txt");
    var sensorCount = File.Exists(counterPath) ? int.Parse(File.ReadAllText(counterPath)) + 1 : 1;
    File.WriteAllText(counterPath, sensorCount.ToString());
    Console.WriteLine($"00000000:01:00.0, Integration Fixture GPU, {100 + sensorCount}, {40 + sensorCount}, {50 + sensorCount}, {2000 + sensorCount}, {9000 + sensorCount}");
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
var accepted = 4;
Console.WriteLine($"FIXTURE_OUTPUT_REDIRECTED={Console.IsOutputRedirected}");
Console.WriteLine($"FIXTURE_CONSOLE_VISIBLE={Native.IsWindowVisible(Native.GetConsoleWindow())}");
Console.WriteLine($"FIXTURE_MINER_PID={Environment.ProcessId}");
for (var i = 0; i < 4; i++) Console.WriteLine("\u001b[32mGPU0[t0] share accepted [ 100ms ][testhash][0]\u001b[0m");
Console.Error.WriteLine("GPU0[t0] share rejected [ 10ms ][testhash][0]");
Console.WriteLine("ID Name Hashrate Power Eff Fan Temp CClk MClk A R HW");
Console.WriteLine("#0 Integration Fixture GPU 1.234 MH/s 100W 0.012 50% 60C 2000 9000 4 1 0");
var outputTask = Task.Run(async () =>
{
    try
    {
        while (!lifetime.IsCancellationRequested)
        {
            await Task.Delay(100, lifetime.Token);
            foreach (var kind in new[] { "accepted", "rejected", "timeout", "api-accepted", "flush-accepted" })
            {
                var trigger = Path.Combine(Environment.CurrentDirectory, "fixture-" + kind);
                if (!File.Exists(trigger)) continue;
                File.Delete(trigger);
                if (kind == "api-accepted") { Interlocked.Increment(ref accepted); continue; }
                if (kind == "flush-accepted") { Console.WriteLine("GPU0[t0] share accepted [777ms] [testhash][0] DELAYED_FIXTURE"); continue; }
                if (kind == "accepted") Interlocked.Increment(ref accepted);
                Console.WriteLine("GPU0[t0] share " + kind + " [ 100ms ][testhash][0]");
            }
        }
    }
    catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
});
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
            algorithms = new[] { new { name = "testhash", hashrate = new { now = 1234000 + (int)(DateTimeOffset.UtcNow - started).TotalSeconds * 10000, gpu = new { gpu0 = 1234000 + (int)(DateTimeOffset.UtcNow - started).TotalSeconds * 10000 } }, shares = new { accepted = args.Contains("--fixture-no-share-api") ? (int?)null : Volatile.Read(ref accepted), rejected = args.Contains("--fixture-no-share-api") ? (int?)null : 1 } } }
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
