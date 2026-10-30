using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CyberScarecrow;

internal enum DecoyStartResult
{
    Started,
    AlreadyRunning,
    MissingHost,
    Failed
}

internal sealed class DecoyManager : IDisposable
{
    private readonly Dictionary<string, Process> _running = new(StringComparer.OrdinalIgnoreCase);
    private readonly IntPtr _job;
    private readonly string _hostPath;
    private readonly string _decoyRoot;
    private bool _disposed;

    public DecoyManager()
    {
        _hostPath = Path.Combine(AppContext.BaseDirectory, "CyberScarecrow.Host.exe");
        _decoyRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CyberScarecrow",
            "decoys");
        _job = CreateKillOnCloseJob();
    }

    public bool HostAvailable => File.Exists(_hostPath);

    public bool JobAvailable => _job != IntPtr.Zero;

    public bool IsRunning(Decoy decoy) =>
        _running.TryGetValue(decoy.Id, out var process) && !SafeHasExited(process);

    internal Process? GetProcess(Decoy decoy) =>
        _running.TryGetValue(decoy.Id, out var process) && !SafeHasExited(process) ? process : null;

    public DecoyStartResult Start(Decoy decoy)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DecoyManager));
        if (!File.Exists(_hostPath)) return DecoyStartResult.MissingHost;

        if (_running.TryGetValue(decoy.Id, out var existing))
        {
            if (!SafeHasExited(existing)) return DecoyStartResult.Started;
            _running.Remove(decoy.Id);
            existing.Dispose();
        }

        if (ExternalProcessExists(decoy)) return DecoyStartResult.AlreadyRunning;

        try
        {
            var directory = Path.Combine(_decoyRoot, decoy.Id);
            Directory.CreateDirectory(directory);
            var executable = Path.Combine(directory, decoy.ExeName);
            File.Copy(_hostPath, executable, true);

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = directory,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.Environment["CYBERSCARECROW_TITLE"] = decoy.WindowTitle;
            startInfo.Environment["CYBERSCARECROW_CLASS"] = decoy.WindowClass;
            startInfo.Environment["CYBERSCARECROW_PARENT_PID"] = Environment.ProcessId.ToString();

            var process = Process.Start(startInfo);
            if (process is null) return DecoyStartResult.Failed;

            if (_job != IntPtr.Zero)
            {
                try
                {
                    NativeMethods.AssignProcessToJobObject(_job, process.Handle);
                }
                catch
                {
                    // The parent-watch in the host is a second line of defense.
                }
            }

            _running[decoy.Id] = process;
            return DecoyStartResult.Started;
        }
        catch
        {
            return DecoyStartResult.Failed;
        }
    }

    public void Stop(Decoy decoy)
    {
        if (!_running.TryGetValue(decoy.Id, out var process)) return;
        _running.Remove(decoy.Id);
        TryKill(process);
    }

    public void StopAll()
    {
        foreach (var process in _running.Values)
        {
            TryKill(process);
        }
        _running.Clear();
    }

    /// <summary>
    /// Returns process names (without .exe) for everything running on the machine
    /// except the decoys this manager owns.
    /// </summary>
    public HashSet<string> GetExternalProcessNames()
    {
        var owned = new HashSet<int>(
            _running.Values.Where(p => !SafeHasExited(p)).Select(p => p.Id));
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (!owned.Contains(process.Id))
                {
                    names.Add(process.ProcessName);
                }
            }
            catch
            {
                // Some system processes cannot be queried; ignore them.
            }
            finally
            {
                process.Dispose();
            }
        }

        return names;
    }

    private static bool ExternalProcessExists(Decoy decoy)
    {
        var processName = Path.GetFileNameWithoutExtension(decoy.ExeName);
        foreach (var process in Process.GetProcessesByName(processName))
        {
            process.Dispose();
            return true;
        }
        return false;
    }

    private static bool SafeHasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch
        {
            return true;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Already gone, or access denied. Nothing else to do.
        }
        finally
        {
            process.Dispose();
        }
    }

    private static IntPtr CreateKillOnCloseJob()
    {
        var job = NativeMethods.CreateJobObjectW(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return IntPtr.Zero;

        var information = new NativeMethods.JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new NativeMethods.JobObjectBasicLimitInformation
            {
                LimitFlags = NativeMethods.JobObjectLimitKillOnJobClose
            }
        };

        var size = Marshal.SizeOf<NativeMethods.JobObjectExtendedLimitInformation>();
        if (!NativeMethods.SetInformationJobObject(
                job,
                NativeMethods.JobObjectExtendedLimitInformationClass,
                ref information,
                size))
        {
            NativeMethods.CloseHandle(job);
            return IntPtr.Zero;
        }

        return job;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopAll();

        if (_job != IntPtr.Zero)
        {
            // Closing a kill-on-close job terminates any decoy still assigned to it.
            NativeMethods.CloseHandle(_job);
        }
    }
}
