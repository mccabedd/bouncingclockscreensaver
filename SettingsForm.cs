using System.Diagnostics;
using System.Globalization;

namespace BouncingClockScreensaver;

/// <summary>
/// The screensaver's "Settings..." dialog, opened via /c. Organized into tabs —
/// Time, Date, Logo, Layout, General — since the number of options has grown
/// past what fits comfortably on one flat page. OK/Apply/Cancel stay fixed
/// along the bottom, outside the tab strip, as in classic Windows dialogs.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly IntPtr _ownerHwnd;

    // ---- Time tab ----
    private CheckBox _showTimeCheck = null!;
    private TextBox _timeFormatBox = null!;
    private Button _timeFormatHelpButton = null!;
    private Label _timeFormatPreview = null!;
    private ComboBox _timeFontCombo = null!;
    private NumericUpDown _timeSizeUpDown = null!;
    private Button _timeColorButton = null!;
    private CheckBox _timeBoldCheck = null!;
    private CheckBox _timeItalicCheck = null!;
    private ComboBox _timeJustificationCombo = null!;

    // ---- Date tab ----
    private CheckBox _showDateCheck = null!;
    private TextBox _dateFormatBox = null!;
    private Button _dateFormatHelpButton = null!;
    private Label _dateFormatPreview = null!;
    private ComboBox _dateFontCombo = null!;
    private NumericUpDown _dateSizeUpDown = null!;
    private Button _dateColorButton = null!;
    private CheckBox _dateBoldCheck = null!;
    private CheckBox _dateItalicCheck = null!;
    private ComboBox _dateJustificationCombo = null!;

    // ---- Logo tab ----
    private CheckBox _enableLogoCheck = null!;
    private Button _logoBrowseButton = null!;
    private TextBox _logoPathBox = null!;
    private TrackBar _logoSizeTrack = null!;
    private Label _logoSizeValueLabel = null!;
    private ComboBox _logoJustificationCombo = null!;

    // ---- Layout tab ----
    private ListBox _orderList = null!;
    private Button _orderUpButton = null!;
    private Button _orderDownButton = null!;

    // ---- General tab ----
    private NumericUpDown _speedUpDown = null!;
    private Button _backgroundButton = null!;
    private CheckBox _monospaceOnlyCheck = null!;

    private Color _timeColor;
    private Color _dateColor;
    private Color _backgroundColor;

    private const string TimeFormatHelpText =
@"Time format uses .NET's custom date/time format specifiers (the
same family of idea as a Windows GetTimeFormatEx picture string,
adapted for this app). Tokens are case-sensitive.

  h / hh     12-hour clock          (7 / 07)
  H / HH     24-hour clock          (7 / 07)
  m / mm     minutes                (6 / 06)
  s / ss     seconds                (9 / 09)
  t / tt     AM/PM designator       (A / AM)
  f...fff    fractional seconds     (1-3 digits)
  'text'     literal text, e.g. 'at'

Anything that isn't a recognized token (spaces, colons, commas,
brackets, pipes, etc.) is shown exactly as typed.

Examples:
  HH:mm:ss           ->  07:46:29
  hh:mm:ss tt        ->  07:46:29 AM
  H:mm 'hrs'         ->  7:46 hrs";

    private const string DateFormatHelpText =
@"Date format uses .NET's custom date/time format specifiers (the
same family of idea as the Windows day/month/year/era picture
format). Tokens are case-sensitive — use lowercase 'dd'/'yyyy',
not 'DD'/'YYYY'.

  d / dd       day of month            (6 / 06)
  ddd / dddd   short / full day name   (Sun / Sunday)
  M / MM       month number            (8 / 08)
  MMM / MMMM   short / full month      (Aug / August)
  yy / yyyy    2 / 4-digit year        (26 / 2026)
  gg           era                     (A.D.)
  'text'       literal text, e.g. 'the'

Anything that isn't a recognized token (spaces, commas, brackets,
pipes, dashes, etc.) is shown exactly as typed, so formats and
literal text can be freely mixed:

  ddd, dd MMMM yyyy                 ->  Sun, 16 August 2026
  ddd, dd MMMM yyyy | [yyyy-MM-dd]  ->  Sun, 16 August 2026 | [2026-08-16]";

    public SettingsForm(AppSettings settings, IntPtr ownerHwnd = default)
    {
        _settings = settings.Clone();
        _ownerHwnd = ownerHwnd;
        _timeColor = _settings.TimeColor;
        _dateColor = _settings.DateColor;
        _backgroundColor = _settings.BackgroundColor;

        Text = "Bouncing Clock Screensaver Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = SystemFonts.MessageBoxFont;

        BuildLayout();
        PopulateFromSettings();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_ownerHwnd != IntPtr.Zero)
        {
            NativeMethods.SetParent(Handle, IntPtr.Zero); // dialog stays top-level; owner is informational only
        }
    }

    private static Label MakeLabel(string text, int x, int y) => new()
    {
        Text = text,
        Location = new Point(x, y),
        AutoSize = true,
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private static ComboBox MakeJustificationCombo(int x, int y, int width)
    {
        var combo = new ComboBox
        {
            Location = new Point(x, y),
            Width = width,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        combo.Items.AddRange(Enum.GetValues<Justification>().Cast<object>().ToArray());
        return combo;
    }

    private void BuildLayout()
    {
        var tabs = new TabControl { Location = new Point(12, 12), Size = new Size(560, 290) };

        var timeTab = new TabPage("Time");
        var dateTab = new TabPage("Date");
        var logoTab = new TabPage("Logo");
        var layoutTab = new TabPage("Layout");
        var generalTab = new TabPage("General");

        BuildTimeTab(timeTab);
        BuildDateTab(dateTab);
        BuildLogoTab(logoTab);
        BuildLayoutTab(layoutTab);
        BuildGeneralTab(generalTab);

        tabs.TabPages.AddRange(new[] { timeTab, dateTab, logoTab, layoutTab, generalTab });
        Controls.Add(tabs);

        ClientSize = new Size(584, tabs.Bottom + 78);

        var creditLabel = new Label
        {
            Text = "Created by David McCabe",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Font = new Font(Font.FontFamily, Font.Size - 1f, FontStyle.Italic),
        };
        creditLabel.Location = new Point(ClientSize.Width - 16 - creditLabel.PreferredWidth, tabs.Bottom + 8);
        Controls.Add(creditLabel);

        var githubLink = new LinkLabel
        {
            Text = "github.com/mccabedd/bouncingclockscreensaver",
            AutoSize = true,
            Font = new Font(Font.FontFamily, Font.Size - 1f),
        };
        githubLink.Location = new Point(ClientSize.Width - 16 - githubLink.PreferredWidth, creditLabel.Bottom + 2);
        githubLink.LinkClicked += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://github.com/mccabedd/bouncingclockscreensaver") { UseShellExecute = true });
            }
            catch
            {
                // Best-effort — no browser available or no default handler registered.
                // This dialog is always interactive (never runs unattended), but there's
                // no useful fallback if launching the browser fails, so just ignore it.
            }
        };
        Controls.Add(githubLink);

        var okButton = new Button { Text = "OK", Location = new Point(ClientSize.Width - 260, ClientSize.Height - 40), Width = 80 };
        var applyButton = new Button { Text = "Apply", Location = new Point(ClientSize.Width - 172, ClientSize.Height - 40), Width = 80 };
        var cancelButton = new Button { Text = "Cancel", Location = new Point(ClientSize.Width - 84, ClientSize.Height - 40), Width = 68 };

        okButton.Click += (_, _) => { ApplySettings(); DialogResult = DialogResult.OK; Close(); };
        applyButton.Click += (_, _) => ApplySettings();
        cancelButton.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.Add(okButton);
        Controls.Add(applyButton);
        Controls.Add(cancelButton);

        AcceptButton = okButton;
        CancelButton = cancelButton;
    }

    private void BuildTimeTab(TabPage page)
    {
        const int labelX = 16, controlX = 130, rowH = 30;
        int y = 16;

        _showTimeCheck = new CheckBox { Text = "Show Time", Location = new Point(labelX, y), AutoSize = true };
        _showTimeCheck.CheckedChanged += (_, _) => UpdateTimeControlsEnabled();
        page.Controls.Add(_showTimeCheck);
        y += rowH + 4;

        page.Controls.Add(MakeLabel("Format:", labelX, y + 3));
        _timeFormatBox = new TextBox { Location = new Point(controlX, y), Width = 190 };
        _timeFormatBox.TextChanged += (_, _) => UpdateTimeFormatPreview();
        page.Controls.Add(_timeFormatBox);
        _timeFormatHelpButton = new Button { Text = "?", Location = new Point(controlX + 196, y - 1), Width = 26 };
        _timeFormatHelpButton.Click += (_, _) => ShowFormatHelp(TimeFormatHelpText, "Time Format Help");
        page.Controls.Add(_timeFormatHelpButton);
        y += rowH;

        page.Controls.Add(MakeLabel("Preview:", labelX, y + 3));
        _timeFormatPreview = new Label { Location = new Point(controlX, y + 3), AutoSize = true, ForeColor = SystemColors.GrayText };
        page.Controls.Add(_timeFormatPreview);
        y += rowH + 6;

        page.Controls.Add(MakeLabel("Font:", labelX, y + 3));
        _timeFontCombo = new ComboBox { Location = new Point(controlX, y), Width = 190, DropDownStyle = ComboBoxStyle.DropDownList };
        page.Controls.Add(_timeFontCombo);
        y += rowH;

        page.Controls.Add(MakeLabel("Size:", labelX, y + 3));
        _timeSizeUpDown = new NumericUpDown { Location = new Point(controlX, y), Width = 70, Minimum = 8, Maximum = 400 };
        page.Controls.Add(_timeSizeUpDown);
        page.Controls.Add(MakeLabel("Color:", controlX + 90, y + 3));
        _timeColorButton = new Button { Text = "Choose...", Location = new Point(controlX + 150, y - 2), Width = 110, FlatStyle = FlatStyle.System };
        _timeColorButton.Click += (_, _) => PickColor(ref _timeColor, _timeColorButton);
        page.Controls.Add(_timeColorButton);
        y += rowH;

        _timeBoldCheck = new CheckBox { Text = "Bold", Location = new Point(controlX, y), AutoSize = true };
        _timeItalicCheck = new CheckBox { Text = "Italic", Location = new Point(controlX + 70, y), AutoSize = true };
        page.Controls.Add(_timeBoldCheck);
        page.Controls.Add(_timeItalicCheck);
        y += rowH + 6;

        page.Controls.Add(MakeLabel("Justification:", labelX, y + 3));
        _timeJustificationCombo = MakeJustificationCombo(controlX, y, 120);
        page.Controls.Add(_timeJustificationCombo);
    }

    private void BuildDateTab(TabPage page)
    {
        const int labelX = 16, controlX = 130, rowH = 30;
        int y = 16;

        _showDateCheck = new CheckBox { Text = "Show Date", Location = new Point(labelX, y), AutoSize = true };
        _showDateCheck.CheckedChanged += (_, _) => UpdateDateControlsEnabled();
        page.Controls.Add(_showDateCheck);
        y += rowH + 4;

        page.Controls.Add(MakeLabel("Format:", labelX, y + 3));
        _dateFormatBox = new TextBox { Location = new Point(controlX, y), Width = 190 };
        _dateFormatBox.TextChanged += (_, _) => UpdateDateFormatPreview();
        page.Controls.Add(_dateFormatBox);
        _dateFormatHelpButton = new Button { Text = "?", Location = new Point(controlX + 196, y - 1), Width = 26 };
        _dateFormatHelpButton.Click += (_, _) => ShowFormatHelp(DateFormatHelpText, "Date Format Help");
        page.Controls.Add(_dateFormatHelpButton);
        y += rowH;

        page.Controls.Add(MakeLabel("Preview:", labelX, y + 3));
        _dateFormatPreview = new Label { Location = new Point(controlX, y + 3), AutoSize = true, ForeColor = SystemColors.GrayText };
        page.Controls.Add(_dateFormatPreview);
        y += rowH + 6;

        page.Controls.Add(MakeLabel("Font:", labelX, y + 3));
        _dateFontCombo = new ComboBox { Location = new Point(controlX, y), Width = 190, DropDownStyle = ComboBoxStyle.DropDownList };
        page.Controls.Add(_dateFontCombo);
        y += rowH;

        page.Controls.Add(MakeLabel("Size:", labelX, y + 3));
        _dateSizeUpDown = new NumericUpDown { Location = new Point(controlX, y), Width = 70, Minimum = 6, Maximum = 200 };
        page.Controls.Add(_dateSizeUpDown);
        page.Controls.Add(MakeLabel("Color:", controlX + 90, y + 3));
        _dateColorButton = new Button { Text = "Choose...", Location = new Point(controlX + 150, y - 2), Width = 110, FlatStyle = FlatStyle.System };
        _dateColorButton.Click += (_, _) => PickColor(ref _dateColor, _dateColorButton);
        page.Controls.Add(_dateColorButton);
        y += rowH;

        _dateBoldCheck = new CheckBox { Text = "Bold", Location = new Point(controlX, y), AutoSize = true };
        _dateItalicCheck = new CheckBox { Text = "Italic", Location = new Point(controlX + 70, y), AutoSize = true };
        page.Controls.Add(_dateBoldCheck);
        page.Controls.Add(_dateItalicCheck);
        y += rowH + 6;

        page.Controls.Add(MakeLabel("Justification:", labelX, y + 3));
        _dateJustificationCombo = MakeJustificationCombo(controlX, y, 120);
        page.Controls.Add(_dateJustificationCombo);
    }

    private void BuildLogoTab(TabPage page)
    {
        const int labelX = 16, controlX = 130;

        // The Logo tab has fewer field groups than Time/Date, but shares the
        // same TabControl size (so all tabs stay visually consistent) — these
        // offsets are spread out deliberately, rather than reusing the tighter
        // rowH used above, so the tab doesn't end in a large empty gap.
        _enableLogoCheck = new CheckBox { Text = "Enable Logo", Location = new Point(labelX, 16), AutoSize = true };
        _enableLogoCheck.CheckedChanged += (_, _) => UpdateLogoControlsEnabled();
        page.Controls.Add(_enableLogoCheck);

        const int imageY = 76;
        page.Controls.Add(MakeLabel("Image:", labelX, imageY + 3));
        _logoPathBox = new TextBox { Location = new Point(controlX, imageY), Width = 260, ReadOnly = true };
        page.Controls.Add(_logoPathBox);
        _logoBrowseButton = new Button { Text = "Browse...", Location = new Point(controlX + 268, imageY - 2), Width = 100 };
        _logoBrowseButton.Click += LogoBrowseButton_Click;
        page.Controls.Add(_logoBrowseButton);

        const int sizeY = 136;
        page.Controls.Add(MakeLabel("Size (-10..+10):", labelX, sizeY + 3));
        _logoSizeTrack = new TrackBar
        {
            Location = new Point(controlX - 4, sizeY - 4),
            Width = 260,
            Minimum = -10,
            Maximum = 10,
            TickFrequency = 1,
            LargeChange = 1,
            SmallChange = 1,
        };
        _logoSizeTrack.ValueChanged += (_, _) => _logoSizeValueLabel.Text = _logoSizeTrack.Value.ToString();
        page.Controls.Add(_logoSizeTrack);
        _logoSizeValueLabel = new Label { Location = new Point(controlX + 264, sizeY + 3), AutoSize = true };
        page.Controls.Add(_logoSizeValueLabel);

        const int justificationY = 204;
        page.Controls.Add(MakeLabel("Justification:", labelX, justificationY + 3));
        _logoJustificationCombo = MakeJustificationCombo(controlX, justificationY, 120);
        page.Controls.Add(_logoJustificationCombo);
    }

    private void BuildLayoutTab(TabPage page)
    {
        const int labelX = 16;
        int y = 16;

        var info = new Label
        {
            Text = "Choose the vertical stacking order of the enabled elements.\n" +
                   "Select an item below and use Move Up / Move Down to reorder it.",
            Location = new Point(labelX, y),
            AutoSize = true,
        };
        page.Controls.Add(info);
        y += 44;

        _orderList = new ListBox { Location = new Point(labelX, y), Width = 220, Height = 160 };
        page.Controls.Add(_orderList);

        _orderUpButton = new Button { Text = "Move Up", Location = new Point(labelX + 232, y), Width = 100 };
        _orderUpButton.Click += (_, _) => MoveSelectedOrderItem(-1);
        page.Controls.Add(_orderUpButton);

        _orderDownButton = new Button { Text = "Move Down", Location = new Point(labelX + 232, y + 34), Width = 100 };
        _orderDownButton.Click += (_, _) => MoveSelectedOrderItem(1);
        page.Controls.Add(_orderDownButton);
    }

    private void BuildGeneralTab(TabPage page)
    {
        const int labelX = 16, controlX = 200;

        // Only 3 field groups on this tab — spread across the same tab height
        // the other, busier tabs use (rather than clustering at the top) so
        // the tab doesn't end in a large empty gap.
        page.Controls.Add(MakeLabel("Movement Speed (0-10):", labelX, 19));
        _speedUpDown = new NumericUpDown { Location = new Point(controlX, 16), Width = 60, Minimum = 0, Maximum = 10 };
        page.Controls.Add(_speedUpDown);

        const int backgroundY = 116;
        page.Controls.Add(MakeLabel("Background:", labelX, backgroundY + 3));
        _backgroundButton = new Button { Text = "Choose...", Location = new Point(controlX, backgroundY - 2), Width = 130, FlatStyle = FlatStyle.System };
        _backgroundButton.Click += (_, _) => PickColor(ref _backgroundColor, _backgroundButton);
        page.Controls.Add(_backgroundButton);

        const int monospaceY = 216;
        _monospaceOnlyCheck = new CheckBox { Text = "Show only monospaced fonts", Location = new Point(labelX, monospaceY), AutoSize = true };
        _monospaceOnlyCheck.CheckedChanged += (_, _) => ReloadFontLists();
        page.Controls.Add(_monospaceOnlyCheck);
    }

    private void MoveSelectedOrderItem(int delta)
    {
        int idx = _orderList.SelectedIndex;
        if (idx < 0) return;
        int newIdx = idx + delta;
        if (newIdx < 0 || newIdx >= _orderList.Items.Count) return;
        var item = _orderList.Items[idx];
        _orderList.Items.RemoveAt(idx);
        _orderList.Items.Insert(newIdx, item);
        _orderList.SelectedIndex = newIdx;
    }

    private void UpdateTimeFormatPreview()
    {
        try
        {
            _timeFormatPreview.Text = DateTime.Now.ToString(_timeFormatBox.Text, CultureInfo.CurrentCulture);
            _timeFormatPreview.ForeColor = SystemColors.GrayText;
        }
        catch
        {
            _timeFormatPreview.Text = "(invalid format)";
            _timeFormatPreview.ForeColor = Color.Firebrick;
        }
    }

    private void UpdateDateFormatPreview()
    {
        try
        {
            _dateFormatPreview.Text = DateTime.Now.ToString(_dateFormatBox.Text, CultureInfo.CurrentCulture);
            _dateFormatPreview.ForeColor = SystemColors.GrayText;
        }
        catch
        {
            _dateFormatPreview.Text = "(invalid format)";
            _dateFormatPreview.ForeColor = Color.Firebrick;
        }
    }

    private void ShowFormatHelp(string content, string title)
    {
        using var dlg = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(480, 360),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowIcon = false,
            Font = Font,
        };

        var okBtn = new Button { Text = "OK", Dock = DockStyle.Bottom, Height = 32 };
        okBtn.Click += (_, _) => dlg.Close();

        var box = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Font = new Font(FontFamily.GenericMonospace, 9f),
            // The native Win32 edit control backing a WinForms TextBox only
            // breaks lines on CRLF, not a bare LF — normalize regardless of
            // what line endings the source file happens to have, or the text
            // renders as one word-wrapped run instead of the intended lines.
            Text = content.Replace("\r\n", "\n").Replace("\n", "\r\n"),
            BorderStyle = BorderStyle.None,
            BackColor = SystemColors.Window,
        };

        // DockStyle.Fill controls must be added last so the Bottom-docked button
        // has already claimed its space before Fill computes what's left.
        dlg.Controls.Add(okBtn);
        dlg.Controls.Add(box);
        dlg.AcceptButton = okBtn;
        dlg.ShowDialog(this);
    }

    private void UpdateTimeControlsEnabled()
    {
        bool en = _showTimeCheck.Checked;
        _timeFormatBox.Enabled = en;
        _timeFormatHelpButton.Enabled = en;
        _timeFormatPreview.Enabled = en;
        _timeFontCombo.Enabled = en;
        _timeSizeUpDown.Enabled = en;
        _timeColorButton.Enabled = en;
        _timeBoldCheck.Enabled = en;
        _timeItalicCheck.Enabled = en;
        _timeJustificationCombo.Enabled = en;
    }

    private void UpdateDateControlsEnabled()
    {
        bool en = _showDateCheck.Checked;
        _dateFormatBox.Enabled = en;
        _dateFormatHelpButton.Enabled = en;
        _dateFormatPreview.Enabled = en;
        _dateFontCombo.Enabled = en;
        _dateSizeUpDown.Enabled = en;
        _dateColorButton.Enabled = en;
        _dateBoldCheck.Enabled = en;
        _dateItalicCheck.Enabled = en;
        _dateJustificationCombo.Enabled = en;
    }

    private void UpdateLogoControlsEnabled()
    {
        bool en = _enableLogoCheck.Checked;
        _logoBrowseButton.Enabled = en;
        _logoPathBox.Enabled = en;
        _logoSizeTrack.Enabled = en;
        _logoSizeValueLabel.Enabled = en;
        _logoJustificationCombo.Enabled = en;
    }

    private void LogoBrowseButton_Click(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Choose Logo Image",
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|PNG (recommended, supports transparency) (*.png)|*.png|All files (*.*)|*.*",
            FilterIndex = 1,
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _logoPathBox.Text = dlg.FileName;
        }
    }

    private void PickColor(ref Color target, Button swatchButton)
    {
        using var dlg = new ColorDialog { Color = target, FullOpen = true };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            target = dlg.Color;
            ApplySwatch(swatchButton, target);
        }
    }

    private static void ApplySwatch(Button button, Color color)
    {
        button.BackColor = color;
        // Pick readable text color against the swatch background.
        double luminance = 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
        button.ForeColor = luminance > 140 ? Color.Black : Color.White;
    }

    private void ReloadFontLists()
    {
        string? prevTime = _timeFontCombo.SelectedItem as string;
        string? prevDate = _dateFontCombo.SelectedItem as string;

        var families = FontFamily.Families
            .Where(f => !_monospaceOnlyCheck.Checked || FontUtils.IsMonospace(f))
            .Select(f => f.Name)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _timeFontCombo.BeginUpdate();
        _timeFontCombo.Items.Clear();
        _timeFontCombo.Items.AddRange(families);
        _timeFontCombo.EndUpdate();

        _dateFontCombo.BeginUpdate();
        _dateFontCombo.Items.Clear();
        _dateFontCombo.Items.AddRange(families);
        _dateFontCombo.EndUpdate();

        SelectOrFallback(_timeFontCombo, prevTime ?? _settings.TimeFontFamily);
        SelectOrFallback(_dateFontCombo, prevDate ?? _settings.DateFontFamily);
    }

    private static void SelectOrFallback(ComboBox combo, string desired)
    {
        int idx = combo.Items.IndexOf(desired);
        combo.SelectedIndex = idx >= 0 ? idx : (combo.Items.Count > 0 ? 0 : -1);
    }

    private void PopulateFromSettings()
    {
        ReloadFontLists();

        _showTimeCheck.Checked = _settings.ShowTime;
        _timeFormatBox.Text = _settings.TimeFormat;
        _timeSizeUpDown.Value = (decimal)Math.Clamp(_settings.TimeFontSize, (float)_timeSizeUpDown.Minimum, (float)_timeSizeUpDown.Maximum);
        ApplySwatch(_timeColorButton, _timeColor);
        _timeBoldCheck.Checked = _settings.TimeBold;
        _timeItalicCheck.Checked = _settings.TimeItalic;
        _timeJustificationCombo.SelectedItem = _settings.TimeJustification;
        UpdateTimeFormatPreview();

        _showDateCheck.Checked = _settings.ShowDate;
        _dateFormatBox.Text = _settings.DateFormat;
        _dateSizeUpDown.Value = (decimal)Math.Clamp(_settings.DateFontSize, (float)_dateSizeUpDown.Minimum, (float)_dateSizeUpDown.Maximum);
        ApplySwatch(_dateColorButton, _dateColor);
        _dateBoldCheck.Checked = _settings.DateBold;
        _dateItalicCheck.Checked = _settings.DateItalic;
        _dateJustificationCombo.SelectedItem = _settings.DateJustification;
        UpdateDateFormatPreview();

        _enableLogoCheck.Checked = _settings.EnableLogo;
        _logoPathBox.Text = _settings.LogoPath;
        _logoSizeTrack.Value = Math.Clamp(_settings.LogoSizeAdjust, _logoSizeTrack.Minimum, _logoSizeTrack.Maximum);
        _logoSizeValueLabel.Text = _logoSizeTrack.Value.ToString();
        _logoJustificationCombo.SelectedItem = _settings.LogoJustification;

        _orderList.Items.Clear();
        foreach (var element in AppSettings.NormalizeElementOrder(_settings.ElementOrder))
        {
            _orderList.Items.Add(element);
        }

        _speedUpDown.Value = Math.Clamp(_settings.MovementSpeed, 0, 10);
        ApplySwatch(_backgroundButton, _backgroundColor);
        _monospaceOnlyCheck.Checked = _settings.MonospacedFontsOnly;

        UpdateTimeControlsEnabled();
        UpdateDateControlsEnabled();
        UpdateLogoControlsEnabled();
    }

    private void ApplySettings()
    {
        _settings.ShowTime = _showTimeCheck.Checked;
        _settings.TimeFormat = string.IsNullOrWhiteSpace(_timeFormatBox.Text) ? _settings.TimeFormat : _timeFormatBox.Text;
        _settings.TimeFontFamily = _timeFontCombo.SelectedItem as string ?? _settings.TimeFontFamily;
        _settings.TimeFontSize = (float)_timeSizeUpDown.Value;
        _settings.TimeColor = _timeColor;
        _settings.TimeBold = _timeBoldCheck.Checked;
        _settings.TimeItalic = _timeItalicCheck.Checked;
        _settings.TimeJustification = _timeJustificationCombo.SelectedItem is Justification tj ? tj : _settings.TimeJustification;

        _settings.ShowDate = _showDateCheck.Checked;
        _settings.DateFormat = string.IsNullOrWhiteSpace(_dateFormatBox.Text) ? _settings.DateFormat : _dateFormatBox.Text;
        _settings.DateFontFamily = _dateFontCombo.SelectedItem as string ?? _settings.DateFontFamily;
        _settings.DateFontSize = (float)_dateSizeUpDown.Value;
        _settings.DateColor = _dateColor;
        _settings.DateBold = _dateBoldCheck.Checked;
        _settings.DateItalic = _dateItalicCheck.Checked;
        _settings.DateJustification = _dateJustificationCombo.SelectedItem is Justification dj ? dj : _settings.DateJustification;

        _settings.EnableLogo = _enableLogoCheck.Checked;
        _settings.LogoPath = _logoPathBox.Text;
        _settings.LogoSizeAdjust = _logoSizeTrack.Value;
        _settings.LogoJustification = _logoJustificationCombo.SelectedItem is Justification lj ? lj : _settings.LogoJustification;

        _settings.ElementOrder = _orderList.Items.Cast<ClockElement>().ToList();

        _settings.MovementSpeed = (int)_speedUpDown.Value;
        _settings.BackgroundColor = _backgroundColor;
        _settings.MonospacedFontsOnly = _monospaceOnlyCheck.Checked;

        SettingsManager.Save(_settings);
    }
}
