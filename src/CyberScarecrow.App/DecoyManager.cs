using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
    private readonly Dictionary<string, string> _deployedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _manifest = new(StringComparer.OrdinalIgnoreCase);
    private readonly IntPtr _job;
    private readonly string _hostPath;
    private readonly string _decoyRoot;
    private readonly string _programsRoot;
    private readonly string _manifestPath;
    private readonly byte[] _hostHash;
    private readonly long _hostLength;
    private bool _disposed;

    public DecoyManager()
    {
        _hostPath = Path.Combine(AppContext.BaseDirectory, "CyberScarecrow.Host.exe");
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Path.Combine(localAppData, "CyberScarecrow");
        _decoyRoot = Path.Combine(appData, "decoys");
        _programsRoot = Path.Combine(localAppData, "Programs");
        _manifestPath = Path.Combine(appData, "deployed.json");
        _hostHash = ComputeFileHash(_hostPath);
        _hostLength = GetFileLength(_hostPath);
        LoadManifest();
        _job = CreateKillOnCloseJob();
        CleanupStaleDeployments();
    }

    public bool DeepDisguise { get; set; }

    public string LastReparentError { get; private set; } = string.Empty;

    public bool HostAvailable => File.Exists(_hostPath);

    public bool JobAvailable => _job != IntPtr.Zero;

    public bool IsRunning(Decoy decoy) =>
        _running.TryGetValue(decoy.Id, out var process) && !SafeHasExited(process);

    internal Process? GetProcess(Decoy decoy) =>
        _running.TryGetValue(decoy.Id, out var process) && !SafeHasExited(process) ? process : null;

    public DecoyStartResult Start(Decoy decoy)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DecoyManager));
        if (!HostAvailable) return DecoyStartResult.MissingHost;

        if (_running.TryGetValue(decoy.Id, out var existing))
        {
            if (!SafeHasExited(existing)) return DecoyStartResult.Started;
            _running.Remove(decoy.Id);
            existing.Dispose();
            CleanupDeployment(decoy);
        }

        if (ExternalProcessExists(decoy)) return DecoyStartResult.AlreadyRunning;

        try
        {
            var (directory, executable) = GetDeployment(decoy);
            Directory.CreateDirectory(directory);
            File.Copy(_hostPath, executable, true);
            _deployedPaths[decoy.Id] = executable;
            RecordDeployment(executable);

            if (DeepDisguise && TryStartReparented(decoy, executable, directory, out var reparented))
            {
                _running[decoy.Id] = reparented!;
                return DecoyStartResult.Started;
            }

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
            if (process is null)
            {
                CleanupDeployment(decoy);
                return DecoyStartResult.Failed;
            }

            AssignToJob(process);
            _running[decoy.Id] = process;
            return DecoyStartResult.Started;
        }
        catch
        {
            CleanupDeployment(decoy);
            return DecoyStartResult.Failed;
        }
    }

    public void Stop(Decoy decoy)
    {
        if (_running.TryGetValue(decoy.Id, out var process))
        {
            _running.Remove(decoy.Id);
            TryKill(process);
        }

        CleanupDeployment(decoy);
    }

    public void StopAll()
    {
        var paths = _deployedPaths.Values.ToList();
        _deployedPaths.Clear();

        foreach (var process in _running.Values)
        {
            TryKill(process);
        }
        _running.Clear();

        foreach (var path in paths)
        {
            DeleteDeployedFile(path);
        }
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

    private (string Directory, string Executable) GetDeployment(Decoy decoy)
    {
        if (DeepDisguise)
        {
            var folder = SanitizeFolderName(decoy.FolderName);
            var directory = Path.Combine(_programsRoot, folder);
            var executable = Path.Combine(directory, decoy.ExeName);

            // Never overwrite a real per-user installation. Files recorded in
            // our manifest are ours even if they came from an older build.
            if (!File.Exists(executable) || IsOurHost(executable) || MatchesRecordedHash(executable))
            {
                return (directory, executable);
            }
        }

        var fallback = Path.Combine(_decoyRoot, decoy.Id);
        return (fallback, Path.Combine(fallback, decoy.ExeName));
    }

    private static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            builder.Append(invalid.Contains(character) ? '_' : character);
        }

        var result = builder.ToString().Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(result) ? "Tool" : result;
    }

    private bool TryStartReparented(Decoy decoy, string executable, string directory, out Process? process)
    {
        process = null;
        LastReparentError = string.Empty;

        using var explorer = FindExplorerProcess();
        if (explorer is null)
        {
            LastReparentError = "explorer.exe not found";
            return false;
        }

        var explorerHandle = NativeMethods.OpenProcess(
            NativeMethods.ProcessCreateProcess,
            false,
            explorer.Id);
        if (explorerHandle == IntPtr.Zero)
        {
            LastReparentError = $"OpenProcess failed ({Marshal.GetLastWin32Error()})";
            return false;
        }

        IntPtr attributeList = IntPtr.Zero;
        IntPtr environment = IntPtr.Zero;
        IntPtr parentHandleValue = IntPtr.Zero;

        try
        {
            var size = IntPtr.Zero;
            NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            if (size == IntPtr.Zero)
            {
                LastReparentError = $"InitializeProcThreadAttributeList(size) failed ({Marshal.GetLastWin32Error()})";
                return false;
            }

            attributeList = Marshal.AllocHGlobal(size);
            if (!NativeMethods.InitializeProcThreadAttributeList(attributeList, 1, 0, ref size))
            {
                LastReparentError = $"InitializeProcThreadAttributeList failed ({Marshal.GetLastWin32Error()})";
                return false;
            }

            parentHandleValue = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(parentHandleValue, explorerHandle);

            if (!NativeMethods.UpdateProcThreadAttribute(
                    attributeList,
                    0,
                    (IntPtr)NativeMethods.ProcThreadAttributeParentProcess,
                    parentHandleValue,
                    (IntPtr)IntPtr.Size,
                    IntPtr.Zero,
                    IntPtr.Zero))
            {
                LastReparentError = $"UpdateProcThreadAttribute failed ({Marshal.GetLastWin32Error()})";
                return false;
            }

            var startupInfo = new NativeMethods.StartupInfoEx();
            startupInfo.StartupInfo.Size = Marshal.SizeOf<NativeMethods.StartupInfoEx>();
            startupInfo.AttributeList = attributeList;

            environment = BuildEnvironmentBlock(decoy);
            var commandLine = new StringBuilder("\"" + executable + "\"");
            var flags = NativeMethods.CreateSuspended |
                        NativeMethods.CreateUnicodeEnvironment |
                        NativeMethods.ExtendedStartupInfoPresent;

            if (!NativeMethods.CreateProcessW(
                    executable,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    flags,
                    environment,
                    directory,
                    ref startupInfo,
                    out var information))
            {
                LastReparentError = $"CreateProcessW failed ({Marshal.GetLastWin32Error()})";
                return false;
            }

            try
            {
                AssignToJob(information.Process);

                if (NativeMethods.ResumeThread(information.Thread) == uint.MaxValue)
                {
                    LastReparentError = $"ResumeThread failed ({Marshal.GetLastWin32Error()})";
                    NativeMethods.TerminateProcess(information.Process, 1);
                    return false;
                }

                process = Process.GetProcessById(information.ProcessId);
                return true;
            }
            catch (Exception exception)
            {
                LastReparentError = exception.Message;
                NativeMethods.TerminateProcess(information.Process, 1);
                return false;
            }
            finally
            {
                NativeMethods.CloseHandle(information.Thread);
                NativeMethods.CloseHandle(information.Process);
            }
        }
        finally
        {
            if (environment != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(environment);
            }

            if (attributeList != IntPtr.Zero)
            {
                NativeMethods.DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }

            if (parentHandleValue != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(parentHandleValue);
            }

            NativeMethods.CloseHandle(explorerHandle);
        }
    }

    private static Process? FindExplorerProcess()
    {
        // The shell window belongs to the actual desktop explorer, not a
        // separate File Explorer window that happens to share the name.
        var shellWindow = NativeMethods.GetShellWindow();
        if (shellWindow != IntPtr.Zero)
        {
            NativeMethods.GetWindowThreadProcessId(shellWindow, out var shellPid);
            if (shellPid != 0)
            {
                try
                {
                    return Process.GetProcessById((int)shellPid);
                }
                catch
                {
                    // Fall through to the name-based lookup.
                }
            }
        }

        Process? result = null;
        var session = Process.GetCurrentProcess().SessionId;

        foreach (var process in Process.GetProcessesByName("explorer"))
        {
            if (result is null && process.SessionId == session)
            {
                result = process;
            }
            else
            {
                process.Dispose();
            }
        }

        return result;
    }

    private static IntPtr BuildEnvironmentBlock(Decoy decoy)
    {
        var values = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                values[key] = value;
            }
        }

        values["CYBERSCARECROW_TITLE"] = decoy.WindowTitle;
        values["CYBERSCARECROW_CLASS"] = decoy.WindowClass;
        values["CYBERSCARECROW_PARENT_PID"] = Environment.ProcessId.ToString();

        var builder = new StringBuilder();
        foreach (var pair in values)
        {
            builder.Append(pair.Key).Append('=').Append(pair.Value).Append('\0');
        }
        builder.Append('\0');

        return Marshal.StringToHGlobalUni(builder.ToString());
    }

    private void AssignToJob(IntPtr process)
    {
        if (_job == IntPtr.Zero) return;

        try
        {
            NativeMethods.AssignProcessToJobObject(_job, process);
        }
        catch
        {
            // The parent-watch in the host is a second line of defense.
        }
    }

    private void AssignToJob(Process process)
    {
        if (_job == IntPtr.Zero) return;

        try
        {
            NativeMethods.AssignProcessToJobObject(_job, process.Handle);
        }
        catch
        {
            // The parent-watch in the host is a second line of defense.
        }
    }

    private void CleanupDeployment(Decoy decoy)
    {
        if (_deployedPaths.Remove(decoy.Id, out var path))
        {
            DeleteDeployedFile(path);
        }
    }

    private void DeleteDeployedFile(string path)
    {
        var canForget = false;
        try
        {
            if (!File.Exists(path))
            {
                canForget = true;
            }
            else if (IsOurHost(path) || MatchesRecordedHash(path))
            {
                File.Delete(path);
                canForget = true;
            }
            else
            {
                // The file is not ours anymore; stop tracking it.
                canForget = true;
            }

            if (canForget)
            {
                ForgetDeployment(path);
            }

            DeleteEmptyParents(path);
        }
        catch
        {
            // The file may still be locked; the next startup retries cleanup.
        }
    }

    private bool IsOurHost(string path)
    {
        try
        {
            if (_hostHash.Length == 0) return false;

            var info = new FileInfo(path);
            if (!info.Exists || info.Length != _hostLength) return false;

            var hash = SHA256.HashData(File.ReadAllBytes(path));
            return CryptographicOperations.FixedTimeEquals(hash, _hostHash);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] ComputeFileHash(string path)
    {
        try
        {
            return File.Exists(path) ? SHA256.HashData(File.ReadAllBytes(path)) : Array.Empty<byte>();
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }

    private static long GetFileLength(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch
        {
            return 0;
        }
    }

    private void LoadManifest()
    {
        try
        {
            if (!File.Exists(_manifestPath)) return;

            var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(_manifestPath));
            if (entries is null) return;

            foreach (var pair in entries)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                {
                    _manifest[pair.Key] = pair.Value;
                }
            }
        }
        catch
        {
            _manifest.Clear();
        }
    }

    private void SaveManifest()
    {
        try
        {
            if (_manifest.Count == 0)
            {
                if (File.Exists(_manifestPath))
                {
                    File.Delete(_manifestPath);
                }

                return;
            }

            var directory = Path.GetDirectoryName(_manifestPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_manifestPath, JsonSerializer.Serialize(_manifest));
        }
        catch
        {
            // Best effort; the current build's hash scan still cleans up.
        }
    }

    private void RecordDeployment(string path)
    {
        if (_hostHash.Length == 0) return;

        _manifest[path] = Convert.ToHexString(_hostHash);
        SaveManifest();
    }

    private void ForgetDeployment(string path)
    {
        if (_manifest.Remove(path))
        {
            SaveManifest();
        }
    }

    private bool MatchesRecordedHash(string path)
    {
        if (!_manifest.TryGetValue(path, out var expected)) return false;

        try
        {
            if (!File.Exists(path)) return false;

            var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void CleanupManifestEntries()
    {
        foreach (var pair in _manifest.ToList())
        {
            if (!IsUnderManagedRoots(pair.Key))
            {
                _manifest.Remove(pair.Key);
                continue;
            }

            try
            {
                if (!File.Exists(pair.Key))
                {
                    _manifest.Remove(pair.Key);
                    continue;
                }

                var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pair.Key)));
                if (string.Equals(actual, pair.Value, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(pair.Key);
                    DeleteEmptyParents(pair.Key);
                }

                // If the hash no longer matches, something else replaced the
                // file. Stop tracking it and leave it alone.
                _manifest.Remove(pair.Key);
            }
            catch
            {
                // Keep the entry and retry on the next launch.
            }
        }

        SaveManifest();
    }

    private bool IsUnderManagedRoots(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            return IsUnder(full, _programsRoot) || IsUnder(full, _decoyRoot);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsUnder(string path, string root)
    {
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private void DeleteEmptyParents(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            while (!string.IsNullOrEmpty(directory) &&
                   !PathsEqual(directory, _programsRoot) &&
                   !PathsEqual(directory, _decoyRoot) &&
                   Directory.Exists(directory) &&
                   !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
                directory = Path.GetDirectoryName(directory);
            }
        }
        catch
        {
            // Leave it for the next launch.
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private void CleanupStaleDeployments()
    {
        CleanupManifestEntries();
        CleanupDirectory(_decoyRoot);

        foreach (var decoy in DecoyCatalog.All)
        {
            var folder = SanitizeFolderName(decoy.FolderName);
            var directory = Path.Combine(_programsRoot, folder);
            var executable = Path.Combine(directory, decoy.ExeName);

            if (File.Exists(executable) && IsOurHost(executable))
            {
                try
                {
                    File.Delete(executable);
                }
                catch
                {
                    // Retry on the next launch.
                }
            }

            try
            {
                if (Directory.Exists(directory) &&
                    !Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch
            {
                // A real installation or another file is using the folder.
            }
        }
    }

    private void CleanupDirectory(string root)
    {
        try
        {
            if (!Directory.Exists(root)) return;

            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (IsOurHost(file))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // Retry on the next launch.
                    }
                }
            }

            foreach (var directory in Directory
                         .EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        Directory.Delete(directory);
                    }
                }
                catch
                {
                    // Leave it for the next launch.
                }
            }

            if (!Directory.EnumerateFileSystemEntries(root).Any())
            {
                Directory.Delete(root);
            }
        }
        catch
        {
            // Cleanup is best-effort.
        }
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
