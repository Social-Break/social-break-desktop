using System.Runtime.InteropServices;

namespace SocialBreakTray.Tracking;

/// <summary>Win32 P/Invoke implementation of IIdleTimeProvider via
/// GetLastInputInfo.</summary>
public class Win32IdleTimeProvider : IIdleTimeProvider
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    public TimeSpan GetIdleTime()
    {
        var info = new LASTINPUTINFO();
        info.cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>();

        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;

        // GetLastInputInfo's dwTime is a GetTickCount()-style 32-bit tick
        // count, so it must be compared against Environment.TickCount (also
        // 32-bit), not TickCount64 - unchecked subtraction here still
        // produces the correct delta across the ~49.7-day wraparound point
        // via two's complement arithmetic, as long as the actual elapsed
        // idle time is far shorter than that (which it always is here).
        unchecked
        {
            int idleTicks = Environment.TickCount - (int)info.dwTime;
            return TimeSpan.FromMilliseconds(Math.Max(0, idleTicks));
        }
    }
}
