namespace GoBoard.Core;

internal enum InactivityMode { Off, Hide, Minimize, Transparent }

internal sealed record InactivitySettings
{
    public InactivityMode Mode { get; init; }
    public int DelayMs { get; init; } = 1000;
    public int RevealMs { get; init; } = 200;
    public int TransitionMs { get; init; } = 200;
    public int OpacityPercent { get; init; } = 15;
    public int SizePercent { get; init; } = 20;
    public InactivitySettings Normalize() => this with
    {
        Mode = Enum.IsDefined(Mode) ? Mode : InactivityMode.Off,
        DelayMs = Math.Clamp(DelayMs, 250, 30000),
        RevealMs = Math.Clamp(RevealMs, 0, 2000),
        TransitionMs = Math.Clamp(TransitionMs, 0, 2000),
        OpacityPercent = Math.Clamp(OpacityPercent, 0, 80),
        SizePercent = Math.Clamp(SizePercent, 10, 50)
    };
}

// Hosts aggregate controller-specific focus after draining events. A stationary
// pointer is activity; typing frequency is deliberately irrelevant.
internal sealed class KeyboardInactivity
{
    private InactivitySettings settings;
    private bool available;
    private double idleSince = double.NaN, revealSince = double.NaN, transitionAt;
    public bool Dormant { get; private set; }
    public float Progress { get; private set; }
    public float Opacity => settings?.Mode switch
    {
        InactivityMode.Hide => 1 - Progress,
        InactivityMode.Transparent => 1 - Progress * (1 - settings.OpacityPercent / 100f),
        _ => 1
    };
    public float Scale => settings?.Mode == InactivityMode.Minimize
        ? 1 - Progress * (1 - settings.SizePercent / 100f) : 1;

    public void Update(InactivitySettings next, double now, bool visible, bool engaged, bool revealHovered, bool manipulating = false)
    {
        if (!double.IsFinite(now)) return;
        if (next != settings)
        {
            settings = next;
            Wake();
        }
        if (visible != available)
        {
            available = visible;
            // Dashboard/native-keyboard visibility suspends interaction, but
            // preserves whether the user had left the keyboard idle.
            idleSince = revealSince = double.NaN;
        }
        if (next.Mode == InactivityMode.Off) { Wake(); return; }
        if (!visible)
        {
            // Finish an outgoing animation while hidden so reopening cannot
            // briefly flash a full-size or opaque keyboard.
            if (Dormant) Progress = 1;
            return;
        }
        if (manipulating) { Wake(); return; }
        if (Dormant)
        {
            if (!revealHovered) revealSince = double.NaN;
            else
            {
                if (double.IsNaN(revealSince)) revealSince = now;
                if (now - revealSince + 1e-9 >= next.RevealMs / 1000.0) { Wake(); return; }
            }
            Progress = next.TransitionMs == 0 ? 1 : Math.Max(Progress,
                (float)Math.Clamp((now - transitionAt) * 1000 / next.TransitionMs, 0, 1));
            return;
        }
        if (engaged || revealHovered) { idleSince = double.NaN; return; }
        if (double.IsNaN(idleSince)) idleSince = now;
        if (now - idleSince + 1e-9 < next.DelayMs / 1000.0) return;
        Dormant = true;
        transitionAt = now;
        Progress = next.TransitionMs == 0 ? 1 : 0;
    }

    // Revealing is immediate after the hover delay: visual animation must never
    // postpone a key press or leave a moving keyboard under the user's pointer.
    public void Wake()
    {
        Dormant = false; Progress = 0;
        idleSince = revealSince = double.NaN;
    }
}
