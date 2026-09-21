# GPU keyboard rendering

The VR keyboard and shortcut palette now render with Skia on the existing OpenGL context. They draw directly into the persistent back texture; there is no full-frame CPU bitmap, readback, or pixel upload on this path. Baselines, character-transition layers, interruption snapshots and animated compositing use GPU surfaces. Soft-key face images remain a small reusable CPU cache that Skia uploads as needed.

Desktop, PNG previews, settings and handle rendering retain their CPU path. Their immutable caches avoid unnecessary copies when creating Skia images. Drawing commands, keyboard state, geometry, input injection and saved settings remain shared and unchanged.

## Selection and fallback

GPU rendering is attempted by default. The startup log reports the selected renderer. Set `GOBOARD_RENDERER=cpu` before launching to force the CPU/upload path for comparison. This is a process environment override, not a saved setting; remove it to return to automatic selection.

If Skia GPU initialization or drawing fails, the session switches to CPU/upload and logs the fallback. Queued Skia work is abandoned before the CPU writes the back texture, and context-bound caches are discarded. OpenVR publication failures remain errors; they are not hidden as renderer fallback. A lost underlying OpenGL device/context can still prevent either path from working.

Both paths preserve persistent double buffering and retain submitted textures through overlay destruction and OpenVR shutdown. GPU rendering uses premultiplied pixels and the matching OpenVR flag; CPU uploads restore the appropriate alpha flag. GPU framebuffer origin was checked against raw GL texture rows so the existing U 0→1, V 1→0 bounds and pointer mapping remain intact.

The GPU path intentionally retains `GL.Finish` before publication. Removing that wait requires a separately validated synchronization design. Input and rendering still share the VR loop: this change reduces rendering work but does not introduce a rendering worker or promise stall-free input under GPU saturation.

## Timing diagnostics

Set `GOBOARD_TRACE_RENDER=1` before a normal VR launch to log aggregate timings approximately every five seconds, with a final summary at shutdown. This does not log key identities, labels or typed text. Each stage retains at most its latest 1024 samples per reporting interval; `n` counts all observations, `sampled` counts retained observations, and `max` covers the whole interval. Percentiles use the retained samples.

- `gpu.baseline` / `gpu.animation`: CPU time issuing Skia drawing, split by whether the baseline was rebuilt. GPU completion is separate.
- `gpu.draw-and-flush`: drawing plus submitting Skia GPU work; overlaps the preceding stage and must not be added to it.
- `cpu.baseline` / `cpu.animation`: CPU raster time for frames that changed.
- `cpu.texture-upload`, `texture.gpu-wait`, `texture.publish`: upload call, completion wait, and OpenVR texture setter respectively. Wait includes GPU work already issued; it is not pure CPU execution time.
- `input.event-age`: age reported by OpenVR for accepted keyboard event types, before state handling; it is not input-to-photon latency.
- `loop.work`, `loop.frame-sync`, `loop.total`: VR loop work, visible-frame synchronization, and the whole loop including hidden waits. Settings and other overlays are included in total work.

Cold texture creation, shader compilation and first publication can be much slower than warm frames. Startup frames remain visible in the aggregates so their cost is not concealed. For steady-state comparison, wait for the next reporting interval.

## Validation commands

Run from the repository root after the standard locked restore and Release build:

```powershell
dotnet run --project src/GoBoard.App -c Release --no-build -- --gpu-render-check
dotnet run --project src/GoBoard.App -c Release --no-build -- --gpu-overlay-check
dotnet run --project src/GoBoard.App -c Release --no-build -- --numpad-resize-check
dotnet run --project src/GoBoard.App -c Release --no-build -- --shortcut-resize-check
```

`--gpu-render-check` needs a functioning OpenGL GPU but no SteamVR/headset. It uses production rendering and framebuffer wrappers in a hidden context, checks texture orientation, compares CPU/GPU images with an antialiasing tolerance, alternates two textures, exercises two synthetic pointers and rapidly interrupted character transitions, then verifies cancellation, idle rendering and a return to exact CPU output after changing backend. It covers both themes, US/Swedish layouts, numpad and shortcuts, and saves paired animation/settled PNGs under `.runtime/gpu-check`. Readback is confined to verification.

It also reports CPU draw and GPU draw/flush/completion timings with current default effects at actual VR texture sizes and content bounds (20 warmup + 60 measured frame steps per case). Samples include only changed frames. It excludes CPU upload, SteamVR publication and compositor/display latency. CPU and GPU are measured in that order on each synthetic step, so these are exploratory comparisons, not a controlled load study or a standalone GPU timestamp benchmark. Shortcut PNGs show the fixed raster; live texel aspect restores their physical proportions.

`--gpu-overlay-check` requires running SteamVR. It creates one hidden, uniquely named overlay, checks actual GPU publication/alpha flags, injects a drawing failure with queued GPU work, checks CPU fallback byte-for-byte through SteamVR readback, and verifies UV bounds. The fallback message during this command is expected. It destroys the test overlay and shuts down its own OpenVR connection before deleting textures. It does not stop the running GoBoard instance, inject input, or change settings.

The existing numpad/shortcut checks use transparent isolated overlays and validate texture changes, dimensions, ray hitboxes, UVs and mouse scale across geometry/size changes. They also send no input or settings writes.

## Observed results and remaining acceptance

On the development RTX 3090 Ti, the final native comparison measured main-keyboard CPU medians of 28.3–30.0 ms versus GPU medians of 2.27–2.55 ms. GPU p95 was 5.91–8.56 ms versus CPU p95 of 60.0–78.3 ms. An earlier run measured GPU p95 of 8.69–11.15 ms, illustrating variation on the active machine. These synthetic, frequently interrupted transitions deliberately exercise costly states and are not typical typing or headset latency measurements. They use the updated CPU path with copy reductions, not the historical unmodified baseline. [Raw comparison and integration outputs](../experiments/GoBoard.RenderingProbe/results/2026-09-21/).

Image comparisons across the eight scenarios had mean absolute byte differences at most 0.53/255, with at most 0.001% of channel values differing by more than 32. Raster and GPU antialiasing are not bit-identical. Paired PNGs should still be visually inspected; numerical similarity alone does not establish readability.

The Release build (zero warnings/errors), all 351 regressions, native GPU comparison, actual SteamVR publication/failure fallback, and the 13-update numpad and 81-update shortcut resize checks passed during implementation. Animated and settled VR previews plus the desktop preview were visually inspected. The transparent integration checks also observed cold-start spikes in drawing/publication, so warm medians should not be applied to startup.

Remaining acceptance: headset legibility and controller feel; representative VRChat CPU/GPU contention; input-to-display latency; longer-session GPU memory behavior; other vendors/hybrid adapters and device loss. Settings/handles remain rasterized, and synchronization/input-loop isolation remain candidates for a later measured change. The initial [backend investigation](rendering-performance-investigation.md) records the alternative Direct2D/D3D11 route if OpenGL proves limiting.
