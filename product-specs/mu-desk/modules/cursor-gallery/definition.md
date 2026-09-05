# 光标画廊模块定义

Parent SKU: `MU-DESK`  
Module ID: `cursor-gallery`  
Gate: Product Definition — accepted 2026-08-22

## Product role

光标画廊是 MU Desk 内部按需模块。用户从 MU Desk 首页或统一托盘进入；窗口关闭后不保留独立后台进程，也不建立独立品牌、托盘或开机启动项。

## User and dominant task

- User: 希望快速切换本机 Windows 光标外观的个人用户。
- Dominant task: 浏览本地光标皮肤，确认内容后应用一套皮肤，必要时恢复 Windows 默认。

## Existing data

- Reuse: `%AppData%\CursorSkinManager\library.json`.
- Library: 当前已有 17 套皮肤及各光标角色预览。
- Apply target: `HKCU\Control Panel\Cursors`.
- Recovery: Windows Aero values or built-in cursor paths.
- Library backup: applying a skin preserves `.bak.json` before updating applied state.

## Visible content

- Collection: skin preview, name, available cursor count, completeness, current-applied state.
- Detail: selected skin's cursor-role previews, missing-role state, import note when present.
- Persistent context: current Windows cursor scheme and local-library source.

## Actions

- Apply selected skin.
- Restore Windows default.
- Return to MU Desk.

## States

- Loading library.
- Populated library.
- Empty or missing legacy library.
- Selected skin.
- Currently applied skin.
- Apply/reset success.
- Registry, file, or Windows refresh failure.

## Explicit exclusions

- Online store, discovery, download, account, or cloud sync.
- Importing, editing, deleting, packaging, or authoring cursor skins.
- A standalone user-facing executable, tray icon, startup item, or background process.
- Changes to the existing cursor file format or registry behavior unless a verified compatibility problem requires it.

## Preservation requirements

- Keep all 17 existing skin packages and previews reachable.
- Keep apply and restore-default behavior.
- Preserve legacy library compatibility and the applied-state backup.
- Surface real errors; do not substitute demo data.
- Closing the window releases the module UI without leaving WebView or another process.
