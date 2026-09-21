# Technical design

GoBoard is a Windows desktop keyboard and SteamVR overlay sharing the same keyboard model, input backend, settings, and SkiaSharp presentation. See [AGENTS.md](../AGENTS.md) for project ownership and build commands, [development](development.md) for feature details and validation evidence, and [automatic layouts](automatic-keyboard-layouts.md) for Windows layout support.

## Application boundaries

`GoBoard.slnx` contains the production projects and regression tests:

- `GoBoard.Core` owns geometry, physical key identity, legends, pointer state, modifiers, pose math, and settings. It has no Windows, SkiaSharp, or OpenVR dependency.
- `GoBoard.Platform.Windows` observes the foreground target and keyboard layout, reads installed keyboard tables, injects balanced native input, and plays key sounds.
- `GoBoard.Presentation.Skia` draws the shared keyboard, settings, and handles. Rendering does not own input injection or the VR lifecycle.
- `GoBoard.OpenVR` owns overlay creation, dashboard following, controller routing, movement, resizing, texture submission, and shutdown.
- `GoBoard.App` provides the executable entry point and Windows Forms desktop/settings hosts. Desktop mode does not initialize SteamVR.

## Rendering and presentation

See [GPU rendering](gpu-rendering.md) for backend selection, diagnostics and validation, and the [initial investigation](rendering-performance-investigation.md) for the earlier CPU measurements and backend comparison.

Rendering and hit testing use the same logical key geometry. OpenVR mouse coordinates have a bottom-left origin; Skia uses top-left. The shared geometry converts between them independently of texture resolution and physical size.

The VR host uses persistent, double-buffered OpenGL textures. OpenTK owns a hidden context; the main keyboard and shortcut palette draw through Skia GPU surfaces wrapping the back texture. Baseline images, character layers, interrupted transitions and effect compositing stay on the GPU. Small reusable soft-key backgrounds still originate in the shared raster cache. `GL.Finish` completes drawing before `SetOverlayTexture` publishes it; textures, framebuffers and the Skia context remain alive through overlay destruction and OpenVR shutdown. Texture bounds remain U 0→1 and V 1→0, with a top-left Skia surface origin matching the existing CPU-upload row convention. GPU textures use OpenVR's premultiplied-alpha flag.

Skia GPU initialization or drawing failure disables that backend for the session and falls back to CPU rendering plus texture upload, provided the underlying GL context remains usable. Pending Skia work is abandoned before CPU upload and renderer caches reset when the context changes. `GOBOARD_RENDERER=cpu` forces the previous path. Settings, handles, desktop and PNG previews retain CPU rendering. The shared renderer accepts a host canvas as well as returning owned raster frames; immutable CPU caches avoid unnecessary image copies.

The keyboard is an independent overlay with `VisibleInDashboard`, an explicit mouse scale, and an intersection mask matching its geometry. Settings use a separate dashboard tab. Custom grab and resize handles have their own overlays. The keyboard and grab handle enable SteamVR's native backside surface without changing global SteamVR settings.

Desktop mode uses a topmost, non-activating Windows Forms surface. Both hosts use the shared Skia renderers and settings controls. Themes change visual tokens while preserving geometry and input behavior. Optional effects consume accepted interaction state; they do not schedule or delay typing. Idle rendering avoids unnecessary work, and renderer benchmarks remain separate from full presentation latency.

## Dashboard placement and controller ownership

`DashboardFollower` resolves SteamVR's menu bar through `FindOverlay("valve.steam.gamepadui.bar")` and reads its pose in standing space. This internal overlay name is a compatibility dependency. Missing or invalid anchors hide the panel and produce a diagnostic.

The keyboard and its handles follow a stored dashboard-relative pose. Dashboard scale does not resize the keyboard, and GoBoard never modifies the dashboard transform. A grab captures the panel-to-controller transform and uses `SetOverlayTransformTrackedDeviceRelative` for movement. Display-predicted controller poses supply the retained offset on release. Runtime transform readbacks are not treated as independent dragging.

Tracked-device identity owns capture; cursor slots are transient. A grab cancels only its owner's keyboard capture, allowing the other hand to type. Releases, focus changes, tracking loss, and hiding invalidate stale input. Queued events are checked against controller identity and release/focus timestamps before a new attachment or keystroke is accepted. Fresh motion may restore hover but cannot initiate a press or grab.

Resizing captures one controller and previews proportional scaling around the keyboard center. It pauses typing, excludes simultaneous movement, and saves the size on release. Cancellation restores the saved size; external size edits take precedence. Geometry changes discard stale captures and hover state.

## Windows layouts and input

The Windows backend observes the foreground HWND, its thread, and its full HKL. It revalidates the target and layout before input. Normal typing does not steal focus or change the user's Windows input language.

Production legends come from installed Windows keyboard DLL tables and cached managed data. Label generation does not call live `ToUnicodeEx`, which can inherit pending dead-key state. Physical arrangement is separate from character labels. When labels cannot be generated, GoBoard preserves the real HKL and shows fallback labels with a warning; input errors take precedence over that notice.

Ordinary keys use physical scan codes so Windows handles shortcuts and composition. Extended-key identity is preserved through key-up. `SendInput` results are checked, accepted downs are tracked for partial-failure cleanup, and only GoBoard-owned keys are released. Physical modifiers borrowed from the user are never released by GoBoard. Target/layout changes, pointer loss, hiding, and shutdown cancel pending input and release owned keys. Typed text is not logged.

## Shared keyboard state

Each pointer has independent hover and capture state. Leaving a captured key cancels it; moving to another key while held does not type that key. Shared-key ownership and repeat timing live in the model, so desktop and VR follow the same keyboard rules.

Ctrl, Alt, AltGr, and Shift cycle Idle → OneShot → Locked → Idle. An ordinary successful stroke consumes active one-shots together, while repeats retain their captured chord. Locked modifiers persist for later keys. Win uses arm-then-tap behavior: the first click arms a shortcut, and a second click sends a balanced standalone Windows-key tap. It has no locked mode. Each stroke balances its native modifiers and key immediately, preventing modifier leakage between pointers.

## Settings and validation

Desktop and VR share persisted settings. An edit merges only the changed field into the latest file under a per-file mutex and replaces the file atomically. Failed writes leave applied settings unchanged. Malformed files retain the last working values and surface an error. Stable setting IDs and migrations preserve existing preferences.

`SettingsEditor` owns the shared pointer-to-edit sequence: selection, eligibility checks against current and latest saved settings, persistence, error feedback, and pointer reconfiguration. It returns sound-audition and autostart requests for the hosts to execute. The hosts retain native event translation, cached Windows layout observation, drawing, audio playback, and autostart execution; `SettingsStore` retains file locking and atomic replacement.

Regression tests cover shared behavior without requiring SteamVR or a headset. Native input checks use disposable foreground targets, and settings checks use disposable settings files. PNG previews exercise the real renderers. These checks do not establish headset legibility, controller feel, real application focus behavior, sound routing, or end-to-end display latency; those remain explicit manual acceptance checks documented in [development](development.md).
