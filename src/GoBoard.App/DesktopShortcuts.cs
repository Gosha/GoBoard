using System.Diagnostics;
using GoBoard.Core;
using GoBoard.Platform.Windows;
using GoBoard.Presentation.Skia;
using SkiaSharp;

namespace GoBoard.App;

// Two non-activating windows: a small launcher and an independently draggable
// palette. Both use the owner's guarded input backend; neither steals focus.
internal sealed class DesktopShortcuts : IDisposable
{
    private sealed class Surface : Form
    {
        private DesktopFrame frame;
        public int DragHeight;
        public bool DragAtBottom;
        public string Status;
        public bool StatusError;
        public Surface(string title, bool preview)
        {
            Text = title; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true; BackColor = Color.FromArgb(12, 21, 30);
            SetStyle(ControlStyles.Selectable, false);
            if (preview) Opacity = 0;
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000088; return p; } }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21) { m.Result = 3; return; }
            if (m.Msg == 0x84)
            {
                var packed = (long)m.LParam;
                var p = PointToClient(new((short)(packed & 0xffff), (short)(packed >> 16)));
                if (p.X >= 0 && p.X < Width && (DragAtBottom ? p.Y >= Height - DragHeight : p.Y < DragHeight) && p.Y >= 0 && p.Y < Height)
                { m.Result = 2; return; }
            }
            base.WndProc(ref m);
        }
        public void Draw(SKBitmap bitmap)
        {
            frame?.Dispose(); frame = new DesktopFrame(bitmap); Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var area = DragAtBottom ? new Rectangle(0, 0, Width, Height - DragHeight) : new Rectangle(0, DragHeight, Width, Height - DragHeight);
            frame?.Draw(e.Graphics, area);
            var y = DragAtBottom ? Height - DragHeight / 2 : DragHeight / 2;
            var color = StatusError ? Color.FromArgb(255, 176, 160) : Color.FromArgb(102, 192, 244);
            if (Status != null)
                TextRenderer.DrawText(e.Graphics, Status, Font, new Rectangle(2, y - DragHeight / 2, Width - 4, DragHeight), color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            else
            {
                using var pen = new Pen(color, 2);
                e.Graphics.DrawLine(pen, Width / 2 - 12, y, Width / 2 + 12, y);
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) frame?.Dispose(); base.Dispose(disposing); }
    }
    private readonly DesktopKeyboardForm owner;
    private readonly WindowsKeyboard output;
    private readonly Surface launcher, panel;
    private readonly ShortcutLauncherState toggle = new();
    private readonly KeyboardState keyboard;
    private readonly AnimatedKeyboardRenderer renderer = new();
    private readonly KeyAudio audio = new();
    private readonly ToolTip tooltip = new();
    private BoardSettings applied;
    private InputTarget target;
    private int drawnToggle = -1;
    private bool visible, placing, releasingCapture, positioned;
    private Point panelOffset;
    private string error;
    private readonly bool preview;
    private int appliedDpi;
    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    internal Form Launcher => launcher;
    internal Form PanelWindow => panel;
    internal KeyboardState State => keyboard;
    internal bool Expanded => toggle.Expanded;

    public DesktopShortcuts(DesktopKeyboardForm owner, WindowsKeyboard output, bool preview)
    {
        this.owner = owner; this.output = output; this.preview = preview;
        keyboard = new(output, shortcutsOnly: true, shortcutFooter: false);
        launcher = new("GoBoard shortcut button", preview) { DragAtBottom = true };
        panel = new("GoBoard shortcuts", preview);
        tooltip.SetToolTip(launcher, "Click to show or hide shortcuts. Drag the small line to move.");
        tooltip.SetToolTip(panel, "Drag the top line to move shortcuts.");
        launcher.MouseMove += (_, e) => Button(e.Location);
        launcher.MouseLeave += (_, _) => Button(new(-1, -1), leave: true);
        launcher.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { launcher.Capture = true; Button(e.Location, down: true); } };
        launcher.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) { Button(e.Location, up: true); ReleaseCapture(launcher); } };
        launcher.MouseCaptureChanged += (_, _) => { if (!releasingCapture && !launcher.Capture) toggle.Reset(Now); };
        launcher.Move += (_, _) => { if (positioned && !placing) PlacePanel(); };
        panel.Move += (_, _) => { if (positioned && !placing) panelOffset = new(panel.Left - launcher.Left, panel.Top - launcher.Top); };
        panel.ResizeBegin += (_, _) => Cancel();
        panel.MouseMove += (_, e) => { if (!preview) { var p = ToPanel(e.Location); keyboard.Enter(0, 0, Now); keyboard.Move(0, 0, p.X, p.Y); } };
        panel.MouseLeave += (_, _) => { keyboard.Leave(0, 0, Now); audio.Cancel(); };
        panel.MouseDown += (_, e) => Press(e);
        panel.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || preview) return;
            if (keyboard.Up(0, 0, Now)) audio.Click(released: true);
            ReleaseCapture(panel);
        };
        panel.MouseCaptureChanged += (_, _) => { if (!releasingCapture && !panel.Capture) Cancel(); };
    }
    private void ReleaseCapture(Form form) { releasingCapture = true; try { form.Capture = false; } finally { releasingCapture = false; } }
    private (float X, float Y) ToPanel(Point p) => (p.X * keyboard.Width / (float)panel.Width,
        keyboard.Height - (p.Y - panel.DragHeight) * keyboard.Height / (float)(panel.Height - panel.DragHeight));
    internal Point KeyPoint(string id)
    {
        var b = keyboard.Keys.Single(k => k.Id == id).Bounds;
        return new((int)((b.X + b.Width / 2) * panel.Width / keyboard.Width),
            panel.DragHeight + (int)((b.Y + b.Height / 2) * (panel.Height - panel.DragHeight) / keyboard.Height));
    }
    private void Button(Point p, bool down = false, bool up = false, bool leave = false)
    {
        if (preview || !visible) return;
        if (toggle.Process(0, 0, p.X * ProgrammableKeys.ToggleSize / (float)launcher.Width,
            p.Y * ProgrammableKeys.ToggleSize / (float)(launcher.Height - launcher.DragHeight), Now, Now, down, up, leave))
        { Cancel(); ShowPanel(); }
        Draw();
    }
    private void Press(MouseEventArgs e)
    {
        if (preview || !visible || !toggle.Expanded || e.Button != MouseButtons.Left || e.Y < panel.DragHeight || !owner.PrepareShortcutInput()) return;
        if (target != output.Target) { Cancel(); target = output.Target; }
        keyboard.SetLayout(owner.State.Layout, Now);
        var now = Now; var p = ToPanel(e.Location);
        try
        {
            keyboard.Enter(0, 0, now);
            if (keyboard.Press(0, 0, p.X, p.Y, now, now))
            { panel.Capture = true; audio.Click(key: keyboard.Hit(p.X, p.Y)); error = null; }
        }
        catch (Exception ex) { Cancel(); error = ex.Message; }
        tooltip.SetToolTip(panel, error ?? "Drag the top line to move shortcuts.");
        Draw();
    }
    public void Update(BoardSettings settings, bool show)
    {
        if (applied != settings || appliedDpi != owner.DeviceDpi)
        {
            Cancel(); toggle.Reset(Now, collapse: !settings.ProgrammableKeys.Enabled);
            var reset = applied == null || applied.PositionResetId != settings.PositionResetId || applied.NumpadEnabled != settings.NumpadEnabled;
            applied = settings; appliedDpi = owner.DeviceDpi; audio.Apply(settings); keyboard.SetShortcuts(settings.ProgrammableKeys, Now);
            var scale = settings.Scale * owner.DeviceDpi / 96f;
            var area = Screen.FromControl(owner).WorkingArea;
            scale = Math.Min(scale, Math.Min((area.Width - 16f) / keyboard.Width, (area.Height - 48f) / keyboard.Height));
            launcher.DragHeight = Math.Max(8, (int)(8 * scale));
            launcher.ClientSize = new((int)(44 * scale), (int)(44 * scale) + launcher.DragHeight);
            panel.DragHeight = Math.Max(16, (int)(20 * scale));
            panel.ClientSize = new((int)(keyboard.Width * scale), (int)(keyboard.Height * scale) + panel.DragHeight);
            if (reset)
            {
                placing = true;
                launcher.Location = new(Math.Max(area.Left, owner.Left - launcher.Width - 12), owner.Top + 42);
                placing = false; panelOffset = new(-panel.Width - 8, 0); positioned = true;
            }
            PlacePanel(); drawnToggle = -1;
        }
        if (target != output.Target)
        { Cancel(); target = output.Target; }
        keyboard.SetLayout(owner.State.Layout, Now);
        show &= settings.ProgrammableKeys.Enabled;
        if (visible != show)
        {
            visible = show; Cancel(); toggle.Reset(Now);
            if (show) launcher.Show(); else launcher.Hide();
            ShowPanel();
        }
        Draw();
    }
    private void PlacePanel()
    {
        var area = Screen.FromControl(launcher).WorkingArea;
        var x = launcher.Left + panelOffset.X; var y = launcher.Top + panelOffset.Y;
        if (x < area.Left) { x = launcher.Right + 8; y = launcher.Bottom + 8; }
        placing = true;
        panel.Location = new(Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - panel.Width)),
            Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - panel.Height)));
        placing = false;
    }
    private void ShowPanel()
    {
        if (visible && toggle.Expanded) { PlacePanel(); panel.Show(); }
        else { Cancel(); panel.Hide(); }
    }
    internal void ExpandPreview() { toggle.Process(0, 0, 10, 10, Now, Now, down: true); toggle.Process(0, 0, 10, 10, Now, Now, up: true); ShowPanel(); Draw(); }
    private void Draw()
    {
        if (applied == null || !visible) return;
        if (drawnToggle != toggle.Revision)
        {
            using var bitmap = ShortcutLauncherRenderer.Render(toggle.Expanded, toggle.Hovered, applied.Theme);
            launcher.Draw(bitmap.Copy(SKColorType.Bgra8888)); drawnToggle = toggle.Revision;
        }
        if (!toggle.Expanded) return;
        panel.Status = error != null ? "Input error" : keyboard.Layout.Notice != null ? "Layout warning" : null;
        panel.StatusError = error != null;
        var details = error ?? keyboard.Layout.Notice ?? "Drag the top line to move shortcuts.";
        if (tooltip.GetToolTip(panel) != details) tooltip.SetToolTip(panel, details);
        panel.AccessibleDescription = details;
        var pixels = renderer.Render(keyboard, false, error, false, false, false, applied.Theme, applied.Effects, Now,
            new SKImageInfo(panel.Width, panel.Height - panel.DragHeight, SKColorType.Bgra8888, SKAlphaType.Opaque));
        if (pixels != null) panel.Draw(pixels);
    }
    private void Cancel()
    {
        keyboard.Cancel(Now); audio.Cancel();
        try { output.ReleaseAll(); } catch (Exception ex) { error = ex.Message; }
    }
    public void Dispose() { Cancel(); launcher.Dispose(); panel.Dispose(); audio.Dispose(); renderer.Dispose(); tooltip.Dispose(); }
}
