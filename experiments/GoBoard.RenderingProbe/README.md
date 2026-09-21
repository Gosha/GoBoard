# Rendering capability and cost probes

Investigation tools, separate from the production solution and executable. See [findings and proposed work](../../docs/rendering-performance-investigation.md).

Run from the repository root in PowerShell with the SDK selected by `global.json`:

```powershell
dotnet restore experiments/GoBoard.RenderingProbe/GoBoard.RenderingProbe.csproj --locked-mode
dotnet build experiments/GoBoard.RenderingProbe/GoBoard.RenderingProbe.csproj -c Release --no-restore
dotnet run --project experiments/GoBoard.RenderingProbe -c Release --no-build -- gpu
dotnet run --project experiments/GoBoard.RenderingProbe -c Release --no-build -- copies
dotnet run --project experiments/GoBoard.RenderingProbe -c Release --no-build -- straight
dotnet run --project experiments/GoBoard.RenderingProbe -c Release --no-build -- premul
```

- `gpu` creates a hidden, unfocused OpenGL 3.3 context with the same context settings as production, creates a texture-backed Skia surface, draws two colors, and verifies them through a diagnostic readback. It reports the actual GL renderer. This verifies the shipped libraries can create a GPU surface on this machine; it is not a rendering benchmark or an OpenVR integration check. Readback is for verification only, not a proposed live presentation path.
- `copies` compares `SKImage.FromBitmap` for mutable and immutable 3138×846 RGBA bitmaps. It measures image creation, excludes image disposal, warms up 20 times and measures 200 times. This isolates copying; it does not measure a complete keyboard frame.
- `straight` / `premul` invoke the existing production effects benchmark through reflection with different output alpha formats. Reflection occurs once outside frame timing. This avoids exposing application internals or duplicating the renderer. Each scenario warms up 30 frames and measures 120 simulated frames at 90 Hz. Timings include only calls that return a new frame. The benchmark uses its historical effect settings, not user defaults, and shifts the labels periodically.

The format probes use the live main texture's current dimensions (3138×846), but stretch the base keyboard across that surface. They do **not** reproduce the live content bounds, numpad, expanded shortcuts, dual pointers, or real scheduling. Use them only as a format sensitivity check. Geometry constants must be revisited if the live texture dimensions change. Their existing benchmark header says "Excludes presentation/upload" because no GPU upload is performed.

These commands never initialize OpenVR, publish overlays, read saved settings, or inject input. Output goes to stdout; use `Tee-Object .runtime/<name>.txt` to save it. Run scenarios sequentially. Vary their order and repeat under recorded CPU/GPU load before drawing performance conclusions; the initial exploratory results are not a controlled load comparison.
