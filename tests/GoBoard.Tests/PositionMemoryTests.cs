using System.Numerics;
using GoBoard.Core;
using Xunit;

namespace GoBoard.Tests;

public sealed class PositionMemoryTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"GoBoard-position-{Guid.NewGuid():N}.json");
    private PositionMemory<VrKeyboardPosition> VrMemory(SettingsStore store) =>
        new(store, s => s.VrPosition, (s, position) => s with { VrPosition = position });
    private static readonly VrKeyboardPosition Moved = VrKeyboardPosition.FromMatrix(
        Matrix4x4.CreateRotationX(.4f) * Matrix4x4.CreateRotationY(.2f) * Matrix4x4.CreateTranslation(.2f, -.6f, .5f));

    [Fact]
    public void RestoreKeepsLocalTranslationAndRotationAgainstANewDashboard()
    {
        var store = new SettingsStore(path);
        Assert.True(store.Update(s => s with { RememberPosition = true }));
        var memory = VrMemory(store);
        Assert.True(memory.Update(Moved, false, store.Current));
        var saved = new SettingsStore(path).Current;
        Assert.Equal(Moved, saved.VrPosition);
        var pose = new RelativePose();
        pose.Restore(saved.VrPosition);
        Assert.False(pose.AtDefaultPosition);
        var parent = Matrix4x4.CreateRotationY(.9f) * Matrix4x4.CreateTranslation(1, 2, 3);
        Assert.True(RelativePose.Near(Moved.ToMatrix() * parent, pose.Update(parent).World));
        Assert.False(pose.Update(parent).Write);
        pose.Reset();
        Assert.True(pose.AtDefaultPosition);
        pose.Update(parent * Matrix4x4.CreateTranslation(3, 4, 5));
        Assert.True(pose.AtDefaultPosition);
        pose.SetWorld(parent, Matrix4x4.CreateTranslation(0, -.3001f, .1001f) * parent);
        Assert.True(pose.AtDefaultPosition); // Floating-point noise at release is not a move.
    }

    [Fact]
    public void SavingIsOptInAndOnlyCommitsSettledMovementOrEnabling()
    {
        var store = new SettingsStore(path);
        var memory = VrMemory(store);
        Assert.True(memory.Update(Moved, false, store.Current));
        Assert.False(File.Exists(path));
        Assert.True(store.Update(s => SettingsControls.Apply(SettingsAction.ToggleRememberPosition, s)));
        Assert.True(memory.Update(Moved, true, store.Current));
        Assert.Null(store.Current.VrPosition);
        Assert.True(memory.Update(Moved, false, store.Current));
        Assert.Equal(Moved, store.Current.VrPosition);
        // A stable frame must not even try to read/write the settings file.
        File.WriteAllText(path, "{broken");
        Assert.True(memory.Update(Moved, false, store.Current));
        Assert.Null(store.Error);
        var changed = Moved with { X = 1 };
        var previous = store.Current;
        Assert.False(memory.Update(changed, false, store.Current));
        Assert.Equal(previous, store.Current);
        Assert.NotNull(store.Error);
        Assert.Equal("{broken", File.ReadAllText(path));
    }

    [Fact]
    public void PositionsMergeAcrossHostsAndResetWinsOverStaleSaves()
    {
        var vr = new SettingsStore(path);
        Assert.True(vr.Update(s => s with { RememberPosition = true }));
        var desktop = new SettingsStore(path);
        var vrMemory = VrMemory(vr);
        var desktopMemory = new PositionMemory<DesktopKeyboardPosition>(desktop,
            s => s.DesktopPosition, (s, position) => s with { DesktopPosition = position });
        Assert.True(vrMemory.Update(Moved, false, vr.Current));
        Assert.True(desktopMemory.Update(new(-700, 400), false, desktop.Current));
        Assert.Equal(Moved, new SettingsStore(path).Current.VrPosition);
        Assert.Equal(new(-700, 400), new SettingsStore(path).Current.DesktopPosition);
        var stale = vr.Current;
        Assert.True(desktop.Update(s => SettingsControls.Apply(SettingsAction.ResetPosition, s)));
        Assert.True(vrMemory.Update(Moved with { X = 1 }, false, stale));
        var reset = new SettingsStore(path).Current;
        Assert.True(reset.RememberPosition);
        Assert.Null(reset.VrPosition);
        Assert.Null(reset.DesktopPosition);
        Assert.NotEqual(stale.PositionResetId, reset.PositionResetId);
        // Disabling from another host also rejects an in-flight move.
        Assert.True(desktop.Update(s => SettingsControls.Apply(SettingsAction.ToggleRememberPosition, s)));
        Assert.True(vrMemory.Update(Moved with { X = 2 }, false, reset));
        Assert.False(new SettingsStore(path).Current.RememberPosition);
        Assert.Null(new SettingsStore(path).Current.VrPosition);
    }

    [Fact]
    public void PreferencesAreIndependentAndOldSettingsKeepExistingBehavior()
    {
        File.WriteAllText(path, "{\"SizePercent\":120}");
        var store = new SettingsStore(path);
        Assert.False(store.Current.RememberPosition);
        Assert.False(store.Current.Inactivity.KeepVisibleAtDefaultPosition);
        Assert.True(store.Update(s => SettingsControls.Apply(SettingsAction.ToggleKeepVisibleAtDefaultPosition, s)));
        Assert.False(store.Current.RememberPosition);
        Assert.True(store.Update(s => SettingsControls.Apply(SettingsAction.ToggleRememberPosition, s)));
        Assert.True(store.Current.Inactivity.KeepVisibleAtDefaultPosition);
        var reset = SettingsControls.Apply(SettingsAction.ResetInactivity, store.Current);
        Assert.True(reset.RememberPosition);
        Assert.False(reset.Inactivity.KeepVisibleAtDefaultPosition);
        Assert.Equal(120, reset.SizePercent);
    }

    [Fact]
    public void InvalidSavedPositionsFallBackWithoutChangingPreferences()
    {
        var bad = new VrKeyboardPosition(1, 2, 3, 0, 0, 0, 0);
        Assert.Null(bad.Normalize());
        Assert.Null((Moved with { X = float.NaN }).Normalize());
        Assert.Null((Moved with { Qx = float.PositiveInfinity }).Normalize());
        var settings = new BoardSettings { RememberPosition = true, VrPosition = bad,
            DesktopPosition = new(int.MaxValue, int.MinValue) }.Normalize();
        Assert.True(settings.RememberPosition);
        Assert.Null(settings.VrPosition);
        Assert.Null(settings.DesktopPosition);
        var pose = new RelativePose();
        pose.Restore(bad);
        Assert.True(pose.AtDefaultPosition);
    }

    public void Dispose() => File.Delete(path);
}
