using System.Text.Json;
using Microsoft.Win32;

namespace CyberScarecrow;

internal sealed class AppConfig
{
    public string Preset { get; set; } = nameof(PresetLevel.Medium);
    public List<string> Selected { get; set; } = DecoyCatalog.ForPreset(PresetLevel.Medium).Select(d => d.Id).ToList();
    public bool MinimizeToTray { get; set; }

    private static string ConfigDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CyberScarecrow");

    private static string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json);
                if (config is not null)
                {
                    config.Selected ??= new List<string>();
                    config.Selected = config.Selected
                        .Where(id => DecoyCatalog.Find(id) is not null)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (string.IsNullOrWhiteSpace(config.Preset) ||
                        !Enum.TryParse<PresetLevel>(config.Preset, true, out _))
                    {
                        config.Preset = nameof(PresetLevel.Custom);
                    }

                    return config;
                }
            }
        }
        catch
        {
            // A corrupt config should never stop the app from starting.
        }

        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            var temporaryPath = ConfigPath + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, ConfigPath, true);
        }
        catch
        {
            // Settings are a convenience; ignore failures.
        }
    }
}

internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CyberScarecrow";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
        if (enabled)
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable))
            {
                throw new InvalidOperationException("Could not determine the application path.");
            }

            key.SetValue(ValueName, $"\"{executable}\" --minimized --arm");
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
    }
}
