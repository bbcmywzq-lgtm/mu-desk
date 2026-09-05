# Cue GPU rendering and camera validation — 2026-09-05

## Decision

Use Windows Graphics Capture, D3D11 and DirectComposition for the local lens.
Captured pixels remain on the GPU; capture and presentation are independent of
the WPF UI dispatcher. Keep one newest pending frame, reuse a desktop texture,
and use a two-buffer composition swap chain with maximum frame latency one.
The shader performs aspect-correct magnification, clipping and border rendering.
On supported systems, request a 2 ms minimum capture interval. Older Windows
uses its available capture cadence; no promise of a fixed refresh rate is made.

Full-screen production focus continues to use Windows' native transform, now
driven by a dedicated DWM-paced thread and a shared scale/translation critically
damped camera. Initial focus anchors the pointed content at its existing screen
position. After entrance settles, edge following becomes active. Target changes
preserve position and velocity; release converges to identity. Native API startup
time is excluded from animation elapsed time. Reset and disposal restore identity.

The GPU full-screen candidate is available ONLY in the diagnostic executable via
`--gpu-focus`. Its visual path works, but it does not implement transformed mouse
input. It is deliberately not a selectable production mode: raw click-through
alone cannot make enlarged targets receive correctly mapped input. Further work
on that candidate must test click/drag/scroll mapping before adoption.

## Evidence

System query: active Intel display reported 2560 x 1600 at 165 Hz. Tests executed
locally with a WPF moving-stripe source window and the existing desktop workload.
These are short diagnostic samples, not a controlled hardware benchmark.

| Probe | Result |
| --- | --- |
| Legacy 16 ms UI-thread GDI capture, 6.014 s | 226 updates, about 37.6/s; interval median 29.747 ms, P95 38.106 ms; work median 7.183 ms |
| GPU lens + native focus, 8.271 s | 1127 captured frames (about 136.3/s), 1327 presentation submissions |
| Same GPU run submission timing | Median 5.717 ms, P95 11.613 ms, max 22.783 ms |
| Newly acquired frame age at processing | Median 2.894 ms, P95 6.521 ms |
| GPU draw submission CPU time | Median 0.409 ms, P95 0.709 ms |
| Native focus rapid release/retrigger/wheel sequence | 177 measured intervals; median 7.278 ms, P95 10.855 ms; no API errors |
| Native restoration readback | Successful MagGetFullscreenTransform: zoom=1, x=0, y=0 |
| Native lens hit testing | WindowFromPoint at pointer did not return the lens window |
| Shader output check | Explicit one-frame GPU readback inspected: circular clipped magnified content with transparent exterior |
| GPU full-screen visual-only candidate, 6.062 s | 773 captured frames, 1027 submissions; median interval 5.345 ms; no reported backend failure |
| GPU lens sustained 30.059 s | 4463 captured frames (148.5/s), 4815 submissions; recent interval median 5.870 ms, P95 10.822 ms, max 23.944 ms; no reported backend failure; click-through check passed |
| Module lifecycle probe | Start, pause (GPU disposed), resume (new GPU instance), reset, stop passed with no reported errors |
| Published startup | `artifacts/PersonalToolbox-win-x64-cue-gpu/PersonalToolbox.exe` started and responded with main title MU Desk; old host exited and native identity transform was explicitly verified before replacement |

Frame submissions are NOT distinct displayed frames, and frame age at processing
is NOT end-to-end input-to-photon latency. The legacy probe omits final image
drawing, so it is a conservative capture-path comparison. Snapshot runs introduce
a deliberate GPU readback and PNG encoding stall and are excluded from timing
claims. Static desktop content need not produce new capture frames.

Automated camera tests cover frame-rate independence (30/60/165), irregular time
steps, anchored content, preservation of position/velocity on interruption, and
convergence to identity. Existing MU Desk build and regression checks passed.

## Reproduction

Run with the bundled SDK from the repository root:

```powershell
.\work\dotnet-sdk\dotnet.exe run --project src\Cue.Diagnostics -c Release -- --legacy
.\work\dotnet-sdk\dotnet.exe run --project src\Cue.Diagnostics -c Release -- --focus
.\work\dotnet-sdk\dotnet.exe run --project src\Cue.Diagnostics -c Release -- --soak
.\work\dotnet-sdk\dotnet.exe run --project src\Cue.Diagnostics -c Release -- --lifecycle
.\work\dotnet-sdk\dotnet.exe run --project src\Cue.Diagnostics -c Release -- --gpu-focus
.\work\dotnet-sdk\dotnet.exe run --project src\Cue.Diagnostics -c Release -- --snapshot
```

The last command writes `artifacts/cue-gpu-probe.png` for visual inspection. Only
the diagnostic path reads pixels back or writes an image. The focus probe briefly
changes desktop magnification and restores it before exit.

## Remaining validation

- Subjective hand-feel on the user's actual workflow and extended use.
- Mixed-DPI/multiple physical monitors, HDR, display disconnection and device loss.
- Recording/meeting application visibility of the new overlay.
- Native focus mouse interaction across target applications; automated coverage
  in this pass verifies transforms and reset, not injected clicks into user apps.
- Full GPU focus transformed input, before any production adoption.
