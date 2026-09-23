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
    private readonly SettingsEditor editor;
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 500 };
    private readonly ToolTip details = new();
    private readonly bool previewOnly, desktopMode;
    private readonly AutostartController autostart;
    private bool releasingCapture;
    private Bitmap frame;
    private readonly Icon windowIcon;
    private (BoardSettings Settings, int Hover, string Error, AutostartState Autostart)? drawn;
    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    private SettingsViewport Viewport => SettingsViewport.Fit(ClientSize.Width, ClientSize.Height);
    internal SettingsControl ControlFor(SettingsAction action) => SettingsControls.ForPage(editor.Pointers.Page, editor.Current,
        editor.Pointers.Layout, editor.Pointers.ShortcutSlot).Single(c => c.Action == action);
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
        string autostartExecutable = null, AutostartController autostart = null, SettingsPage previewPage = SettingsPage.General)
    {
        this.previewOnly = previewOnly;
        this.desktopMode = desktopMode;
        this.store = store ?? new SettingsStore();
        editor = new(this.store, geometry => WindowsLayoutProvider.Get(WindowsKeyboard.Foreground().Layout, geometry));
        if (previewOnly) editor.Pointers.SelectPage(previewPage);
        this.autostart = previewOnly ? null : autostart ?? new AutostartController(autostartExecutable);
        Text = "GoBoard Settings";
        using (var stream = typeof(SettingsForm).Assembly.GetManifestResourceStream("GoBoard.Icon.ico"))
            windowIcon = new Icon(stream);
        Icon = windowIcon;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(SettingsControls.Width, SettingsControls.Height);
        MinimumSize = new Size(560, 440);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = UiColors.Current.WindowBackground.ToDrawingColor();
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
        var now = Now;
        var effect = editor.Process(0, point.X, point.Y, now, now, down, up, leave);
        if (effect == SettingsEditorEffect.ToggleAutostart) autostart?.Toggle();
        else if (effect == SettingsEditorEffect.AuditionSound)
        {
            audio.Apply(editor.Current);
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
        editor?.Reset();
        if (editor != null && !Disposing && !IsDisposed) RenderFrame();
    }

    private void RenderFrame()
    {
        editor.Refresh();
        var hover = editor.Pointers.Revision;
        var error = editor.Error;
        var autostartState = autostart?.State ?? AutostartState.Preview;
        var signature = (editor.Current, hover, error, autostartState);
        if (drawn == signature) return;
        using var pixels = SettingsPanel.Render(editor.Current, editor.Pointers, error, desktopMode, autostartState);
        using var bgra = pixels.Copy(SKColorType.Bgra8888);
        using var borrowed = new Bitmap(bgra.Width, bgra.Height, bgra.RowBytes, PixelFormat.Format32bppPArgb, bgra.GetPixels());
        var next = new Bitmap(borrowed);
        frame?.Dispose();
        frame = next;
        drawn = signature;
        details.SetToolTip(this, error ?? autostartState.Error);
        AccessibleDescription = error ?? autostartState.Error ?? $"GoBoard settings. SteamVR autostart: {autostartState.Status}";
        audio.Apply(editor.Current);
        Invalidate();
    }
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
