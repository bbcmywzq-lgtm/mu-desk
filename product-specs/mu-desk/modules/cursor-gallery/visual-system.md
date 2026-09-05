# 光标画廊视觉系统

Parent SKU: `MU-DESK`  
Module ID: `cursor-gallery`  
Gate: Visual System — accepted 2026-08-22

## Inheritance

- Standard: `MU Product Standard 1.0.0`.
- Surface profile: `light-outlined`.
- Density: normal.
- Token overrides: none.
- Typography: Segoe UI Variable with Microsoft YaHei UI fallback.
- Copy: concise and functional; no character voice.

Family reference: `../../concepts/ui-direction.png`.

## Dominant composition

Use a collection-and-detail window rather than the current equal-card wall.

1. Compact top bar
   - Window identity: 光标画廊.
   - One-line explanation: 选择一套本地光标并应用到 Windows.
   - Current scheme status.
   - Secondary action: 恢复 Windows 默认.
2. Left collection
   - Scrollable skin list.
   - Each row shows Arrow preview, name, available count, completeness, and current state.
   - Selected row uses pale violet fill plus charcoal/violet outline; selection never relies on color alone.
3. Right detail
   - Selected skin name and status.
   - Grid of cursor-role previews from the existing `Roles` data.
   - Missing roles remain visible with a clear “缺失” state.
   - Show `ImportNote` only when it exists.
   - Primary action: 应用这套.
4. Bottom feedback region
   - Persistent, compact success or failure message.
   - No decorative footer when there is no message.

At narrow window widths, the detail moves below the collection in document flow; do not shrink the role previews below a useful size.

## State contract

- Loading: skeleton rows and preview cells shaped like final content.
- Populated: first valid skin is selected unless the currently applied skin exists.
- Empty: explain that the legacy local library was not found; do not fabricate example skins.
- Selected: detail is visible, Apply is enabled unless already current.
- Applied: text badge “正在使用”, success feedback, selected item retained.
- Incomplete: show count and missing-role labels; do not hide incomplete packages.
- Failure: persistent error with the real registry/file/refresh message.
- Reset success: current scheme becomes Windows default and list states refresh.

## Components

- Skin collection: rounded rows, not nested card grids.
- Role preview: compact off-white tiles with charcoal outline and role label.
- Primary button: violet fill, “应用这套”.
- Secondary button: off-white outlined, “恢复 Windows 默认”.
- Status badges: text plus dot or icon.
- Destructive styling is not used; reset is reversible but requires an explicit click.

## Icon plan

Module icon metaphor: one rounded cursor arrow crossing a compact two-card gallery stack, with one violet card or swatch. It must not resemble the quick-ring icon or a generic mouse hardware icon.

- Production targets: 16, 20, 24, 32, 64, 128, 256 px.
- Controls and role labels use code-native icons or text, not generated raster miniatures.
- Current concept state: planned; no final asset exists.

## Illustration decision

No character illustration inside the gallery. Cursor skins and role previews are already the focal visual content. The module must remain complete without a reserved illustration slot.

## Motion

- 120–160 ms color, outline, opacity, or 1–2 px position feedback.
- No continuously animated cards, cursors, navigation, or status icons.
- Animated `.ani` cursor files are previews/content, not decorative interface motion.
- Respect reduced-motion settings.

## Accessibility and geometry

- Keyboard selection moves through the skin list; focus remains visible.
- Applying requires an explicit button activation, not selection alone.
- Labels identify unfamiliar cursor roles.
- Layout remains readable at 200% Windows scaling.
- The window has no page-level horizontal scrolling.
- Lists and role previews own their natural vertical scroll regions.

## Visual acceptance

- The selected skin and current applied skin are distinguishable.
- All 17 packages remain reachable.
- Incomplete and missing roles are visible.
- The primary Apply action stays visible with normal content.
- Empty and error states do not look successful.
- No legacy blue accent, generic white-card wall, character artwork, or independent product branding remains.
