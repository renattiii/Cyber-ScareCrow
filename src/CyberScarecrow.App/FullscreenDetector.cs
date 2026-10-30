using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CyberScarecrow;

internal static class FullscreenDetector
{
    public static bool TryGetFullscreenApp(out string name, out IntPtr window)
    {
        name = string.Empty;
        window = IntPtr.Zero;

        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == NativeMethods.GetShellWindow())
        {
            return false;
        }

        if (!NativeMethods.IsWindowVisible(foreground))
        {
            return false;
        }

        var className = new StringBuilder(256);
        NativeMethods.GetClassName(foreground, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd")
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(foreground, out var processId);
        if (processId == 0 || processId == Environment.ProcessId)
        {
            return false;
        }

        var monitor = NativeMethods.MonitorFromWindow(
            foreground,
            NativeMethods.MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var monitorInfo = new NativeMethods.MonitorInfo
        {
            Size = Marshal.SizeOf<NativeMethods.MonitorInfo>()
        };
        if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        if (!NativeMethods.GetWindowRect(foreground, out var rect))
        {
            return false;
        }

        const int tolerance = 8;
        var coversMonitor =
            rect.Left <= monitorInfo.Monitor.Left + tolerance &&
            rect.Top <= monitorInfo.Monitor.Top + tolerance &&
            rect.Right >= monitorInfo.Monitor.Right - tolerance &&
            rect.Bottom >= monitorInfo.Monitor.Bottom - tolerance;
        if (!coversMonitor)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            name = process.ProcessName;
        }
        catch
        {
            name = "fullscreen app";
        }

        window = foreground;
        return true;
    }
}
