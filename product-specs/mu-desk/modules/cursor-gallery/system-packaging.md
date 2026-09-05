# 光标画廊系统包装

Parent SKU: `MU-DESK`  
Module ID: `cursor-gallery`  
Gate: System Packaging — accepted 2026-08-22

## Identity and entry

- User-facing product identity: MU Desk.
- Module display name: 光标画廊.
- Window title: `光标画廊 · MU Desk`.
- Executable: no module executable; run inside the MU Desk process.
- Taskbar/application icon: MU Desk product icon.
- In-window module icon: approved Cursor Gallery module icon.
- Entry points:
  - MU Desk module card.
  - One direct item “光标画廊…” in the MU Desk tray menu.
- Reopening an already visible gallery activates the same window rather than creating another instance.

## Startup and background behavior

- No module-specific startup registration.
- MU Desk owns the only startup item.
- The gallery window is created on demand.
- Closing the gallery releases its window and leaves no WebView or child process.
- Module library access does not require a continuously running worker.

## Data ownership and migration

Target metadata location:

`%LocalAppData%\MU Desk\cursor-gallery\library.json`

Compatibility source:

`%AppData%\CursorSkinManager\library.json`

First-use migration:

1. If the MU Desk target exists, use it.
2. Otherwise, if the legacy library exists, copy it to the target atomically.
3. Do not move or delete the legacy library or any cursor files.
4. Preserve absolute cursor and preview paths from the legacy library.
5. Before changing applied-state metadata, write `library.bak.json` beside the MU Desk copy.
6. If migration fails, keep the legacy library readable and show the real failure; do not create an empty successful state.

MU Desk becomes the writer after migration. The legacy application is a compatibility source, not a second synchronized editor.

## Windows integration

- Apply and reset affect only the current user under `HKCU\Control Panel\Cursors`.
- No administrator elevation is requested.
- Apply writes existing cursor roles, falls back to Windows Aero for missing roles, refreshes Windows cursors, and updates local applied state.
- Reset restores the Windows Aero/default values and refreshes the gallery state.
- The module does not change system-wide or other-user settings.

## Feedback and errors

- Apply/reset success appears in the gallery feedback region.
- File, JSON, registry, and Windows-refresh failures remain visible in the gallery and are also forwarded to MU Desk's unified error channel.
- Do not use successful-looking fallback data after a failure.
- Do not show a tray balloon for ordinary apply/reset success.
- Blocking errors use title `MU Desk · 光标画廊`.

## Recovery

- Primary recovery: “恢复 Windows 默认”.
- Metadata recovery: `library.bak.json`.
- Legacy source remains untouched and can be reread if the new target has not been established.
- The package does not delete cursor files, legacy metadata, or registry values outside the declared current-user cursor keys.

## Settings, About, version, and distribution

- No separate module settings page; the gallery window owns its relevant interaction.
- No separate About page, publisher metadata, version, installer, or update channel.
- Version and release notes inherit MU Desk.
- Publish only inside the MU Desk Windows x64 package.
- Creator credit appears only in MU Desk About/release metadata.

## Legacy cleanup

- Remove or ignore legacy startup entries for `CursorSkinManager`, `Cursor Gallery`, and `com.cursor-skin-manager.desktop`.
- Do not remove the legacy library directory automatically.
- Do not expose the old Tauri/WebView launcher in ordinary navigation or the tray.

## Packaging acceptance

- One MU Desk process and one gallery window.
- One tray entry, no independent tray icon.
- One MU Desk startup entry, no independent startup value.
- Legacy 17-skin library migrates without data loss.
- Apply/reset affect only the current user.
- Closing the window leaves no child process.
- Errors remain visible and recoverable.
- No separate product branding or distribution artifact is created.
