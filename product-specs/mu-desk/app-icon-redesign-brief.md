# MU Desk application icon redesign brief

Status: concept direction pending user selection  
Date: 2026-08-22  
Scope: application icon only; module icons and UI tokens remain unchanged

## Why v1/v2 failed

- The icon reads as a miniature window screenshot rather than one memorable symbol.
- The outer white window, inner three cells, outlines, and violet dock all compete at 16–32 px.
- Most of the visual area is pale or empty, so the taskbar silhouette feels weak beside Chrome and other solid app marks.
- Scaling the same composition up increases line noise without improving recognition.

## New non-negotiable constraints

- Design at 24 px first, then verify at 16, 20, 32, 48, and 256 px.
- One dominant silhouette; at most one supporting internal division.
- High-contrast filled mass must occupy roughly 80–88% of the optical canvas.
- No title bar dots, miniature window chrome, tiny module thumbnails, letters, or creator identity.
- Retain the MU family palette: charcoal, off-white, twilight violet; warm yellow only as an optional single status point.
- The symbol must mean “one modular desktop toolbox”, not any single module.

## Candidate directions for concept review

### A — Module Dock (recommended)

A strong violet rounded-square body containing three large off-white modular tabs seated into one charcoal dock. The tabs are broad shapes, not miniature windows. Reads as “several tools, one home”. Best taskbar presence and clearest continuity with the accepted MU palette.

### B — Utility Stack

Three chunky offset rounded cards forming one compact diagonal stack, with the foremost card violet and the rear cards off-white/charcoal. Reads as “a collection of utilities”. More mature and less literal, but slightly weaker at 16 px.

### C — Modular Block

Three interlocking rounded blocks forming one near-square emblem, one violet and two off-white/charcoal. Reads as “modular system”. Strongest abstract silhouette, but says less about desktop/toolbox function.

## Concept proof required before Gate B

- Produce a single comparison sheet with A/B/C and small-size previews.
- Judge the candidates first at 24 px in a Windows taskbar strip, not as 512 px artwork.
- Select one direction, then refine only that direction into 3 variants.
- Final asset production and application integration remain blocked until the revised Visual System, System Packaging, and Implementation Authorization gates are explicitly accepted.

Concept artifact: `../../artifacts/mu-desk-app-icon-redesign-concepts-v1/preview/contact-sheet.png`  
Selected mother direction: Direction A — Module Dock; user replied `A` on 2026-08-22. Variant and final rendering remain pending.

Refined concept artifact: `../../artifacts/mu-desk-app-icon-module-dock-concepts-v2/preview/taskbar-24px-comparison.png`  
Selected variant: A3 solid app tile (`dock-solid-tile`); user replied `KEYI` after the A1/A2/A3 taskbar comparison and A3 recommendation. Final flat rendering and production exports remain blocked until revised Gate B acceptance.
