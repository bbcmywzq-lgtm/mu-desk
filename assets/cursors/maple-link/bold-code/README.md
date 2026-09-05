# Maple hand — precise contour weight

Approved code-based refinement on 2026-09-05. Source: the accepted ../Maple-Link-Minimal.cur and ../preview.png, not the rejected image-generation trials in ../bold.

The existing dark contour is expanded inward using a 32-direction fractional-radius minimum filter with bilinear sampling. Radius is 1.2% of occupied hand height. Every alpha byte is preserved through PNG round-trip, leaving the exact external silhouette, dimensions and all six original hotspot coordinates unchanged. No runtime filter is added; the application loads the finished CUR normally.

Build from repository root: `work/dotnet-sdk/dotnet.exe run --project src/Cue.Diagnostics -c Release -- --maple-hand --bold`.

Verified native Windows CUR loading and hotspot coordinates for 26/39/52/77/103/205 px; visual QA on light/dark backgrounds in size-check.png. Gallery preview is 221 px with unchanged placement.

Installed Link.cur and fresh Link-minimal-v3.png; only Maple Hand previewPath changed in the library. Other roles unchanged; live Hand refresh succeeded without switching schemes. Previous thin hand, v2 preview and library snapshot backed up at `C:/Users/bbcmy/AppData/Local/MU Desk/cursor-gallery/backups/maple-link-20260905-214031-939`.

To restore the previous thin version, restore only Link.cur from that backup and change only Maple Hand previewPath back to the retained Link-minimal-v2.png. Do not restore the whole library snapshot over newer unrelated changes.
