namespace GoBoard.Core;

// SteamVR cursor indices are laser slots, not stable hand identities. Both
// hands can report slot 0 when the primary laser switches without FocusEnter.
internal sealed class OverlayPointers
{
    private readonly Dictionary<uint, uint?> sources = new();

    public uint? Resolve(uint slot, uint? controller, bool focusEnter = false)
    {
        if (!controller.HasValue) return sources.GetValueOrDefault(slot);
        if (focusEnter || !sources.TryGetValue(slot, out var previous)) sources[slot] = controller;
        else if (previous != controller) sources[slot] = null;
        // Explicit controller identity always wins. An ambiguous unidentified
        // event must never borrow whichever hand happened to be primary last.
        return controller;
    }

    public static void Verify()
    {
        var pointers = new OverlayPointers();
        if (pointers.Resolve(0, 7, true) != 7 || pointers.Resolve(0, 8) != 8 ||
            pointers.Resolve(1, 7) != 7 || pointers.Resolve(0, null) != null ||
            pointers.Resolve(0, 8, true) != 8 || pointers.Resolve(0, null) != 8)
            throw new InvalidOperationException("Controller routing failed across reused laser slots.");
        Console.WriteLine("Pointer routing checks passed: primary/secondary slot changes, explicit hand identity, ambiguous-source rejection.");
    }
}
