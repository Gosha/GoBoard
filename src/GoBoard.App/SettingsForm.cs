using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using GoBoard.Core;
using GoBoard.Platform.Windows;
using GoBoard.Presentation.Skia;
using GoBoard.Vr;
using SkiaSharp;

namespace GoBoard.App;

// Windows hosts the shared Skia panel; drawing, targets, and actions belong to
// the same renderer/model as the SteamVR dashboard.
internal sealed class SettingsForm : Form
{
    private readonly SettingsStore store;
    private readonly KeyAudio audio = new();
    private readonly SettingsPointerState pointers = new();
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 500 };
    private readonly ToolTip details = new();
    private readonly bool previewOnly, desktopMode;
    private readonly AutostartController autostart;
    private string actionError;
    private bool releasingCapture;
    private Bitmap frame;
    private readonly Icon windowIcon;
    private (BoardSettings Settings, int Hover, string Error, AutostartState Autostart)? drawn;
    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    private SettingsViewport Viewport => SettingsViewport.Fit(ClientSize.Width, ClientSize.Height);
    internal SettingsControl ControlFor(SettingsAction action) => SettingsControls.ForPage(pointers.Page, store.Current, pointers.Layout, pointers.ShortcutSlot).Single(c => c.Action == action);
    protected override bool ShowWithoutActivation => previewOnly;

    protected override void SetVisibleCore(bool value)
    {
        base.SetVisibleCore(value);
        // The launcher hides the console with STARTUPINFO/SW_HIDE. Windows can
        // apply that to this first activating window while WinForms caches
        // Visible=true. Explicitly show it once that startup hint is consumed.
        if (value && !previewOnly && !IsWindowVisible(Handle)) ShowWindow(Handle, 5); // SW_SHOW
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    public SettingsForm(bool previewOnly = false, bool desktopMode = false, SettingsStore store = null,
        string autostartExecutable = null, AutostartController autostart = null)
    {
        this.previewOnly = previewOnly;
        this.desktopMode = desktopMode;
        this.store = store ?? new SettingsStore();
        this.autostart = previewOnly ? null : autostart ?? new AutostartController(autostartExecutable);
        Text = "GoBoard Settings";
        using (var stream = typeof(SettingsForm).Assembly.GetManifestResourceStream("GoBoard.Icon.ico"))
            windowIcon = new Icon(stream);
        Icon = windowIcon;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(SettingsControls.Width, SettingsControls.Height);
        MinimumSize = new Size(560, 440);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(12, 21, 30);
        DoubleBuffered = true;
        if (previewOnly) { ShowInTaskbar = false; Opacity = 0; }
        refresh.Tick += (_, _) => { this.store.Reload(); this.autostart?.Update(Now); RenderFrame(); };
        Shown += (_, _) =>
        {
            var area = Screen.FromControl(this).WorkingArea;
            Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
            this.autostart?.Update(Now);
            RenderFrame();
            refresh.Start();
        };
        RenderFrame();
    }

    private void Pointer(Point location, bool down = false, bool up = false, bool leave = false)
    {
        if (previewOnly || Disposing || IsDisposed) return;
        var point = Viewport.ToPanel(location.X, location.Y);
        RefreshLayout();
        var now = Now;
        var action = pointers.Process(0, point.X, point.Y, now, now, down, up, leave);
        if (action == SettingsAction.Autostart) autostart?.Toggle();
        else if (action.HasValue && SettingsControls.Enabled(action.Value, store.Current))
        {
            var saved = store.Update(s => SettingsControls.Enabled(action.Value, s) ? SettingsControls.Apply(action.Value, s, pointers.ShortcutSlot, pointers.Layout) : s);
            actionError = saved ? null : store.Error;
            audio.Apply(store.Current);
            if (saved && SettingsControls.AuditionsSound(action.Value))
                audio.Preview();
        }
        RenderFrame();
    }

    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); Pointer(e.Location); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Pointer(Point.Empty, leave: true); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || previewOnly) return;
        Capture = true;
        Pointer(e.Location, down: true);
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || previewOnly) return;
        Pointer(e.Location, up: true);
        releasingCapture = true;
        try { Capture = false; } finally { releasingCapture = false; }
    }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture && !releasingCapture) ResetPointer();
    }
    protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); ResetPointer(); }
    protected override void OnResize(EventArgs e) { base.OnResize(e); ResetPointer(); Invalidate(); }
    private void ResetPointer()
    {
        pointers?.Reset();
        if (store != null && !Disposing && !IsDisposed) RenderFrame();
    }

    private void RenderFrame()
    {
        RefreshLayout();
        var hover = pointers.Revision;
        var error = actionError ?? store.Error;
        var autostartState = autostart?.State ?? AutostartState.Preview;
        var signature = (store.Current, hover, error, autostartState);
        if (drawn == signature) return;
        using var pixels = SettingsPanel.Render(store.Current, pointers, error, desktopMode, autostartState);
        using var bgra = pixels.Copy(SKColorType.Bgra8888);
        using var borrowed = new Bitmap(bgra.Width, bgra.Height, bgra.RowBytes, PixelFormat.Format32bppPArgb, bgra.GetPixels());
        var next = new Bitmap(borrowed);
        frame?.Dispose();
        frame = next;
        drawn = signature;
        details.SetToolTip(this, error ?? autostartState.Error);
        AccessibleDescription = error ?? autostartState.Error ?? $"GoBoard settings. Changes apply immediately. SteamVR autostart: {autostartState.Status}";
        audio.Apply(store.Current);
        Invalidate();
    }
    private void RefreshLayout() => pointers.Configure(store.Current,
        WindowsLayoutProvider.Get(WindowsKeyboard.Foreground().Layout, store.Current.Geometry));

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var v = Viewport;
        if (frame != null && v.Scale > 0)
            e.Graphics.DrawImage(frame, new RectangleF(v.X, v.Y, v.Width, v.Height));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { refresh.Dispose(); details.Dispose(); frame?.Dispose(); audio.Dispose(); }
        base.Dispose(disposing);
        if (disposing) windowIcon?.Dispose();
    }
}
