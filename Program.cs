namespace BouncingClockScreensaver;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize(); // sets PerMonitorV2 DPI mode (see csproj) + visual styles

        // A screensaver runs unattended (often on a locked machine); an unhandled
        // exception must never leave a crash dialog sitting on top of the screen.
        // Swallow, log best-effort, and exit quietly instead.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => HandleFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => HandleFatal(e.ExceptionObject as Exception);

        var mode = ParseMode(args, out IntPtr hwndArg);

        try
        {
            switch (mode)
            {
                case Mode.RunScreensaver:
                    RunScreensaver();
                    break;

                case Mode.Preview:
                    RunPreview(hwndArg);
                    break;

                case Mode.Configure:
                default:
                    RunConfig(hwndArg);
                    break;
            }
        }
        catch (Exception ex)
        {
            HandleFatal(ex);
        }
    }

    private static void HandleFatal(Exception? ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BouncingClockScreensaver");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"), $"{DateTime.Now:O}: {ex}\n\n");
        }
        catch
        {
            // Logging is best-effort; never let the logger itself crash the process.
        }

        Environment.Exit(1);
    }

    private enum Mode
    {
        Configure,
        RunScreensaver,
        Preview,
    }

    /// <summary>
    /// Implements the standard Windows screensaver command-line contract:
    ///   /s          run full-screen
    ///   /c, /c:HWND show the settings dialog
    ///   /p HWND     render into the given preview window
    ///   (no args)   treat as /c
    /// Accepts both "/" and "-" switch prefixes and is case-insensitive, since some
    /// launchers (and manual testing from a shell) pass either form.
    /// </summary>
    private static Mode ParseMode(string[] args, out IntPtr hwnd)
    {
        hwnd = IntPtr.Zero;

        if (args.Length == 0)
        {
            return Mode.Configure;
        }

        string first = args[0].Trim();
        string normalized = first.ToLowerInvariant();

        if (normalized.StartsWith("/s") || normalized.StartsWith("-s"))
        {
            return Mode.RunScreensaver;
        }

        if (normalized.StartsWith("/p") || normalized.StartsWith("-p"))
        {
            // Either "/p:1234" / "/p1234" in one token, or "/p 1234" as two tokens.
            string digits = new string(normalized.SkipWhile(c => !char.IsDigit(c)).ToArray());
            if (digits.Length == 0 && args.Length > 1)
            {
                digits = args[1].Trim();
            }
            if (long.TryParse(digits, out long handleValue))
            {
                hwnd = new IntPtr(handleValue);
            }
            return Mode.Preview;
        }

        if (normalized.StartsWith("/c") || normalized.StartsWith("-c"))
        {
            string digits = new string(normalized.SkipWhile(c => !char.IsDigit(c) && c != ':').ToArray()).TrimStart(':');
            if (digits.Length == 0 && args.Length > 1)
            {
                digits = args[1].Trim();
            }
            if (long.TryParse(digits, out long handleValue))
            {
                hwnd = new IntPtr(handleValue);
            }
            return Mode.Configure;
        }

        return Mode.Configure;
    }

    private static void RunConfig(IntPtr ownerHwnd)
    {
        var settings = SettingsManager.Load();
        using var dlg = new SettingsForm(settings, ownerHwnd);
        dlg.ShowDialog();
    }

    private static void RunPreview(IntPtr previewHwnd)
    {
        if (previewHwnd == IntPtr.Zero)
        {
            return; // nothing sensible to render into
        }

        var settings = SettingsManager.Load();
        NativeMethods.GetClientRect(previewHwnd, out var rect);
        var bounds = new Rectangle(0, 0,
            Math.Max(1, rect.Right - rect.Left),
            Math.Max(1, rect.Bottom - rect.Top));

        using var form = new ScreensaverForm(settings, bounds, isPreview: true, previewHwnd);
        Application.Run(form);
    }

    private static void RunScreensaver()
    {
        var settings = SettingsManager.Load();
        var forms = new List<ScreensaverForm>();

        foreach (var screen in Screen.AllScreens)
        {
            var form = new ScreensaverForm(settings, screen.Bounds, isPreview: false, IntPtr.Zero);
            forms.Add(form);
            form.Show();
        }

        // Give keyboard focus to the form on the primary monitor so KeyDown-based
        // exit works predictably; every form still independently reacts to mouse
        // input regardless of which one currently has focus.
        var primary = forms.FirstOrDefault(f => f.Bounds == Screen.PrimaryScreen?.Bounds) ?? forms.FirstOrDefault();
        primary?.Activate();

        if (forms.Count == 0)
        {
            return;
        }

        Application.Run();

        foreach (var form in forms)
        {
            form.Dispose();
        }
    }
}
