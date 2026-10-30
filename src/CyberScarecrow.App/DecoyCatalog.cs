namespace CyberScarecrow;

internal enum PresetLevel
{
    Low,
    Medium,
    High,
    Custom
}

internal sealed record Decoy(
    string Id,
    string Tool,
    string ExeName,
    string WindowTitle,
    string WindowClass,
    int Tier);

internal static class DecoyCatalog
{
    // Tier 1 = Low, tier 2 = Medium, tier 3 = High.
    // Tier 4 tools are available individually and via "Select all".
    public static IReadOnlyList<Decoy> All { get; } = new List<Decoy>
    {
        // Low
        new("x64dbg", "x64dbg", "x64dbg.exe", "x64dbg - [CPU]", "Qt5QWindowIcon", 1),
        new("windbg", "WinDbg", "windbg.exe", "WinDbg:2.0.0.0 AMD64", "WinDbgFrameClass", 1),
        new("ghidra", "Ghidra", "ghidra.exe", "Ghidra", "SunAwtFrame", 1),
        new("wireshark", "Wireshark", "Wireshark.exe", "The Wireshark Network Analyzer", "Qt5QWindowIcon", 1),
        new("procmon64", "Process Monitor", "Procmon64.exe", "Process Monitor - Sysinternals: www.sysinternals.com", "ProcmonWindowClass", 1),

        // Medium
        new("ida64", "IDA Pro", "ida64.exe", "IDA - untitled.i64", "Qt5QWindowIcon", 2),
        new("procexp64", "Process Explorer", "procexp64.exe", "Process Explorer - Sysinternals: www.sysinternals.com", "ProcexpWindowClass", 2),
        new("windbgx", "WinDbg Preview", "WinDbgX.exe", "WinDbg", "WinDbgFrameClass", 2),
        new("dnspy", "dnSpy", "dnSpy.exe", "dnSpy", "HwndWrapper", 2),
        new("fiddler", "Fiddler", "Fiddler.exe", "Fiddler", "WindowsForms10.Window.8.app.0", 2),
        new("pebear", "PE-bear", "PE-bear.exe", "PE-bear", "Qt5QWindowIcon", 2),
        new("x32dbg", "x32dbg", "x32dbg.exe", "x32dbg - [CPU]", "Qt5QWindowIcon", 2),

        // High
        new("ida", "IDA Pro 32", "ida.exe", "IDA - untitled.idb", "Qt5QWindowIcon", 3),
        new("procmon", "Process Monitor 32", "Procmon.exe", "Process Monitor - Sysinternals: www.sysinternals.com", "ProcmonWindowClass", 3),
        new("procexp", "Process Explorer 32", "procexp.exe", "Process Explorer - Sysinternals: www.sysinternals.com", "ProcexpWindowClass", 3),
        new("tshark", "tshark", "tshark.exe", "tshark", "tshark", 3),
        new("ilspy", "ILSpy", "ILSpy.exe", "ILSpy", "HwndWrapper", 3),
        new("cffexplorer", "CFF Explorer", "CFF Explorer.exe", "CFF Explorer", "CFFExplorerMainFrame", 3),
        new("hxd", "HxD", "HxD.exe", "HxD - untitled", "HxDMainWindow", 3),
        new("scylla", "Scylla", "Scylla_x64.exe", "Scylla x64", "ScyllaMainWindow", 3),
        new("apimonitor", "API Monitor", "apimonitor-x64.exe", "API Monitor", "API Monitor", 3),
        new("cheatengine", "Cheat Engine", "cheatengine-x86_64.exe", "Cheat Engine 7.5", "CheatEngineMainWindow", 3),
        new("processhacker", "Process Hacker", "ProcessHacker.exe", "Process Hacker", "ProcessHacker", 3),
        new("tcpview", "TCPView", "tcpview64.exe", "TCPView - Sysinternals: www.sysinternals.com", "TCPView", 3),

        // Extras (custom selection)
        new("autoruns", "Autoruns", "autoruns64.exe", "Autoruns - Sysinternals: www.sysinternals.com", "Autoruns", 4),
        new("dbgview", "DebugView", "Dbgview64.exe", "DebugView - Sysinternals: www.sysinternals.com", "DbgviewWindowClass", 4),
        new("resourcehacker", "Resource Hacker", "ResourceHacker.exe", "Resource Hacker", "ResourceHackerMainWindow", 4),
        new("die", "Detect It Easy", "DIE.exe", "Detect It Easy", "DIE", 4),
        new("binaryninja", "Binary Ninja", "binaryninja.exe", "Binary Ninja", "Qt5QWindowIcon", 4),
        new("cutter", "Cutter", "Cutter.exe", "Cutter", "Qt5QWindowIcon", 4)
    };

    public static IReadOnlyList<Decoy> ForPreset(PresetLevel preset) => preset switch
    {
        PresetLevel.Low => All.Where(d => d.Tier <= 1).ToList(),
        PresetLevel.Medium => All.Where(d => d.Tier <= 2).ToList(),
        PresetLevel.High => All.Where(d => d.Tier <= 3).ToList(),
        _ => Array.Empty<Decoy>()
    };

    public static Decoy? Find(string id) =>
        All.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
}
