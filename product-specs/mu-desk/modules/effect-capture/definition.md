# 动态拾取模块定义

Parent SKU: `MU-DESK`  
Module ID: `effect-capture`  
Gate: Product Definition — pending review

## Product role

动态拾取是 MU Desk 内部按需模块。它让用户框选屏幕上的一个小范围，录制极短的动态效果，再把连续画面整理成 AI 更容易分析的上下文包。录制完成后，用户可以通过一个明确的主按钮把精选关键帧和分析提示发送给 Codex。它不是完整录屏软件，也不会在未经用户操作时上传内容。

## User and dominant task

- User: 看到喜欢的网页或桌面动态效果，却很难用语言准确描述的个人用户。
- Dominant task: 框选效果区域，触发并录制约 1–2 秒动画，得到可以手动附加给 AI 的结构化素材包。
- Success: 用户无需剪辑或逐帧截图，录制结束后即可保存一份尺寸可控、顺序明确的结果，并能通过一次明确点击把适合视觉理解的内容提交给 Codex。

## Entry model

- Primary entry: MU Desk 首页模块卡片。
- Quick entry candidate: 快捷轮盘中的“动态拾取”动作。
- Global hotkey candidate: 后续在 System Packaging gate 决定是否提供及如何避免冲突。
- No standalone executable, tray icon, startup item, version identity, installer, or updater.

## Core workflow

1. 进入框选状态，并显示当前屏幕的低干扰遮罩。
2. 拖拽确定一个小范围；允许取消或重新框选。
3. 使用默认 2 秒或用户预先选择的短时档位开始录制。
4. 录制期间只显示边界、剩余时间与停止/取消入口，不遮挡选区内容。
5. 停止后在本机短暂处理，生成一个完整素材包。
6. 显示完成摘要，以“在 Codex 中打开”为主操作，并提供打开输出位置或再次录制。
7. 用户点击后，以素材包目录打开 Codex 新任务并预填分析说明；用户在可见输入框中确认并发送。

## Capture contract

- Default duration: 2 seconds.
- Maximum duration: 7 seconds.
- Capture target: one user-selected rectangular screen region.
- Motion priority: retain fast UI transitions and short overshoot; the implementation should capture at a cadence suitable for sub-second motion.
- Audio: none.
- Network before send: none.
- Network submission is owned by Codex and occurs only after the user sends from the visible Codex composer.
- Background behavior: no continuous screen observation while idle.

## Output contract

One capture produces one local folder containing:

- `source.mp4`: compact source motion reference.
- `contact-sheet.png`: ordered overview of the selected key frames.
- `frames/`: a small set of original-resolution PNG key frames.
- `effect.md`: duration, frame timestamps, capture region, trigger note when provided, and a concise instruction for AI analysis.

The first version should normally select 8–16 useful frames instead of exporting every captured frame. The source video remains available when later re-analysis needs information omitted by key-frame selection.

## Codex send contract

- Primary completion action: `在 Codex 中打开`.
- Trigger: explicit user click only; completing a recording never sends automatically.
- First-version destination: always create a new Codex task. Selecting an existing or active task is deferred.
- Submitted turn input:
  - a concise text prompt derived from `effect.md`;
  - the ordered original-resolution key frames as local image inputs;
  - the local package path as context so Codex can find `source.mp4` if later tool-assisted inspection is useful.
- The MP4 is not treated as the model's primary visual input and is not silently attached as a substitute for frames.
- Success condition: Codex accepts the new thread and first turn and returns their identifiers. Merely opening an application, copying a path, or writing a prompt file is not success.
- Failure condition: Codex is unavailable, authentication is missing, task creation fails, or the turn is rejected. Preserve the local package and show the real failure.
- Integration basis: the documented local `codex://new` deep link with `path` and `prompt` query parameters.
- Desktop navigation: automatically focusing the new task in the Codex desktop UI is optional until an officially supported task-navigation mechanism is verified; sending itself must not depend on UI automation.

## Visible states

- Ready.
- Selecting region.
- Region selected.
- Countdown or armed state.
- Recording.
- Processing.
- Completed.
- Connecting to Codex.
- Sending to Codex.
- Sent to Codex.
- Codex sign-in required.
- Codex send failed.
- Cancelled.
- Capture permission, encoder, storage, or output failure.

## Privacy and safety boundary

- Capture, encoding, and key-frame selection are local.
- Nothing is uploaded, sent to Codex, or placed on the clipboard automatically.
- Clicking `在 Codex 中打开` is a local handoff; the visible Send action inside Codex is the submission boundary.
- `source.mp4` remains local by default; sending it later requires a separate explicit action or a future accepted scope change.
- The user sees the exact capture rectangle before recording starts.
- Cancelling before completion must not leave a successful-looking partial package.
- Temporary frames are removed after successful packaging; failure cleanup and recoverability are defined at the System Packaging gate.

## Explicit exclusions for the first version

- Automatic Codex submission, background upload, direct OpenAI Responses API integration, or storing a separate API key inside MU Desk.
- Full-screen/session recording, microphone/system audio, webcam, live streaming, or long recordings.
- Timeline editing, trimming, annotation, captions, filters, or GIF authoring.
- A searchable effect library, tagging system, favorites, cloud sync, or account.
- Selecting an existing Codex task, silently steering the active task, or embedding a full Codex conversation UI.
- Browser extension behavior and extraction of DOM, computed CSS, `@keyframes`, Canvas, or WebGL implementation details.
- Automatic reconstruction or code generation inside MU Desk.

## Deferred extension

A later “网页增强” scope may combine selector-style DOM targeting with CSS and animation metadata. It must return to Product Definition because it adds browser permissions, page-data collection, a new integration surface, and new privacy obligations.

## Product Definition acceptance conditions

Gate A for this addition is accepted only when the user explicitly accepts the complete definition, including:

- internal-module identity and the user-facing name “动态拾取”;
- default 2-second and maximum 7-second duration boundary;
- local AI context package as the outcome;
- one explicit `在 Codex 中打开` button that opens a new composer with the package workspace and prompt;
- local-only processing before that click and a visible consent boundary at send time;
- no editor, audio, library, cloud, or browser metadata extraction in the first version;
- browser-enhanced capture remaining deferred.
