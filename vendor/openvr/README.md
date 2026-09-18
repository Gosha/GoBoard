# OpenVR SDK subset

Unmodified files from Valve's [OpenVR v2.15.6](https://github.com/ValveSoftware/openvr/tree/v2.15.6):

- `headers/openvr_api.cs`
- `bin/win64/openvr_api.dll`
- `LICENSE`

Requires a SteamVR runtime supporting this SDK's interfaces. Keep the native library and generated binding from the same SDK release. `MinimalControlBar` suppresses dashboard controls; it is not a standalone grab-handle API. Neither that flag nor `EnableControlBar` has produced a native grab handle for this POC's independent overlay.

SHA-256:

```text
openvr_api.cs  C17E878B7B3B925D1F22EF5382561389C47DB8B92019DE840705FF5FF28C317A
openvr_api.dll BAB8AC6EF64E68A9CA53315B0014D131088584B2EFDFA6DB511D67EC03CFCB4A
```
