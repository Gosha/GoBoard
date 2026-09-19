using System.Runtime.InteropServices;
using GoBoard.Platform.Windows;
using Xunit;

namespace GoBoard.Tests;

public sealed class ForegroundInputTargetTests
{
    [Fact]
    public void EditorLayoutFollowsSwitchesWhileFrameLayoutStaysSwedish()
    {
        var windows = new FakeWindows();
        foreach (var layout in new nint[] { 0x041d041d, 0x08090809, 0x04110411, 0x04090409 })
        {
            windows.Layouts[20] = layout;
            var target = ForegroundInputTarget.Read(windows);
            Assert.Equal(new InputTarget(100, layout, 1, 200, 20), target);
            Assert.Equal((nint)0x041d041d, windows.Layouts[10]);
        }
        Assert.All(windows.LayoutQueries, thread => Assert.Equal(20u, thread));
    }

    [Fact]
    public void FullLayoutHandleIsPreservedIncludingVariants()
    {
        var windows = new FakeWindows();
        windows.Layouts[20] = unchecked((nint)(long)0xfffffffff0020409);
        Assert.Equal(windows.Layouts[20], ForegroundInputTarget.Read(windows).Layout);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoFocusedControlOrUnavailableGuiInfoStillAllowsFrameShortcuts(bool unavailable)
    {
        var windows = new FakeWindows { FocusWindow = unavailable ? null : 0 };
        Assert.Equal(new InputTarget(100, 0x041d041d, 1, 100, 10), ForegroundInputTarget.Read(windows));
    }

    [Fact]
    public void SameThreadEditorUsesItsNormalLayout()
    {
        var windows = new FakeWindows();
        windows.Owners[200] = (10, 1);
        Assert.Equal(new InputTarget(100, 0x041d041d, 1, 200, 10), ForegroundInputTarget.Read(windows));
    }

    [Fact]
    public void CrossProcessHostedChildUsesItsInputThread()
    {
        var windows = new FakeWindows();
        windows.Owners[200] = (20, 2);
        Assert.Equal(new InputTarget(100, 0x08090809, 1, 200, 20), ForegroundInputTarget.Read(windows));
    }

    [Fact]
    public void FocusAndThreadChangesInvalidateCapturedTargetEvenWithSameLayout()
    {
        var windows = new FakeWindows();
        var before = ForegroundInputTarget.Read(windows);
        windows.FocusWindow = 300;
        windows.Owners[300] = (30, 1);
        windows.Layouts[30] = windows.Layouts[20];
        Assert.NotEqual(before, ForegroundInputTarget.Read(windows));
    }

    [Theory]
    [InlineData("foreground")]
    [InlineData("frame-thread")]
    [InlineData("focus-thread")]
    [InlineData("unrelated-focus")]
    [InlineData("layout")]
    public void InvalidTargetsAreRejectedWithoutReadingCallerLayout(string invalid)
    {
        var windows = new FakeWindows();
        switch (invalid)
        {
            case "foreground": windows.Frame = 0; break;
            case "frame-thread": windows.Owners[100] = default; break;
            case "focus-thread": windows.Owners[200] = default; break;
            case "unrelated-focus": windows.Child = false; break;
            case "layout": windows.Layouts[20] = 0; break;
        }
        Assert.Equal(default, ForegroundInputTarget.Read(windows));
        Assert.DoesNotContain(0u, windows.LayoutQueries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActivationRacesDoNotCombineDifferentTargets(bool changeFocus)
    {
        var windows = new FakeWindows();
        windows.AfterLayout = () => { if (changeFocus) windows.FocusWindow = 300; else windows.Frame = 400; };
        Assert.Equal(default, ForegroundInputTarget.Read(windows));
    }

    [Fact]
    public void GuiThreadInfoMatchesWindowsX64Abi()
    {
        Assert.Equal(72, Marshal.SizeOf<ForegroundInputTarget.GuiThreadInfo>());
        Assert.Equal((nint)16, Marshal.OffsetOf<ForegroundInputTarget.GuiThreadInfo>("Focus"));
    }

    private sealed class FakeWindows : ForegroundInputTarget.IWindows
    {
        public nint Frame = 100;
        public nint? FocusWindow = 200;
        public bool Child = true;
        public Action AfterLayout;
        public readonly Dictionary<nint, (uint Thread, uint Process)> Owners = new() { [100] = (10, 1), [200] = (20, 1) };
        public readonly Dictionary<uint, nint> Layouts = new() { [10] = 0x041d041d, [20] = 0x08090809 };
        public readonly List<uint> LayoutQueries = new();
        public nint Foreground() => Frame;
        public (uint Thread, uint Process) Owner(nint window) => Owners.GetValueOrDefault(window);
        public nint? Focus(uint thread) { Assert.Equal(10u, thread); return FocusWindow; }
        public bool IsChild(nint parent, nint child) { Assert.Equal((nint)100, parent); return Child; }
        public nint Layout(uint thread)
        {
            LayoutQueries.Add(thread);
            var result = Layouts.GetValueOrDefault(thread);
            AfterLayout?.Invoke();
            return result;
        }
    }
}
