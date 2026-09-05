# Maple link-selection refinement

Requested change: simplify only Maple Cursor's link-selection hand, matching the rounded monochrome family. No inner finger-slot strokes, shaded wrist or extra decoration. Other roles and the user's chosen scheme are unchanged.

Artwork mode: built-in image generation (`image_gen`), using the old hand as edit target and the Maple arrow as style reference. Transparent generated master: `generated-hand.png`.

Final prompt: Redesign only the link-selection hand as a simple upright pointing index finger, small thumb and compact rounded palm. Imply remaining fingers through a smooth knuckle contour; no interior lines. Flat white fill and even black outline, rounded corners matching the Maple arrow. No shadow, glow, gradient, cuff, click rays, text, UI or color. Transparent background; fingertip is the highest point and click hotspot; readable at small cursor sizes.

Build: `work/dotnet-sdk/dotnet.exe run --project src/Cue.Diagnostics -c Release -- --maple-hand` (run from workspace root, before installing, because original size reference is the existing Maple Link.cur).

Outputs: `Maple-Link-Minimal.cur` (26/39/52/77/103/205 px), `preview.png`, `size-check.png` (light/dark backgrounds) and individual frames. All six sizes loaded through Windows LoadImage and verified with GetIconInfo for fingertip hotspots. Packaging crops transparent margins and resizes the generated image; no manual redrawing substitutes the generated artwork.

Install: `assets/cursors/maple-link/install.ps1`. Makes a timestamped backup beneath MU Desk's cursor-gallery/backups; replaces only Link.cur and its referenced preview. Does not edit registry values. Refreshes only the live system Hand cursor, and only when the registry already points to that exact Maple asset.

2026-09-05 installation: the old preview was held by WPF's image cache, so a new sibling `Link-minimal-v2.png` was created and only Maple Hand's `previewPath` was changed in MU Desk's library.json. All other library fields/roles remain unchanged. The original Link.cur, preview.png and library.json are preserved in `%LocalAppData%/MU Desk/cursor-gallery/backups/maple-link-20260905-184704-612`. The later `184753-473` backup is a retry snapshot, not the original artwork. Installer reported `OtherRolesUnchanged=true`, `LiveHandUpdated=true`. The skin was not switched and no host rebuild/restart was needed.

Restore: use Link.cur from the original `184704-612` backup, and point only Maple Hand's previewPath back to the retained `Link.cur-e524c6197a53b693.centered-v1.png`, then reapply the same cursor scheme. Do not overwrite unrelated roles or replace the whole library with an old snapshot.
