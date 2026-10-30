using System.Text;

namespace CyberScarecrow;

internal static class SmokeTest
{
    public static void Run()
    {
        var report = new StringBuilder();
        var reportPath = Path.Combine(Path.GetTempPath(), "cyberscarecrow-smoke.txt");

        try
        {
            using var manager = new DecoyManager();
            report.AppendLine($"Host: {Path.Combine(AppContext.BaseDirectory, "CyberScarecrow.Host.exe")}");
            report.AppendLine($"Host available: {manager.HostAvailable}");
            report.AppendLine($"Job object: {manager.JobAvailable}");

            var decoys = DecoyCatalog.All
                .Where(d => d.Id is "x64dbg" or "windbg" or "ghidra")
                .ToArray();

            foreach (var decoy in decoys)
            {
                report.AppendLine($"{decoy.Id}: start={manager.Start(decoy)}");
            }

            Thread.Sleep(1500);

            foreach (var decoy in decoys)
            {
                var process = manager.GetProcess(decoy);
                string title = string.Empty;
                try
                {
                    title = process?.MainWindowTitle ?? string.Empty;
                }
                catch
                {
                    title = "<unavailable>";
                }

                report.AppendLine(
                    $"{decoy.Id}: managerRunning={manager.IsRunning(decoy)} " +
                    $"pid={process?.Id.ToString() ?? "none"} " +
                    $"title=\"{title}\"");
            }

            manager.StopAll();
            Thread.Sleep(700);

            foreach (var decoy in decoys)
            {
                report.AppendLine($"{decoy.Id}: afterStopRunning={manager.IsRunning(decoy)}");
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
}
