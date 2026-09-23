using GoBoard.Core;
using GoBoard.Presentation.Skia;

namespace GoBoard.App;

// Desktop debug presentation of the same state machine. The idle image is a
// layered, click-through window; only the small reveal surface accepts input.
internal sealed class DesktopInactivity(DesktopKeyboardForm owner) : IDisposable
{
    private sealed class Surface(bool passive) : Form
    {
        // Win32 extended styles and mouse-activation message/result constants.
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int MA_NOACTIVATE = 3;

        public Bitmap Image;
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get
            {
                var p = base.CreateParams;
                p.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
                if (passive) p.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT;
                return p;
            }
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEACTIVATE) { m.Result = MA_NOACTIVATE; return; }
            base.WndProc(ref m);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Image != null) e.Graphics.DrawImage(Image, ClientRectangle);
        }
        protected override void Dispose(bool disposing) { if (disposing) Image?.Dispose(); base.Dispose(disposing); }
    }
    private readonly KeyboardInactivity state = new();
    private readonly Surface prompt = Create(false), image = Create(true);
    private Rectangle previousBounds;
    private bool dormant, initialized;
    public bool Dormant => dormant;
    internal Form RevealWindow => prompt;
    internal Form IdleWindow => image;
    private static Surface Create(bool passive) => new(passive)
    {
        Text = passive ? "GoBoard idle keyboard" : "GoBoard reveal keyboard",
        FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false,
        StartPosition = FormStartPosition.Manual, AutoScaleMode = AutoScaleMode.None,
        BackColor = UiColors.Current.WindowBackground.ToDrawingColor()
    };

    public void Update(BoardSettings settings, double now, bool engaged, bool manipulating)
    {
        if (previousBounds != owner.Bounds)
        {
            previousBounds = owner.Bounds;
            state.Wake();
        }
        if (!initialized)
        {
            initialized = true;
            using var pixels = GrabHandleRenderer.Render(0, dormant: true);
            using var bgra = pixels.Copy(SkiaSharp.SKColorType.Bgra8888);
            using var borrowed = DesktopFrame.BorrowBitmap(bgra);
            prompt.Image = new Bitmap(borrowed);
        }
        var available = owner.Visible || dormant;
        var enabled = available && settings.Inactivity.Mode != InactivityMode.Off;
        var area = Screen.FromControl(owner).WorkingArea;
        const float logicalDpi = 96;
        var width = (int)(OverlayGeometry.GrabWidth * owner.DeviceDpi / logicalDpi);
        var height = (int)(OverlayGeometry.GrabHeight * owner.DeviceDpi / logicalDpi);
        prompt.Bounds = new(owner.Left + (owner.Width - width) / 2,
            Math.Min(owner.Bottom + 2, area.Bottom - height), width, height);
        if (!enabled && prompt.Visible) prompt.Hide();
        var miniatureHovered = state.CanRevealFromKeyboard && image.Visible && image.Bounds.Contains(Cursor.Position);
        state.Update(settings.Inactivity, now, available, engaged,
            enabled && (prompt.Bounds.Contains(Cursor.Position) || miniatureHovered), manipulating);
        if (state.Dormant != dormant)
        {
            dormant = state.Dormant;
            if (dormant)
            {
                image.Image?.Dispose();
                image.Image = owner.IdleSnapshot();
                owner.Hide(); // Existing visibility cleanup releases owned keys and captures.
            }
            else
            {
                image.Hide();
                owner.Show();
            }
        }
        // The desktop host has no permanent VR grab strip. Keep the reveal prompt
        // out of the active keys, especially when the window is near the taskbar.
        if (dormant && !prompt.Visible) prompt.Show();
        if (!dormant && prompt.Visible) prompt.Hide();
        if (!dormant) return;
        var scale = state.Scale;
        var original = owner.KeyboardScreenBounds;
        var size = new Size(Math.Max(1, (int)(original.Width * scale)), Math.Max(1, (int)(original.Height * scale)));
        var bottom = original.Bottom - (int)(Math.Max(0, original.Bottom - prompt.Top + 4) * state.Progress *
            (settings.Inactivity.Mode == InactivityMode.Minimize ? 1 : 0));
        var bounds = new Rectangle(original.Left + (original.Width - size.Width) / 2, bottom - size.Height, size.Width, size.Height);
        if (image.Bounds != bounds) { image.Bounds = bounds; image.Invalidate(); }
        // Keep WS_EX_LAYERED even at full opacity so WS_EX_TRANSPARENT passes
        // input through to windows belonging to other processes too.
        image.Opacity = Math.Clamp(state.Opacity, 0, .999);
        if (state.Opacity > 0 && !image.Visible) image.Show();
        if (state.Opacity <= 0 && image.Visible) image.Hide();
    }
    public void Dispose() { prompt.Dispose(); image.Dispose(); }
}
