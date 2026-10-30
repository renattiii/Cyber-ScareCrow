// CyberScarecrow decoy host.
//
// A tiny, harmless process that stays alive and exposes a visible-but-offscreen
// top-level window with a configurable title/class. The manager copies this
// executable under tool names (x64dbg.exe, windbg.exe, ...) and passes the
// window metadata through environment variables.
//
// It performs no file, network, or process inspection. It just pumps messages
// until the manager (or its job object) terminates it.

#define UNICODE
#define _UNICODE
#define WIN32_LEAN_AND_MEAN

#include <windows.h>
#include <cstdlib>
#include <cwchar>
#include <iterator>
#include <new>
#include <string>

namespace {

constexpr wchar_t kTitleEnv[] = L"CYBERSCARECROW_TITLE";
constexpr wchar_t kClassEnv[] = L"CYBERSCARECROW_CLASS";
constexpr wchar_t kParentEnv[] = L"CYBERSCARECROW_PARENT_PID";
constexpr wchar_t kFallbackName[] = L"decoy";

std::wstring ReadEnv(const wchar_t* name)
{
    wchar_t buffer[512];
    const DWORD length = GetEnvironmentVariableW(name, buffer, static_cast<DWORD>(std::size(buffer)));
    if (length > 0 && length < std::size(buffer)) {
        return std::wstring(buffer, length);
    }
    return std::wstring();
}

std::wstring ExeStem()
{
    wchar_t path[MAX_PATH];
    const DWORD length = GetModuleFileNameW(nullptr, path, static_cast<DWORD>(std::size(path)));
    if (length == 0) {
        return kFallbackName;
    }

    const wchar_t* base = wcsrchr(path, L'\\');
    base = (base != nullptr) ? base + 1 : path;

    std::wstring stem(base);
    const std::wstring::size_type dot = stem.find_last_of(L'.');
    if (dot != std::wstring::npos && dot > 0) {
        stem.erase(dot);
    }
    return stem.empty() ? std::wstring(kFallbackName) : stem;
}

struct ParentWatch {
    HANDLE parent;
};

DWORD WINAPI WatchParent(LPVOID parameter)
{
    auto* watch = static_cast<ParentWatch*>(parameter);
    WaitForSingleObject(watch->parent, INFINITE);
    // The manager is gone. Do not linger as an orphaned decoy.
    ExitProcess(0);
    return 0;
}

void WatchManager()
{
    const std::wstring pidText = ReadEnv(kParentEnv);
    if (pidText.empty()) {
        return;
    }

    const DWORD pid = static_cast<DWORD>(wcstoul(pidText.c_str(), nullptr, 10));
    if (pid == 0 || pid == GetCurrentProcessId()) {
        return;
    }

    HANDLE parent = OpenProcess(SYNCHRONIZE, FALSE, pid);
    if (parent == nullptr) {
        return;
    }

    auto* watch = new (std::nothrow) ParentWatch{parent};
    if (watch == nullptr) {
        CloseHandle(parent);
        return;
    }

    HANDLE thread = CreateThread(nullptr, 0, WatchParent, watch, 0, nullptr);
    if (thread != nullptr) {
        CloseHandle(thread);
    } else {
        delete watch;
        CloseHandle(parent);
    }
}

LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
{
    switch (message) {
    case WM_CLOSE:
        // Ignore close requests; only the manager decides when a decoy stops.
        return 0;
    case WM_DESTROY:
        PostQuitMessage(0);
        return 0;
    default:
        return DefWindowProcW(window, message, wParam, lParam);
    }
}

} // namespace

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int)
{
    WatchManager();

    const std::wstring stem = ExeStem();

    std::wstring title = ReadEnv(kTitleEnv);
    if (title.empty()) {
        title = stem;
    }

    std::wstring windowClass = ReadEnv(kClassEnv);
    if (windowClass.empty()) {
        windowClass = stem;
    }

    WNDCLASSEXW wc{};
    wc.cbSize = sizeof(wc);
    wc.lpfnWndProc = WindowProc;
    wc.hInstance = instance;
    wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    wc.hbrBackground = reinterpret_cast<HBRUSH>(COLOR_WINDOW + 1);
    wc.lpszClassName = windowClass.c_str();

    if (RegisterClassExW(&wc) == 0) {
        // Never spin; just idle until the manager stops us.
        Sleep(INFINITE);
        return 0;
    }

    // WS_EX_TOOLWINDOW keeps it out of the taskbar and Alt-Tab.
    // The window is technically visible (IsWindowVisible == TRUE) but lives
    // far offscreen, so window-title checks can find it without bothering
    // the user.
    HWND window = CreateWindowExW(
        WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
        windowClass.c_str(),
        title.c_str(),
        WS_OVERLAPPEDWINDOW,
        -32000,
        -32000,
        1024,
        700,
        nullptr,
        nullptr,
        instance,
        nullptr);

    if (window == nullptr) {
        Sleep(INFINITE);
        return 0;
    }

    ShowWindow(window, SW_SHOWNOACTIVATE);
    UpdateWindow(window);

    MSG message;
    while (GetMessageW(&message, nullptr, 0, 0) > 0) {
        TranslateMessage(&message);
        DispatchMessageW(&message);
    }

    return 0;
}
