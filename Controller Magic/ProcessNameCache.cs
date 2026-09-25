using System.ComponentModel;
using System.Diagnostics;

namespace ControllerMagic;

// The foreground window's process name, looked up once per (window, process) rather than on every
// fullscreen check; a handle can be reused by another process, so both make up the key.
internal sealed class ProcessNameCache
{
    private readonly Func<int, string> _resolve;
    private (IntPtr HWnd, int Pid)? _key;
    private string? _name;

    public ProcessNameCache()
        : this(ResolveProcessName)
    {
    }

    internal ProcessNameCache(Func<int, string> resolve)
    {
        _resolve = resolve;
    }

    // Null when the process can't be read (gone, or protected); that isn't retried for the same window.
    public string? NameFor(IntPtr hWnd, int pid)
    {
        if (_key == (hWnd, pid))
            return _name;

        _key = (hWnd, pid);
        _name = TryResolve(pid);
        return _name;
    }

    private string? TryResolve(int pid)
    {
        try
        {
            return _resolve(pid);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            AppLog.Default.Warning($"FullscreenHelper: can't read the name of foreground process {pid}", ex);
            return null;
        }
    }

    private static string ResolveProcessName(int pid)
    {
        using var process = Process.GetProcessById(pid);
        return process.ProcessName;
    }
}
