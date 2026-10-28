using System.Net;
using System.Net.Sockets;
using System.Text;
using SrbMinerMonitor.Core;

namespace SrbMinerMonitor.Tests;

public sealed class ApiReaderTests
{
    [Fact]
    public async Task RestartWaitsForPreviousListenerToRelease()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var waiting = LocalApiPort.WaitUntilReleasedAsync(port, TimeSpan.FromSeconds(2));
        Assert.False(waiting.IsCompleted);
        listener.Stop();
        Assert.True(await waiting);
    }
    [Fact]
    public async Task ReleaseWaitDoesNotTakeOverAnotherListener()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Assert.False(await LocalApiPort.WaitUntilReleasedAsync(port, TimeSpan.FromMilliseconds(100)));
        Assert.Throws<InvalidOperationException>(() => LocalApiPort.EnsureAvailable(port));
    }
    [Fact]
    public void LocalPortCheckDistinguishesListenerFromUnusedPortWithoutConnecting()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Assert.Throws<InvalidOperationException>(() => LocalApiPort.EnsureAvailable(port));
        listener.Stop();
        LocalApiPort.EnsureAvailable(port);
    }
    [Fact]
    public async Task HttpReaderReturnsTheSnapshotAndRejectsRedirects()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var api = new ApiReader();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var reading = api.ReadAsync(port, timeout.Token);
        using var client = await listener.AcceptTcpClientAsync(timeout.Token);
        var stream = client.GetStream();
        await ReadHeaders(stream, timeout.Token);
        var json = "{\"miner_version\":\"3.8.0\",\"algorithms\":[{\"name\":\"test\",\"shares\":{\"accepted\":4,\"rejected\":0}}]}";
        await stream.WriteAsync(Encoding.UTF8.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {Encoding.UTF8.GetByteCount(json)}\r\nConnection: close\r\n\r\n{json}"), timeout.Token);
        var result = await reading;
        Assert.Equal(4, result.Snapshot.Shares.Accepted);

        var redirect = api.ReadAsync(port, timeout.Token);
        using var second = await listener.AcceptTcpClientAsync(timeout.Token);
        var secondStream = second.GetStream(); await ReadHeaders(secondStream, timeout.Token);
        await secondStream.WriteAsync(Encoding.UTF8.GetBytes("HTTP/1.1 302 Found\r\nLocation: http://example.com\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), timeout.Token);
        await Assert.ThrowsAsync<HttpRequestException>(async () => await redirect);
    }

    [Fact]
    public async Task TimeoutCoversResponseBodyAndDoesNotReturnInventedStatistics()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var api = new ApiReader();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var reading = api.ReadAsync(((IPEndPoint)listener.LocalEndpoint).Port, timeout.Token);
        using var client = await listener.AcceptTcpClientAsync(timeout.Token);
        var stream = client.GetStream();
        await ReadHeaders(stream, timeout.Token);
        await stream.WriteAsync(Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 10000\r\n\r\n{"), timeout.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reading);
        Assert.False(timeout.IsCancellationRequested); // The reader's own 3-second limit fired.
    }
    private static async Task ReadHeaders(NetworkStream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
        while (await reader.ReadLineAsync(ct) is { Length: > 0 }) { }
    }
}
