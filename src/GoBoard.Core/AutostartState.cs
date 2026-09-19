namespace GoBoard.Core;

// SteamVR owns this state; it is never saved with keyboard preferences.
internal sealed record AutostartState(bool? Enabled = null, bool Busy = false, string Error = null)
{
    public string ButtonLabel => Busy ? "Please wait…" : Error != null ? "Retry" :
        Enabled == true ? "Unregister autostart" : "Register autostart";
    public string Status => Error != null ? "Unavailable · Retry to check SteamVR" :
        Busy ? "Updating SteamVR…" : Enabled == true ? "On · Starts when SteamVR starts" :
        Enabled == false ? "Off · Start GoBoard manually" : "Checking SteamVR…";
    public bool CanClick => !Busy && (Enabled.HasValue || Error != null);
    public static AutostartState Preview => new(false);
}
