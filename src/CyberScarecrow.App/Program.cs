using System.Windows.Forms;

namespace CyberScarecrow;

internal static class Program
{
    private const string MutexName = @"Local\CyberScarecrow.SingleInstance";

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Any(a => a.Equals("--smoke-test", StringComparison.OrdinalIgnoreCase)))
        {
            SmokeTest.Run(args);
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "CyberScarecrow is already running.\n\nLook for the scarecrow icon in the system tray.",
                "CyberScarecrow",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var startMinimized = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        var autoArm = args.Any(a => a.Equals("--arm", StringComparison.OrdinalIgnoreCase));

        Application.Run(new MainForm(startMinimized, autoArm));
    }
}
