# Rendering performance investigation

Investigation date: 2026-09-21. This is the historical investigation of the CPU renderer, with independent [diagnostic probes](../experiments/GoBoard.RenderingProbe/README.md). The subsequent [GPU implementation](gpu-rendering.md) supersedes the descriptions of the current production path below; the measurements here describe the earlier CPU baseline.

## Recommendation

Prototype **Skia GPU rendering on the existing OpenGL context first**, while measuring input-loop delays and presentation separately. Keep the CPU path for PNGs, regression tests, and fallback. The installed SkiaSharp 4.152.1/OpenTK dependencies successfully created and drew into a texture-backed Skia surface on this machine's NVIDIA GeForce RTX 3090 Ti (GL 3.3, NVIDIA 610.47). The probe verified two colors through readback; it did not publish a SteamVR overlay or benchmark the complete GPU renderer.

Direct2D/DirectWrite on D3D11 is the strongest alternative to investigate if OpenGL sharing/synchronization remains a bottleneck. There is currently no measured comparison establishing that it is faster for GoBoard. Replacing Skia is a larger project than changing Skia's render target.

GPU rendering should reduce CPU raster work and full-image transfers. It cannot guarantee responsiveness when VRChat and SteamVR already saturate the GPU. Keeping input responsive during rendering stalls is a separate architectural requirement.

## Current costs and likely causes

The main path is:

```text
Poll input → update keyboard → CPU Skia bitmap → full GL upload
           → GL.Finish → SetOverlayTexture → WaitFrameSync → next input poll
```

Relevant code:

- `AnimatedKeyboardRenderer.Render` creates an output `SKBitmap` for each changed/animated frame and composites the cached baseline, character transitions, and pointer effects into it on the CPU.
- `Panel.Render` creates full-resolution CPU bitmaps. State changes rebuild the baseline; character changes can also rebuild a legend-free surface and per-character raster layers. Desktop baselines still originate at VR raster scale before being resized.
- `OverlayGraphics.Upload` uploads every pixel with `TexSubImage2D`, calls `GL.Finish`, then publishes the texture. Its two persistent GL textures prevent frame replacement/lifetime problems; they do not make CPU rasterization or GPU waits asynchronous.
- `KeyboardOverlay.EndFrame` renders synchronously on the event-processing loop. Settings and shortcut rendering also run in that loop; an expanded shortcut panel can render before main-keyboard event draining. A stall can postpone the next input batch, repeat, or release handling even though effects do not intentionally schedule input.
- Desktop rendering runs on the WinForms UI thread. The 16 ms timer is not a guaranteed deadline under load.

`GL.Finish` waits for earlier GL work to complete, so its duration includes waiting rather than just CPU execution. Time raster work, upload, finish, and submission independently before deciding which dominates. [Microsoft's GL reference](https://learn.microsoft.com/en-us/windows/win32/opengl/glfinish) documents this behavior.

The live main texture is **3138×846**, including fixed space for the optional numpad. Each RGBA upload is 10,618,992 bytes (10.13 MiB). Uploading at 90 Hz would transfer about **956 MB/s** of pixel payload, excluding intermediate CPU copies and driver/compositor work. This is a calculated upper workload for continuously changing frames, not measured throughput. Changing the saved physical size does not reduce the fixed texture dimensions.

Existing optimizations should remain: idle frames return null and skip uploads; hidden keyboards skip rendering; soft key backgrounds are cached; pointer-only motion can reuse the baseline; desktop uses native-sized BGRA output and borrows pixels for GDI instead of cloning them. Reimplementing those would not address the remaining costs.

## Measurements

Release, .NET SDK 10.0.101, SkiaSharp 4.152.1, Windows x64. CPU environment reports Intel family 6/model 183 and 32 logical processors. These are exploratory measurements on the active development machine, without a controlled CPU/GPU load sweep. Background load was not held constant; the first benchmark batch briefly overlapped regression testing, and the end of the format batch overlapped a probe build. Treat the numbers as evidence of expensive paths, not stable speedup estimates or measured display latency.

The existing `--effects-benchmark` uses 2550×846, 30 warmup frames and 120 synthetic frame steps per scenario. It uses historical benchmark settings (including 300 ms character transitions), not the current defaults, changes Shift periodically, and measures only calls returning a frame. Selected results:

| Scenario | Soft median / p95, ms | Flat median / p95, ms |
| --- | ---: | ---: |
| Afterglow | 11.95 / 26.58 | 14.00 / 26.83 |
| Spotlight | 14.41 / 29.90 | 18.43 / 35.04 |
| Lift character transitions | 41.66 / 78.81 | 39.20 / 68.55 |
| All benchmark effects + Lift | 44.82 / 81.65 | 43.06 / 71.13 |

At 90 Hz the entire refresh interval is 11.11 ms. These renderer-only observations already exceed that interval in several scenarios, before upload, submission, or compositor latency. They do not reproduce the user's exact live settings or establish the cause of a particular lag episode.

The separate settled-render benchmark measured Soft at 13.05 ms median / 15.05 ms p95 and Flat at 9.90 / 11.88 ms. The desktop effects benchmark's existing direct BGRA wrap plus unscaled GDI paint measured only 0.35 ms median; its Soft Lift scenario including rendering/painting measured 13.69 ms median / 83.99 ms p95. CPU character/state work merits more attention than the already-optimized desktop bitmap wrapping.

Two additional probes narrow the candidates:

1. **Image copies:** `SKImage.FromBitmap` on a mutable 3138×846 bitmap measured 1.77 ms median / 2.61 ms p95. Marking the completed bitmap immutable measured 0.0001 / 0.0005 ms, effectively negligible at this measurement scale. Skia explicitly permits sharing immutable bitmap storage. This is a copy microbenchmark, not an equivalent reduction in every frame. [Skia's image creation contract](https://api.skia.org/namespaceSkImages.html) explains the ownership condition.
2. **Output alpha:** rerunning the historical effects benchmark at 3138×846 with premultiplied output reduced pointer-effect medians in several scenarios, while transition/state-change spikes remained large and variable. For example, Soft Afterglow measured 14.82 ms with straight alpha and 7.02 ms with premultiplied alpha. The probe stretches the base keyboard to the output size instead of using live centered bounds, and leaves CPU baselines in their current format. This is a format sensitivity result, not a live-mode A/B comparison or evidence of a twofold application speedup.

Raw output is saved under the probe's [results directory](../experiments/GoBoard.RenderingProbe/results/2026-09-21/). Reproduction commands and limitations are in its README. No deliberate system stress was introduced, and no runtime settings were changed.

## Optimizations worth implementing

In priority order:

1. **Measure the whole loop.** Add opt-in aggregated timings for input-event age, input handling, baseline rebuilds, steady animation, upload, GPU wait, submission, and frame sync. Separate changed frames from idle calls and cold cache frames from warm ones. Report p50/p95/p99, frame count, render size, theme, enabled effects, process CPU time, and allocation/GC observations. Do not record keys, legends, text, or per-event logs. Event age is not input-to-photon latency.
2. **Remove avoidable image copies.** Mark owned, finished temporary bitmaps immutable before converting them into cached images in the animated baseline/output cache, character layers, interrupted transitions, and key-surface cache. Do not mark a bitmap that will be drawn into again or borrow memory beyond its owner's lifetime. Existing pixel/retained-frame regression coverage should accompany the change.
3. **Make state changes incremental.** Reuse geometry, fonts, and unchanged label images; compare labels before rasterizing replacements. Cache or repaint changed key regions instead of rebuilding whole baseline/surface images for a press/release. Keep full invalidation for layout/theme/geometry changes. GPU-backed caches later make this work useful on both paths.
4. **Evaluate premultiplied rendering consistently.** Use premultiplied surfaces throughout compositing and set OpenVR's `IsPremultiplied` flag only on overlays actually submitted in that format. The flag exists in the vendored binding. Check rounded transparency, antialiasing, glow, and handles; changing alpha metadata alone changes colors and is incorrect. [Valve's header](https://raw.githubusercontent.com/ValveSoftware/openvr/master/headers/openvr.h) defines this flag.
5. **Reuse output storage with explicit ownership.** A leased buffer/surface pair can remove large native allocations, but the present API transfers each returned bitmap to the caller. Desktop retains those pixels for painting. Reusing them prematurely would corrupt frames; preserve the retained-frame tests.

Reducing raster scale from 3 to 2 would cut pixel area by about 56%, but would also change label sampling. Treat that as a later quality/performance option requiring headset inspection. Preserve fixed per-overlay texture dimensions and pointer conversion; do not silently change hit targets or live GL texture dimensions. Partial texture uploads likewise need both alternating textures kept coherent.

## Backend comparison

These rankings are engineering judgments from the current architecture and documented APIs, not measured backend rankings.

| Candidate | What it moves off the CPU | Fit and remaining work | Recommendation |
| --- | --- | --- | --- |
| Skia Ganesh + OpenGL | Rasterization/compositing into GPU surfaces; removes final CPU frame/upload when submitted directly | Reuses drawing commands and the existing GL/OpenVR path. Needs persistent GPU surfaces, GPU-aware caches, alpha/origin handling and synchronization | First prototype |
| Direct2D/DirectWrite + D3D11 | Hardware 2D drawing into D3D textures | Natural Windows texture path; requires rewriting Skia paths, text, shadows, clipping, effects, and image tests, plus device-loss handling | Main alternative if GL remains limiting |
| Skia Vulkan | Retains Skia drawing with Vulkan GPU targets | More device/queue/image/synchronization integration; native availability must be tested on supported machines | Defer pending evidence |
| Skia Direct3D | Retains Skia with a Direct3D GPU context | Installed managed API exposes `CreateDirect3D`; this alone does not prove packaged native support or a D3D11-compatible submission route | Separate capability spike before committing |
| Custom GL/D3D shaders and glyph atlas | Very specialized drawing/effects | Rebuilds text/layout rendering and clipping infrastructure; larger maintenance and visual-parity burden | Only for a measured residual hotspot |

Skia documents both raster and GPU surfaces using the same canvas API, and its bindings expose GL/Vulkan/Direct3D context factories. [Skia canvas creation](https://skia.org/docs/user/api/skcanvas_creation/), [SkiaSharp context implementation](https://raw.githubusercontent.com/mono/SkiaSharp/main/binding/SkiaSharp/GRContext.cs). The shipped package's managed XML was also checked; only OpenGL received a native capability test here.

Microsoft documents Direct2D drawing into DXGI/D3D surfaces and creating D3D11-backed device contexts. This establishes feasibility, not an automatic speed advantage over Skia GPU rendering. [Direct2D/D3D interoperability](https://learn.microsoft.com/en-us/windows/win32/direct2d/direct2d-and-direct3d-interoperation-overview), [device contexts](https://learn.microsoft.com/en-us/windows/win32/direct2d/devices-and-device-contexts).

## Shape of the first GPU prototype

- Separate drawing from bitmap allocation: draw the keyboard and animations into an `SKCanvas` supplied by the host. Keep geometry/state in Core, drawing in Presentation, and GPU/OpenVR lifetime in the VR host.
- Wrap the existing persistent back textures/framebuffers with Skia GPU surfaces. Render directly into the back surface and submit its texture without `ReadPixels`, PNG encoding, or another CPU bitmap. Initially cached CPU assets may upload when changed; removing those state-change uploads is a second stage.
- Keep two persistent textures, fixed dimensions, gamma behavior and U 0→1/V 1→0. Explicitly reconcile Skia surface origin with GL sampling so the V flip occurs once. Reset Skia's cached GL state after raw GL/OpenVR operations as needed; `SetOverlayTexture` may modify GL texture binding.
- Resolve alpha consistently. GPU render targets should use supported premultiplied formats with matching OpenVR composition flags. Verify both themes, ISO Enter, notices, transitions, numpad padding, shortcuts, settings and handles.
- Preserve resources through overlay destruction and OpenVR shutdown. Handle failed context creation/device loss with a controlled fallback and unchanged input cleanup.

Do not simply delete `GL.Finish` or replace it with `Flush`: issuing work does not establish cross-process readiness. First establish the required producer/compositor synchronization. A fence-based design can keep the last completed frame visible while work is pending, but a producer fence alone does not establish when SteamVR has finished consuming a texture. Texture reuse must remain safe.

If waits still hurt input responsiveness, isolate rendering behind **immutable snapshots and a bounded latest-frame slot**, with one owner of the input model and one owner of the GPU context. Never let a worker render the mutable `KeyboardState` while input changes it. Reject obsolete output after cancellation/layout/geometry changes. Dropping obsolete visual frames is acceptable; dropping input releases is not. Avoid queues that accumulate animation latency under load.

Desktop needs a GPU presentation target too. GPU drawing followed by readback into the current GDI bitmap would retain a transfer/stall. Evaluate a GPU window/swap chain while preserving non-activation, DPI, resizing and transparent companion controls. Native composition is another route for transparent surfaces. [Microsoft's composition initialization example](https://learn.microsoft.com/en-us/windows/win32/directcomp/initialize-directcomposition) describes connecting a D3D device and composition target to a window.

## Validation and decision gate

Completed: locked production restore, Release solution build (zero warnings/errors), all **349 regression tests**, existing renderer/desktop/effects benchmarks, copy/format probes, and a native Skia/OpenGL capability smoke check. No production rendering or visual behavior changed, so README screenshot regeneration was not needed.

Before adopting any renderer, benchmark current CPU, optimized CPU and GPU candidates sequentially with identical live dimensions/content bounds, current defaults, both themes, two pointers, numpad/shortcuts, press/release and repeated modifier transitions. Separate CPU pressure, GPU pressure and combined load. Include cold starts, idle/hidden CPU use, device/context failure and shutdown. Aggregate GPU timestamps asynchronously; CPU draw-call duration alone can hide queued GPU work.

Require lower p95/p99 input-processing and rendering delays without worsening SteamVR frame timing, increasing queue depth, or introducing visual/input regressions. Keep a repeatable CPU fallback for comparison. The remaining evidence must include actual SteamVR submission, GPU completion, in-headset legibility/controller feel, and end-to-end latency under representative VRChat load. None of those is established by the capability smoke test or raster benchmarks.
