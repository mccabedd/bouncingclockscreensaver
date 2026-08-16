using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;

namespace BouncingClockScreensaver;

/// <summary>
/// One instance of this form runs per physical monitor in full-screen (/s) mode,
/// each bouncing independently within its own screen bounds. A single instance is
/// also reused, reparented into a foreign HWND, for the tiny live preview (/p).
/// </summary>
public sealed class ScreensaverForm : Form
{
    private readonly AppSettings _settings;
    private readonly bool _isPreview;
    private readonly IntPtr _previewParentHwnd;
    private readonly Size _virtualSize;

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    private readonly Stopwatch _clock = new();
    private long _lastTicks;

    private PointF _position;   // top-left of the bouncing block, in virtual coordinate space
    private PointF _direction;  // unit vector

    private Image? _logoImage;
    private Bitmap _measureBitmap = new(1, 1);
    private Graphics _measureGraphics;

    // Fonts are expensive GDI objects; cache and only rebuild when the backing
    // settings actually change, rather than allocating one every animation frame.
    private Font? _timeFont;
    private Font? _dateFont;
    private string? _cachedTimeFamily;
    private float _cachedTimeSize;
    private bool _cachedTimeBold;
    private bool _cachedTimeItalic;
    private string? _cachedDateFamily;
    private float _cachedDateSize;
    private bool _cachedDateBold;
    private bool _cachedDateItalic;

    // Ink-bounds cache: the visible glyph bounding box (tight, no font line-height
    // padding) for the current text+font, recomputed only when either changes.
    private string? _cachedTimeInkText;
    private Font? _cachedTimeInkFont;
    private RectangleF _timeInkBounds;
    private string? _cachedDateInkText;
    private Font? _cachedDateInkFont;
    private RectangleF _dateInkBounds;

    // Reserved horizontal width for the time/date blocks, based on the widest
    // string each one's format string could ever render rather than what's
    // currently displayed. With a proportional (non-monospace) font, different
    // rendered text is a different pixel width, so sizing a block from its live
    // text would shift the whole block's — and therefore its neighbors' —
    // position by a few px every time e.g. a digit or month name changes.
    // Reserving a worst-case slot keeps that anchor stable. Cached per (font
    // instance, format string) via reference equality on the font, mirroring
    // the ink-bounds caches above.
    private float _timeSlotWidth;
    private Font? _cachedTimeSlotFont;
    private string? _cachedTimeSlotFormat;
    private float _dateSlotWidth;
    private Font? _cachedDateSlotFont;
    private string? _cachedDateSlotFormat;

    private Point _inputBaseline;      // Cursor.Position recorded at startup, for exit-on-move detection
    private bool _exitRequested;

    public ScreensaverForm(AppSettings settings, Rectangle bounds, bool isPreview, IntPtr previewParentHwnd)
    {
        _settings = settings;
        _isPreview = isPreview;
        _previewParentHwnd = previewParentHwnd;

        _measureGraphics = Graphics.FromImage(_measureBitmap);

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        BackColor = settings.BackgroundColor;

        if (isPreview)
        {
            Bounds = bounds; // tiny preview rect; actual size fixed up in OnLoad once reparented
            _virtualSize = (Screen.PrimaryScreen?.Bounds.Size) is { Width: > 0, Height: > 0 } sz ? sz : new Size(1920, 1080);
        }
        else
        {
            Bounds = bounds; // full physical bounds of the target monitor
            TopMost = true;
            _virtualSize = bounds.Size;
        }

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        DoubleBuffered = true;

        LoadLogo();
    }

    private void LoadLogo()
    {
        _logoImage?.Dispose();
        _logoImage = null;

        if (_settings.EnableLogo && !string.IsNullOrWhiteSpace(_settings.LogoPath) && File.Exists(_settings.LogoPath))
        {
            try
            {
                // Load via memory stream so the source file isn't left locked.
                var bytes = File.ReadAllBytes(_settings.LogoPath);
                using var ms = new MemoryStream(bytes);
                using var loaded = Image.FromStream(ms);
                // Image.FromStream keeps a live reference to the stream for
                // on-demand decoding — it must stay open for the image's whole
                // lifetime. Since `ms` is disposed when this method returns,
                // clone into a self-contained Bitmap that owns its own pixel
                // buffer before that happens.
                _logoImage = new Bitmap(loaded);
            }
            catch
            {
                _logoImage = null;
            }
        }
    }

    private void InitializeMotion()
    {
        var rnd = new Random();
        var block = ComputeLayout(_measureGraphics);

        float maxX = Math.Max(0, _virtualSize.Width - block.BlockWidth);
        float maxY = Math.Max(0, _virtualSize.Height - block.BlockHeight);
        _position = new PointF((float)(rnd.NextDouble() * maxX), (float)(rnd.NextDouble() * maxY));

        // Avoid near-axis-aligned starting angles so the path doesn't look robotic.
        double angle = rnd.NextDouble() * Math.PI * 2;
        _direction = new PointF((float)Math.Cos(angle), (float)Math.Sin(angle));
        if (_direction.X == 0 && _direction.Y == 0)
        {
            _direction = new PointF(0.7071f, 0.7071f);
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        if (_isPreview)
        {
            SetUpAsChildPreview();
        }
        else
        {
            Cursor.Hide();
            _inputBaseline = Cursor.Position;
            MouseMove += ScreensaverForm_MouseMove;
            MouseDown += (_, _) => RequestExit();
            MouseWheel += (_, _) => RequestExit();
            KeyDown += (_, _) => RequestExit();
        }

        // Now that the window exists on its final monitor, swap the measurement
        // Graphics for one sourced from the window itself so text metrics used for
        // bounce-box physics match the DPI actually used when painting (a 1x1 bitmap
        // is fixed at 96 DPI and would drift from real per-monitor scaling).
        _measureGraphics.Dispose();
        _measureBitmap.Dispose();
        _measureGraphics = CreateGraphics();

        InitializeMotion();

        _clock.Start();
        _lastTicks = _clock.ElapsedTicks;
        _timer.Tick += Timer_Tick;
        _timer.Start();
    }

    private void SetUpAsChildPreview()
    {
        if (_previewParentHwnd == IntPtr.Zero) return;

        int style = NativeMethods.GetWindowLong(Handle, NativeMethods.GWL_STYLE);
        const int WS_POPUP = unchecked((int)0x80000000);
        style = (style & ~WS_POPUP) | NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE;
        NativeMethods.SetWindowLong(Handle, NativeMethods.GWL_STYLE, style);
        NativeMethods.SetParent(Handle, _previewParentHwnd);

        NativeMethods.GetClientRect(_previewParentHwnd, out var rect);
        int width = Math.Max(1, rect.Right - rect.Left);
        int height = Math.Max(1, rect.Bottom - rect.Top);
        NativeMethods.SetWindowPos(Handle, IntPtr.Zero, 0, 0, width, height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    private void ScreensaverForm_MouseMove(object? sender, MouseEventArgs e)
    {
        var current = Cursor.Position;
        int dx = current.X - _inputBaseline.X;
        int dy = current.Y - _inputBaseline.Y;
        if ((dx * dx + dy * dy) > 64) // ~8px movement threshold, ignores DPI-scaling jitter
        {
            RequestExit();
        }
    }

    private void RequestExit()
    {
        if (_exitRequested) return;
        _exitRequested = true;
        Cursor.Show();
        Application.Exit();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        long now = _clock.ElapsedTicks;
        double dt = (now - _lastTicks) / (double)Stopwatch.Frequency;
        _lastTicks = now;
        dt = Math.Min(dt, 0.1); // guard against huge jumps (e.g. debugger pause)

        Advance((float)dt);
        Invalidate();
    }

    private void Advance(float dtSeconds)
    {
        if (_settings.MovementSpeed <= 0)
        {
            return;
        }

        var block = ComputeLayout(_measureGraphics);
        float speedPxPerSec = _settings.MovementSpeed * 55f;

        float nx = _position.X + _direction.X * speedPxPerSec * dtSeconds;
        float ny = _position.Y + _direction.Y * speedPxPerSec * dtSeconds;

        float maxX = Math.Max(0, _virtualSize.Width - block.BlockWidth);
        float maxY = Math.Max(0, _virtualSize.Height - block.BlockHeight);

        if (nx < 0)
        {
            nx = 0;
            _direction.X = Math.Abs(_direction.X);
        }
        else if (nx > maxX)
        {
            nx = maxX;
            _direction.X = -Math.Abs(_direction.X);
        }

        if (ny < 0)
        {
            ny = 0;
            _direction.Y = Math.Abs(_direction.Y);
        }
        else if (ny > maxY)
        {
            ny = maxY;
            _direction.Y = -Math.Abs(_direction.Y);
        }

        _position = new PointF(nx, ny);
    }

    private readonly record struct ClockLayout(
        bool ShowTime, string TimeText, Font? TimeFont, RectangleF TimeInk, float TimeY,
        bool ShowDate, string DateText, Font? DateFont, RectangleF DateInk, float DateY,
        bool ShowLogo, SizeF LogoSize, float LogoY,
        float BlockWidth, float BlockHeight);

    private static Font CreateFont(string family, float size, bool bold, bool italic)
    {
        FontStyle style = FontStyle.Regular;
        if (bold) style |= FontStyle.Bold;
        if (italic) style |= FontStyle.Italic;

        try
        {
            return new Font(family, Math.Max(1f, size), style, GraphicsUnit.Point);
        }
        catch
        {
            try
            {
                return new Font("Segoe UI", Math.Max(1f, size), style, GraphicsUnit.Point);
            }
            catch
            {
                return new Font("Segoe UI", Math.Max(1f, size), FontStyle.Regular, GraphicsUnit.Point);
            }
        }
    }

    /// <summary>Rebuilds cached Font objects only when the backing settings actually changed.</summary>
    private void EnsureFonts()
    {
        if (_settings.ShowTime)
        {
            if (_timeFont == null || _cachedTimeFamily != _settings.TimeFontFamily || _cachedTimeSize != _settings.TimeFontSize
                || _cachedTimeBold != _settings.TimeBold || _cachedTimeItalic != _settings.TimeItalic)
            {
                _timeFont?.Dispose();
                _timeFont = CreateFont(_settings.TimeFontFamily, _settings.TimeFontSize, _settings.TimeBold, _settings.TimeItalic);
                _cachedTimeFamily = _settings.TimeFontFamily;
                _cachedTimeSize = _settings.TimeFontSize;
                _cachedTimeBold = _settings.TimeBold;
                _cachedTimeItalic = _settings.TimeItalic;
            }
        }
        else if (_timeFont != null)
        {
            _timeFont.Dispose();
            _timeFont = null;
            _cachedTimeFamily = null;
        }

        if (_settings.ShowDate)
        {
            if (_dateFont == null || _cachedDateFamily != _settings.DateFontFamily || _cachedDateSize != _settings.DateFontSize
                || _cachedDateBold != _settings.DateBold || _cachedDateItalic != _settings.DateItalic)
            {
                _dateFont?.Dispose();
                _dateFont = CreateFont(_settings.DateFontFamily, _settings.DateFontSize, _settings.DateBold, _settings.DateItalic);
                _cachedDateFamily = _settings.DateFontFamily;
                _cachedDateSize = _settings.DateFontSize;
                _cachedDateBold = _settings.DateBold;
                _cachedDateItalic = _settings.DateItalic;
            }
        }
        else if (_dateFont != null)
        {
            _dateFont.Dispose();
            _dateFont = null;
            _cachedDateFamily = null;
        }
    }

    /// <summary>
    /// Tight bounding box of the actual rendered glyph ink for <paramref name="text"/>,
    /// as opposed to Graphics.MeasureString's box (which includes the font's full
    /// line metrics — ascent/descent/leading — and so is taller than what's visibly
    /// drawn for strings with no descenders, like a digit clock). Uses the classic
    /// GraphicsPath.AddString + GetBounds() trick, in the same coordinate space and
    /// StringFormat that DrawString uses, so ink offsets line up with real draws.
    /// </summary>
    private static RectangleF MeasureInkBounds(Graphics g, string text, Font font)
    {
        using var path = new GraphicsPath();
        float emSizePx = font.SizeInPoints * g.DpiY / 72f;
        path.AddString(text, font.FontFamily, (int)font.Style, emSizePx, PointF.Empty, StringFormat.GenericTypographic);
        return path.GetBounds();
    }

    private RectangleF GetTimeInk(Graphics g, string text)
    {
        if (_cachedTimeInkText != text || _cachedTimeInkFont != _timeFont)
        {
            _timeInkBounds = MeasureInkBounds(g, text, _timeFont!);
            _cachedTimeInkText = text;
            _cachedTimeInkFont = _timeFont;
        }
        return _timeInkBounds;
    }

    private RectangleF GetDateInk(Graphics g, string text)
    {
        if (_dateFont == null) return RectangleF.Empty;
        if (_cachedDateInkText != text || _cachedDateInkFont != _dateFont)
        {
            _dateInkBounds = MeasureInkBounds(g, text, _dateFont);
            _cachedDateInkText = text;
            _cachedDateInkFont = _dateFont;
        }
        return _dateInkBounds;
    }

    /// <summary>
    /// Formats <paramref name="dt"/> with a user-supplied custom .NET date/time
    /// format string, falling back to <paramref name="fallbackFormat"/> (and, if
    /// even that somehow fails, the culture default) if the format is invalid.
    /// The screensaver runs unattended and repaints ~60x/second, so a typo in a
    /// user's format string must never be allowed to throw out of OnPaint.
    /// </summary>
    private static string SafeFormat(DateTime dt, string format, string fallbackFormat)
    {
        try
        {
            return dt.ToString(format, CultureInfo.CurrentCulture);
        }
        catch
        {
            try
            {
                return dt.ToString(fallbackFormat, CultureInfo.CurrentCulture);
            }
            catch
            {
                return dt.ToString(CultureInfo.CurrentCulture);
            }
        }
    }

    /// <summary>
    /// Ink width of the widest text <paramref name="format"/> could ever render in
    /// <paramref name="font"/>, found by formatting a spread of representative
    /// DateTime values (every month and weekday name, every hour/AM-PM crossing,
    /// and a spread of minute/second digit combinations) and measuring the widest
    /// result. This works for any valid .NET custom format string — numeric,
    /// month/day names, AM/PM, literals — without needing to parse the format
    /// string ourselves. Formats that throw for a given sample are simply skipped.
    /// </summary>
    private static float ComputeWorstCaseWidth(Graphics g, Font font, string format)
    {
        float maxWidth = 0f;
        bool any = false;

        void Consider(DateTime dt)
        {
            string text;
            try
            {
                text = dt.ToString(format, CultureInfo.CurrentCulture);
            }
            catch
            {
                return;
            }
            float w = MeasureInkBounds(g, text, font).Width;
            if (w > maxWidth) maxWidth = w;
            any = true;
        }

        for (int month = 1; month <= 12; month++)
        {
            Consider(new DateTime(2020, month, 15, 13, 45, 45));
        }
        for (int i = 0; i < 7; i++)
        {
            Consider(new DateTime(2023, 1, 1 + i, 13, 45, 45)); // 2023-01-01 was a Sunday
        }
        for (int hour = 0; hour < 24; hour++)
        {
            Consider(new DateTime(2020, 6, 15, hour, 58, 58));
        }
        foreach (int v in new[] { 0, 11, 22, 33, 44, 55, 58, 59 })
        {
            Consider(new DateTime(2020, 6, 15, 13, v, 45));
            Consider(new DateTime(2020, 6, 15, 13, 45, v));
        }

        if (!any)
        {
            // Format string is entirely invalid for every sample — fall back to
            // measuring it literally so the reserved width is at least sane;
            // SafeFormat's own fallback is what actually gets painted.
            return MeasureInkBounds(g, format, font).Width;
        }

        return maxWidth;
    }

    /// <summary>Recomputes <see cref="_timeSlotWidth"/> only when the time font or format changed.</summary>
    private void EnsureTimeSlotWidth(Graphics g)
    {
        if (_timeFont == null) return;
        if (_cachedTimeSlotFont == _timeFont && _cachedTimeSlotFormat == _settings.TimeFormat && _timeSlotWidth > 0)
        {
            return;
        }
        _timeSlotWidth = ComputeWorstCaseWidth(g, _timeFont, _settings.TimeFormat);
        _cachedTimeSlotFont = _timeFont;
        _cachedTimeSlotFormat = _settings.TimeFormat;
    }

    /// <summary>Recomputes <see cref="_dateSlotWidth"/> only when the date font or format changed.</summary>
    private void EnsureDateSlotWidth(Graphics g)
    {
        if (_dateFont == null) return;
        if (_cachedDateSlotFont == _dateFont && _cachedDateSlotFormat == _settings.DateFormat && _dateSlotWidth > 0)
        {
            return;
        }
        _dateSlotWidth = ComputeWorstCaseWidth(g, _dateFont, _settings.DateFormat);
        _cachedDateSlotFont = _dateFont;
        _cachedDateSlotFormat = _settings.DateFormat;
    }

    /// <summary>Left/Center/Right position of an element within the shared block width.</summary>
    private static float AlignX(float blockLeft, float blockWidth, float elementWidth, Justification justification) => justification switch
    {
        Justification.Left => blockLeft,
        Justification.Right => blockLeft + blockWidth - elementWidth,
        _ => blockLeft + (blockWidth - elementWidth) / 2f,
    };

    private ClockLayout ComputeLayout(Graphics g)
    {
        EnsureFonts();

        var now = DateTime.Now;
        bool showTime = _settings.ShowTime;
        bool showDate = _settings.ShowDate;

        string timeText = showTime ? SafeFormat(now, _settings.TimeFormat, "HH:mm:ss") : string.Empty;
        string dateText = showDate ? SafeFormat(now, _settings.DateFormat, "ddd, d MMMM yyyy") : string.Empty;

        RectangleF timeInk = RectangleF.Empty;
        if (showTime)
        {
            timeInk = GetTimeInk(g, timeText);
            EnsureTimeSlotWidth(g);
        }
        RectangleF dateInk = RectangleF.Empty;
        if (showDate)
        {
            dateInk = GetDateInk(g, dateText);
            EnsureDateSlotWidth(g);
        }

        float gap = Math.Max(6f, _settings.TimeFontSize * 0.18f);

        bool showLogo = _settings.EnableLogo && _logoImage != null;
        SizeF logoSize = SizeF.Empty;
        if (showLogo)
        {
            // Base size is screen-relative, NOT derived from TimeFontSize — it
            // previously was (0.8x), which meant a small Time font (or a Time
            // font set small independently of the logo) forced even a huge
            // source image down to a sliver, with no way to fix it from the
            // Logo tab itself. 12% of the screen's height is the slider's "0"
            // (default) position; each step is still a 15% adjustment.
            float baseHeight = _virtualSize.Height * 0.12f;
            float scale = Math.Max(0.1f, 1f + _settings.LogoSizeAdjust * 0.15f);
            float targetHeight = Math.Clamp(baseHeight * scale, 16f, 800f);
            float aspect = _logoImage!.Width / (float)Math.Max(1, _logoImage.Height);
            float targetWidth = targetHeight * aspect;

            // Safety net, independent of the above: never let the logo's
            // footprint exceed a generous but bounded share of the screen.
            // The height-driven sizing above only accounts for the Time font
            // size and the slider — an extreme-aspect-ratio source image (a
            // wide banner logo, say) or a large Time font could otherwise
            // still compute a width that overflows the monitor, since nothing
            // above ever looks at _virtualSize.
            float maxWidth = _virtualSize.Width * 0.85f;
            float maxHeight = _virtualSize.Height * 0.55f;
            if (targetWidth > maxWidth)
            {
                float s = maxWidth / targetWidth;
                targetWidth *= s;
                targetHeight *= s;
            }
            if (targetHeight > maxHeight)
            {
                float s = maxHeight / targetHeight;
                targetWidth *= s;
                targetHeight *= s;
            }

            logoSize = new SizeF(targetWidth, targetHeight);
        }

        // Stack whichever of time/logo/date are actually enabled, top to bottom,
        // in the user-chosen element order. NormalizeElementOrder guarantees all
        // three appear exactly once even from a hand-edited settings file, so an
        // enabled element can never silently fail to be added here. Building this
        // list from what's actually present (rather than always assuming all
        // three) means any combination — time-only, logo+date, date-only, etc. —
        // lays out correctly with no special-casing.
        var order = new List<(ClockElement Kind, float Height)>(3);
        foreach (var kind in AppSettings.NormalizeElementOrder(_settings.ElementOrder))
        {
            switch (kind)
            {
                case ClockElement.Time when showTime: order.Add((kind, timeInk.Height)); break;
                case ClockElement.Logo when showLogo: order.Add((kind, logoSize.Height)); break;
                case ClockElement.Date when showDate: order.Add((kind, dateInk.Height)); break;
            }
        }

        float timeY = 0f, logoY = 0f, dateY = 0f, cursor = 0f;
        for (int i = 0; i < order.Count; i++)
        {
            var (kind, height) = order[i];
            switch (kind)
            {
                case ClockElement.Time: timeY = cursor; break;
                case ClockElement.Logo: logoY = cursor; break;
                case ClockElement.Date: dateY = cursor; break;
            }
            cursor += height;
            if (i < order.Count - 1) cursor += gap;
        }

        // Use the reserved (worst-case) slot widths, not the live text's ink
        // width, so the block's footprint — and every element's aligned position
        // within it — doesn't shift every time the rendered text changes shape.
        float blockWidth = 0f;
        if (showTime) blockWidth = Math.Max(blockWidth, _timeSlotWidth);
        if (showDate) blockWidth = Math.Max(blockWidth, _dateSlotWidth);
        if (showLogo) blockWidth = Math.Max(blockWidth, logoSize.Width);

        return new ClockLayout(
            showTime, timeText, _timeFont, timeInk, timeY,
            showDate, dateText, _dateFont, dateInk, dateY,
            showLogo, logoSize, logoY,
            blockWidth, cursor);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        g.Clear(_settings.BackgroundColor);

        float scaleX = _isPreview ? ClientSize.Width / (float)_virtualSize.Width : 1f;
        float scaleY = _isPreview ? ClientSize.Height / (float)_virtualSize.Height : 1f;
        var savedState = g.Save();
        if (_isPreview)
        {
            g.ScaleTransform(scaleX, scaleY);
        }

        var layout = ComputeLayout(g);

        float blockLeft = _position.X;
        float blockTop = _position.Y;

        // Each element is drawn so the visible glyph ink's top edge (not the
        // font's nominal line-box top) sits at its slot's Y, and the ink itself
        // (not the line box) is aligned per that element's own Left/Center/Right
        // justification within the shared block width.
        if (layout.ShowTime && layout.TimeFont != null)
        {
            float elementLeft = AlignX(blockLeft, layout.BlockWidth, layout.TimeInk.Width, _settings.TimeJustification);
            float drawX = elementLeft - layout.TimeInk.X;
            float drawY = blockTop + layout.TimeY - layout.TimeInk.Y;
            DrawGlowString(g, layout.TimeText, layout.TimeFont, _settings.TimeColor, new PointF(drawX, drawY));
        }

        if (layout.ShowLogo && _logoImage != null)
        {
            float elementLeft = AlignX(blockLeft, layout.BlockWidth, layout.LogoSize.Width, _settings.LogoJustification);
            var logoRect = new RectangleF(elementLeft, blockTop + layout.LogoY, layout.LogoSize.Width, layout.LogoSize.Height);
            g.DrawImage(_logoImage, logoRect);
        }

        if (layout.ShowDate && layout.DateFont != null)
        {
            float elementLeft = AlignX(blockLeft, layout.BlockWidth, layout.DateInk.Width, _settings.DateJustification);
            float drawX = elementLeft - layout.DateInk.X;
            float drawY = blockTop + layout.DateY - layout.DateInk.Y;
            DrawGlowString(g, layout.DateText, layout.DateFont, _settings.DateColor, new PointF(drawX, drawY), glow: false);
        }

        g.Restore(savedState);
    }

    // Unit directions for the halo pass; DrawGlowString scales these by a
    // magnitude derived from the font size (see below) rather than using a
    // fixed pixel offset — a fixed offset looked like a smudgy blur on a
    // small font and was barely visible on a large one.
    private static readonly PointF[] GlowDirections =
    {
        new(-1f, 0f), new(1f, 0f), new(0f, -1f), new(0f, 1f),
        new(-0.7071f, -0.7071f), new(0.7071f, 0.7071f), new(-0.7071f, 0.7071f), new(0.7071f, -0.7071f),
    };

    // Must match the StringFormat used by MeasureInkBounds (GraphicsPath.AddString),
    // or the ink-bounds math used to center this text is measuring a different glyph
    // layout than what actually gets painted — the default DrawString overload
    // implicitly uses StringFormat.GenericDefault, which adds extra side bearing
    // that GenericTypographic doesn't, so text would render off-center from where
    // the centering math placed it (worse for larger fonts, since the mismatch is
    // proportional to font size — this is what made the time text drift furthest).
    private static readonly StringFormat InkStringFormat = StringFormat.GenericTypographic;

    private static void DrawGlowString(Graphics g, string text, Font font, Color color, PointF topLeft, bool glow = true)
    {
        if (glow)
        {
            // Halo offset as a proportion of the font's point size (clamped to a
            // sane pixel range), so the glow reads as a consistent, tight halo
            // whether the Time font is set tiny or huge.
            float magnitude = Math.Clamp(font.SizeInPoints * 0.04f, 1f, 6f);
            using var haloBrush = new SolidBrush(Color.FromArgb(50, color));
            foreach (var dir in GlowDirections)
            {
                g.DrawString(text, font, haloBrush, topLeft.X + dir.X * magnitude, topLeft.Y + dir.Y * magnitude, InkStringFormat);
            }
        }

        using var mainBrush = new SolidBrush(color);
        g.DrawString(text, font, mainBrush, topLeft.X, topLeft.Y, InkStringFormat);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _timer.Tick -= Timer_Tick;
        _logoImage?.Dispose();
        _timeFont?.Dispose();
        _dateFont?.Dispose();
        _measureGraphics.Dispose();
        _measureBitmap.Dispose();
        if (!_isPreview)
        {
            Cursor.Show();
        }
        base.OnFormClosed(e);
    }
}
