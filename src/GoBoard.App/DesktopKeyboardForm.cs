using System.Diagnostics;
using GoBoard.Core;
using GoBoard.Platform.Windows;
using SkiaSharp;
using GoBoard.Presentation.Skia;

namespace GoBoard.App;

internal sealed class DesktopKeyboardForm : Form
{
    private readonly WindowsKeyboard output = new();
    private readonly KeyAudio audio = new();
    private readonly AnimatedKeyboardRenderer renderer = new();
    private readonly KeyboardState keyboard;
    private readonly SettingsStore settings;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 16 };
    private readonly bool previewOnly;
    private readonly bool fixedPreview;
    private readonly string stopFile;
    private readonly Func<bool> stopRequested;
    private readonly double seconds;
    private readonly double started = Now;
    private double nextSettingsRead, errorUntil;
    private BoardSettings applied;
    private string error;
    private bool faulted, closing, keyCapture;
    private int headerCapture;
    private SettingsForm settingsForm;
    private DesktopFrame frame;
    private readonly DesktopShortcuts shortcuts;
    internal DesktopShortcuts Shortcuts => shortcuts;
    internal bool PrepareShortcutInput() => RefreshTarget();
    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    private int HeaderHeight => (int)Math.Round(42 * DeviceDpi / 96f);
    private Rectangle SettingsButton => new(ClientSize.Width - (int)(144 * DeviceDpi / 96f), 0, (int)(100 * DeviceDpi / 96f), HeaderHeight);
    private Rectangle CloseButton => new(SettingsButton.Right, 0, ClientSize.Width - SettingsButton.Right, HeaderHeight);
    private DesktopGeometry Geometry => new(ClientSize.Width, ClientSize.Height, HeaderHeight, keyboard.Width);
    internal KeyboardState State => keyboard;
    internal bool HasOwnedKeys => output.HasOwnedKeys;
    internal Point SettingsPoint => new(SettingsButton.Left + SettingsButton.Width / 2, SettingsButton.Top + SettingsButton.Height / 2);
    internal Point KeyPoint(string id)
    {
        var b = keyboard.Keys.Single(k => k.Id == id).Bounds;
        return new((int)((b.X + b.Width / 2) * ClientSize.Width / keyboard.Width),
            HeaderHeight + (int)((b.Y + b.Height / 2) * (ClientSize.Height - HeaderHeight) / OverlayGeometry.PanelHeight));
    }

    public DesktopKeyboardForm(string stopFile = null, double seconds = double.PositiveInfinity, bool previewOnly = false, SettingsStore store = null, bool previewShortcuts = false, Func<bool> stopRequested = null, bool previewNumpad = false)
    {
        this.stopFile = stopFile; this.seconds = seconds; this.previewOnly = previewOnly;
        this.stopRequested = stopRequested;
        fixedPreview = previewOnly && store == null;
        settings = store ?? new SettingsStore();
        keyboard = new KeyboardState(output);
        Text = "GoBoard Desktop";
        FormBorderStyle = FormBorderStyle.None;
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        SetStyle(ControlStyles.Selectable, false);
        SetStyle(ControlStyles.UserMouse, true);
        BackColor = Color.FromArgb(12, 21, 30);
        ForeColor = Color.FromArgb(241, 246, 252);
        Font = new Font("Segoe UI", 10);
        if (previewOnly) Opacity = 0;
        applied = fixedPreview ? new BoardSettings { NumpadEnabled = previewNumpad, ProgrammableKeys = new() { Enabled = previewShortcuts } } : settings.Current;
        keyboard.SetNumpad(applied.NumpadEnabled, Now);
        audio.Apply(applied);
        ResetPosition();
        shortcuts = new(this, output, previewOnly);
        timer.Tick += (_, _) => Frame();
        Shown += (_, _) => { ApplySize(); Frame(); timer.Start(); };
        ResizeBegin += (_, _) => Cancel();
        DpiChanged += (_, _) => { Cancel(); ApplySize(); };
        VisibleChanged += (_, _) => { if (!Visible) { Cancel(); shortcuts?.Update(applied, false); } };
    }

    // No activation on show, clicks, drag, or Windows' activate-on-hover mode.
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        // WinForms' TopMost setter calls SetWindowPos without NOACTIVATE.
        // Set TOPMOST at window creation instead to preserve the typing target.
        get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x00000080 | 0x00000008; return p; }
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x21) { m.Result = 3; return; } // WM_MOUSEACTIVATE / MA_NOACTIVATE
        if (m.Msg == 0x84) // WM_NCHITTEST: draggable header, with clickable controls.
        {
            var packed = (long)m.LParam;
            var point = PointToClient(new Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff)));
            if (point.Y >= 0 && point.Y < HeaderHeight && point.X >= 0 && point.X < SettingsButton.Left)
            { m.Result = 2; return; } // HTCAPTION
        }
        if (m.Msg == 0x215) Cancel(); // WM_CAPTURECHANGED, including capture stolen by another window.
        base.WndProc(ref m);
    }

    private void ResetPosition()
    {
        Cancel();
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        ApplySize(area);
        Location = new(area.Left + (area.Width - Width) / 2, Math.Max(area.Top, area.Bottom - Height - 24));
    }

    private void ApplySize(Rectangle? workingArea = null)
    {
        var area = workingArea ?? Screen.FromControl(this).WorkingArea;
        var geometry = DesktopGeometry.Create(applied.Scale, DeviceDpi / 96f, area.Width - 16, area.Height - 16, keyboard.Width);
        ClientSize = new((int)Math.Round(geometry.Width), (int)Math.Round(geometry.Height));
        Location = new(Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width)),
            Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height)));
        Invalidate();
    }

    private bool RefreshTarget()
    {
        if (previewOnly) return false;
        var target = WindowsKeyboard.Foreground();
        if (target != output.Target)
        {
            Cancel();
            output.Target = target;
            keyboard.SetLayout(WindowsLayoutProvider.Get(target.Layout, applied.Geometry), Now);
            Invalidate();
        }
        // Any foreground window can receive shortcuts, including our Settings
        // window. Windows routes input; a focused text control is not required.
        return !faulted && target.Window != 0;
    }

    internal void RefreshSettings()
    {
        if (fixedPreview) return;
        settings.Reload();
        nextSettingsRead = Now + .5;
        if (applied != settings.Current)
        {
            if (applied.Geometry != settings.Current.Geometry)
            {
                Cancel();
                keyboard.SetLayout(WindowsLayoutProvider.Get(output.Target.Layout, settings.Current.Geometry), Now);
            }
            var resized = applied.SizePercent != settings.Current.SizePercent || applied.NumpadEnabled != settings.Current.NumpadEnabled;
            if (applied.NumpadEnabled != settings.Current.NumpadEnabled)
            {
                Cancel();
                keyboard.SetNumpad(settings.Current.NumpadEnabled, Now);
            }
            var resetPosition = applied.PositionResetId != settings.Current.PositionResetId;
            applied = settings.Current;
            audio.Apply(applied);
            if (resetPosition) ResetPosition();
            else if (resized) { Cancel(); ApplySize(); }
            shortcuts.Update(applied, Visible);
        }
    }

    private void Frame()
    {
        if (closing) return;
        if (stopRequested?.Invoke() == true || (stopFile != null && File.Exists(stopFile)) || Now - started >= seconds) { Close(); return; }
        if (Now >= nextSettingsRead) RefreshSettings();
        if (faulted)
        {
            try { output.ReleaseAll(); faulted = false; }
            catch { RenderFrame(); return; }
        }
        try
        {
            if (RefreshTarget() && Visible) keyboard.Tick(Now);
        }
        catch (Exception ex) { Failed(ex); }
        RenderFrame();
        shortcuts.Update(applied, Visible && !closing);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (previewOnly) return;
        var now = Now;
        var point = Geometry.ToKeyboard(e.X, e.Y);
        keyboard.Enter(0, 0, now);
        keyboard.Move(0, 0, point.X, point.Y);
        // Coalesce high-rate mouse motion into the existing 16 ms frame timer.
        // Picking/input update immediately; rendering must not block each event.
    }
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        keyboard.Leave(0, 0, Now);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (previewOnly || e.Button != MouseButtons.Left) return;
        headerCapture = SettingsButton.Contains(e.Location) ? 1 : CloseButton.Contains(e.Location) ? 2 : 0;
        if (headerCapture != 0) { CancelKeyboard(); Capture = true; return; }
        if (!RefreshTarget()) return;
        var now = Now;
        var point = Geometry.ToKeyboard(e.X, e.Y);
        try
        {
            keyboard.Enter(0, 0, now);
            if (keyboard.Press(0, 0, point.X, point.Y, now, now))
            {
                keyCapture = true; Capture = true;
                audio.Click(key: KeyboardLayout.HitOpenVr(point.X, point.Y, keyboard.Keys));
            }
        }
        catch (Exception ex) { Failed(ex); }
        RenderFrame();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (previewOnly || e.Button != MouseButtons.Left) return;
        var action = headerCapture;
        headerCapture = 0;
        // Pointer releases retain one-shot/locked modifiers. Capture loss from
        // another window, in contrast, cancels the entire pending chord.
        if (keyCapture && keyboard.Up(0, 0, Now)) audio.Click(released: true);
        keyCapture = false;
        releasingCapture = true;
        try { Capture = false; } finally { releasingCapture = false; }
        if (action == 2 && CloseButton.Contains(e.Location)) { Close(); return; }
        if (action == 1 && SettingsButton.Contains(e.Location)) OpenSettings();
        RenderFrame();
    }
    private bool releasingCapture;

    private void OpenSettings()
    {
        Cancel();
        if (settingsForm == null || settingsForm.IsDisposed) settingsForm = new SettingsForm(desktopMode: true, store: settings);
        // Owning this window would make it inherit the keyboard's topmost state.
        if (settingsForm.WindowState == FormWindowState.Minimized) settingsForm.WindowState = FormWindowState.Normal;
        if (!settingsForm.Visible) settingsForm.Show();
        settingsForm.BringToFront();
        settingsForm.Activate();
    }

    private void CancelKeyboard()
    {
        audio.Cancel();
        keyboard.Cancel(Now);
        keyCapture = false;
        try { output.ReleaseAll(); }
        catch (Exception ex) { faulted = true; error = ex.Message; errorUntil = Now + 4; }
    }
    private void Cancel()
    {
        if (releasingCapture || keyboard == null) return;
        headerCapture = 0;
        CancelKeyboard();
    }
    private void Failed(Exception ex)
    {
        Cancel();
        error = ex.Message;
        errorUntil = Now + 4;
        Console.Error.WriteLine($"Desktop keyboard: {error}");
    }

    private void RenderFrame()
    {
        var shift = keyboard.Shift || !previewOnly && WindowsKeyboard.PhysicalShift;
        var altGr = keyboard.AltGr || !previewOnly && WindowsKeyboard.PhysicalAltGr ||
            keyboard.Mode(0x1d) != ModifierMode.Idle && keyboard.Mode(0x38) != ModifierMode.Idle;
        var caps = !previewOnly && WindowsKeyboard.CapsLock;
        var scroll = !previewOnly && WindowsKeyboard.ScrollLock;
        keyboard.SetNumLock(previewOnly || WindowsKeyboard.NumLock);
        var notice = faulted || Now < errorUntil ? error : keyboard.Layout.Notice;
        if (ClientSize.Width <= 0 || ClientSize.Height <= HeaderHeight) return;
        var output = new SKImageInfo(ClientSize.Width, ClientSize.Height - HeaderHeight, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var pixels = renderer.Render(keyboard, shift, notice, altGr, caps, scroll, applied.Theme, applied.Effects, Now, output);
        if (pixels == null) return;
        var next = new DesktopFrame(pixels);
        frame?.Dispose();
        frame = next;
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        frame?.Draw(e.Graphics, new Rectangle(0, HeaderHeight, ClientSize.Width, ClientSize.Height - HeaderHeight));
        var title = previewOnly ? "GoBoard · Desktop · Drag to move" : "GoBoard · " + WindowsKeyboard.TargetName(output.Target);
        TextRenderer.DrawText(e.Graphics, title, Font, new Rectangle(12, 0, Math.Max(0, SettingsButton.Left - 16), HeaderHeight), ForeColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        using var background = new SolidBrush(Color.FromArgb(27, 44, 57));
        e.Graphics.FillRectangle(background, SettingsButton);
        TextRenderer.DrawText(e.Graphics, "Settings", Font, SettingsButton, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(e.Graphics, "×", Font, CloseButton, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        closing = true;
        timer.Stop();
        Cancel();
        shortcuts.Update(applied, false);
        settingsForm?.Close();
        base.OnFormClosing(e);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            settingsForm?.Dispose();
            frame?.Dispose();
            shortcuts?.Dispose();
            renderer.Dispose();
            output.Dispose();
            audio.Dispose();
        }
        base.Dispose(disposing);
    }
}
