# 动态拾取视觉系统

Parent SKU: `MU-DESK`  
Module ID: `effect-capture`  
Gate: Visual System — pending review

## Inheritance

- Standard: `MU Product Standard 1.0.0`.
- Management and completion surfaces: `light-outlined`.
- Region selection, countdown, and recording surfaces: `dark-translucent`.
- Density: compact during capture, normal in the completion panel.
- Token overrides: none.
- Typography: Segoe UI Variable with Microsoft YaHei UI fallback.
- Copy: concise and functional; no character voice.

Family reference: `../../concepts/ui-direction.png`.

## Visual principle

The captured effect is the content and the focal point. UI chrome must make selection, timing, privacy, and send state legible without competing with the motion being captured. Do not add a character, decorative illustration, fake waveform, neon recording treatment, or ornamental motion.

## Surface map

### 1. MU Desk module card

- Use the existing MU Desk card construction and spacing.
- Name: `动态拾取`.
- Descriptor: `框选一小段动态，整理后在 Codex 中打开`.
- Primary card action: `开始拾取`.
- Status is on-demand, not “running”; the card does not show a permanent active indicator.
- Module icon appears at the same optical size as peer module icons.

### 2. Region-selection overlay

- Cover all monitors with a neutral charcoal veil at approximately 42–50% opacity while preserving enough desktop detail to identify the target.
- The selected rectangle is a clear cutout, not a blurred or tinted preview.
- Selection border: 1.5–2 px violet line with four visible corner handles; do not use animated marching ants.
- Outside the selection, show a compact instruction pill: `拖动框选 · Esc 取消`.
- Show live width × height near the lower-right corner of the selection.
- Crosshair precision is limited to the pointer neighborhood; do not draw full-screen guide lines that add visual noise.
- Keyboard focus and cancellation remain available even though this is a mouse-first surface.

### 3. Armed controls and countdown

- Place the compact control bar immediately outside the selected rectangle, preferring below and flipping above when space is insufficient.
- Controls: duration segmented choice `2 秒 / 5 秒 / 7 秒`, secondary `重选`, primary `开始录制`.
- The default duration is visibly selected as `2 秒` using pale violet fill plus an outline/shape cue.
- After Start, replace the bar with a short `3 · 2 · 1` countdown pill outside the capture rectangle.
- Countdown is functional live state, not decorative animation. Use number replacement plus a restrained opacity transition; reduced-motion mode uses instantaneous number changes.
- The countdown and all controls must be excluded from the captured pixels.

### 4. Recording boundary and HUD

- Keep the capture interior completely unobstructed.
- Boundary uses the MU danger color with a solid line; status also includes a small filled dot and text `录制中`, so red is not the only cue.
- Place the HUD outside the selected rectangle and flip to the nearest safe edge when needed.
- HUD content: `录制中`, elapsed/total time, `停止`, and `取消`.
- `停止` is the primary immediate control; `取消` is secondary and never disguised as Stop.
- A real recording may update the timer continuously. No other element pulses, glows, bounces, or loops.

### 5. Processing state

- Return to a compact `light-outlined` panel after recording.
- Copy: `正在整理关键帧…` with concrete substatus for encoding, frame selection, and package write.
- Use a determinate progress bar when measurable; otherwise use one restrained progress indicator.
- Keep `取消` available while cancellation is still safe. Do not show a success-shaped empty card during processing.

### 6. Completion panel

Use one focused window rather than a dashboard or asset manager.

1. Header
   - Title: `已拾取 1.8 秒动态` using the actual duration.
   - Compact metadata: capture dimensions, frame count, and output location summary.
2. Preview
   - Large still poster from the most representative frame.
   - A horizontal ordered strip of 8–16 key-frame thumbnails with timestamp labels.
   - Selecting a thumbnail replaces the large still; keyboard arrows move through frames.
   - A manual `播放预览` control may replay the short local clip. It does not autoplay indefinitely and respects reduced motion.
3. Send summary
   - Explicit text immediately above the action: `将发送 12 张关键帧和分析说明；原始 MP4 保留在本机。`
   - The image count is real and updates with the package.
4. Actions
   - Primary: violet filled `在 Codex 中打开`.
   - Secondary: outlined `打开文件夹`.
   - Tertiary text action: `重新录制`.
   - Only one primary action exists in this region.

## Codex connection and send states

- Signed out: replace the primary label with `连接 Codex` and explain that sign-in is required before anything is sent.
- Connecting: primary button is disabled and reads `正在连接…` with one compact progress indicator.
- Ready: button reads `在 Codex 中打开`; the visible final-send note remains beside it.
- Sending: button reads `正在发送 12 张图片…`; prevent duplicate submissions.
- Success: show text `已在 Codex 中准备好`; retain the local-package actions and allow `再次打开 Codex`.
- Failure: show a persistent outlined error region with the actual connection/authentication/task error and an explicit `重试发送` action.
- Never show success when only the prompt was copied, the Codex executable was launched, or a path was opened.

## Color, shape, and type

- Use inherited foundation tokens unchanged: canvas `#EEF0F5`, surface `#FAFAFC`, ink `#25252A`, muted ink `#73737D`, violet `#8055D9`, pale violet `#E9DEFF`, warm status `#FFD878`, success `#49C978`, danger `#D95C68`.
- Card radius: 14 px; compact HUD/control radius: 9–10 px.
- UI outline: 1–1.5 px; capture boundary may use 2 px for visibility over unknown content.
- No ordinary glow. Desktop overlays may use controlled background blur only when it does not contaminate the capture rectangle.
- Body and metadata sizes follow the existing MU Desk scale. The timer uses tabular numerals where available.

## Module icon plan

Metaphor: four compact selection corners surrounding three offset frame sheets, with the middle or motion-transition frame in violet. The icon represents selecting a short sequence, not a generic camera or screen recorder.

- Family language: flat rounded shapes, charcoal outline, off-white body, one violet state part.
- Avoid video-camera silhouettes, film reels, red record circles as the dominant metaphor, sparkle-only icons, and character portraits.
- The 16–20 px version reduces to selection corners plus two frame edges; no tiny motion trails.
- Production targets after Gate B: 16, 20, 24, 32, 64, 128, 256 px.
- Fallback: code-native selection corners and stacked frames.
- Current state: planned; no final raster asset is authorized.

## Illustration decision

No character or editorial illustration is used in this module. The selected effect, contact sheet, and key frames already provide the visual content. Empty or failure states use code-native icons and direct copy, and remain complete when no artwork is available.

## Motion boundary

- Ordinary transitions: 120–160 ms opacity, outline, or 1–2 px position feedback.
- Region dragging and resizing track the pointer without eased lag.
- Countdown and recording timer are functional live states.
- Preview playback is user initiated and stops at the end; no decorative infinite loop.
- Respect Windows reduced-motion settings for countdown, preview defaults, and state transitions.

## Accessibility and platform fit

- `Esc` cancels selection/countdown or exits the current transient state; it must not silently discard a completed package.
- Enter/Space activates focused controls. Visible focus uses outline plus contrast, not color alone.
- All unfamiliar icon controls have labels or tooltips and accessible names.
- The selection border, recording status, and send status use text/shape cues in addition to color.
- HUD and control bars remain readable at 200% Windows scaling and relocate rather than clip at monitor edges.
- Multi-monitor overlays preserve per-monitor DPI and do not create one blurry stretched surface.
- Completion content reflows before introducing horizontal page scrolling.

## Visual acceptance

- The capture rectangle is always obvious while the target pixels remain unobstructed.
- Countdown and HUD never appear inside the encoded region.
- Armed, recording, processing, ready-to-send, sending, success, and failure states cannot be mistaken for one another.
- The completion panel states exactly what will leave the device before the user clicks Send.
- `在 Codex 中打开` is the only primary completion action.
- Key-frame order and timestamps are readable without playing the MP4.
- The module visibly belongs to MU Desk and introduces no independent brand, character illustration, neon recorder styling, or decorative continuous animation.
