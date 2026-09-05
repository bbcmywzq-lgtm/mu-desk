# Drop edge-shelf refinement — 2026-09-05

User accepted the clarified scope with “对”: refine the right-edge Drop shelf using the same visual and motion approach as Cue. This scoped implementation consumes MU-DESK / MU Standard 1.0.1's existing dark-translucent profile and approved Cue reveal approach; it does not create new product identities or packaging.

- Compact persistent edge handle; charcoal surface, violet interaction state, quiet secondary controls, clear empty and drag states.
- Fixed-size content during reveal, animated drawing clip and opacity, continuous reversals; release cache at rest. Respect Windows reduced-motion setting.
- Click/valid file drag opens. Pointer departure gets a grace period; do not collapse during drag or confirmation. Escape and explicit close collapse.
- Preserve grouping, pinning, path-only storage, Copy/Link drag effects, successful-drag cleanup, undo, validation, left docking and monitor preference. No source-file operations or data migration.
- Ownership: FileShelfWindow XAML/code-behind and motion partial; narrow host-state notification; isolated Cue.Diagnostics check. Cue motion code stays unchanged.
- Verify build/core tests, WPF reveal/reversal/hidden cleanup, native bounds and transparent-area hit testing, drag routing, isolated fixture pin/remove/undo, screenshots, fresh local package. Retain previous package.

## Verification

- `build-toolbox.ps1`: zero warnings/errors; Toolbox, MouseRing, DesktopOrganizer (21/21) and PersonalTools suites passed.
- `Cue.Diagnostics --drop`: real WPF window, 22×76 DIP persistent handle, 360 DIP panel, fixed native bounds during animation, transparent area hit-through, continuous retarget, temporary cache release, reduced-motion immediate path and left docking passed.
- Eight rapid retargets: 0 content measure calls, 0 content arrange calls, 9 native placements at transition boundaries. This is structural animation evidence, not a measured display-frame-rate guarantee.
- Isolated local fixture: routed valid/invalid/self drag events, pinning, actual remove/undo buttons, clear excluding pinned paths, and source-content preservation passed. Routed drag tests do not replace manual Explorer-to-application OLE drag testing.
- Empty, populated, collapsed and left-docked screenshots checked: `artifacts/drop-empty.png`, `drop-files.png`, `drop-handle.png`, `drop-left.png`.
- MU SKU validator passed (MU-DESK, Standard 1.0.1, implementation authorized).
- Published fresh `artifacts/PersonalToolbox-win-x64-drop-reveal`; old package retained. New host launched and responding, MU Desk shown. Existing shelf data hash unchanged after restart; native screen magnification recovered to 1× / 0,0 during replacement. Unchanged Memo/Pal workers were left running.
