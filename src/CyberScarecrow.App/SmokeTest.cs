using System.Diagnostics;
using System.Text;

namespace CyberScarecrow;

internal static class SmokeTest
{
    public static void Run(string[] args)
    {
        var deep = args.Any(a => a.Equals("--deep", StringComparison.OrdinalIgnoreCase));
        var report = new StringBuilder();
        var reportPath = Path.Combine(Path.GetTempPath(), "cyberscarecrow-smoke.txt");

        try
        {
            using var manager = new DecoyManager { DeepDisguise = deep };
            var explorerPid = FindExplorerPid();

            report.AppendLine($"Host: {Path.Combine(AppContext.BaseDirectory, "CyberScarecrow.Host.exe")}");
            report.AppendLine($"Host available: {manager.HostAvailable}");
            report.AppendLine($"Job object: {manager.JobAvailable}");
            report.AppendLine($"Deep disguise: {deep}");
            report.AppendLine($"Explorer PID: {explorerPid}");

            var decoys = DecoyCatalog.All
                .Where(d => d.Id is "x64dbg" or "windbg" or "ghidra")
                .ToArray();

            foreach (var decoy in decoys)
            {
                var result = manager.Start(decoy);
                report.AppendLine($"{decoy.Id}: start={result}");
                if (deep && !string.IsNullOrEmpty(manager.LastReparentError))
                {
                    report.AppendLine($"{decoy.Id}: reparentError={manager.LastReparentError}");
                }
            }

            Thread.Sleep(1500);

            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var decoy in decoys)
            {
                var process = manager.GetProcess(decoy);
                var title = string.Empty;
                var path = string.Empty;
                var parentPid = 0;

                try
                {
                    title = process?.MainWindowTitle ?? string.Empty;
                    path = process?.MainModule?.FileName ?? string.Empty;
                    if (process is not null)
                    {
                        parentPid = NativeMethods.GetParentProcessId(process);
                    }
                }
                catch
                {
                    title = "<unavailable>";
                    path = "<unavailable>";
                }

                if (!string.IsNullOrEmpty(path) && path != "<unavailable>")
                {
                    paths[decoy.Id] = path;
                }

                report.AppendLine(
                    $"{decoy.Id}: managerRunning={manager.IsRunning(decoy)} " +
                    $"pid={process?.Id.ToString() ?? "none"} " +
                    $"parentPid={parentPid} " +
                    $"parentIsExplorer={parentPid == explorerPid} " +
                    $"path=\"{path}\" " +
                    $"title=\"{title}\"");
            }

            manager.StopAll();
            Thread.Sleep(700);

            foreach (var decoy in decoys)
            {
                var fileExists = paths.TryGetValue(decoy.Id, out var path) && File.Exists(path);
                report.AppendLine(
                    $"{decoy.Id}: afterStopRunning={manager.IsRunning(decoy)} " +
                    $"fileExistsAfterStop={fileExists}");
            }

            report.AppendLine("RESULT: OK");
        }
        catch (Exception exception)
        {
            report.AppendLine("RESULT: ERROR");
            report.AppendLine(exception.ToString());
        }

        File.WriteAllText(reportPath, report.ToString());
    }

    private static int FindExplorerPid()
    {
        var session = Process.GetCurrentProcess().SessionId;

        foreach (var process in Process.GetProcessesByName("explorer"))
        {
            try
            {
                if (process.SessionId == session)
                {
                    return process.Id;
                }
            }
            finally
            {
                process.Dispose();
            }
        }

        return 0;
    }
}
