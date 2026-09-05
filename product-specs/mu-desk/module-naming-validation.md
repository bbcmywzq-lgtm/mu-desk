# Module naming validation — 2026-09-05

MU-DESK / MU Product Standard 1.0.1. Scoped naming amendment accepted and implemented; the four previously accepted gates remain in force.

- Applied eight approved Mujun identities across the shell, module headers/titles, tray entry points, accessibility labels, errors, worker display metadata and current user documentation. Compact Memo sidebar uses `Memo` with its Chinese descriptor on the next line.
- Kept CISP 题库 and MU Desk unchanged. No executable names, namespaces, runtime locks, pipe endpoints, startup keys, settings keys, resource paths, backup extensions or storage paths were renamed. Functionality and animation implementation were not changed.
- `build-toolbox.ps1`: build succeeded with zero warnings/errors; Toolbox, MouseRing, DesktopOrganizer (21/21) and PersonalTools console suites passed. LightPet console suite separately passed (24 checks).
- `Cue.Diagnostics --names`: all nine card labels found; title/status layouts fit at 900 and 1020 DIP; runtime module IDs and CISP display name assertions passed. Uses non-started modules, a unique temporary capture root and no settings writes. This is a WPF shell layout check, not an end-to-end test of every module action.
- Layout images: `artifacts/module-names-900-top.png`, `artifacts/module-names-900-bottom.png`, and corresponding 1020 DIP files. Diagnostic rendering does not guarantee every asynchronous image resource has loaded.
- `validate_sku.py product-specs/mu-desk`: passed, no errors, implementation-authorized.
- Published a fresh package at `artifacts/PersonalToolbox-win-x64-mujun-names`; old release folders were not overwritten or removed. Host and both workers were restarted from this package and reported responding; the host was shown. Native screen transform recovery verified 1× / 0,0 during replacement.
