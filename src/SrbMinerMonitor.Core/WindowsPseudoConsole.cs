using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SrbMinerMonitor.Core;

// The child sees real console handles while the GUI receives the terminal's UTF-8 stream.
internal sealed class WindowsPseudoConsole : IDisposable
{
    private IntPtr _handle;
    private readonly SafeFileHandle _inputWrite;
    public IntPtr Handle => _handle;
    public StreamReader Output { get; }
    public WindowsPseudoConsole()
    {
        if (!CreatePipe(out var inputRead, out var inputWrite, IntPtr.Zero, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        _inputWrite = inputWrite;
        using (inputRead)
        {
            try
            {
                if (!CreatePipe(out var outputRead, out var outputWrite, IntPtr.Zero, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
                using (outputWrite)
                {
                    var hr = CreatePseudoConsole(new Coord { X = 300, Y = 100 }, inputRead, outputWrite, 0, out _handle);
                    if (hr < 0) { outputRead.Dispose(); Marshal.ThrowExceptionForHR(hr); }
                    Output = new StreamReader(new FileStream(outputRead, FileAccess.Read), new UTF8Encoding(false), true);
                }
            }
            catch { _inputWrite.Dispose(); if (_handle != IntPtr.Zero) ClosePseudoConsole(_handle); throw; }
        }
    }
    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
        if (handle != IntPtr.Zero) ClosePseudoConsole(handle);
        _inputWrite.Dispose();
    }
    [StructLayout(LayoutKind.Sequential)] private struct Coord { public short X, Y; }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, IntPtr attributes, uint size);
    [DllImport("kernel32.dll")] private static extern int CreatePseudoConsole(Coord size, SafeFileHandle input, SafeFileHandle output, uint flags, out IntPtr handle);
    [DllImport("kernel32.dll")] private static extern void ClosePseudoConsole(IntPtr handle);
}
