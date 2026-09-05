# MU Desk acceptance

Active scope: File Shelf (`file-shelf`) addition  
Implementation authorized: yes — 2026-08-22T16:30:27.1552549+08:00  
Current gate: implemented and locally released

## Contract and implementation evidence — File Shelf

- [x] Product Definition accepted — user replied `接受 Gate A`.
- [x] Visual System accepted — user replied `b` at Gate B.
- [x] System Packaging accepted — user replied `c` at Gate C.
- [x] Implementation explicitly authorized — user replied `D` after reviewing `implementation-map.json`.
- [x] Core models cover same-drop batches, stable order, pin state, path kinds, 200-item cap and accepted metadata only.
- [x] `FileShelfStore` uses versioned JSON, temp write, atomic replacement, one backup, backup recovery and no demo entries.
- [x] WPF edge surface supports collapsed/expanded states, valid/invalid FileDrop feedback, Windows Shell type icons, missing paths, pin, remove, clear and one-session undo.
- [x] Drag-out advertises Copy/Link-compatible effects and removes only unpinned metadata after Windows returns a non-None result; source file operations are absent.
- [x] MU Desk homepage shows six tools in the existing two-column layout with File Shelf toggle, true count and Show/Collapse action.
- [x] One MU Desk-owned tray command, pause/resume, startup, shutdown, error and disposal paths are connected; there is no second process, taskbar identity, tray icon or startup item.
- [x] Icon library generation produced a traceable 3×3 source sheet, manifest, preview and QA report; all nine icons and 16–256 px exports passed validation with zero issues.
- [x] `2026-08-22 .\build-toolbox.ps1` completed with 0 warnings and 0 errors; Toolbox tests, MouseRing tests and DesktopOrganizer 21/21 checks passed.
- [x] `2026-08-22 .\publish-toolbox.ps1` updated the single local MU Desk package after closing only the exact old published process.
- [x] The published executable cold-started successfully and remained the sole `PersonalToolbox` process for that exact path.
- [x] Windows UI inspection confirmed “本机工具 · 6”, the integrated icon/card, `运行中`, `显示货架`, `0 项`, the right-edge collapsed tab, and the expanded empty shelf with no horizontal overflow.
- [x] The final UI state was returned to the collapsed edge tab. Empty startup did not create the File Shelf data directory.

Evidence:

- `modules/file-shelf/definition.md`
- `modules/file-shelf/visual-system.md`
- `modules/file-shelf/system-packaging.md`
- `implementation-map.json`
- `../../artifacts/file-shelf-icons-v1/manifest.json`
- `../../artifacts/file-shelf-icons-v1/qa-report.json`
- `../../artifacts/file-shelf-icons-v1/preview/contact-sheet.png`
- `../../artifacts/PersonalToolbox-win-x64/PersonalToolbox.exe`

## Historical active scope — Dynamic Capture

Historical scope:

- Cursor Gallery received a separate Gate D authorization on 2026-08-22T00:30:36.5201100+08:00.
- Its implementation map is preserved at `modules/cursor-gallery/implementation-map.snapshot.json`.
- That authorization does not authorize Dynamic Capture code, assets, live Codex sends, publishing, deletion, or release.

## Contract evidence — Dynamic Capture

- [x] Product Definition accepted — 2026-08-22T00:36:21.9238608+08:00
- [x] Visual System accepted — 2026-08-22T00:38:31.4092265+08:00
- [x] System Packaging accepted — 2026-08-22T00:43:52.4626452+08:00
- [x] Implementation map prepared and validator passed
- [x] Implementation explicitly authorized — user replied `D` after the Gate D prompt

Accepted contracts:

- `modules/effect-capture/definition.md`
- `modules/effect-capture/visual-system.md`
- `modules/effect-capture/system-packaging.md`
- `implementation-map.json`

## Implementation evidence — record after Gate D

### Build and package

- [x] `2026-08-22 .\build-toolbox.ps1` completed with 0 warnings and 0 errors after MU Desk reintegration.
- [x] `2026-08-22 .\publish-toolbox.ps1` updated `artifacts/PersonalToolbox-win-x64/PersonalToolbox.exe`; the published process cold-started and the second launch activated the single existing window.
- [x] Published artifact metadata reports `MU Desk` / version `0.1.0`; package contains the single shell executable plus its log directory, with no FFmpeg, credential material, or Dynamic Capture standalone executable.

### Core and compatibility tests

- [x] Toolbox core tests, MouseRing tests (including serialized action value 10), and DesktopOrganizer 21/21 checks passed after reintegration.
- [ ] Record settings migration, key-frame selector, package atomicity, send-state, JSON-RPC, sanitization, and hosted-action test results.
- [ ] Record one representative smoke path for every preserved MU Desk module.

### Native capture and package

- [ ] Record exact monitor, scaling, selection geometry, duration, logical/encoded dimensions, frame count, and package path for 2, 5, and 7 second checks.
- [ ] Verify overlays/HUD never appear in encoded pixels.
- [ ] Verify odd dimensions, high-speed overshoot retention, cancellation, cleanup, and repeated-run resource release.
- [ ] Verify source.mp4, contact sheet, ordered PNGs, effect.md, and send-state.json are valid.

### Codex integration

- [ ] Record fake app-server scenarios and results.
- [ ] Record Codex-missing, incompatible, signed-out, cancelled-login, service-error, and retry behavior.
- [x] A non-sensitive real send created thread `01a0255e-197c-7582-8cdd-add4af37ef15` and turn `01a0255e-1a5e-7c33-a520-1993bd29f643`; `send-state.json` retained the destination and the installed Codex task deep link opened it successfully.
- [ ] Verify submitted inputs are prompt plus ordered localImage frames, MP4 remains local by default, and no unrelated workspace is writable.

### Visual and accessibility

- [ ] Capture screenshots of selection, countdown, recording HUD, processing, completion, consent, sending, success, and failure states.
- [ ] Record keyboard, visible focus, non-color state cues, 200% scaling, multi-monitor DPI, reduced motion, and no-horizontal-overflow results.
- [ ] Record icon QA at 16, 20, 24, 32, 64, 128, and 256 px.
- [x] Published MU Desk homepage was inspected through Windows UI Automation and a live screenshot: “本机工具 · 4”, the Dynamic Capture card, icon, “开始拾取”, “最近任务”, “设置”, and `2–7 秒` were visible without horizontal overflow.

### System packaging and recovery

- [ ] Verify one process/tray/startup/settings/version/installer identity and zero idle capture/Codex workers.
- [ ] Verify tray, Quick Ring, optional hotkey, single-operation behavior, shutdown confirmation, and hidden-window notification behavior.
- [ ] Verify completed packages survive send failure, reset, output-root change, migration failure, and module removal.
- [ ] Record diagnostic logs and confirm they contain no captured media, prompts, OCR text, titles, API keys, tokens, cookies, or passwords.

## Final result

- Status: implementation in progress.
- Evidence owner: MU Desk Dynamic Capture implementation task.
- A passing build alone does not satisfy this checklist.

## Historical scope — MU Desk application icon

Authorization: user previously replied `接受主图标 Gate D`; after rejecting v2 and selecting A3, user explicitly said `你就改吧，别一步步接收了` on 2026-08-22, consolidating the revised B/C/D decisions for this narrow icon replacement.

- [x] Accepted icon packaged as transparent PNG exports from 16–512 px.
- [x] Multi-size Windows ICO contains 16, 20, 24, 32, 40, 48, 64, 128, and 256 px entries.
- [x] Icon-library QA passed with zero file-level issues; 16 px visual inspection remained recognizable.
- [x] `Toolbox.App.csproj` embeds the icon as the application icon.
- [x] Main Window and Cursor Gallery explicitly use the parent MU Desk icon.
- [x] Published EXE returned the accepted icon through `Icon.ExtractAssociatedIcon`, which is also the tray icon source.
- [x] Real Windows capture confirmed the icon in both window title bars.
- [x] User-reported taskbar size issue corrected without changing the accepted metaphor, palette, or silhouette: chroma residue was removed, geometry was recentered, and effective safe padding was reduced to 7.5%.
- [x] At 32 px, the high-opacity subject bounds increased from approximately 16×17 px in v1 to 26×26 px in v2.
- [x] v2 was rejected after a second real taskbar review; the replacement was redrawn as the flat A3 solid Module Dock rather than further scaling the miniature-window composition.
- [x] v3 uses only solid MU violet, charcoal, and off-white; its 24 px high-opacity bounds are 20×20 px with a high-contrast filled tile instead of a pale sparse frame.
- [x] v3 icon-library validation passed with zero issues across 16, 20, 24, 32, 40, 48, 64, 128, 256, and 512 px exports.
- [x] Source and integrated ICO SHA-256 hashes matched; the published EXE returned the v3 icon through `Icon.ExtractAssociatedIcon`.
- [x] A live Windows capture confirmed the v3 icon in the published MU Desk main-window title bar.
- [x] DPI-aware live taskbar capture confirmed the optically compensated icon beside neighboring applications; user confirmed `现在大小差不多了`.
- [x] `.\build-toolbox.ps1` passed with 0 warnings and 0 errors; all preserved tests passed, including Desktop Organizer 21/21.
- [x] `.\publish-toolbox.ps1` updated the existing single shell package; no standalone MouseRing or DesktopOrganizer executable was added.

Evidence:

- `artifacts/mu-desk-app-icon-v3/design-spec.md`
- `artifacts/mu-desk-app-icon-v3/manifest.json`
- `artifacts/mu-desk-app-icon-v3/qa-report.json`
- `artifacts/mu-desk-app-icon-v3/preview/contact-sheet.png`
- `artifacts/mu-desk-app-icon-v3/preview/live-taskbar-strip.png`
- `artifacts/mu-desk-app-icon-v3/qa-platform-override.md`
- `artifacts/mu-desk-app-icon-v3/published-exe-icon.png`
- `mu-desk-app-icon-implementation-map.json`

## Home visual synchronization — Quick Ring and Desktop Organizer

- [x] `四向轮盘` 首页入口从蓝色准星改为 MU 深描边四分区轮盘图标，保留明确功能隐喻。
- [x] `栖格 · 桌面整理` 首页入口从绿色菜单块改为 MU 深描边桌面分区图标，保留明确功能隐喻。
- [x] 首页六张模块卡使用同一暖白表面、深色描边和圆角规则。
- [x] 常驻工具与启动项开关统一为紫色、深描边、非纯颜色状态表达。
- [x] `2026-08-22 .\build-toolbox.ps1` 通过：0 warnings / 0 errors；Toolbox、MouseRing 和 DesktopOrganizer 21/21 检查全部通过。
- [x] `2026-08-22 .\publish-toolbox.ps1` 更新 `artifacts/PersonalToolbox-win-x64/PersonalToolbox.exe`，未新增独立工具外壳。
- [x] 对发布成品执行 `PrintWindow` 实机截图检查；46 px 入口中两枚图标轮廓完整、视觉占比一致，状态徽标、操作按钮和开关未遮挡。

Evidence:

- `src/Toolbox.App/MainWindow.xaml`
- `artifacts/mu-desk-visual-sync/home-printwindow.png`
- `asset-plan.json`

## Independent task-window identity and icons

- [x] CISP 题库、光标画廊和动态拾取结果窗在同一 MU Desk 进程内使用互不相同的窗口任务栏身份。
- [x] CISP 题库标题栏和任务栏使用自己的浅紫 `C?` 文档图标，不再显示 MU Desk 主图标。
- [x] 光标画廊窗口使用 `cursor-gallery-64.png`；动态拾取结果窗与设置窗使用 `effect-capture-64.png`。
- [x] MU Desk 仍然只有一个进程、托盘、启动项、设置和发布包。
- [x] `2026-08-22 .\build-toolbox.ps1` 通过：0 warnings / 0 errors，所有回归测试通过。
- [x] `2026-08-22 .\publish-toolbox.ps1` 已更新发布成品；Windows 实机截图确认 CISP 标题栏显示独立模块图标。
- [x] 用户指出首版 CISP 图标留白过宽后，图标高对比主体扩大至接近 64 px 矢量画布边缘；实机标题栏 16 px 检查中 `C?` 与外轮廓保持清晰且不再显小。
- [x] 首页六个模块图标统一为 46 px 外框内约 34 px 的真实可见主体；动态拾取、CISP、光标画廊和临时货架与用户认可的四向轮盘、桌面整理使用同等光学占比和深描边规则。
- [x] 实机首页截图确认六枚图标在两列三行布局中轮廓完整、视觉重量接近，没有再次使用 PNG 透明画布尺寸代替真实主体尺寸。

## Unified Reminder Notes and Desktop Companion integration

- [x] 首页工具数由 6 更新为 8；`随记`与`桌面伙伴`分别拥有清晰的模块卡片、图标和操作入口。
- [x] 随记由正式包以 `--hosted` 启动；托管模式不创建独立托盘图标，首页继续提供打开随记与新建提醒入口。
- [x] 桌面伙伴由正式包以 `--hosted` 启动；首页和统一托盘提供启用、显示/隐藏与设置操作。
- [x] 实机验证桌宠隐藏、显示和设置 IPC 均复用同一个 LightPet 进程；角色包、动画状态机、拖动动作和现有互动没有改动。
- [x] MU Desk 退出路径负责结束两个托管工作进程；独立开发启动仍保留兼容入口。
- [x] `2026-08-24 .\build-toolbox.ps1` 通过：0 warnings / 0 errors；Toolbox、MouseRing、Desktop Organizer 21/21 与 Reminder Notes 测试全部通过。
- [x] `2026-08-24 .\build-lightpet.ps1` 通过：0 warnings / 0 errors；24 项 LightPet 检查全部通过。
- [x] `2026-08-24 .\publish-toolbox.ps1` 更新正式包；运行进程来自 `artifacts/PersonalToolbox-win-x64`，随记和桌宠均来自其 `tools` 子目录。
- [x] Windows 实机托盘复核只保留 MU Desk 主图标；旧版独立随记图标与旧 `-shelf` 进程均已退出。
- [x] 开机启动项只指向正式 `PersonalToolbox-win-x64\PersonalToolbox.exe --minimized`，没有随记或桌宠独立启动项。

Evidence:

- `integration-reminder-pet-implementation-map.json`
- `src/Toolbox.App/Modules/ReminderNotesModule.cs`
- `src/Toolbox.App/Modules/LightPetModule.cs`
- `src/PersonalTools.App/App.xaml.cs`
- `src/LightPet.App/App.xaml.cs`
- `src/Toolbox.App/MainWindow.xaml`
- `src/Toolbox.App/Services/ToolboxTrayIcon.cs`
- `publish-toolbox.ps1`
