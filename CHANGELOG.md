# Changelog

All notable changes to Precision Control (TingAlex Enhanced) are documented in
this file.

## [1.0.0] - 2026-07-28

First stable release of the enhanced Precision Control workflow.

### Added

- Windows global hotkeys that work while the pen is hovering or out of range.
- A click-through, always-on-top precision border with configurable color,
  thickness, and opacity; the default is black at 40% opacity.
- Unified settings under `Precision Control (TingAlex Enhanced)` so the fork is
  clearly distinguishable from the upstream filter.
- `Pointer Anchored` positioning with independently configurable horizontal and
  vertical anchor percentages.
- Activation anchors based on the last active pointer, last pen position, or
  current mouse position.
- Four configurable nudge actions with independent horizontal and vertical
  step sizes.
- A dedicated reposition action that shows or rebuilds the area at the current
  mouse position in one gesture.
- Optional touchpad-friendly hover behavior for the Windows Ink output mode.

### Changed

- Pen return now maps the complete tablet/output area proportionally into the
  translated precision area, making the next contact point immediately match
  the pen's physical tablet position.
- Area visibility and movement no longer require the pen to remain in tablet
  range. Reposition and nudge actions are blocked only during an active stroke.
- Moving an area keeps the current cursor stationary until the next pen report.

### Compatibility

- Built for OpenTabletDriver 0.6.x and .NET 6.
- Windows-only UI and global-hotkey enhancements; the original filter behavior
  remains available through `Screen Relative (Legacy)`.

[1.0.0]: https://github.com/TingAlex/VoiDPlugins/releases/tag/v1.0.0
