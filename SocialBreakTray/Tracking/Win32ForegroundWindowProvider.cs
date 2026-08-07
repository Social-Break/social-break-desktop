using System.Runtime.InteropServices;
using System.Text;

namespace SocialBreakTray.Tracking;

/// <summary>
/// Win32 P/Invoke implementation of IForegroundWindowProvider.
///
/// Uses QueryFullProcessImageName (needs only PROCESS_QUERY_LIMITED_INFORMATION)
/// rather than System.Diagnostics.Process.MainModule (needs
/// PROCESS_QUERY_INFORMATION | PROCESS_VM_READ) deliberately - the latter
/// commonly throws Win32Exception ("Access is denied") when the foreground
/// window belongs to an elevated process while this app is running
/// unelevated, which is the normal/intended state for this app (see
/// AutoStart.cs and the tray menu - it never asks for or needs admin
/// rights). QueryFullProcessImageName succeeds across that privilege
/// boundary far more often.
///
/// Known limitation: Store/UWP-packaged apps' foreground window is often
/// owned by a shared host process (ApplicationFrameHost.exe), not the app's
/// own exe - this returns "applicationframehost.exe" for those rather than
/// the actual app. Not handled in this version; both of the app's stated
/// example targets (VS Code, Discord) are classic Win32 apps and unaffected.
/// </summary>
public class Win32ForegroundWindowProvider : IForegroundWindowProvider
{
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(nint hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);

    public ForegroundInfo? GetForegroundProcess()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == nint.Zero) return null;

        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return null;

        var hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProcess == nint.Zero) return null;

        try
        {
            var buffer = new StringBuilder(1024);
            uint size = (uint)buffer.Capacity;
            if (!QueryFullProcessImageName(hProcess, 0, buffer, ref size)) return null;

            var exeName = Path.GetFileName(buffer.ToString(0, (int)size)).ToLowerInvariant();
            return string.IsNullOrEmpty(exeName) ? null : new ForegroundInfo(exeName, hwnd);
        }
        catch
        {
            return null;
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }
}
