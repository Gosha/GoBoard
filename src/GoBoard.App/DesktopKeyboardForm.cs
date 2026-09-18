using System.Diagnostics;
using System.Drawing.Imaging;
using GoBoard.Core;
using GoBoard.Platform.Windows;
using SkiaSharp;
using KeyboardPanel = GoBoard.Presentation.Skia.Panel;

namespace GoBoard.App;

internal sealed class DesktopKeyboardForm : Form
{
    private readonly WindowsKeyboard output = new();
    private readonly KeyAudio audio = new();
    private readonly KeyboardState keyboard;
    private readonly SettingsStore settings = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 16 };
    private readonly bool previewOnly;
    private readonly nint testTarget;
    private readonly string stopFile;
    private readonly double seconds;
    private readonly double started = Now;
    private double nextSettingsRead, errorUntil;
    private BoardSettings applied;
    private string error;
    private bool faulted, closing, keyCapture;
    private int headerCapture;
    private SettingsForm settingsForm;
    private Bitmap frame;
    private (int Revision, bool Shift, bool AltGr, bool Caps, bool Scroll, string Status)? drawn;
    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    private int HeaderHeight => (int)Math.Round(42 * DeviceDpi / 96f);
    private Rectangle SettingsButton => new(ClientSize.Width - (int)(144 * DeviceDpi / 96f), 0, (int)(100 * DeviceDpi / 96f), HeaderHeight);
    private Rectangle CloseButton => new(SettingsButton.Right, 0, ClientSize.Width - SettingsButton.Right, HeaderHeight);
    private DesktopGeometry Geometry => new(ClientSize.Width, ClientSize.Height, HeaderHeight);
    internal KeyboardState State => keyboard;
    internal bool HasOwnedKeys => output.HasOwnedKeys;
    internal Point KeyPoint(string id)
    {
        var b = keyboard.Layout.Keys.Single(k => k.Id == id).Bounds;
        return new((int)((b.X + b.Width / 2) * ClientSize.Width / OverlayGeometry.PanelWidth),
            HeaderHeight + (int)((b.Y + b.Height / 2) * (ClientSize.Height - HeaderHeight) / OverlayGeometry.PanelHeight));
    }

    public DesktopKeyboardForm(string stopFile = null, double seconds = double.PositiveInfinity, bool previewOnly = false, nint testTarget = 0)
    {
        this.stopFile = stopFile; this.seconds = seconds; this.previewOnly = previewOnly; this.testTarget = testTarget;
        keyboard = new KeyboardState(output);
        Text = "GoBoard Desktop";
        FormBorderStyle = FormBorderStyle.None;
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        SetStyle(ControlStyles.Selectable, false);
        SetStyle(ControlStyles.UserMouse, true);
        BackColor = Color.FromArgb(16, 24, 32);
        ForeColor = Color.FromArgb(218, 226, 232);
        Font = new Font("Segoe UI", 10);
        if (previewOnly) Opacity = 0;
        applied = settings.Current;
        audio.Apply(applied);
        ApplySize();
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new(area.Left + (area.Width - Width) / 2, area.Bottom - Height - 24);
        timer.Tick += (_, _) => Frame();
        Shown += (_, _) => { ApplySize(); Frame(); timer.Start(); };
        ResizeBegin += (_, _) => Cancel();
        DpiChanged += (_, _) => { Cancel(); ApplySize(); };
        VisibleChanged += (_, _) => { if (!Visible) Cancel(); };
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

    private void ApplySize()
    {
        var area = Screen.FromControl(this).WorkingArea;
        var geometry = DesktopGeometry.Create(applied.Scale, DeviceDpi / 96f, area.Width - 16, area.Height - 16);
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
            keyboard.SetLayout(new WindowsLayout(target.Layout), Now);
            Invalidate();
        }
        return !faulted && target.Window != 0 &&
            (testTarget != 0 ? target.Window == testTarget : target.Process != Environment.ProcessId);
    }

    private void Frame()
    {
        if (closing) return;
        if ((stopFile != null && File.Exists(stopFile)) || Now - started >= seconds) { Close(); return; }
        if (Now >= nextSettingsRead)
        {
            settings.Reload();
            nextSettingsRead = Now + .5;
            if (applied != settings.Current)
            {
                var resized = applied.SizePercent != settings.Current.SizePercent;
                applied = settings.Current;
                audio.Apply(applied);
                if (resized) { Cancel(); ApplySize(); }
            }
        }
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
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (previewOnly) return;
        var now = Now;
        var point = Geometry.ToKeyboard(e.X, e.Y);
        keyboard.Enter(0, 0, now);
        keyboard.Move(0, 0, point.X, point.Y);
        RenderFrame();
    }
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        keyboard.Leave(0, 0, Now);
        RenderFrame();
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
            { keyCapture = true; Capture = true; audio.Click(); }
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
        if (settingsForm == null || settingsForm.IsDisposed) settingsForm = new SettingsForm(desktopMode: true);
        if (!settingsForm.Visible) settingsForm.Show(this);
        settingsForm.Activate();
    }
    private void CancelKeyboard()
    {
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
        var notice = faulted || Now < errorUntil ? error : !keyboard.Layout.Supported ? keyboard.Layout.Status : null;
        var signature = (keyboard.Revision, shift, altGr, caps, scroll, notice);
        if (drawn == signature) return;
        using var pixels = KeyboardPanel.Render(keyboard, shift, notice, altGr, caps, scroll);
        using var bgra = pixels.Copy(SKColorType.Bgra8888);
        using var borrowed = new Bitmap(bgra.Width, bgra.Height, bgra.RowBytes, PixelFormat.Format32bppPArgb, bgra.GetPixels());
        var next = new Bitmap(borrowed);
        frame?.Dispose();
        frame = next;
        drawn = signature;
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (frame != null) e.Graphics.DrawImage(frame, new Rectangle(0, HeaderHeight, ClientSize.Width, ClientSize.Height - HeaderHeight));
        var title = previewOnly ? "GoBoard · Desktop · Drag to move" : "GoBoard · " + WindowsKeyboard.TargetName(output.Target);
        TextRenderer.DrawText(e.Graphics, title, Font, new Rectangle(12, 0, Math.Max(0, SettingsButton.Left - 16), HeaderHeight), ForeColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        using var background = new SolidBrush(Color.FromArgb(35, 51, 62));
        e.Graphics.FillRectangle(background, SettingsButton);
        TextRenderer.DrawText(e.Graphics, "Settings", Font, SettingsButton, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(e.Graphics, "×", Font, CloseButton, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        closing = true;
        timer.Stop();
        Cancel();
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
            output.Dispose();
            audio.Dispose();
        }
        base.Dispose(disposing);
    }
}
