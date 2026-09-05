# Maple hand — heavier contour, revision 2

2026-09-05: User requested another weight increase. Same deterministic inward contour filter as ../bold-code; radius increased from 1.2% to 2.4% of occupied height, still sourced from the immutable thin CUR and preview. Exact alpha and all six native cursor dimensions/hotspots preserved. Six native Windows LoadImage/GetIconInfo checks passed; light/dark size-check.png visually inspected.

Build: `work/dotnet-sdk/dotnet.exe run --project src/Cue.Diagnostics -c Release -- --maple-hand --bold`.

Installed only Maple Link.cur and Link-minimal-v4.png, with only Hand.previewPath changed in library.json. Live Hand refresh succeeded; other roles unchanged. Previous revision backed up to `C:/Users/bbcmy/AppData/Local/MU Desk/cursor-gallery/backups/maple-link-20260905-214255-647`. Previous v3 preview remains in place. No UI automation or scheme switch performed. Reopen Tip to refresh its cached preview.
