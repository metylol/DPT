using System.Runtime.InteropServices;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ValorantStretch;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--recovery-watchdog")
        {
            Environment.ExitCode = RecoveryGuard.RunChild();
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class DisplayMode
{
    public string DeviceName { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public int Frequency { get; init; }
    public Native.DEVMODE NativeMode { get; init; }
    public string AspectRatio
    {
        get
        {
            if (Width <= 0 || Height <= 0) return "—";
            var ratio = (double)Width / Height;
            if (Math.Abs(ratio - 4.0 / 3) < 0.015) return "4:3";
            if (Math.Abs(ratio - 5.0 / 4) < 0.015) return "5:4";
            if (Math.Abs(ratio - 16.0 / 9) < 0.015) return "16:9";
            if (Math.Abs(ratio - 16.0 / 10) < 0.015) return "16:10";
            if (Math.Abs(ratio - 21.0 / 9) < 0.015) return "21:9";
            var a = Width;
            var b = Height;
            while (b != 0) (a, b) = (b, a % b);
            return $"{Width / a}:{Height / a}";
        }
    }
    public override string ToString() => $"{Width} × {Height}     {Frequency} Hz     {AspectRatio}";
}

internal sealed class SoftSelectorButton : Button
{
    internal Color ParentColor = Color.Black;
    internal Color OutlineColor = Color.FromArgb(56, 56, 58);
    internal int CornerRadius = 13;
    internal Color HoverColor = Color.FromArgb(58, 58, 60);
    internal bool ShowOutline = true;
    internal bool ShowChevron;
    internal bool ShowArrow;
    internal bool CenterText;
    internal DisplayMode? ModeInfo;
    internal MonitorTarget? MonitorInfo;
    private bool hovered;
    private bool pressed;

    internal SoftSelectorButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
    }

    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(ParentColor);
        var rect = new RectangleF(0.75f, 0.75f, Width - 1.5f, Height - 1.5f);
        using var path = MainForm.RoundedPathForSelector(rect, CornerRadius);
        var fill = pressed ? (CenterText ? Color.FromArgb(208, 208, 208) : Color.FromArgb(58, 58, 60)) : hovered ? HoverColor : BackColor;
        using var brush = new SolidBrush(fill);
        using var pen = new Pen(OutlineColor, 1f);
        g.FillPath(brush, path);
        if (ShowOutline) g.DrawPath(pen, path);

        if (MonitorInfo is { } monitor)
        {
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(g, monitor.ModelName, Font,
                new Rectangle(14, 0, 82, Height), ForeColor, flags);
            TextRenderer.DrawText(g, $"{monitor.RefreshHz} Hz", Font,
                new Rectangle(100, 0, 48, Height), ForeColor, flags);
            TextRenderer.DrawText(g, monitor.Enabled ? "enabled" : "disabled", Font,
                new Rectangle(153, 0, 58, Height), ForeColor, flags);
        }
        else if (ModeInfo is { } mode)
        {
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(g, $"{mode.Width} × {mode.Height}", Font, new Rectangle(12, 0, 90, Height), ForeColor, flags);
            TextRenderer.DrawText(g, $"{mode.Frequency} Hz", Font, new Rectangle(110, 0, 44, Height), ForeColor, flags);
            TextRenderer.DrawText(g, mode.AspectRatio, Font, new Rectangle(162, 0, 46, Height), ForeColor, flags);
        }
        else
        {
            var rightInset = ShowChevron ? 34 : 24;
            var textRect = CenterText
                ? new Rectangle(10, 0, Math.Max(0, Width - (ShowArrow ? 44 : 20)), Height)
                : new Rectangle(12, 0, Math.Max(0, Width - rightInset), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, Enabled ? ForeColor : MainForm.TextMuted,
                (CenterText ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left) | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
        if (ShowArrow)
        {
            using var arrowPen = new Pen(Enabled ? ForeColor : MainForm.TextMuted, 1.8f)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round,
                LineJoin = System.Drawing.Drawing2D.LineJoin.Round
            };
            var x = Width - 24f;
            var y = Height / 2f;
            g.DrawLine(arrowPen, x - 7f, y, x + 4f, y);
            g.DrawLines(arrowPen, [new PointF(x - 1f, y - 5f), new PointF(x + 4f, y), new PointF(x - 1f, y + 5f)]);
        }
        if (ShowChevron)
        {
            using var arrowPen = new Pen(Enabled ? ForeColor : MainForm.TextMuted, 1.6f)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round
            };
            var x = Width - 13f;
            var y = Height / 2f - 2f;
            g.DrawLines(arrowPen, [new PointF(x - 3f, y), new PointF(x, y + 3f), new PointF(x + 3f, y)]);
        }
        if (Focused)
        {
            using var focusPen = new Pen(Color.FromArgb(150, 255, 255, 255), 1f);
            using var focusPath = MainForm.RoundedPathForSelector(new RectangleF(2.5f, 2.5f, Width - 5f, Height - 5f), Math.Max(3, CornerRadius - 2));
            g.DrawPath(focusPen, focusPath);
        }
    }
}

internal sealed class PillLabel : Control
{
    internal PillLabel() => SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
        ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Color.Black);
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = MainForm.RoundedPathForSelector(rect, Height / 2);
        using var fill = new SolidBrush(Color.FromArgb(37, 37, 39));
        using var border = new Pen(Color.FromArgb(58, 58, 60), 1f);
        g.FillPath(fill, path);
        g.DrawPath(border, path);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }
}

internal enum IconKind { Monitor, Sliders, CheckCircle, AlertCircle, GraphicsChip }

internal sealed class MonochromeIcon : Control
{
    internal IconKind Kind;

    internal MonochromeIcon() => SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
        ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new Pen(ForeColor, 1.5f)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round
        };
        switch (Kind)
        {
            case IconKind.Monitor:
                using (var screen = MainForm.RoundedPathForSelector(new RectangleF(2, 2, Width - 4, Height - 7), 3)) g.DrawPath(pen, screen);
                g.DrawLine(pen, Width / 2f, Height - 5, Width / 2f, Height - 2);
                g.DrawLine(pen, Width / 2f - 4, Height - 2, Width / 2f + 4, Height - 2);
                break;
            case IconKind.Sliders:
                g.DrawLine(pen, 2, 4, Width - 2, 4);
                g.DrawLine(pen, 2, Height / 2f, Width - 2, Height / 2f);
                g.DrawLine(pen, 2, Height - 4, Width - 2, Height - 4);
                g.DrawEllipse(pen, 5, 1.5f, 5, 5);
                g.DrawEllipse(pen, Width - 10, Height / 2f - 2.5f, 5, 5);
                g.DrawEllipse(pen, 8, Height - 6.5f, 5, 5);
                break;
            case IconKind.CheckCircle:
                g.DrawEllipse(pen, 1.5f, 1.5f, Width - 3, Height - 3);
                g.DrawLines(pen, [new PointF(Width * .29f, Height * .52f), new PointF(Width * .44f, Height * .66f), new PointF(Width * .72f, Height * .36f)]);
                break;
            case IconKind.AlertCircle:
                g.DrawEllipse(pen, 1.5f, 1.5f, Width - 3, Height - 3);
                g.DrawLine(pen, Width / 2f, Height * .27f, Width / 2f, Height * .56f);
                g.DrawEllipse(pen, Width / 2f - .7f, Height * .70f, 1.4f, 1.4f);
                break;
            case IconKind.GraphicsChip:
                using (var chip = MainForm.RoundedPathForSelector(new RectangleF(4, 4, Width - 8, Height - 8), 3)) g.DrawPath(pen, chip);
                for (var i = 6; i < Width - 4; i += 5) { g.DrawLine(pen, i, 1.5f, i, 4); g.DrawLine(pen, i, Height - 4, i, Height - 1.5f); }
                g.DrawLine(pen, 1.5f, 7, 4, 7); g.DrawLine(pen, Width - 4, 7, Width - 1.5f, 7);
                g.DrawLine(pen, 1.5f, Height - 7, 4, Height - 7); g.DrawLine(pen, Width - 4, Height - 7, Width - 1.5f, Height - 7);
                break;
        }
    }
}

internal sealed class SoftSurfacePanel : Panel
{
    internal Color ParentColor = Color.FromArgb(17, 17, 19);
    internal Color OutlineColor = Color.FromArgb(56, 56, 58);
    internal int CornerRadius = 14;

    internal SoftSurfacePanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(ParentColor);
        var path = MainForm.RoundedPathForSelector(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), CornerRadius);
        using (path)
        using (var brush = new SolidBrush(BackColor))
        using (var pen = new Pen(OutlineColor, 1f))
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }
        base.OnPaint(e);
    }
}

internal sealed class MainForm : Form
{
    private static readonly HashSet<(int Width, int Height)> StandardStretchResolutions =
    [
        (1680, 1050), (1440, 1080), (1280, 1024), (1280, 960), (1568, 1050), (1024, 768),
        (1568, 1080), (1440, 992), (1280, 882)
    ];
    private static readonly Color Background = Color.FromArgb(17, 17, 19);
    private static readonly Color Surface = Color.FromArgb(28, 28, 30);
    private static readonly Color SurfaceRaised = Color.FromArgb(44, 44, 46);
    private static readonly Color TextPrimary = Color.FromArgb(255, 255, 255);
    internal static readonly Color TextMuted = Color.FromArgb(161, 161, 166);
    private static readonly Color Accent = Color.FromArgb(255, 255, 255);
    private static readonly Color AccentText = Color.FromArgb(8, 8, 8);
    private static readonly Color Border = Color.FromArgb(56, 56, 58);

    private readonly SoftSelectorButton modePicker = new();
    private readonly SoftSelectorButton customButton = new();
    private readonly SoftSelectorButton monitorPicker = new();
    private readonly ContextMenuStrip monitorMenu = new();
    private readonly ContextMenuStrip modeMenu = new();
    private readonly ToolTip monitorTip = new();
    private List<MonitorTarget> monitorTargets = [];
    private MonitorTarget? selectedMonitor;
    private List<MonitorDeviceSnapshot> monitorSnapshots = [];
    private DisplayMode? selectedMode;
    private readonly Label currentResolution = new();
    private readonly Label currentRefresh = new();
    private readonly PillLabel currentDisplayPill = new();
    private readonly Label status = new();
    private readonly SoftSelectorButton apply = new();
    private readonly SoftSelectorButton monitorStateButton = new();
    private readonly SoftSelectorButton keep = new();
    private readonly SoftSelectorButton revert = new();
    private bool NativeResolutionDetected => selectedMonitor is { PreferredWidth: > 0, PreferredHeight: > 0 };
    private int? originalGpuScaling;
    private AmdScalingState? originalAmdScaling;
    private IntelScalingState? originalIntelScaling;
    private WindowsScalingState? originalWindowsScaling;
    private string? scalingDevice;
    private int nativeWidth;
    private int nativeHeight;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer displayRefreshTimer = new() { Interval = 700 };
    private int secondsLeft;
    private int minimumRefresh;
    private DisplayMode? originalMode;
    private RecoveryGuard? recoveryGuard;
    private bool pending;
    private bool confirmingMode;
    private bool pendingDeviceOnly;
    private bool pendingMonitorsEnabled;
    private bool refreshAfterOperation;
    private readonly HashSet<string> changedMonitorIds = new(StringComparer.OrdinalIgnoreCase);

    public MainForm()
    {
        Text = "Display Preset Tool";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        ShowIcon = true;
        ClientSize = new Size(420, 600);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Background;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 10F);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        Opacity = 0;
        ShowInTaskbar = false;

        var currentCard = Card(new Rectangle(20, 88, 380, 100));
        var monitorIcon = new MonochromeIcon { Kind = IconKind.Monitor, Bounds = new Rectangle(20, 15, 18, 18), ForeColor = TextMuted, BackColor = Color.Transparent };
        var currentEyebrow = Label("Current display", 9F, true, TextMuted, 46, 16, 220, 16);
        currentResolution.SetBounds(18, 35, 216, 38);
        currentResolution.Font = new Font("Segoe UI Semibold", 22F);
        currentResolution.AutoEllipsis = true;
        currentResolution.ForeColor = TextPrimary;
        currentRefresh.SetBounds(256, 36, 104, 36);
        currentRefresh.Font = new Font("Segoe UI Semibold", 20F);
        currentRefresh.ForeColor = TextPrimary;
        currentRefresh.BackColor = Color.Transparent;
        currentRefresh.TextAlign = ContentAlignment.MiddleLeft;
        var displayDivider = new Panel { Bounds = new Rectangle(240, 38, 1, 32), BackColor = Border };
        currentDisplayPill.Text = "QHD  ·  16:9";
        currentDisplayPill.SetBounds(18, 74, 116, 20);
        currentDisplayPill.Font = new Font("Segoe UI Semibold", 8.5F);
        currentDisplayPill.ForeColor = TextMuted;
        currentCard.Controls.AddRange([monitorIcon, currentEyebrow, currentResolution, displayDivider, currentRefresh, currentDisplayPill]);

        var modeCard = Card(new Rectangle(20, 202, 380, 202));
        var settingsHeading = Label("Display settings", 18F, true, TextPrimary, 20, 16, 300, 30);
        var targetLabel = Label("Target monitor", 9F, true, TextMuted, 20, 50, 300, 16);
        monitorPicker.SetBounds(20, 70, 230, 40);
        monitorPicker.BackColor = SurfaceRaised;
        monitorPicker.ForeColor = TextPrimary;
        monitorPicker.Font = new Font("Segoe UI Semibold", 9.5F);
        monitorPicker.FlatStyle = FlatStyle.Flat;
        monitorPicker.FlatAppearance.BorderSize = 0;
        monitorPicker.FlatAppearance.BorderColor = SurfaceRaised;
        monitorPicker.FlatAppearance.MouseOverBackColor = Color.FromArgb(58, 58, 60);
        monitorPicker.TextAlign = ContentAlignment.MiddleLeft;
        monitorPicker.Padding = new Padding(10, 0, 10, 0);
        monitorPicker.UseVisualStyleBackColor = false;
        monitorPicker.Cursor = Cursors.Hand;
        monitorPicker.ParentColor = Surface;
        monitorPicker.CornerRadius = 12;
        monitorPicker.OutlineColor = Border;
        monitorPicker.ShowChevron = true;
        monitorMenu.ShowImageMargin = false;
        monitorMenu.ShowCheckMargin = false;
        monitorMenu.BackColor = SurfaceRaised;
        monitorMenu.ForeColor = TextPrimary;
        monitorMenu.Font = new Font("Segoe UI", 9.5F);
        monitorMenu.AutoSize = false;
        monitorMenu.Padding = new Padding(4);
        monitorMenu.Renderer = new SoftMenuRenderer();
        RoundControl(monitorMenu, 10);
        AttachTogglePickerMenu(monitorPicker, monitorMenu);
        var modeLabel = Label("Stretch resolution", 9F, true, TextMuted, 20, 122, 300, 16);
        modePicker.SetBounds(20, 142, 230, 42);
        modePicker.FlatStyle = FlatStyle.Flat;
        modePicker.FlatAppearance.BorderSize = 0;
        modePicker.FlatAppearance.BorderColor = SurfaceRaised;
        modePicker.FlatAppearance.MouseOverBackColor = Color.FromArgb(58, 58, 60);
        modePicker.BackColor = SurfaceRaised;
        modePicker.ForeColor = TextPrimary;
        modePicker.Font = new Font("Segoe UI Semibold", 9.5F);
        modePicker.TextAlign = ContentAlignment.MiddleLeft;
        modePicker.Padding = new Padding(12, 0, 8, 0);
        modePicker.UseVisualStyleBackColor = false;
        modePicker.Cursor = Cursors.Hand;
        modePicker.ParentColor = Surface;
        modePicker.OutlineColor = Border;
        modePicker.CornerRadius = 12;
        modePicker.HoverColor = Color.FromArgb(58, 58, 60);
        modePicker.ShowChevron = true;
        customButton.Text = "+ Custom";
        customButton.SetBounds(260, 142, 100, 42);
        customButton.BackColor = SurfaceRaised;
        customButton.ForeColor = TextPrimary;
        customButton.ParentColor = Surface;
        customButton.Font = new Font("Segoe UI Semibold", 9.5F);
        customButton.CenterText = true;
        customButton.Cursor = Cursors.Hand;
        customButton.Click += (_, _) => { if (!pending && selectedMonitor is not null) ShowCustomResolution(); };
        modeCard.Controls.Add(customButton);
        AttachTogglePickerMenu(modePicker, modeMenu);
        modeMenu.ShowImageMargin = false;
        modeMenu.ShowCheckMargin = false;
        modeMenu.BackColor = SurfaceRaised;
        modeMenu.ForeColor = TextPrimary;
        modeMenu.Font = new Font("Segoe UI", 9.5F);
        modeMenu.Renderer = new SoftMenuRenderer();
        RoundControl(modeMenu, 12);
        modeCard.Controls.AddRange([settingsHeading, targetLabel, monitorPicker, modeLabel, modePicker]);

        apply.Text = "Apply stretch preset";
        apply.SetBounds(20, 418, 380, 54);
        StyleButton(apply, ButtonKind.Primary);
        apply.ParentColor = Background;
        apply.OutlineColor = Color.FromArgb(235, 235, 235);
        apply.CornerRadius = 16;
        apply.CenterText = true;
        apply.HoverColor = Color.FromArgb(215, 215, 215);
        apply.ShowArrow = true;
        apply.Click += async (_, _) => await RunSafely(ApplyMode, "Apply failed");

        UpdateMonitorStateButton();
        monitorStateButton.SetBounds(20, 482, 380, 48);
        StyleButton(monitorStateButton, ButtonKind.Secondary);
        monitorStateButton.ParentColor = Background;
        monitorStateButton.OutlineColor = Border;
        monitorStateButton.CornerRadius = 14;
        monitorStateButton.CenterText = true;
        monitorStateButton.Click += async (_, _) => await RunSafely(RevertToNative, "Revert failed");

        status.SetBounds(20, 536, 380, 42);
        status.AutoSize = false;
        status.AutoEllipsis = true;
        status.Font = new Font("Segoe UI", 8F);
        status.ForeColor = TextMuted;
        status.TextAlign = ContentAlignment.MiddleCenter;
        status.Text = "Your current display mode is safe until you apply a change.";

        keep.Text = "Keep changes";
        keep.SetBounds(20, 418, 184, 54);
        StyleButton(keep, ButtonKind.Secondary);
        keep.ParentColor = Background;
        keep.OutlineColor = Border;
        keep.CornerRadius = 14;
        keep.CenterText = true;
        keep.Visible = false;
        keep.Click += async (_, _) => await RunSafely(ConfirmMode, "Confirm failed");

        revert.Text = "Revert safely";
        revert.SetBounds(212, 418, 188, 54);
        StyleButton(revert, ButtonKind.Secondary);
        revert.ParentColor = Background;
        revert.OutlineColor = Border;
        revert.CornerRadius = 14;
        revert.CenterText = true;
        revert.Visible = false;
        revert.Click += async (_, _) => await RunSafely(RestoreMode, "Restore failed");

        keep.VisibleChanged += (_, _) => LayoutStatusArea();
        revert.VisibleChanged += (_, _) => LayoutStatusArea();
        status.TextChanged += (_, _) => LayoutStatusArea();

        Controls.AddRange([currentCard, modeCard, apply, monitorStateButton, status, keep, revert]);

        var contentControls = Controls.Cast<Control>().ToArray();
        contentHost.BackColor = Background;
        contentHost.Controls.AddRange(contentControls);
        Controls.Add(contentHost);
        monitorMenu.Opening += (_, _) => PreparePickerMenu(monitorMenu, monitorPicker.Width);
        modeMenu.Opening += (_, _) => PreparePickerMenu(modeMenu, modePicker.Width);
        contentHost.Controls.Add(Label("Display presets", 18F, true, TextPrimary, 20, 24, 360, 38));
        contentHost.Controls.Add(Label("Choose a display mode and confirm to keep it.", 9.5F, false, TextMuted, 20, 62, 360, 20));
        LayoutStatusArea();
        timer.Tick += async (_, _) => await RunSafely(Countdown, "Automatic restore failed");
        displayRefreshTimer.Tick += async (_, _) =>
        {
            displayRefreshTimer.Stop();
            if (pending || operationDepth > 0) { refreshAfterOperation = true; return; }
            refreshAfterOperation = false;
            await RunSafely(() => LoadMonitors(), "Display refresh failed");
        };
        Load += async (_, _) =>
        {
            await RunSafely(() => LoadMonitors(), "Startup failed");
            Opacity = 1;
            ShowInTaskbar = true;
        };
        var closeAfterRecovery = false;
        FormClosing += async (sender, e) =>
        {
            if (closeAfterRecovery) return;
            if (operationDepth > 0) { e.Cancel = true; return; }
            timer.Stop();
            displayRefreshTimer.Stop();
            if (!pending) return;
            e.Cancel = true;
            Enabled = false;
            try { recoveryGuard?.Send("RESTORING"); } catch { }
            try
            {
                var monitorsRestored = await Task.Run(() => RestoreSelectedMonitorState(out _));
                var (modeRestored, scalingRestored) = pendingDeviceOnly
                    ? (true, true)
                    : await Task.Run(() => (ChangeMode(originalMode) == Native.DISP_CHANGE_SUCCESSFUL, RestoreGpuScaling()));
                if (monitorsRestored && modeRestored && scalingRestored) await Task.Run(FinishRecoveryGuard);
            }
            catch { /* The watchdog restores the saved state if shutdown recovery fails. */ }
            closeAfterRecovery = true;
            Enabled = true;
            Close();
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.ApplyDarkWindowChrome(Handle);
    }

    protected override void WndProc(ref Message m)
    {
        var refresh = m.Msg is 0x007E or 0x0219 ||
            (m.Msg == 0x0218 && m.WParam.ToInt32() is 0x0006 or 0x0007 or 0x0012);
        base.WndProc(ref m);
        if (refresh) ScheduleDisplayRefresh();
    }

    private void ScheduleDisplayRefresh()
    {
        if (IsDisposed || !IsHandleCreated) return;
        displayRefreshTimer.Stop();
        displayRefreshTimer.Start();
    }

    private void LayoutStatusArea()
    {
        var confirmationVisible = keep.Visible || revert.Visible;
        apply.Visible = !confirmationVisible;
        monitorStateButton.Visible = true;
        const int confirmationGap = 12;
        var confirmationWidth = (apply.Width - confirmationGap) / 2;
        keep.SetBounds(apply.Left, apply.Top, confirmationWidth, apply.Height);
        revert.SetBounds(keep.Right + confirmationGap, apply.Top, confirmationWidth, apply.Height);
        status.Top = monitorStateButton.Bottom + 6;
        var hasStatus = !string.IsNullOrWhiteSpace(status.Text);
        status.Visible = hasStatus;
        var height = (hasStatus ? status.Bottom : monitorStateButton.Bottom) + 12;
        if (ClientSize.Height != Math.Max(height, 520))
            ClientSize = new Size(420, Math.Max(height, 520));
    }

    private static void AttachTogglePickerMenu(Control picker, ContextMenuStrip menu, Func<bool>? canOpen = null)
    {
        var closedByPickerClick = false;
        menu.Closed += (_, e) =>
        {
            if (e.CloseReason is not (ToolStripDropDownCloseReason.AppClicked or ToolStripDropDownCloseReason.AppFocusChange) ||
                !picker.RectangleToScreen(picker.ClientRectangle).Contains(Cursor.Position)) return;
            closedByPickerClick = true;
            picker.BeginInvoke(new Action(() => closedByPickerClick = false));
        };
        picker.Click += (_, _) =>
        {
            if (closedByPickerClick)
            {
                closedByPickerClick = false;
                return;
            }
            if (menu.Visible) { menu.Close(); return; }
            if (canOpen?.Invoke() == false || menu.Items.Count == 0) return;
            menu.Show(picker, new Point(0, picker.Height + 5));
        };
    }

    private void ShowCustomResolution()
    {
        if (selectedMonitor is null) return;
        var device = selectedMonitor.DisplayDeviceName;
        using var dialog = new Form
        {
            Text = "Custom resolution", Icon = Icon, ClientSize = new Size(370, 266),
            BackColor = Background, ForeColor = TextPrimary, Font = new Font("Segoe UI", 10F),
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false
        };
        dialog.HandleCreated += (_, _) => Native.ApplyDarkWindowChrome(dialog.Handle);
        dialog.Controls.Add(Label("Custom stretch", 18F, true, TextPrimary, 20, 16, 330, 32));
        dialog.Controls.Add(Label("WIDTH", 8.5F, true, TextMuted, 20, 61, 150, 18));
        dialog.Controls.Add(Label("HEIGHT", 8.5F, true, TextMuted, 200, 61, 150, 18));
        TextBox Input(int x, string value)
        {
            var surface = new SoftSurfacePanel { Bounds = new Rectangle(x, 84, 150, 42), BackColor = SurfaceRaised, ParentColor = Background, CornerRadius = 12, OutlineColor = Border };
            var box = new TextBox { Bounds = new Rectangle(12, 9, 126, 26), Text = value, BorderStyle = BorderStyle.None, BackColor = SurfaceRaised, ForeColor = TextPrimary, Font = new Font("Segoe UI Semibold", 13F), MaxLength = 5 };
            surface.Controls.Add(box);
            dialog.Controls.Add(surface);
            return box;
        }
        var widthBox = Input(20, (selectedMode?.Width ?? 1440).ToString());
        var heightBox = Input(200, (selectedMode?.Height ?? 1080).ToString());
        var feedback = Label("Tests at the current refresh rate. Unsupported modes may need CRU or GPU software.", 9F, false, TextMuted, 20, 136, 330, 54);
        dialog.Controls.Add(feedback);
        var use = new SoftSelectorButton { Bounds = new Rectangle(20, 202, 330, 44), Text = "Use resolution", BackColor = SurfaceRaised, ForeColor = TextPrimary, ParentColor = Background, CenterText = true, Font = new Font("Segoe UI Semibold", 10F), Cursor = Cursors.Hand };
        dialog.Controls.Add(use);
        dialog.AcceptButton = use;
        bool validating = false;
        dialog.FormClosing += (_, e) => { if (validating) e.Cancel = true; };
        use.Click += async (_, _) =>
        {
            if (!int.TryParse(widthBox.Text, out var width) || !int.TryParse(heightBox.Text, out var height) ||
                width < 640 || height < 480 || width > 16384 || height > 16384 || (width < 1000 && height < 1000))
            {
                feedback.Text = "Enter valid dimensions (640–16384 × 480–16384). At least one dimension must be 1000 or more.";
                return;
            }
            validating = true;
            dialog.Enabled = false;
            feedback.Text = "Checking display support…";
            try
            {
                var requiredHz = minimumRefresh;
                var result = await Task.Run(() =>
                {
                    var dm = Native.GetMode(Native.ENUM_CURRENT_SETTINGS, device);
                    int bestHz = 0;
                    for (uint i = 0; ; i++)
                    {
                        var candidate = Native.CreateMode();
                        if (!Native.EnumDisplaySettings(device, i, ref candidate)) break;
                        if (candidate.Width == width && candidate.Height == height && candidate.Frequency > bestHz)
                        { bestHz = candidate.Frequency; dm = candidate; }
                    }
                    if (bestHz > 0 && bestHz < requiredHz)
                        return (Mode: (DisplayMode?)null, Error: "This size only supports a lower refresh rate on your display.");
                    if (bestHz == 0)
                    {
                        dm = Native.GetMode(Native.ENUM_CURRENT_SETTINGS, device);
                        dm.Frequency = dm.Frequency > 1 ? dm.Frequency : requiredHz;
                    }
                    dm.Width = width;
                    dm.Height = height;
                    dm.Fields |= Native.DM_PELSWIDTH | Native.DM_PELSHEIGHT | Native.DM_DISPLAYFREQUENCY;
                    var code = Native.ChangeDisplaySettings(device, ref dm, Native.CDS_TEST);
                    if (code != Native.DISP_CHANGE_SUCCESSFUL)
                        return (Mode: (DisplayMode?)null, Error: bestHz == 0
                            ? "The display driver rejected this custom mode. Add it with CRU or your GPU software, restart the display driver, then reopen DPT."
                            : "Windows rejected this mode (code " + code + ").");
                    return (Mode: (DisplayMode?)new DisplayMode { DeviceName = device, Width = width, Height = height, Frequency = dm.Frequency, NativeMode = dm }, Error: "");
                });
                if (result.Mode is null) { feedback.Text = result.Error; return; }
                selectedMode = result.Mode;
                var item = new ToolStripMenuItem(result.Mode + "  · Custom") { Tag = result.Mode, ForeColor = TextPrimary, BackColor = SurfaceRaised, Padding = new Padding(12, 8, 16, 8) };
                item.Click += SelectMode;
                modeMenu.Items.Add(item);
                SortStretchMenu();
                SetModePickerText(result.Mode, $"{result.Mode.Width} × {result.Mode.Height} · {result.Mode.Frequency}Hz");
                FitModePicker();
                modePicker.Enabled = true;
                apply.Enabled = selectedMonitor?.CanChangeIndependently == true;
                status.Text = $"Custom {width} × {height} selected. Apply to preview with rollback.";
                validating = false;
                dialog.DialogResult = DialogResult.OK;
                dialog.Close();
            }
            catch (Exception ex) { feedback.Text = "Couldn't check this mode: " + ex.Message; }
            finally { validating = false; if (!dialog.IsDisposed) dialog.Enabled = true; }
        };
        dialog.ShowDialog(this);
    }
    private void SelectMode(object? sender, EventArgs e)
    {
        if (sender is not ToolStripMenuItem { Tag: DisplayMode mode }) return;
        selectedMode = mode;
        SetModePickerText(mode, $"{mode.Width} × {mode.Height} · {mode.Frequency}Hz");
        FitModePicker();
    }

    private void SetModePickerText(DisplayMode? mode, string text)
    {
        modePicker.Text = text;
        modePicker.ModeInfo = mode;
        modePicker.Invalidate();
    }

    private void UpdateMonitorPicker()
    {
        monitorPicker.MonitorInfo = selectedMonitor;
        if (selectedMonitor is null)
        {
            monitorPicker.Text = "No active monitor path matched a device instance";
            monitorPicker.Invalidate();
            return;
        }
        var state = selectedMonitor.Enabled ? "enabled" : "disabled";
        monitorPicker.Text = $"{selectedMonitor.ModelName}   ·   {selectedMonitor.RefreshHz} Hz   ·   {state}";
        monitorPicker.Invalidate();
    }

    private Task SelectMonitor(MonitorTarget target)
    {
        if (ReferenceEquals(target, selectedMonitor)) return Task.CompletedTask;
        selectedMonitor = target;
        selectedMode = null;
        modeMenu.Items.Clear();
        SetModePickerText(null, "Loading display modes…");
        modePicker.Enabled = false;
        apply.Enabled = false;
        UpdateMonitorPicker();
        UpdateMonitorTip();
        UpdateMonitorStateButton();
        return LoadModes();
    }

    private async Task LoadMonitors()
    {
        using var busy = BeginOperation();
        monitorMenu.Items.Clear();
        try
        {
            monitorTargets = await Task.Run(MonitorControl.FindTargets);
            foreach (var target in monitorTargets)
            {
                var item = new ToolStripMenuItem($"{target.ModelName}   ·   {target.RefreshHz} Hz   ·   {(target.Enabled ? "enabled" : "disabled")}")
                {
                    Tag = target,
                    AutoSize = false,
                    Size = new Size(monitorPicker.Width - 8, 36),
                    ForeColor = TextPrimary,
                    BackColor = SurfaceRaised,
                    Padding = new Padding(10, 0, 20, 0),
                    TextAlign = ContentAlignment.MiddleLeft
                };
                item.Click += (_, _) => _ = SelectMonitor(target);
                monitorMenu.Items.Add(item);
            }
            monitorMenu.Size = new Size(monitorPicker.Width, monitorMenu.Items.Count * 36 + 8);
            if (monitorTargets.Count == 0)
            {
                selectedMonitor = null;
                monitorPicker.MonitorInfo = null;
                monitorPicker.Enabled = false;
                monitorStateButton.Enabled = false;
                monitorPicker.Text = "No active monitor path matched a device instance";
                apply.Enabled = false;
                status.Text = "Cannot safely identify an active monitor by device path and EDID.";
                return;
            }
            monitorPicker.Enabled = true;
            var preferredMonitor = monitorTargets.FirstOrDefault(target =>
                string.Equals(target.InstanceId, selectedMonitor?.InstanceId, StringComparison.OrdinalIgnoreCase))
                ?? monitorTargets.MaxBy(target => target.RefreshHz)!;
            await SelectMonitor(preferredMonitor);
        }
        catch (Exception ex)
        {
            selectedMonitor = null;
            selectedMode = null;
            monitorTargets.Clear();
            monitorMenu.Items.Clear();
            modeMenu.Items.Clear();
            monitorPicker.MonitorInfo = null;
            monitorPicker.Enabled = false;
            SetModePickerText(null, "Select an active display first");
            modePicker.Enabled = false;
            monitorStateButton.Enabled = false;
            apply.Enabled = false;
            status.Text = "Monitor identification failed: " + ex.Message;
        }
    }

    private void UpdateMonitorTip()
    {
        if (selectedMonitor is null) return;
        monitorTip.SetToolTip(monitorPicker,
            $"Model: {selectedMonitor.ModelName}\nInstance: {selectedMonitor.InstanceId}\nHardware IDs: {string.Join("; ", selectedMonitor.HardwareIds)}\nActive display path: {selectedMonitor.DisplayPath}\nDevice state: {(selectedMonitor.Enabled ? "Enabled" : "Disabled")}");
    }

    private void UpdateMonitorStateButton()
    {
        if (selectedMonitor is null)
        {
            monitorStateButton.Text = "Monitor unavailable";
            monitorStateButton.Enabled = false;
            return;
        }
        monitorStateButton.Text = "Revert";
        if (pending)
        {
            monitorStateButton.Enabled = false;
            return;
        }
        Native.DEVMODE active;
        try { active = Native.GetMode(Native.ENUM_CURRENT_SETTINGS, selectedMonitor.DisplayDeviceName); }
        catch (IOException)
        {
            monitorStateButton.Text = "Display unavailable";
            monitorStateButton.Enabled = false;
            return;
        }
        var nativeResolution = NativeResolutionDetected &&
            active.Width == selectedMonitor.PreferredWidth && active.Height == selectedMonitor.PreferredHeight;
        var allMonitorsEnabled = monitorTargets.Count > 0 && monitorTargets.All(target => target.Enabled);
        monitorStateButton.Enabled = !pending && selectedMonitor.CanChangeIndependently &&
            !(nativeResolution && allMonitorsEnabled);
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => SurfaceRaised;
        public override Color MenuBorder => Color.FromArgb(56, 56, 58);
        public override Color MenuItemBorder => Color.FromArgb(68, 68, 70);
        public override Color MenuItemSelected => Color.FromArgb(58, 58, 60);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(58, 58, 60);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(58, 58, 60);
        public override Color MenuItemPressedGradientBegin => Color.FromArgb(50, 50, 52);
        public override Color MenuItemPressedGradientEnd => Color.FromArgb(50, 50, 52);
    }

    private void PreparePickerMenu(ContextMenuStrip menu, int width)
    {
        menu.AutoSize = false;
        var monitorLayout = ReferenceEquals(menu, monitorMenu);
        var rowHeight = monitorLayout ? 36 : 40;
        menu.Font = new Font("Segoe UI", 9.5F);
        foreach (ToolStripMenuItem item in menu.Items)
        {
            item.AutoSize = false;
            item.Size = new Size(width, rowHeight);
            item.Margin = Padding.Empty;
            item.Padding = new Padding(10, 0, 30, 0);
            item.Checked = item.Tag switch
            {
                DisplayMode mode => selectedMode is not null && mode.Width == selectedMode.Width && mode.Height == selectedMode.Height && mode.Frequency == selectedMode.Frequency,
                MonitorTarget target => target.InstanceId == selectedMonitor?.InstanceId,
                _ => false
            };
        }
        menu.Size = new Size(width, Math.Min(492, menu.Items.Count * rowHeight + 4));
    }

    private void FitModePicker()
    {
        modePicker.Width = 230;
        customButton.Left = 260;
        customButton.Width = 100;
    }

    private void SortStretchMenu()
    {
        var items = modeMenu.Items.Cast<ToolStripMenuItem>()
            .OrderByDescending(item => ((DisplayMode)item.Tag!).Width)
            .ThenByDescending(item => ((DisplayMode)item.Tag!).Height).ToArray();
        modeMenu.Items.Clear();
        modeMenu.Items.AddRange(items);
    }

    private sealed class SoftMenuRenderer : ToolStripProfessionalRenderer
    {
        internal SoftMenuRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var bounds = new RectangleF(0.5f, 0.5f, e.ToolStrip.Width - 1f, e.ToolStrip.Height - 1f);
            using var path = RoundedPathForSelector(bounds, 10);
            using var brush = new SolidBrush(SurfaceRaised);
            using var pen = new Pen(Color.FromArgb(52, 52, 52), 1f);
            e.Graphics.FillPath(brush, path);
            e.Graphics.DrawPath(pen, path);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var item = (ToolStripMenuItem)e.Item;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var path = RoundedPathForSelector(new RectangleF(1, 2, item.Width - 2, item.Height - 4), 9);
            using var fill = new SolidBrush(item.Selected ? Color.FromArgb(58, 58, 60) : item.Checked ? Color.FromArgb(50, 50, 52) : SurfaceRaised);
            e.Graphics.FillPath(fill, path);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var item = (ToolStripMenuItem)e.Item;
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
            var foreground = item.Enabled ? TextPrimary : TextMuted;
            if (item.Tag is DisplayMode mode)
            {
                TextRenderer.DrawText(e.Graphics, $"{mode.Width} × {mode.Height}", e.TextFont,
                    new Rectangle(12, 0, 90, item.Height), foreground, flags);
                TextRenderer.DrawText(e.Graphics, $"{mode.Frequency} Hz", e.TextFont,
                    new Rectangle(110, 0, 44, item.Height), TextMuted, flags);
                TextRenderer.DrawText(e.Graphics, mode.AspectRatio, e.TextFont,
                    new Rectangle(162, 0, 46, item.Height), foreground, flags);
            }
            else if (item.Tag is MonitorTarget monitor)
            {
                TextRenderer.DrawText(e.Graphics, monitor.ModelName, e.TextFont,
                    new Rectangle(12, 0, 82, item.Height), foreground, flags);
                TextRenderer.DrawText(e.Graphics, $"{monitor.RefreshHz} Hz", e.TextFont,
                    new Rectangle(100, 0, 48, item.Height), TextMuted, flags);
                TextRenderer.DrawText(e.Graphics, monitor.Enabled ? "enabled" : "disabled", e.TextFont,
                    new Rectangle(153, 0, 58, item.Height), TextMuted, flags);
            }
            else
                TextRenderer.DrawText(e.Graphics, item.Text, e.TextFont,
                    new Rectangle(12, 0, item.Width - 44, item.Height), foreground, flags);
            if (item.Checked)
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var pen = new Pen(TextPrimary, 1.6f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
                float x = item.Width - 13, y = item.Height / 2f;
                e.Graphics.DrawLines(pen, [new PointF(x, y), new PointF(x + 3, y + 3), new PointF(x + 9, y - 4)]);
            }
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
    }
    private static SoftSurfacePanel Card(Rectangle bounds)
    {
        var card = new SoftSurfacePanel
        {
            Bounds = bounds,
            BackColor = Surface,
            ParentColor = Background,
            OutlineColor = Border,
            CornerRadius = 14
        };
        return card;
    }

    private static void RoundControl(Control control, int radius)
    {
        void UpdateRegion()
        {
            if (control.Width < radius * 2 || control.Height < radius * 2) return;
            var path = RoundedPath(new Rectangle(0, 0, control.Width, control.Height), radius);
            var old = control.Region;
            control.Region = new System.Drawing.Region(path);
            old?.Dispose();
            path.Dispose();
        }
        control.SizeChanged += (_, _) => UpdateRegion();
        UpdateRegion();
    }

    internal static System.Drawing.Drawing2D.GraphicsPath RoundedPathForSelector(RectangleF bounds, int radius)
    {
        return RoundedPath(Rectangle.Round(bounds), radius);
    }

    private static System.Drawing.Drawing2D.GraphicsPath RoundedPath(Rectangle bounds, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var d = radius * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Label Label(string text, float size, bool semibold, Color color, int x, int y, int width, int height) => new()
    {
        Text = text,
        Font = new Font(semibold ? "Segoe UI Semibold" : "Segoe UI", size),
        ForeColor = color,
        BackColor = Color.Transparent,
        Bounds = new Rectangle(x, y, width, height),
        AutoSize = false,
        AutoEllipsis = true
    };

    private enum ButtonKind { Primary, Secondary }

    private static void StyleButton(Button button, ButtonKind kind)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.BorderColor = kind == ButtonKind.Primary ? Accent : SurfaceRaised;
        button.FlatAppearance.MouseOverBackColor = kind == ButtonKind.Primary ? Color.FromArgb(215, 215, 215) : Color.FromArgb(58, 58, 60);
        button.BackColor = kind == ButtonKind.Primary ? Accent : SurfaceRaised;
        button.ForeColor = kind == ButtonKind.Primary ? AccentText : TextPrimary;
        button.Font = new Font("Segoe UI Semibold", 10F);
        button.Cursor = Cursors.Hand;
        button.UseVisualStyleBackColor = false;
    }

    private async Task LoadModes()
    {
        if (selectedMonitor is null) return;
        var target = selectedMonitor;
        (Native.DEVMODE Active, int MinimumHz, List<DisplayMode> Choices) result;
        try
        {
            result = await Task.Run(() =>
            {
                var active = Native.GetMode(Native.ENUM_CURRENT_SETTINGS, target.DisplayDeviceName);
                var minimumHz = Math.Max(0, active.Frequency > 0 ? active.Frequency : target.RefreshHz);
                var available = new Dictionary<(int Width, int Height, int Hz), DisplayMode>();
                for (uint i = 0; ; i++)
                {
                    var dm = Native.CreateMode();
                    if (!Native.EnumDisplaySettings(target.DisplayDeviceName, i, ref dm)) break;
                    if (dm.Width < 640 || dm.Height < 480 || dm.Frequency <= 1) continue;
                    var mode = new DisplayMode { DeviceName = target.DisplayDeviceName, Width = dm.Width, Height = dm.Height, Frequency = dm.Frequency, NativeMode = dm };
                    available[(mode.Width, mode.Height, mode.Frequency)] = mode;
                }
                var customModes = DriverCustomModes.Read(target.DisplayDeviceName);
                var choices = available.Values
                    .Where(m => StandardStretchResolutions.Contains((m.Width, m.Height)) || customModes.Contains((m.Width, m.Height)))
                    .Where(m => (m.Width >= 1000 || m.Height >= 1000) && m.Frequency >= minimumHz)
                    .GroupBy(m => (m.Width, m.Height))
                    .Select(g => g.OrderByDescending(m => m.Frequency).First())
                    .OrderByDescending(m => m.Width).ThenByDescending(m => m.Height).ToList();
                return (Active: active, MinimumHz: minimumHz, Choices: choices);
            });
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(target, selectedMonitor))
            {
                modeMenu.Items.Clear();
                selectedMode = null;
                SetModePickerText(null, "Display modes unavailable");
                modePicker.Enabled = false;
                apply.Enabled = false;
                status.Text = "Couldn't read display modes: " + ex.Message;
            }
            return;
        }
        if (!ReferenceEquals(target, selectedMonitor)) return;
        nativeWidth = target.PreferredWidth;
        nativeHeight = target.PreferredHeight;
        var active = result.Active;
        minimumRefresh = result.MinimumHz;
        SetCurrentReadout(active.Width, active.Height, active.Frequency);
        originalMode = new DisplayMode { DeviceName = selectedMonitor.DisplayDeviceName, Width = active.Width, Height = active.Height, Frequency = active.Frequency, NativeMode = active };
        modeMenu.Items.Clear();
        foreach (var mode in result.Choices)
        {
            var item = new ToolStripMenuItem(mode.ToString()) { Tag = mode, ForeColor = TextPrimary, BackColor = SurfaceRaised, Padding = new Padding(12, 8, 16, 8) };
            item.Click += SelectMode;
            modeMenu.Items.Add(item);
        }
        selectedMode = result.Choices.FirstOrDefault();
        modePicker.Enabled = selectedMode is not null;
        SetModePickerText(selectedMode, selectedMode is null ? "No supported modes" : $"{selectedMode.Width} × {selectedMode.Height} · {selectedMode.Frequency}Hz");
        FitModePicker();
        apply.Enabled = result.Choices.Count > 0 && selectedMonitor.CanChangeIndependently;
        status.Text = !selectedMonitor.CanChangeIndependently
            ? "This monitor shares a display path. Changing it could affect the other monitor, so it is blocked."
            : result.Choices.Count == 0
            ? $"No available modes for {selectedMonitor.ModelName} at {minimumRefresh} Hz or higher."
            : "";
    }

    private bool EnsureElevatedInstance()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        if (new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator)) return true;
        status.Text = "Administrator access is required. Restart DPT using Run as administrator.";
        return false;
    }

    private void StartRecoveryGuard()
    {
        if (originalMode is null) throw new IOException("Current display mode was not recorded.");
        var monitors = monitorTargets.Select(target => monitorSnapshots.First(snapshot =>
            string.Equals(snapshot.InstanceId, target.InstanceId, StringComparison.OrdinalIgnoreCase))).ToArray();
        recoveryGuard = RecoveryGuard.Start(new RecoveryState(originalMode.DeviceName,
            originalMode.Width, originalMode.Height, originalMode.Frequency,
            monitors, originalGpuScaling, originalAmdScaling, originalIntelScaling, originalWindowsScaling));
    }

    private bool FinishRecoveryGuard()
    {
        if (recoveryGuard is null) return true;
        try { recoveryGuard.Complete(); return true; }
        catch { return false; }
        finally { recoveryGuard.Dispose(); recoveryGuard = null; }
    }

    private async Task ApplyMode()
    {
        if (!EnsureElevatedInstance()) return;
        using var busy = BeginOperation();
        status.Text = "Checking display state…";
        if (selectedMode is not DisplayMode selected) return;
        if (selectedMonitor is null)
        {
            status.Text = "Choose an identified active monitor first.";
            return;
        }
        if (!selectedMonitor.CanChangeIndependently)
        {
            status.Text = "This monitor shares a display path. The app will not risk changing the other monitor.";
            status.ForeColor = TextPrimary;
            return;
        }
        if (NativeResolutionDetected)
        {
            nativeWidth = selectedMonitor.PreferredWidth;
            nativeHeight = selectedMonitor.PreferredHeight;
        }
        if (nativeWidth <= 0 || nativeHeight <= 0)
        {
            status.Text = "Couldn't read the monitor's preferred resolution.";
            status.ForeColor = TextPrimary;
            return;
        }
        try
        {
            monitorSnapshots = await Task.Run(MonitorControl.SnapshotAll);
        }
        catch (Exception ex)
        {
            status.Text = "Could not record monitor states: " + ex.Message;
            status.ForeColor = TextPrimary;
            return;
        }
        var selectedSnapshot = monitorSnapshots.FirstOrDefault(s =>
            string.Equals(s.InstanceId, selectedMonitor.InstanceId, StringComparison.OrdinalIgnoreCase));
        if (selectedSnapshot is null || !await Task.Run(() => MonitorControl.IsActivePath(selectedMonitor)))
        {
            status.Text = "Selected monitor identity or active display path changed. Scan again before applying.";
            status.ForeColor = TextPrimary;
            await LoadMonitors();
            return;
        }
        if (selectedSnapshot.Enabled != selectedMonitor.Enabled)
        {
            status.Text = "Selected monitor state changed. Scan again before applying.";
            status.ForeColor = TextPrimary;
            await LoadMonitors();
            return;
        }
        if (!pending)
        {
            try { originalMode = CurrentMode(); }
            catch (IOException ex)
            {
                status.Text = "Could not record the current display mode: " + ex.Message;
                return;
            }
        }
        originalAmdScaling = null;
        originalIntelScaling = null;
        originalWindowsScaling = null;
        try
        {
            scalingDevice = selectedMonitor.DisplayDeviceName;
            originalGpuScaling = await Task.Run(() => NvidiaScaling.ReadCurrent(scalingDevice));
        }
        catch (Exception nvidiaError)
        {
            originalGpuScaling = null;
            try { originalAmdScaling = await Task.Run(() => AmdScaling.ReadCurrent(
                selectedMonitor.DisplayDeviceName, selectedMonitor.ModelName)); }
            catch (Exception amdError)
            {
                try { originalIntelScaling = await Task.Run(() => IntelScaling.ReadCurrent(selectedMonitor.DisplayTargetId)); }
                catch (Exception intelError)
                {
                    try { originalWindowsScaling = await Task.Run(() => WindowsScaling.ReadCurrent(
                        selectedMonitor.DisplayDeviceName, selectedMonitor.DisplayTargetId)); }
                    catch (Exception windowsError)
                    {
                        status.Text = "GPU scaling unavailable: " + nvidiaError.Message + " / " + amdError.Message + " / " + intelError.Message + " / " + windowsError.Message;
                        return;
                    }
                }
            }
        }
        try { await Task.Run(StartRecoveryGuard); }
        catch (Exception ex)
        {
            status.Text = "Cannot prepare automatic recovery: " + ex.Message;
            return;
        }
        status.Text = "Applying resolution…";
        var result = await Task.Run(() => ChangeMode(selected));
        if (result != Native.DISP_CHANGE_SUCCESSFUL)
        {
            await Task.Run(FinishRecoveryGuard);
            status.Text = $"Windows couldn't apply {selected.Width} × {selected.Height} at {selected.Frequency} Hz (code {result}).";
            status.ForeColor = TextPrimary;
            return;
        }
        try
        {
            recoveryGuard!.Send("WORKING");
            status.Text = "Applying fullscreen scaling…";
            if (originalAmdScaling is not null)
                await Task.Run(() => AmdScaling.ApplyFullPanel(originalAmdScaling));
            else if (originalIntelScaling is not null)
                await Task.Run(() => IntelScaling.ApplyStretched(originalIntelScaling));
            else if (originalWindowsScaling is not null)
                await Task.Run(() => WindowsScaling.ApplyStretched(originalWindowsScaling));
            else
                // NV_SCALING_GPU_SCALING_TO_NATIVE = Force GPU, Full Screen.
                await Task.Run(() => NvidiaScaling.Apply(scalingDevice!, 2));
        }
        catch (Exception ex)
        {
            await RestoreMode();
            status.Text = "GPU scaling failed: " + ex.Message;
            return;
        }
        pendingMonitorsEnabled = false;
        status.Text = "Updating monitor devices…";
        var (disabledAll, disableError) = await SetAllMonitorsAsync(false);
        if (!disabledAll)
        {
            await RestoreMode();
            status.Text = disableError;
            return;
        }
        try { recoveryGuard!.Send("ARM"); }
        catch (Exception ex)
        {
            await RestoreMode();
            status.Text = "Recovery process stopped: " + ex.Message;
            return;
        }
        UpdateMonitorPicker();
        pending = true;
        pendingDeviceOnly = false;
        secondsLeft = 10;
        timer.Start();
        apply.Enabled = false;
        monitorPicker.Enabled = false;
        modePicker.Enabled = false;
        UpdateMonitorStateButton();
        monitorStateButton.Text = "Revert";
        keep.Visible = true;
        revert.Visible = true;
        LayoutStatusArea();
        status.ForeColor = TextPrimary;
        status.Text = $"{selectedMonitor.ModelName} updated. Keep within {secondsLeft}s or everything rolls back.";
    }

    private async Task RevertToNative()
    {
        if (!EnsureElevatedInstance()) return;
        using var busy = BeginOperation();
        if (pending) return;
        status.Text = "Restoring native display…";
        if (selectedMonitor is null || !selectedMonitor.CanChangeIndependently)
        {
            status.Text = "Choose an independently active monitor first.";
            status.ForeColor = TextPrimary;
            return;
        }
        if (NativeResolutionDetected)
        {
            nativeWidth = selectedMonitor.PreferredWidth;
            nativeHeight = selectedMonitor.PreferredHeight;
        }
        if (nativeWidth <= 0 || nativeHeight <= 0)
        {
            status.Text = "Couldn't detect this monitor's native resolution.";
            status.ForeColor = TextPrimary;
            return;
        }
        var nativeMode = await Task.Run(() =>
        {
            DisplayMode? found = null;
            for (uint i = 0; ; i++)
            {
                var mode = Native.CreateMode();
                if (!Native.EnumDisplaySettings(selectedMonitor.DisplayDeviceName, i, ref mode)) break;
                if (mode.Width == nativeWidth && mode.Height == nativeHeight &&
                    (found is null || mode.Frequency > found.Frequency))
                    found = new DisplayMode { DeviceName = selectedMonitor.DisplayDeviceName, Width = mode.Width, Height = mode.Height, Frequency = mode.Frequency, NativeMode = mode };
            }
            return found;
        });
        Native.DEVMODE current;
        try { current = Native.GetMode(Native.ENUM_CURRENT_SETTINGS, selectedMonitor.DisplayDeviceName); }
        catch (IOException ex)
        {
            status.Text = "Could not read the current display mode: " + ex.Message;
            return;
        }
        var alreadyNative = current.Width == nativeWidth && current.Height == nativeHeight;
        if (nativeMode is null && !alreadyNative)
        {
            status.Text = $"Windows doesn't offer {nativeWidth} × {nativeHeight} for this display.";
            status.ForeColor = TextPrimary;
            return;
        }
        var result = alreadyNative ? Native.DISP_CHANGE_SUCCESSFUL : await Task.Run(() => ChangeMode(nativeMode));
        status.ForeColor = TextPrimary;
        if (result != Native.DISP_CHANGE_SUCCESSFUL)
        {
            status.Text = $"Windows couldn't restore native resolution (code {result}).";
            return;
        }
        if (monitorTargets.Any(target => !target.Enabled))
        {
            try { monitorSnapshots = await Task.Run(MonitorControl.SnapshotAll); }
            catch (Exception ex) { status.Text = "Native resolution restored, but monitor states could not be read: " + ex.Message; return; }
            var (enabled, error) = await SetAllMonitorsAsync(true, deviceOnly: true);
            if (!enabled) { status.Text = "Native resolution restored, but monitors could not be enabled: " + error; return; }
            changedMonitorIds.Clear();
            monitorSnapshots.Clear();
        }
        UpdateCurrentDisplay();
        UpdateMonitorStateButton();
        status.Text = $"Native resolution restored: {nativeWidth} × {nativeHeight}. All monitors enabled.";
    }

    private async Task Countdown()
    {
        secondsLeft--;
        if (secondsLeft <= 0) { await RestoreMode(); return; }
        status.Text = pendingDeviceOnly
            ? $"Monitor devices {(pendingMonitorsEnabled ? "enabled" : "disabled")}. Keep within {secondsLeft}s or it rolls back."
            : $"{selectedMonitor?.ModelName ?? "Monitor"} updated. Keep within {secondsLeft}s or everything rolls back.";
    }

    private async Task ConfirmMode()
    {
        if (!pending || confirmingMode) return;
        confirmingMode = true;
        timer.Stop();
        keep.Enabled = false;
        revert.Enabled = false;
        if (!await Task.Run(FinishRecoveryGuard))
        {
            confirmingMode = false;
            _ = RestoreMode();
            return;
        }
        pending = false;
        apply.Enabled = modeMenu.Items.Count > 0 && selectedMonitor?.CanChangeIndependently == true;
        keep.Visible = false;
        revert.Visible = false;
        status.ForeColor = TextPrimary;
        status.Text = pendingDeviceOnly
            ? $"All identified monitor devices remain {(pendingMonitorsEnabled ? "enabled" : "disabled")}."
            : $"Confirmed. Display preset {selectedMode?.Width} × {selectedMode?.Height} kept.";
        pendingDeviceOnly = false;
        changedMonitorIds.Clear();
        originalGpuScaling = null;
        originalAmdScaling = null;
        originalIntelScaling = null;
        originalWindowsScaling = null;
        scalingDevice = null;
        if (changedMonitorIds.Count == 0) monitorSnapshots.Clear();
        monitorPicker.Enabled = true;
        modePicker.Enabled = modeMenu.Items.Count > 0;
        UpdateMonitorStateButton();
        UpdateCurrentDisplay();
        confirmingMode = false;
        if (refreshAfterOperation) ScheduleDisplayRefresh();
    }

    private async Task RestoreMode()
    {
        using var busy = BeginOperation();
        timer.Stop();
        status.Text = "Restoring saved display state…";
        try { recoveryGuard?.Send("RESTORING"); } catch { }
        var wasDeviceOnly = pendingDeviceOnly;
        var (monitorRestored, monitorError) = await RestoreMonitorAsync();
        var (modeRestored, scalingRestored) = wasDeviceOnly
            ? (true, true)
            : await Task.Run(() => (originalMode is null || ChangeMode(originalMode) == Native.DISP_CHANGE_SUCCESSFUL, RestoreGpuScaling()));
        var restored = monitorRestored && modeRestored && scalingRestored;
        if (restored) await Task.Run(FinishRecoveryGuard);
        pending = !restored;
        pendingDeviceOnly = !restored && wasDeviceOnly;
        apply.Enabled = restored && modeMenu.Items.Count > 0 && selectedMonitor?.CanChangeIndependently == true;
        monitorPicker.Enabled = restored && monitorTargets.Count > 0;
        modePicker.Enabled = modeMenu.Items.Count > 0;
        keep.Visible = false;
        revert.Visible = !restored;
        status.ForeColor = TextMuted;
        status.Text = !monitorRestored
            ? "Monitor restore failed: " + monitorError
            : !modeRestored ? "Display mode restore failed; use Revert again."
            : wasDeviceOnly ? "Monitor devices returned to their recorded states."
            : scalingRestored ? "Display, monitor states, and GPU scaling restored."
            : "Display restored; GPU scaling restore needs attention.";
        if (changedMonitorIds.Count == 0) monitorSnapshots.Clear();
        UpdateMonitorStateButton();
        UpdateCurrentDisplay();
        if (refreshAfterOperation) ScheduleDisplayRefresh();
    }

    private void UpdateCurrentDisplay()
    {
        Native.DEVMODE active;
        try
        {
            active = selectedMonitor is null ? Native.GetMode(Native.ENUM_CURRENT_SETTINGS) :
                Native.GetMode(Native.ENUM_CURRENT_SETTINGS, selectedMonitor.DisplayDeviceName);
        }
        catch (IOException)
        {
            currentResolution.Text = "Unavailable";
            currentRefresh.Text = "— Hz";
            currentDisplayPill.Text = "DISPLAY UNAVAILABLE";
            return;
        }
        SetCurrentReadout(active.Width, active.Height, active.Frequency);
    }

    private void SetCurrentReadout(int width, int height, int frequency)
    {
        currentResolution.Text = $"{width} × {height}";
        currentRefresh.Text = $"{frequency} Hz";
        if (width <= 0 || height <= 0) { currentDisplayPill.Text = "DISPLAY MODE"; return; }
        var ratio = (double)width / height;
        var aspect = Math.Abs(ratio - 16d / 9) < .025 ? "16:9"
            : Math.Abs(ratio - 16d / 10) < .025 ? "16:10"
            : Math.Abs(ratio - 4d / 3) < .025 ? "4:3"
            : Math.Abs(ratio - 21d / 9) < .04 ? "21:9"
            : "CUSTOM";
        var label = aspect == "21:9" ? (width >= 3440 ? "UWQHD" : "ULTRAWIDE")
            : aspect == "16:10" ? (width >= 2560 ? "WQXGA" : "WUXGA")
            : aspect == "4:3" ? "4:3 DISPLAY"
            : width >= 3840 ? "4K UHD"
            : width >= 2560 ? "QHD"
            : width >= 1920 ? "FHD"
            : "HD";
        currentDisplayPill.Text = $"{label}  ·  {aspect}";
    }

    private DisplayMode CurrentMode()
    {
        var deviceName = selectedMonitor?.DisplayDeviceName ?? "";
        var m = Native.GetMode(Native.ENUM_CURRENT_SETTINGS, deviceName);
        return new DisplayMode { DeviceName = deviceName, Width = m.Width, Height = m.Height, Frequency = m.Frequency, NativeMode = m };
    }

    private readonly Panel contentHost = new() { Dock = DockStyle.Fill };
    private int operationDepth;
    private async Task RunSafely(Func<Task> action, string label)
    {
        try { await action(); }
        catch (Exception ex)
        {
            var message = label + ": " + ex.Message;
            if (recoveryGuard is not null && !confirmingMode)
            {
                try
                {
                    await RestoreMode();
                    message += pending ? " Restore needs attention." : " Original display restored.";
                }
                catch (Exception restoreError) { message += " Recovery is still active: " + restoreError.Message; }
            }
            status.ForeColor = TextPrimary;
            status.Text = message;
        }
    }

    private IDisposable BeginOperation()
    {
        operationDepth++;
        contentHost.Enabled = false;
        UseWaitCursor = true;
        return new OperationScope(this);
    }
    private sealed class OperationScope(MainForm form) : IDisposable
    {
        public void Dispose()
        {
            if (--form.operationDepth == 0)
            {
                form.contentHost.Enabled = true;
                form.UseWaitCursor = false;
                if (form.refreshAfterOperation && !form.pending) form.ScheduleDisplayRefresh();
            }
        }
    }
    private async Task<(bool Success, string Error)> RestoreMonitorAsync()
    {
        var result = await Task.Run(() => { var ok = RestoreSelectedMonitorState(out var error); return (ok, error); });
        UpdateMonitorPicker();
        UpdateMonitorStateButton();
        RefreshMonitorMenuLabels();
        return result;
    }
    private async Task<(bool Success, string Error)> SetAllMonitorsAsync(bool enabled, bool deviceOnly = false)
    {
        var targets = monitorTargets.ToArray();
        var result = await Task.Run(() =>
        {
            try
            {
                if (targets.Length == 0) return (false, "No monitors could be identified safely.");
                var devices = NativeMonitorDevices.Enumerate();
                if (!MonitorControl.AreActivePaths(targets, devices)) throw new IOException("Monitor paths changed; scan again.");
                foreach (var target in targets)
                {
                    var snapshot = monitorSnapshots.FirstOrDefault(s => string.Equals(s.InstanceId, target.InstanceId, StringComparison.OrdinalIgnoreCase));
                    if (snapshot is null) throw new IOException("Monitor list changed; scan again.");
                    if (snapshot.Enabled == enabled) continue;
                    recoveryGuard?.Send("WORKING");
                    changedMonitorIds.Add(snapshot.InstanceId);
                    if (!MonitorControl.SetEnabled(snapshot.InstanceId, enabled, devices, out var error)) throw new IOException(error);
                    if (!MonitorControl.AreActivePaths(targets, devices)) throw new IOException("A monitor left the active display topology. Changes were cancelled.");
                }
                foreach (var target in monitorTargets) target.Enabled = enabled;
                return (true, "");
            }
            catch (Exception ex)
            {
                var restored = RestoreSelectedMonitorState(out var restoreError);
                return (false, ex.Message + (restored ? " Original device states restored." : " Restore needs attention: " + restoreError));
            }
        });
        UpdateMonitorPicker();
        if (!result.Item1 && changedMonitorIds.Count > 0)
        {
            pending = true;
            pendingDeviceOnly = deviceOnly;
            apply.Enabled = false;
            monitorPicker.Enabled = false;
            monitorStateButton.Enabled = false;
            keep.Visible = false;
            revert.Visible = true;
        }
        // Menu rows must reflect the new state, not their startup snapshot.
        RefreshMonitorMenuLabels();
        return result;
    }

    private void RefreshMonitorMenuLabels()
    {
        foreach (ToolStripItem item in monitorMenu.Items)
            if (item.Tag is MonitorTarget target)
                item.Text = $"{target.ModelName}   ·   {target.RefreshHz} Hz   ·   {(target.Enabled ? "enabled" : "disabled")}";
    }
    private bool RestoreSelectedMonitorState(out string error)
    {
        var errors = new List<string>();
        foreach (var id in changedMonitorIds.ToArray())
        {
            var snapshot = monitorSnapshots.FirstOrDefault(s => string.Equals(s.InstanceId, id, StringComparison.OrdinalIgnoreCase));
            if (snapshot is null) { errors.Add("Missing recorded state for " + id); continue; }
            if (!MonitorControl.SetEnabled(id, snapshot.Enabled, out var failure)) { errors.Add(id + ": " + failure); continue; }
            foreach (var target in monitorTargets.Where(t => string.Equals(t.InstanceId, id, StringComparison.OrdinalIgnoreCase)))
                target.Enabled = snapshot.Enabled;
            changedMonitorIds.Remove(id);
        }
        error = string.Join("; ", errors);
        return errors.Count == 0;
    }
    private bool RestoreGpuScaling()
    {
        var restored = true;
        if (originalGpuScaling.HasValue && scalingDevice is not null)
        {
            try { NvidiaScaling.Apply(scalingDevice, originalGpuScaling.Value); originalGpuScaling = null; }
            catch { restored = false; }
        }
        if (originalAmdScaling is not null)
        {
            try { AmdScaling.Restore(originalAmdScaling); originalAmdScaling = null; }
            catch { restored = false; }
        }
        if (originalIntelScaling is not null)
        {
            try { IntelScaling.Restore(originalIntelScaling); originalIntelScaling = null; }
            catch { restored = false; }
        }
        if (originalWindowsScaling is not null)
        {
            try { WindowsScaling.Restore(originalWindowsScaling); originalWindowsScaling = null; }
            catch { restored = false; }
        }
        return restored;
    }
    private static int ChangeMode(DisplayMode? mode)
    {
        if (mode is null) return -1;
        var dm = mode.NativeMode;
        dm.Width = mode.Width;
        dm.Height = mode.Height;
        dm.Frequency = mode.Frequency;
        dm.Fields |= Native.DM_PELSWIDTH | Native.DM_PELSHEIGHT | Native.DM_DISPLAYFREQUENCY;
        var deviceName = string.IsNullOrWhiteSpace(mode.DeviceName) ? null : mode.DeviceName;
        var test = Native.ChangeDisplaySettings(deviceName, ref dm, Native.CDS_TEST);
        return test == Native.DISP_CHANGE_SUCCESSFUL
            ? Native.ChangeDisplaySettings(deviceName, ref dm, Native.CDS_UPDATEREGISTRY)
            : test;
    }
}

internal static class Native
{
    internal const uint ENUM_CURRENT_SETTINGS = 0xFFFFFFFF;
    internal const int CDS_TEST = 0x00000002, CDS_UPDATEREGISTRY = 0x00000001, DISP_CHANGE_SUCCESSFUL = 0;
    internal const int DM_PELSWIDTH = 0x00080000, DM_PELSHEIGHT = 0x00100000, DM_DISPLAYFREQUENCY = 0x00400000;
    private const uint DISPLAY_DEVICE_ACTIVE = 0x00000001, DISPLAY_DEVICE_PRIMARY_DEVICE = 0x00000004;

    [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, uint size);

    internal static void ApplyDarkWindowChrome(IntPtr handle)
    {
        var enabled = 1;
        var size = (uint)Marshal.SizeOf<int>();
        if (DwmSetWindowAttribute(handle, 20, ref enabled, size) != 0)
            DwmSetWindowAttribute(handle, 19, ref enabled, size);
        var roundedCorners = 2;
        DwmSetWindowAttribute(handle, 33, ref roundedCorners, size);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string? deviceName, uint deviceNumber, ref DISPLAY_DEVICE displayDevice, uint flags);

    private static DISPLAY_DEVICE CreateDisplayDevice() => new()
    {
        Size = Marshal.SizeOf<DISPLAY_DEVICE>(),
        DeviceName = new string('\0', 32),
        DeviceString = new string('\0', 128),
        DeviceID = new string('\0', 128),
        DeviceKey = new string('\0', 128)
    };

    internal static bool TryGetPreferredMonitorMode(out int width, out int height)
    {
        width = height = 0;
        for (uint adapterIndex = 0; adapterIndex < 16; adapterIndex++)
        {
            var adapter = CreateDisplayDevice();
            if (!EnumDisplayDevices(null, adapterIndex, ref adapter, 0)) break;
            if ((adapter.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) == 0) continue;
            for (uint monitorIndex = 0; monitorIndex < 16; monitorIndex++)
            {
                var monitor = CreateDisplayDevice();
                if (!EnumDisplayDevices(adapter.DeviceName, monitorIndex, ref monitor, 0)) break;
                if ((monitor.StateFlags & DISPLAY_DEVICE_ACTIVE) == 0) continue;
                if (TryReadEdidPreferredMode(monitor.DeviceID, out width, out height)) return true;
            }
        }
        return TryFindPreferredModeFromConnectedEdids(out width, out height);
    }

    private static bool TryReadEdidPreferredMode(string deviceId, out int width, out int height)
    {
        width = height = 0;
        var parts = deviceId.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || !string.Equals(parts[0], "MONITOR", StringComparison.OrdinalIgnoreCase)) return false;
        var instance = parts.Length >= 4 ? $"{parts[2]}\\{parts[3]}" : parts[2];
        var keyPath = $@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{parts[1]}\{instance}\Device Parameters";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath, writable: false);
            if (key?.GetValue("EDID") is not byte[] edid || edid.Length < 128) return false;
            var bestArea = 0;
            var firstIsPreferred = edid[18] == 1 && edid[19] >= 3 && (edid[24] & 0x02) != 0;
            for (var offset = 54; offset <= 108; offset += 18)
            {
                if (edid[offset] == 0 && edid[offset + 1] == 0) continue;
                var candidateWidth = edid[offset + 2] | ((edid[offset + 4] & 0xF0) << 4);
                var candidateHeight = edid[offset + 5] | ((edid[offset + 7] & 0xF0) << 4);
                var area = candidateWidth * candidateHeight;
                if (candidateWidth <= 0 || candidateHeight <= 0) continue;
                if (firstIsPreferred && offset == 54)
                {
                    width = candidateWidth;
                    height = candidateHeight;
                    return true;
                }
                if (area > bestArea) { bestArea = area; width = candidateWidth; height = candidateHeight; }
            }
            return bestArea > 0;
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
    }

    private static bool TryFindPreferredModeFromConnectedEdids(out int width, out int height)
    {
        width = height = 0;
        var supported = new HashSet<(int Width, int Height)>();
        for (uint i = 0; ; i++)
        {
            var mode = CreateMode();
            if (!EnumDisplaySettings(null, i, ref mode)) break;
            if (mode.Width > 0 && mode.Height > 0) supported.Add((mode.Width, mode.Height));
        }

        try
        {
            using var displays = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY", writable: false);
            if (displays is null) return false;
            var candidates = new List<(int Width, int Height, bool Supported)>();
            foreach (var hardware in displays.GetSubKeyNames())
            using (var hardwareKey = displays.OpenSubKey(hardware, writable: false))
            {
                if (hardwareKey is null) continue;
                foreach (var instance in hardwareKey.GetSubKeyNames())
                using (var instanceKey = hardwareKey.OpenSubKey(instance + @"\Device Parameters", writable: false))
                {
                    if (instanceKey?.GetValue("EDID") is not byte[] edid || !TryParseEdid(edid, out var w, out var h)) continue;
                    candidates.Add((w, h, supported.Contains((w, h))));
                }
            }
            var matching = candidates.Where(c => c.Supported).OrderByDescending(c => c.Width * c.Height).FirstOrDefault();
            var selected = matching.Width > 0 ? matching : candidates.OrderByDescending(c => c.Width * c.Height).FirstOrDefault();
            if (selected.Width <= 0 || selected.Height <= 0) return false;
            width = selected.Width;
            height = selected.Height;
            return true;
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
    }

    private static bool TryParseEdid(byte[] edid, out int width, out int height)
    {
        width = height = 0;
        if (edid.Length < 128) return false;
        var bestArea = 0;
        var firstIsPreferred = edid[18] == 1 && edid[19] >= 3 && (edid[24] & 0x02) != 0;
        for (var offset = 54; offset <= 108; offset += 18)
        {
            if (edid[offset] == 0 && edid[offset + 1] == 0) continue;
            var w = edid[offset + 2] | ((edid[offset + 4] & 0xF0) << 4);
            var h = edid[offset + 5] | ((edid[offset + 7] & 0xF0) << 4);
            var area = w * h;
            if (w <= 0 || h <= 0) continue;
            if (firstIsPreferred && offset == 54) { width = w; height = h; return true; }
            if (area > bestArea) { bestArea = area; width = w; height = h; }
        }
        return bestArea > 0;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public ushort SpecVersion, DriverVersion, Size, DriverExtra;
        public uint Fields;
        public DISPLAY_UNION Position;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public ushort LogPixels;
        public uint BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency;
        public uint ICMMethod, ICMIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
        public int Width { readonly get => (int)PelsWidth; set => PelsWidth = (uint)value; }
        public int Height { readonly get => (int)PelsHeight; set => PelsHeight = (uint)value; }
        public int Frequency { readonly get => (int)DisplayFrequency; set => DisplayFrequency = (uint)value; }
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct DISPLAY_UNION
    {
        [FieldOffset(0)] public int PositionX;
        [FieldOffset(4)] public int PositionY;
        [FieldOffset(8)] public uint DisplayOrientation;
        [FieldOffset(12)] public uint DisplayFixedOutput;
    }

    internal static DEVMODE CreateMode() => new()
    {
        DeviceName = new string('\0', 32),
        FormName = new string('\0', 32),
        Size = (ushort)Marshal.SizeOf<DEVMODE>()
    };

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplaySettings(string? deviceName, uint modeNum, ref DEVMODE devMode);

    [DllImport("user32.dll", EntryPoint = "ChangeDisplaySettingsExW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int ChangeDisplaySettingsEx(string? deviceName, ref DEVMODE devMode, IntPtr window, uint flags, IntPtr parameter);

    internal static int ChangeDisplaySettings(string? deviceName, ref DEVMODE devMode, uint flags)
        => ChangeDisplaySettingsEx(deviceName, ref devMode, IntPtr.Zero, flags, IntPtr.Zero);

    internal static DEVMODE GetMode(uint mode, string? deviceName = null)
    {
        var dm = CreateMode();
        if (!EnumDisplaySettings(deviceName, mode, ref dm))
            throw new IOException($"Windows could not read the current mode for {deviceName ?? "the primary display"}.");
        return dm;
    }
}










