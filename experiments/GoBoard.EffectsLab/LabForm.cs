using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.Json;
using GoBoard.Core;
using SkiaSharp;

namespace GoBoard.EffectsLab;

internal sealed class KeyboardView : Control
{
    public readonly LocalSession Session = new();
    public readonly Effects Effects = new();
    public readonly EffectOptions Options = new();
    public readonly LabRenderer Renderer = new();
    public readonly Stopwatch Clock = Stopwatch.StartNew();
    public bool Compare = true, Demo, ModifierDemo, CharacterDetail = true;
    public double LastRenderMs { get; private set; }
    public event Action Changed;
    internal event Action<double, double, bool> FrameDrawn;
    private SKRect activeRect;
    private SKBitmap surface;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 16 };
    private double demoClick;
    private bool animationWasRunning;
    private int modifierStep;
    private double modifierAt;
    private static readonly string[] ModifierSequence = ["Shift", "e", "AltGr", "e", "Caps", "Caps"];
    public double Now => Clock.Elapsed.TotalSeconds;

    public KeyboardView()
    {
        Options.Preset("Baseline");
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Dock = DockStyle.Fill; BackColor = Color.FromArgb(9, 17, 25); Cursor = Cursors.Cross;
        timer.Tick += (_, _) =>
        {
            var now = Now; Effects.Prune(now);
            if (ModifierDemo && now >= modifierAt)
            {
                modifierAt = now + 1.1;
                TriggerKey(ModifierSequence[modifierStep++ % ModifierSequence.Length]);
            }
            if (Demo)
            {
                // Sweep over real keys, including the tight inter-key gaps.
                var x = 350 + 315 * MathF.Sin((float)now * .8f);
                var y = 143 + 75 * MathF.Sin((float)now * .43f);
                Effects.Move(new(x, y), Session.Keyboard.Layout.Keys, Options, now);
                if (now - demoClick > 1.2 && Effects.Hover != null && !Effects.Hover.IsModifier)
                {
                    demoClick = now; Effects.Down(now, Options); Session.Click(Effects.Pointer, now); Changed?.Invoke();
                }
                if (now - demoClick > .13) Effects.Up();
            }
            var animating = Effects.Animating(now, Options) || Renderer.Transitions.Animating(now);
            // Paint one final settled frame, including when the last pulse expires.
            if (Demo || ModifierDemo || animating || animationWasRunning) Invalidate();
            animationWasRunning = animating;
        };
        timer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 1 || Height < 1) return;
        var now = Now; // Keyboard, comparison and close-ups share one animation frame.
        var watch = Stopwatch.StartNew();
        if (surface == null || surface.Width != Width || surface.Height != Height)
        {
            surface?.Dispose(); surface = new(new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        }
        using (var canvas = new SKCanvas(surface))
        {
            canvas.Clear(new SKColor(9, 17, 25));
            using var typeface = SKTypeface.FromFamilyName("Segoe UI");
            using var heading = new SKFont(typeface, 16);
            using var small = new SKFont(typeface, 12);
            using var paint = new SKPaint { IsAntialias = true, Color = new(0xd8, 0xe6, 0xf0) };
            var gap = Compare ? 64 : 0;
            var detailHeight = CharacterDetail ? 155 : 0;
            var usableHeight = Math.Max(50, (Height - detailHeight - 110 - gap) / (Compare ? 2f : 1));
            var w = Math.Max(30, Math.Min(Width - 44, usableHeight * OverlayGeometry.PanelWidth / OverlayGeometry.PanelHeight));
            var h = w * OverlayGeometry.PanelHeight / OverlayGeometry.PanelWidth;
            var x = (Width - w) / 2;
            var top = Math.Max(56, (Height - detailHeight - (Compare ? h * 2 + gap : h)) / 2);
            activeRect = new(x, top, x + w, top + h);
            canvas.DrawText("CHARACTERS  /  " + (ModifierDemo ? "SHIFT / ALTGR DEMO" : Demo ? "AUTO SWEEP" : Options.Transition.ToString().ToUpperInvariant()), x, top - 17, SKTextAlign.Left, heading, paint);
            Renderer.Draw(canvas, activeRect, Session, Effects, Options, now, demoPointer: Demo || ModifierDemo);
            if (Compare)
            {
                var baseline = new SKRect(x, top + h + gap, x + w, top + 2 * h + gap);
                paint.Color = new(0x8c, 0xa4, 0xb6);
                canvas.DrawText("BASELINE  /  INSTANT STATE CHANGES", x, baseline.Top - 17, SKTextAlign.Left, heading, paint);
                Renderer.Draw(canvas, baseline, Session, Effects, Options, now, baselineOnly: true, demoPointer: Demo || ModifierDemo);
            }
            if (CharacterDetail)
            {
                var ids = new[] { "a", "e", "2" };
                var captions = new[] { "Shift: a ↔ A", Session.Keyboard.Layout.HasAltGr ? "AltGr: e ↔ €" : "Shift: e ↔ E", Session.Keyboard.Layout.HasAltGr ? "Shift: 2 ↔ \"   AltGr: @" : "Shift: 2 ↔ @" };
                var left = (Width - 570) / 2f;
                for (var i = 0; i < ids.Length; i++)
                {
                    var key = Session.Keyboard.Layout.Keys.First(k => k.Id == ids[i]);
                    var tile = new SKRect(left + 190 * i, Height - 138, left + 190 * i + 104, Height - 34);
                    paint.Color = new(0x8c, 0xa4, 0xb6);
                    canvas.DrawText(captions[i], tile.Left, tile.Top - 10, SKTextAlign.Left, small, paint);
                    var scale = tile.Width / key.Bounds.Width;
                    canvas.Save(); canvas.ClipRect(tile);
                    var panelLeft = tile.Left - key.Bounds.X * scale;
                    var panelTop = tile.Top - key.Bounds.Y * scale;
                    Renderer.Draw(canvas, new(panelLeft, panelTop, panelLeft + OverlayGeometry.PanelWidth * scale, panelTop + OverlayGeometry.PanelHeight * scale), Session, Effects, Options, now);
                    canvas.Restore();
                }
            }
            paint.Color = new(0x8c, 0xa4, 0xb6);
            canvas.DrawText("Character changes animate; keycaps and modifier indicators stay fixed.", x, Height - 12, SKTextAlign.Left, small, paint);
        }
        using (var borrowed = new Bitmap(surface.Width, surface.Height, surface.RowBytes, PixelFormat.Format32bppPArgb, surface.GetPixels()))
            e.Graphics.DrawImageUnscaled(borrowed, 0, 0);
        LastRenderMs = watch.Elapsed.TotalMilliseconds;
        FrameDrawn?.Invoke(now, LastRenderMs, Renderer.Transitions.Animating(now));
    }

    private bool Pick(Point p, out SKPoint logical)
    {
        logical = default;
        if (activeRect.Width <= 0 || !activeRect.Contains(p.X, p.Y)) return false;
        logical = new((p.X - activeRect.Left) / activeRect.Width * OverlayGeometry.PanelWidth, (p.Y - activeRect.Top) / activeRect.Height * OverlayGeometry.PanelHeight);
        return true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); if (Demo || ModifierDemo) return;
        if (Pick(e.Location, out var point)) Effects.Move(point, Session.Keyboard.Layout.Keys, Options, Now);
        else Effects.Leave(Options, Now);
        Invalidate(); Changed?.Invoke();
    }
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e); if (Demo || ModifierDemo) return; Effects.Leave(Options, Now); Invalidate(); Changed?.Invoke();
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (Demo || ModifierDemo || e.Button != MouseButtons.Left || !Pick(e.Location, out var point)) return;
        Effects.Move(point, Session.Keyboard.Layout.Keys, Options, Now);
        Effects.Down(Now, Options); Session.Click(point, Now); Capture = true; Invalidate(); Changed?.Invoke();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e); Effects.Up(); Capture = false; Invalidate();
    }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e); if (!Capture) { Effects.Up(); Invalidate(); }
    }
    public void ResetMotion() { Effects.Reset(); Invalidate(); }
    public void TriggerKey(string id)
    {
        var bounds = Session.Keyboard.Layout.Keys.First(k => k.Id == id).Bounds;
        var point = new SKPoint(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        Effects.Move(point, Session.Keyboard.Layout.Keys, Options, Now);
        Effects.Down(Now, Options); Session.Click(point, Now); Effects.Up();
        Invalidate(); Changed?.Invoke();
    }
    public void StartModifierDemo(bool enabled)
    {
        ModifierDemo = enabled; Demo = false; modifierStep = 0; modifierAt = Now + .5;
        Session.Reset(Now); ResetMotion(); Changed?.Invoke();
    }
    internal void ExerciseMouse(SKPoint logical, bool click)
    {
        var x = (int)(activeRect.Left + logical.X / OverlayGeometry.PanelWidth * activeRect.Width);
        var y = (int)(activeRect.Top + logical.Y / OverlayGeometry.PanelHeight * activeRect.Height);
        OnMouseMove(new(MouseButtons.None, 0, x, y, 0));
        if (click) { OnMouseDown(new(MouseButtons.Left, 1, x, y, 0)); OnMouseUp(new(MouseButtons.Left, 1, x, y, 0)); }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); Renderer.Dispose(); surface?.Dispose(); }
        base.Dispose(disposing);
    }
}

internal sealed class LabForm : Form
{
    public readonly KeyboardView View = new();
    private readonly List<Action> refresh = new();
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 40, Padding = new(16, 10, 0, 0) };
    private readonly System.Windows.Forms.Timer metrics = new() { Interval = 500 };
    private bool updating;
    public LabForm()
    {
        Text = "GoBoard · Skia Effects Lab"; ClientSize = new(1440, 850); MinimumSize = new(1040, 740);
        StartPosition = FormStartPosition.CenterScreen; BackColor = Color.FromArgb(15, 25, 35);
        ForeColor = Color.FromArgb(219, 231, 240); Font = new("Segoe UI", 10);
        var rail = new System.Windows.Forms.Panel { Dock = DockStyle.Right, Width = 302 };
        var tabs = new TabControl { Dock = DockStyle.Fill };
        FlowLayoutPanel Section(string title)
        {
            var page = new TabPage(title) { BackColor = BackColor, ForeColor = ForeColor };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new(14, 12, 8, 8), AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            page.Controls.Add(flow); tabs.TabPages.Add(page); return flow;
        }
        var common = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 172, Padding = new(18, 4, 8, 8), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        rail.Controls.Add(tabs); rail.Controls.Add(common);
        Controls.Add(View); Controls.Add(rail); Controls.Add(status);
        var sidebar = Section("Characters");
        void Label(string text, int height = 24) => sidebar.Controls.Add(new Label { Text = text, Width = 238, Height = height, Margin = new(0, 3, 0, 3) });
        Label("a → A   /   e → €", 28);
        Label("Animate characters when Shift, AltGr\nor Caps changes their output.", 42);
        var transitions = new ComboBox { Width = 236, DropDownStyle = ComboBoxStyle.DropDownList };
        transitions.Items.AddRange(Enum.GetNames<TransitionStyle>()); transitions.SelectedItem = View.Options.Transition.ToString();
        sidebar.Controls.Add(transitions);
        transitions.SelectedIndexChanged += (_, _) => { View.Options.Transition = Enum.Parse<TransitionStyle>(transitions.Text); View.Renderer.Transitions.Reset(); View.Invalidate(); };
        Slider("Transition", 0, 700, () => View.Options.TransitionMs, v => View.Options.TransitionMs = v, "ms");
        Slider("Cascade spread", 0, 400, () => View.Options.StaggerMs, v => View.Options.StaggerMs = v, "ms");
        Slider("Lift distance", 0, 16, () => View.Options.Travel, v => View.Options.Travel = v, "u");
        Label("Crossfade · dissolve characters\nLift · slide old / new characters\nFlip · fold characters through center\nCascade · stagger character changes", 78);
        Check("Enlarged character previews", () => View.CharacterDetail, v => View.CharacterDetail = v);
        Check("Loop character changes", () => View.ModifierDemo, v => { View.StartModifierDemo(v); RefreshChecks(); });
        Button("Cycle Shift", () => View.TriggerKey("Shift"));
        Button("Cycle AltGr / right Alt", () => View.TriggerKey("AltGr"));
        Button("Toggle Caps", () => View.TriggerKey("Caps"));
        Button("Tap E · consume one-shots", () => View.TriggerKey("e"));
        Label("Loop: a → A → a, e → € → e,\nthen Caps on / off.", 42);
        sidebar = Section("Pointer");
        Label("POINTER EFFECTS", 28);
        var presets = new ComboBox { Width = 236, DropDownStyle = ComboBoxStyle.DropDownList };
        presets.Items.AddRange(EffectOptions.Presets); presets.SelectedItem = "Baseline";
        sidebar.Controls.Add(presets);
        presets.SelectedIndexChanged += (_, _) =>
        {
            View.Options.Preset(presets.Text); RefreshChecks(); View.ResetMotion();
        };
        Check("Hover afterglow", () => View.Options.Afterglow, v => View.Options.Afterglow = v);
        Check("Pointer spotlight", () => View.Options.Spotlight, v => View.Options.Spotlight = v);
        Check("Proximity border light", () => View.Options.Edges, v => View.Options.Edges = v);
        Check("Click ripple", () => View.Options.Ripples, v => View.Options.Ripples = v);
        Check("Press flash / held tint", () => View.Options.PressFlash, v => View.Options.PressFlash = v);
        Slider("Hover in", 0, 120, () => View.Options.EnterMs, v => View.Options.EnterMs = v, "ms");
        Slider("Hover out", 0, 900, () => View.Options.LeaveMs, v => View.Options.LeaveMs = v, "ms");
        Slider("Light radius", 20, 160, () => View.Options.Radius, v => View.Options.Radius = v, "u");
        Slider("Effect strength", 0, 100, () => View.Options.Strength, v => View.Options.Strength = v, "%");
        Slider("Ripple duration", 150, 650, () => View.Options.RippleMs, v => View.Options.RippleMs = v, "ms");
        Check("Automatic pointer sweep", () => View.Demo, v => { View.Demo = v; View.ModifierDemo = false; RefreshChecks(); View.ResetMotion(); });
        sidebar = common;
        Check("Show baseline below", () => View.Compare, v => View.Compare = v);
        var layouts = new ComboBox { Width = 236, DropDownStyle = ComboBoxStyle.DropDownList };
        layouts.Items.AddRange(["Swedish · ISO", "US English · ANSI"]); layouts.SelectedIndex = 0;
        layouts.SelectedIndexChanged += (_, _) => { View.Session.SetLayout(layouts.SelectedIndex == 0, View.Now); View.ResetMotion(); UpdateStatus(); };
        sidebar.Controls.Add(layouts);
        Button("Clear modifiers", () => { View.ModifierDemo = false; RefreshChecks(); View.Session.Reset(View.Now); View.ResetMotion(); UpdateStatus(); });
        Button("Save keyboard PNG + settings", Save);
        View.Changed += UpdateStatus;
        metrics.Tick += (_, _) => UpdateStatus(); metrics.Start(); UpdateStatus();

        void RefreshChecks() { updating = true; foreach (var action in refresh) action(); updating = false; }

        void Check(string text, Func<bool> get, Action<bool> set)
        {
            var check = new CheckBox { Text = text, Checked = get(), AutoSize = false, Width = 238, Height = 25, Margin = new(0, 1, 0, 1) };
            sidebar.Controls.Add(check); refresh.Add(() => check.Checked = get());
            check.CheckedChanged += (_, _) => { if (updating) return; set(check.Checked); View.Invalidate(); };
        }
        void Slider(string name, int min, int max, Func<int> get, Action<int> set, string unit)
        {
            var label = new Label { Width = 238, Height = 20, Margin = new(0, 5, 0, 0) };
            var slider = new TrackBar { Width = 238, Height = 28, AutoSize = false, Minimum = min, Maximum = max, Value = get(), TickStyle = TickStyle.None, Margin = new(0) };
            void Display() => label.Text = $"{name}   {get()} {unit}";
            Display(); sidebar.Controls.Add(label); sidebar.Controls.Add(slider);
            slider.ValueChanged += (_, _) => { set(slider.Value); Display(); View.Invalidate(); };
        }
        void Button(string text, Action action)
        {
            var button = new Button { Text = text, Width = 236, Height = 32, FlatStyle = FlatStyle.Flat, Margin = new(0, 6, 0, 0) };
            button.Click += (_, _) => action(); sidebar.Controls.Add(button);
        }
    }
    private void UpdateStatus() => status.Text = $"LOCAL PREVIEW   {View.Session.LastChord}     |     Hover: {View.Effects.Hover?.Id ?? "—"}     |     Skia CPU frame: {View.LastRenderMs:F1} ms";
    private void Save()
    {
        using var dialog = new SaveFileDialog { Filter = "PNG image|*.png", FileName = "goboard-effects.png" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            View.Renderer.Save(dialog.FileName, View.Session, View.Effects, View.Options, View.Now);
            File.WriteAllText(Path.ChangeExtension(dialog.FileName, ".json"), JsonSerializer.Serialize(new { Layout = View.Session.Keyboard.Layout.Name, Options = View.Options }, new JsonSerializerOptions { WriteIndented = true }));
            status.Text = "Saved " + dialog.FileName;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
    protected override void Dispose(bool disposing) { if (disposing) metrics.Dispose(); base.Dispose(disposing); }
}
