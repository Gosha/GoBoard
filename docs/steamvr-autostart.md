# SteamVR autostart

## Configure

In the desktop or SteamVR settings page, open **General** and use **Register autostart** above the reset buttons. It registers the production executable and enables SteamVR autostart. When autostart is on, the button becomes **Unregister autostart**, which disables it and removes the managed registration. Removing autostart leaves the current keyboard running. If SteamVR has disabled autostart while leaving the application registered, **Register autostart** enables it again.

The status refreshes from SteamVR every ten seconds while the settings page is visible. During a change the button is disabled; errors offer **Retry**, which checks status before allowing another change. Hover over the desktop panel for error details. A separate hidden utility process performs the OpenVR calls, so neither settings host blocks the typing loop or shuts down the live overlay's OpenVR connection. Resetting keyboard preferences does not alter autostart.

The settings window launched by `settings-goboard.ps1` registers the standard production Release build, building it first if missing. Settings opened from a running keyboard register that keyboard's executable. For a standalone installation, `GoBoard.exe --settings` registers the executable beside that settings window; `--settings --executable PATH` can explicitly select another production installation. Preview rendering does not query or modify SteamVR.

Run `steamvr-goboard.ps1 -Action Enable` to register the production Release executable and enable autostart. The script builds production if missing, then uses a separate helper build to configure SteamVR without locking or registering the helper executable. To update an existing production build, stop GoBoard and run `dotnet build src/GoBoard.App -c Release` first. No PowerShell script or build runs during SteamVR startup.

For an installed/published copy, pass `-Executable 'C:\Apps\GoBoard\GoBoard.exe'` to Enable or Register. Keep the entire build/publish output, including .NET dependencies and `openvr_api.dll`, together in a permanent directory. A framework-dependent build needs the .NET 10 Desktop Runtime. Do not register a temporary worktree that you plan to delete.

| Action | Effect |
| --- | --- |
| `Register` | Register/update the executable; preserve an existing autostart setting, default off for a new registration. |
| `Enable` | Register/update the executable and enable autostart. |
| `Disable` | Disable autostart without removing registration. |
| `Unregister` | Disable autostart, remove the managed manifest registration, then delete the generated manifest. |
| `Status` | Print registration, autostart, executable, and managed manifest path. |

The same commands are available without repository scripts:

```powershell
& 'C:\Apps\GoBoard\GoBoard.exe' --steamvr enable
& 'C:\Apps\GoBoard\GoBoard.exe' --steamvr status
& 'C:\Apps\GoBoard\GoBoard.exe' --steamvr disable
& 'C:\Apps\GoBoard\GoBoard.exe' --steamvr unregister
```

`--steamvr register|enable --executable PATH` targets another production executable. Without this option, it uses `GoBoard.exe` beside the command's assembly, even when invoked using `dotnet GoBoard.dll`; it never registers the dotnet host. Spaces and Unicode are serialized as JSON path values, without shell quoting inside the manifest. Failure returns exit code 1 with the OpenVR error. If connection fails, start SteamVR and retry. Configuration uses `VRApplication_Utility`, without starting the keyboard or requiring graphics initialization.

SteamVR is the source of truth for autostart. The keyboard's `settings.json` contains only keyboard preferences. Disabling/unregistering does not close an already-running keyboard; use `stop-goboard.ps1` or `GoBoard.exe --stop` for that. Normal startup never registers or re-enables autostart.

## Identity, paths, and lifecycle

The shipped `goboard.vrmanifest` is copied to build and publish output and embedded as the generation template. Commands register a persistent, absolute-path manifest at `%LOCALAPPDATA%\GoBoard\steamvr\goboard.vrmanifest` with application key `goboard.app`. Both `binary_path_windows` and `working_directory` point at the selected installation. The managed manifest stays at one path across builds so another GoBoard copy can disable/unregister an old installation even after its executable has been removed. Re-run Register or Enable with the new executable after moving the installation; one identity means only one installation is registered at a time. Use these commands instead of manually registering the shipped template at additional paths.

Autostart requires the manifest's `is_dashboard_overlay` flag; the actual keyboard still uses its existing independent overlay, with a separate settings dashboard tab. The API contract is described in Valve's [OpenVR header](https://github.com/ValveSoftware/openvr/blob/master/headers/openvr.h) under `IVRApplications`. Registration uses `AddApplicationManifest(..., false)` and explicit `SetApplicationAutoLaunch`; a registered running keyboard calls `IdentifyApplication` after overlay initialization.

Manual, script, and SteamVR launches share `Local\GoBoard.Runtime` and `Local\GoBoard.Runtime.Stop`. Only one live desktop or VR keyboard runs in the Windows session; a duplicate exits successfully before touching OpenVR, logs, or the stop signal. Settings, registration, render, and diagnostic modes do not acquire this guard. A stop while idle leaves no file to poison a later launch. An explicit `--stop-file PATH` remains supported for external callers, who own clearing that file themselves.

Each live launch writes `goboard.log` and `goboard.error.log` under `%LOCALAPPDATA%\GoBoard\runtime`, independently of its working directory; the latest launch replaces the previous logs. SteamVR quit events are acknowledged and run the existing input/overlay cleanup. The stop script no longer relies on PID files, and does not force-kill any process.

## Validation

Automated regression tests cover the manifest identity/schema, absolute paths containing spaces and Unicode, invalid executable rejection, register/enable/disable/unregister transitions, relocation, preserved autostart settings, manifest rollback on registration failure, retryable configuration failures, and duplicate/stop/restart behavior. The API tests use a fake OpenVR applications interface; they do not modify this PC's SteamVR registration.

Verification on 2026-09-18: Release solution build and production publish passed; all 53 unit tests passed; the published executable's `--self-test` and invalid registration argument checks passed; all changed PowerShell scripts parsed successfully. Publish output includes `GoBoard.exe`, `goboard.vrmanifest`, and `openvr_api.dll`.

After rebasing onto `main` on 2026-09-19, locked restore, Release solution build, all 188 regression tests, and PowerShell parsing passed. The rebase preserves the launcher's isolated outputs and `-BuildOnly` option, along with the current desktop settings and position-reset behavior.

Settings button validation on 2026-09-19: Release build, all 191 regression tests, and `--settings-input-check` passed. The native check exercises register/unregister clicks at three window sizes with a fake SteamVR backend and verifies that keyboard preferences are unchanged. Controller tests cover busy-state click suppression, confirmed state updates, error/retry behavior, and external status refresh. Desktop and VR settings previews were rendered and visually inspected, and the README screenshot was regenerated. These checks did not change the user's SteamVR registration; live desktop/VR autostart acceptance remains below.

Rebase validation against `main` at `e9b709d`: Release build and all 268 regression tests passed; the isolated native settings check passed on rerun. The updated settings views preserve the Shortcuts tab and keep autostart above the reset buttons. Settings screenshots were regenerated with the current defaults.

The process smoke check below briefly displays a desktop keyboard without injecting input. It checks a launch from a different working directory, duplicate VR-mode launch, the stop script, restart, and log creation. Close all GoBoard processes first. This check was attempted but stopped at its existing-process guard on this PC; the user's running GoBoard was left untouched. Live SteamVR registration/autostart was not changed or validated here.

```powershell
.\tests\runtime-launch-check.ps1
# Or test a published installation:
.\tests\runtime-launch-check.ps1 -Executable 'C:\Apps\GoBoard\GoBoard.exe'
```

Before release, perform these checks with SteamVR and a headset (not established by unit tests):

1. Enable using a permanent installation path containing spaces. Check Status shows that executable and autostart enabled, and SteamVR lists GoBoard once.
2. Quit and restart SteamVR. Confirm exactly one GoBoard overlay and one settings tab appear, with saved keyboard settings applied. Confirm a manual second launch exits without disrupting the keyboard.
3. Hold a keyboard modifier, then run `stop-goboard.ps1`. Confirm the overlay disappears and no injected keys remain held. Restart SteamVR and confirm GoBoard starts again.
4. Quit SteamVR while GoBoard is running. Confirm GoBoard exits, its log records cleanup, and restarting SteamVR launches it again.
5. Disable, restart SteamVR, and confirm GoBoard stays closed. Change the setting in SteamVR and confirm Status reflects it. Register again and confirm the setting is preserved.
6. Move/copy the installation, Register the new executable, and confirm only that path launches on restart. Unregister; confirm GoBoard is removed and no longer autostarts. Repeat Unregister to check idempotence.
7. In each settings host, register and unregister using the autostart button above the reset buttons. Confirm the label/status updates, Status agrees, and typing continues. Change autostart externally in SteamVR and confirm the panel refreshes. With SteamVR unavailable, confirm the desktop panel offers Retry and shows an error instead of claiming success.
