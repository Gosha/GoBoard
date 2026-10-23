namespace GoBoard.Core;

// Grip grabs start only on a fresh press while that controller points at the
// target. Holding the grip while moving onto the target never starts a grab.
internal sealed class GripPresses
{
    private readonly HashSet<uint> held = new();

    public uint? Update(IEnumerable<(uint Device, bool Held)> grips, ICollection<uint> targets)
    {
        uint? pressed = null;
        foreach (var (device, down) in grips)
        {
            if (!down) held.Remove(device);
            else if (held.Add(device) && pressed == null && targets.Contains(device)) pressed = device;
        }
        return pressed;
    }
}
