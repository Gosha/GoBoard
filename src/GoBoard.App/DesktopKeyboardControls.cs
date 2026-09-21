using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using GoBoard.Core;
using GoBoard.Presentation.Skia;
using SkiaSharp;

namespace GoBoard.App;

// Non-activating, per-pixel-alpha windows keep the arrow background transparent.
internal sealed class DesktopKeyboardControls : IDisposable
{
    private sealed class ButtonWindow : Form
    {
        private DesktopFrame frame;
        private readonly bool preview;
        public ButtonWindow(KeyboardAction action, bool preview)
        {
            this.preview = preview;
            Text = AccessibleName = MainKeyboardControls.Name(action);
            AccessibleRole = AccessibleRole.PushButton;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
            SetStyle(ControlStyles.Selectable, false);
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08080088; return p; } }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21) { m.Result = 3; return; }
            base.WndProc(ref m);
        }
        public void Draw(SKBitmap source)
        {
            var pixels = new SKBitmap(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(pixels))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(source, new SKRect(0, 0, Width, Height), new SKSamplingOptions(SKFilterMode.Linear));
            }
            frame?.Dispose(); frame = new DesktopFrame(pixels);
            using var bitmap = DesktopFrame.BorrowBitmap(pixels);
            var screen = GetDC(0);
            var memory = CreateCompatibleDC(screen);
            nint native = 0, previous = 0;
            try
            {
                native = bitmap.GetHbitmap(Color.FromArgb(0));
                previous = SelectObject(memory, native);
                var location = Location; var size = Size; var origin = Point.Empty;
                var blend = new BlendFunction { Alpha = preview ? (byte)0 : (byte)255, Format = 1 };
                if (!UpdateLayeredWindow(Handle, screen, ref location, ref size, memory, ref origin, 0, ref blend, 2))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                if (previous != 0) SelectObject(memory, previous);
                if (native != 0) DeleteObject(native);
                if (memory != 0) DeleteDC(memory);
                if (screen != 0) ReleaseDC(0, screen);
            }
        }
        public void DrawPreview(Graphics graphics, Rectangle bounds) => frame?.Draw(graphics, bounds);
        protected override void Dispose(bool disposing) { if (disposing) frame?.Dispose(); base.Dispose(disposing); }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct BlendFunction { public byte Operation, Flags, Alpha, Format; }
        [DllImport("user32.dll")] private static extern nint GetDC(nint window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
        [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
        [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint value);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(nint window, nint screen,
            ref Point destination, ref Size size, nint source, ref Point origin, uint key, ref BlendFunction blend, uint flags);
    }
    private sealed class Button(KeyboardAction action, bool preview)
    {
        public readonly KeyboardAction Action = action;
        public readonly ButtonWindow Window = new(action, preview);
        public readonly FloatingButtonState Input = new();
        public (int Revision, bool Enabled, string Theme, Size Size)? Drawn;
    }
    private readonly DesktopKeyboardForm owner;
    private readonly Action<KeyboardAction> activate;
    private readonly bool preview;
    private readonly Button[] buttons;
    private readonly ToolTip tooltip = new();
    private BoardSettings applied;
    private bool visible, releasingCapture;
    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    internal IEnumerable<Form> Windows => buttons.Select(b => (Form)b.Window);
    internal Form Window(KeyboardAction action) => buttons.Single(b => b.Action == action).Window;

    public DesktopKeyboardControls(DesktopKeyboardForm owner, bool preview, Action<KeyboardAction> activate)
    {
        this.owner = owner; this.preview = preview; this.activate = activate;
        buttons = MainKeyboardControls.Actions.Select(a => new Button(a, preview)).ToArray();
        foreach (var b in buttons)
        {
            tooltip.SetToolTip(b.Window, MainKeyboardControls.Name(b.Action));
            b.Window.MouseMove += (_, e) => Process(b, e.Location);
            b.Window.MouseLeave += (_, _) => Process(b, new(-1, -1), leave: true);
            b.Window.MouseDown += (_, e) =>
            {
                if (e.Button != MouseButtons.Left || preview) return;
                b.Window.Capture = true; Process(b, e.Location, down: true);
            };
            b.Window.MouseUp += (_, e) =>
            {
                if (e.Button != MouseButtons.Left || preview) return;
                Process(b, e.Location, up: true);
                ReleaseCapture(b);
            };
            b.Window.MouseCaptureChanged += (_, _) =>
            {
                if (!releasingCapture && !b.Window.Capture) { b.Input.Reset(Now); Draw(b); }
            };
        }
    }

    public void Update(BoardSettings settings, bool show)
    {
        if (applied != settings || visible != show) Cancel();
        applied = settings; visible = show;
        var scale = owner.ClientSize.Width / (float)owner.State.Width;
        var header = owner.ClientSize.Height - OverlayGeometry.PanelHeight * scale;
        foreach (var b in buttons)
        {
            var showButton = show && MainKeyboardControls.Visible(b.Action, settings);
            var bounds = MainKeyboardControls.DesktopBounds(b.Action, header, scale);
            var rectangle = new Rectangle(owner.Left + (int)Math.Round(bounds.X), owner.Top + (int)Math.Round(bounds.Y),
                Math.Max(1, (int)Math.Round(bounds.Width)), Math.Max(1, (int)Math.Round(bounds.Height)));
            if (b.Window.Bounds != rectangle) { b.Input.Reset(Now); ReleaseCapture(b); b.Window.Bounds = rectangle; }
            if (showButton) Draw(b);
            if (b.Window.Visible != showButton) { if (showButton) b.Window.Show(); else b.Window.Hide(); }
        }
    }

    private void Process(Button b, Point point, bool down = false, bool up = false, bool leave = false)
    {
        if (preview || !visible || !b.Window.Visible || !MainKeyboardControls.Visible(b.Action, applied)) return;
        owner.ObserveControlTarget();
        var now = Now;
        var clicked = b.Input.Process(0, 0, point.X * MainKeyboardControls.Size / (float)b.Window.Width,
            point.Y * MainKeyboardControls.Size / (float)b.Window.Height, now, now, down, up, leave);
        if (clicked)
        {
            ReleaseCapture(b);
            Cancel();
            activate(b.Action);
        }
        Draw(b);
    }
    private void ReleaseCapture(Button b)
    {
        releasingCapture = true;
        try { b.Window.Capture = false; } finally { releasingCapture = false; }
    }
    public void Cancel()
    {
        foreach (var b in buttons) { b.Input.Reset(Now); ReleaseCapture(b); }
    }
    private void Draw(Button b)
    {
        if (applied == null || !visible || !MainKeyboardControls.Visible(b.Action, applied)) return;
        var signature = (b.Input.Revision, b.Action == KeyboardAction.ToggleNumpad && applied.NumpadEnabled, applied.Theme, b.Window.Size);
        if (b.Drawn == signature) return;
        using var bitmap = MainKeyboardControlRenderer.Render(b.Action, signature.Item2, b.Input.Hovered, b.Input.Pressed, applied.Theme);
        b.Window.Draw(bitmap); b.Drawn = signature;
    }
    public void DrawPreview(Graphics graphics, Point origin)
    {
        foreach (var b in buttons.Where(b => b.Window.Visible))
            b.Window.DrawPreview(graphics, new(b.Window.Left - origin.X, b.Window.Top - origin.Y, b.Window.Width, b.Window.Height));
    }
    public void Dispose() { Cancel(); foreach (var b in buttons) b.Window.Dispose(); tooltip.Dispose(); }
}
