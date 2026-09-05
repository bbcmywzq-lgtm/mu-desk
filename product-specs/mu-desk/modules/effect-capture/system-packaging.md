# 动态拾取系统包装

Parent SKU: `MU-DESK`  
Module ID: `effect-capture`  
Gate: System Packaging — pending review

## Identity and entry

- User-facing product identity: MU Desk.
- Module display name: 动态拾取.
- Window title: `动态拾取 · MU Desk`.
- Executable: no module executable; capture and packaging run inside the MU Desk process.
- Taskbar/application icon: MU Desk product icon.
- In-window/module icon: approved “selection corners + sequential frames” module direction.
- Entry points:
  - MU Desk module card: `开始拾取`.
  - One direct MU Desk tray item: `动态拾取…`.
  - One assignable Quick Ring action: `动态拾取`.
- A module-specific global hotkey is supported as an optional setting but is unassigned by default to avoid collisions with Windows screenshot, Xbox recording, and third-party capture tools.
- Invoking any entry while selection, recording, or processing is active activates the existing operation instead of starting a second capture.

## Startup, process, and background behavior

- No module-specific startup registration. MU Desk owns the only startup identity.
- No screen capture, encoder, frame analyzer, or Codex bridge runs while the module is idle.
- The selection overlay and completion window are created on demand.
- Only one capture/encoding operation may run at a time.
- No Codex child process or background send is created. The completion action only launches a documented Codex deep link.
- Exiting MU Desk while capture is active shows a concrete confirmation. Confirmed exit interrupts the local operation where possible and preserves every completed package.

## Settings ownership

Dynamic Capture settings live in the shared MU Desk settings model and unified settings center. There is no standalone settings window or settings file.

Settings:

- Default duration: `2 seconds`; choices are 2, 5, and 7 seconds.
- Countdown: enabled by default; compact 3–2–1 sequence.
- Capture frame cadence: automatic high-detail mode suitable for short UI motion; no advanced codec panel in the first version.
- Output root: default `%LocalAppData%\MU Desk\effect-capture\captures`; the user may choose another local folder.
- Quick Ring assignment: optional.
- Global hotkey: optional and unassigned by default; conflicts must be rejected before saving.
- Reduced-motion behavior follows Windows and is not duplicated as a private toggle.

Do not persist the last screen rectangle, captured window title, pointer location, or the text contents visible inside the selection.

## Local data and package ownership

Default root:

`%LocalAppData%\MU Desk\effect-capture`

Structure:

```text
effect-capture/
├── captures/
│   └── yyyy-MM-dd/
│       └── yyyyMMdd-HHmmss-fff/
│           ├── source.mp4
│           ├── contact-sheet.png
│           ├── frames/
│           ├── effect.md
│           └── send-state.json
├── temp/
└── logs/
```

- A package is written to a unique temporary directory and atomically renamed into `captures` only after the required files are valid.
- Completed capture packages are user data and are never automatically deleted by cleanup, reset, upgrade, or uninstall logic.
- Temporary directories older than 24 hours may be deleted on module startup only when they are positively identified as incomplete Dynamic Capture workspaces.
- `send-state.json` remains a local package-format marker for compatibility. The deep-link handoff does not store a Codex task or turn identifier.
- Changing the output root affects new captures only and never moves prior packages automatically.

## Codex integration

- Integration protocol: official `codex://new` desktop deep link.
- Query parameter `path` sets the completed capture package as the new local task workspace.
- Query parameter `prompt` preloads a concise request to inspect `effect.md`, `contact-sheet.png`, `frames/`, and, when useful, `source.mp4`.
- The deep link opens a visible Codex composer and does not automatically submit it. The user reviews and sends from Codex.
- MU Desk does not discover or launch `codex.exe`, authenticate, create a hidden task, retain a bridge process, or store a task/turn identifier.
- Codex owns authentication, model access, file inspection, network behavior, and the created task after the handoff.
- Reopening from the result window starts another prefilled composer; the UI labels this explicitly as `再次打开 Codex` so it is not mistaken for a retry of a known task.

## Privacy and consent

- Capture, encoding, key-frame selection, contact-sheet generation, and package writing occur locally.
- No network request is made merely by opening the module or recording.
- The completion panel states the exact number of key frames and that Codex will open with the local package as its workspace.
- Clicking `在 Codex 中打开` performs only the local application handoff. The visible Send action inside Codex is the network-submission boundary.
- On first use, show a concise disclosure that selected frames may contain private on-screen content. Do not repeat a modal confirmation after the user has acknowledged it unless the send scope changes.
- The user may inspect the local folder before sending.
- Diagnostics never include image bytes, OCR text, prompt contents, captured window titles, or credential material.
- Ordinary filenames in diagnostic records are reduced to package IDs or relative technical paths.

## Tray and notifications

MU Desk remains the only tray icon. Add one command without creating a nested module tray:

1. Open MU Desk.
2. Dynamic Capture…
3. Existing module commands.
4. Pause/restore all tools.
5. Exit MU Desk.

- Capture completion does not show a balloon while the completion panel is visible.
- There is no active Codex send for MU Desk to monitor and no Codex-completion tray notification.
- Pausing all background tools does not cancel a capture already in progress; it prevents new Quick Ring/global-hotkey entry until resumed. The explicit tray or module-card entry remains available.

## Feedback and errors

- Capture/encoding/package failures remain visible in the module and are forwarded to MU Desk's unified error channel.
- Codex handoff errors distinguish whether the `codex://` protocol could be opened; authentication, model, service, and submission errors remain visible and owned by Codex.
- Never replace a capture/package failure with demo frames or an empty successful package.
- Error copy names the failed stage and a concrete next action such as `安装或启动 Codex` or `打开本地素材`.
- Ordinary success stays in the completion panel. Tray balloons are reserved for hidden-window completion/failure and blocking host errors.

## Recovery, reset, and diagnostics

- Local package completion is independent from Codex delivery; a send failure never deletes or corrupts the capture.
- There is no automatic send retry or hidden duplicate-task recovery; `再次打开 Codex` visibly creates another composer.
- A package missing required files is marked incomplete and cannot be sent until reprocessed or recaptured.
- Resetting module settings restores 2-second duration, countdown enabled, default output root, no global hotkey, and no Quick Ring assignment.
- Reset never deletes completed captures, Codex tasks, or Codex authentication.
- `打开保存位置` and `打开日志位置` are recovery actions in unified settings/diagnostics.
- Logs contain only local stage, duration, dimensions, frame count, package ID, handoff result, and sanitized errors.

## About, versioning, and distribution

- No separate About page, creator credit, publisher, version, installer, updater, privacy page, or release channel.
- Version and release notes inherit MU Desk semantic versioning.
- Publisher and creator credit appear only in MU Desk About and package metadata.
- Publish only inside the MU Desk Windows x64 package.
- The module requires the same supported Windows baseline as MU Desk and documents the separately discoverable official Codex dependency for the Send action.
- Recording and local packaging remain usable when Codex is absent; only `在 Codex 中打开` is unavailable.

## System Packaging acceptance

- One MU Desk process, tray icon, startup entry, settings owner, version, and installer.
- On-demand capture with no idle recording or encoding worker.
- Homepage, tray, and Quick Ring entry; optional global hotkey defaults to unassigned.
- Completed packages are local user data and are never automatically deleted.
- Explicit handoff opens a visible new Codex composer with the package workspace and prefilled prompt.
- Codex-managed authentication is used; MU Desk stores no credentials or API key.
- MU Desk performs no hidden task creation, automatic submission, upload, or result monitoring.
- The user reviews and sends from Codex; all package files remain locally inspectable.
- No hidden standalone executable, independent tray, updater, or product identity is introduced.
