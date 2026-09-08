using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SocialBreakTray.Ui;

/// <summary>
/// The single source of truth for this app's colors and for the few Win32
/// calls needed to stop Windows drawing its own light-themed chrome around
/// a dark window. Every form used to repeat its own
/// Color.FromArgb(0x1e, 0x1e, 0x2e) literals, which drifted; they now all
/// read from here so a palette change is one edit.
///
/// The palette is Catppuccin Mocha, same family the website and the
/// extension's block page already use - the desktop app looking like a
/// different product than the site it's an accessory to was the main thing
/// making it feel bolted-on.
/// </summary>
internal static class Theme
{
    public static readonly Color Bg = Color.FromArgb(0x1e, 0x1e, 0x2e);
    public static readonly Color Surface = Color.FromArgb(0x25, 0x26, 0x37);
    public static readonly Color Card = Color.FromArgb(0x29, 0x2c, 0x3c);
    public static readonly Color CardLive = Color.FromArgb(0x24, 0x35, 0x2b);
    public static readonly Color Well = Color.FromArgb(0x11, 0x11, 0x1b);
    public static readonly Color Border = Color.FromArgb(0x3b, 0x3e, 0x52);
    public static readonly Color Avatar = Color.FromArgb(0x45, 0x47, 0x5a);

    public static readonly Color TextPrimary = Color.FromArgb(0xcd, 0xd6, 0xf4);
    public static readonly Color TextSecondary = Color.FromArgb(0xa6, 0xad, 0xc8);
    public static readonly Color TextMuted = Color.FromArgb(0x6c, 0x70, 0x86);

    public static readonly Color AccentGreen = Color.FromArgb(0xa6, 0xe3, 0xa1);
    public static readonly Color AccentYellow = Color.FromArgb(0xf9, 0xe2, 0xaf);
    public static readonly Color AccentRed = Color.FromArgb(0xf3, 0x8b, 0xa8);
    public static readonly Color ButtonGreen = Color.FromArgb(0x4c, 0xaf, 0x50);
    public static readonly Color ButtonGreenHover = Color.FromArgb(0x5c, 0xbf, 0x60);

    // Menu/caption interaction states. Kept as opaque colors rather than
    // alpha overlays so they render identically whether they land on the
    // flat menu surface or on the caption's gradient.
    public static readonly Color MenuHover = Color.FromArgb(0x38, 0x3a, 0x50);
    public static readonly Color CaptionHover = Color.FromArgb(0x3a, 0x33, 0x55);
    // Windows' own standard close-button hover red - deliberately matching
    // the OS here, since this is the one caption affordance users navigate
    // by muscle memory and color rather than by shape.
    public static readonly Color CloseHover = Color.FromArgb(0xc4, 0x2b, 0x1c);

    private static readonly Color HeaderFrom = Color.FromArgb(0x20, 0x1c, 0x33);
    private static readonly Color HeaderTo = Color.FromArgb(0x2a, 0x22, 0x45);

    public static readonly Font UiFont = new("Segoe UI", 9f);

    /// <summary>The window-header gradient, used by both the custom caption
    /// bar and any banner sitting directly beneath it. Both pass a rectangle
    /// of the same width so the two runs line up seamlessly and read as one
    /// continuous header rather than two stacked bars.</summary>
    public static Brush CreateHeaderBrush(Rectangle rect)
    {
        // A zero-width/height rect throws inside LinearGradientBrush; a
        // control can legitimately be measured at 0 before its first layout.
        if (rect.Width <= 0 || rect.Height <= 0) return new SolidBrush(HeaderFrom);
        return new LinearGradientBrush(rect, HeaderFrom, HeaderTo, LinearGradientMode.Horizontal);
    }

    public static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        int d = Math.Max(1, radius * 2);
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // --- App icon ---------------------------------------------------------
    //
    // app.ico ships four frames (16, 32, 48, 256). Icon.ExtractAssociatedIcon
    // - the obvious way to read back the exe's own icon, and what this app
    // used everywhere - returns only the 32x32 one, so every use was a
    // rescale of that single frame: the 16px tray and caption icons were
    // downscaled from it and the 40px header icon upscaled, which is why they
    // looked soft. Reading the embedded copy instead lets each caller get the
    // frame it actually wants.
    //
    // Icons are cached per size: they are GDI handles, and creating one per
    // paint (as the header did) leaks a handle on every repaint.

    private const string IconResourceName = "SocialBreakTray.app.ico";

    private static readonly Dictionary<int, Icon?> IconCache = new();
    private static Bitmap? _iconMaster;
    private static bool _iconMasterLoaded;

    private static Stream? OpenIconStream() =>
        typeof(Theme).Assembly.GetManifestResourceStream(IconResourceName);

    /// <summary>The multi-frame icon, for Form.Icon / NotifyIcon - handing
    /// Windows the whole set lets it pick the right frame per context
    /// (taskbar, Alt-Tab, tray) instead of scaling one we chose.</summary>
    public static Icon? AppIcon { get; } = LoadAppIcon();

    private static Icon? LoadAppIcon()
    {
        try
        {
            using var stream = OpenIconStream();
            if (stream != null) return new Icon(stream);
        }
        catch
        {
            // Fall through to the Win32 resource below.
        }

        try
        {
            return Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The icon frame that best fits <paramref name="size"/>.</summary>
    public static Icon? GetIcon(int size)
    {
        lock (IconCache)
        {
            if (IconCache.TryGetValue(size, out var cached)) return cached;

            Icon? icon = null;
            try
            {
                using var stream = OpenIconStream();
                // This overload picks the closest frame in the file rather
                // than scaling, so a requested 16 or 48 comes back native.
                if (stream != null) icon = new Icon(stream, new Size(size, size));
            }
            catch
            {
                // Handled by the fallback below.
            }

            icon ??= AppIcon;
            IconCache[size] = icon;
            return icon;
        }
    }

    /// <summary>Draws the app icon into <paramref name="rect"/>. Uses the
    /// native frame when one matches exactly, and otherwise resamples the
    /// 256px master with bicubic filtering - Graphics.DrawIcon stretches a
    /// mismatched frame with nearest-neighbour, which is what made the 40px
    /// header icon look ragged.</summary>
    public static void DrawAppIcon(Graphics g, Rectangle rect)
    {
        var icon = GetIcon(rect.Width);
        if (icon != null && icon.Width == rect.Width && icon.Height == rect.Height)
        {
            g.DrawIconUnstretched(icon, rect);
            return;
        }

        var master = IconMaster();
        if (master == null)
        {
            if (icon != null) g.DrawIcon(icon, rect);
            return;
        }

        var previousInterpolation = g.InterpolationMode;
        var previousOffset = g.PixelOffsetMode;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        // Without this the bicubic sampler is offset by half a pixel, which
        // shows up as a soft edge all the way round a small icon.
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(master, rect);
        g.InterpolationMode = previousInterpolation;
        g.PixelOffsetMode = previousOffset;
    }

    /// <summary>The largest frame, kept as a bitmap to downsample from.</summary>
    private static Bitmap? IconMaster()
    {
        lock (IconCache)
        {
            if (_iconMasterLoaded) return _iconMaster;
            _iconMasterLoaded = true;
            try
            {
                using var stream = OpenIconStream();
                if (stream != null)
                {
                    using var large = new Icon(stream, new Size(256, 256));
                    _iconMaster = large.ToBitmap();
                }
            }
            catch
            {
                _iconMaster = null;
            }
            return _iconMaster;
        }
    }

    // --- Win32 chrome -----------------------------------------------------
    //
    // Windows draws the title bar itself, outside the client area, so no
    // amount of WinForms painting reaches it. DWM attributes are the only
    // supported way to recolor it. Forms that keep the system title bar
    // (the pre-login dialogs) call ApplySystemDarkTitleBar; forms with our
    // own caption bar (DarkForm) call ApplyBorderlessChrome instead, which
    // only asks Win11 for rounded corners and a matching outline.

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModePre20H1 = 19;
    private const int DwmBorderColor = 34;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmCornerRound = 2;

    /// <summary>Turns the system-drawn title bar dark. Supported from
    /// Windows 10 1809 onward; a no-op (failed HRESULT, ignored) on
    /// anything older, which just leaves the light bar as before.</summary>
    public static void ApplySystemDarkTitleBar(Form form) => WhenHandleReady(form, handle =>
    {
        int on = 1;
        // The attribute id changed between 1809 and 20H1. Trying the modern
        // one first and falling back is cheaper and more reliable than
        // gating on an OS build number.
        if (DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref on, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(handle, DwmUseImmersiveDarkModePre20H1, ref on, sizeof(int));
        }
        SetBorderColor(handle);
    });

    /// <summary>Rounded corners and a themed outline for a window that
    /// draws its own caption. Both attributes are Windows 11 only and fail
    /// harmlessly on Windows 10, where the form's own 1px frame is the
    /// border instead.</summary>
    public static void ApplyBorderlessChrome(Form form) => WhenHandleReady(form, handle =>
    {
        int round = DwmCornerRound;
        DwmSetWindowAttribute(handle, DwmWindowCornerPreference, ref round, sizeof(int));
        SetBorderColor(handle);
    });

    private static void SetBorderColor(nint handle)
    {
        // DWM takes a COLORREF (0x00BBGGRR), not ARGB.
        int colorRef = Border.R | (Border.G << 8) | (Border.B << 16);
        DwmSetWindowAttribute(handle, DwmBorderColor, ref colorRef, sizeof(int));
    }

    /// <summary>DWM attributes need a real HWND, which a Form doesn't have
    /// until it's shown - and they're lost if the handle is ever recreated
    /// (a DPI change, a restyle), so this re-applies on every creation
    /// rather than only the first.</summary>
    private static void WhenHandleReady(Form form, Action<nint> apply)
    {
        if (form.IsHandleCreated) apply(form.Handle);
        form.HandleCreated += (_, _) => apply(form.Handle);
    }
}
