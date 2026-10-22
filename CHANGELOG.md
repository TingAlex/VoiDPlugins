# Changelog

All notable changes to Precision Control (TingAlex Enhanced) are documented in
this file.

## [1.6.0] - 2026-10-22

- Keep document navigation on the physical touchpad by default. Retain pen page
  panning/jitter code and tests behind a closed internal gate; remove all page
  parameters from OTD's generated UI and loader so stale JSON cannot enable it.
- Preserve immediate third-button mapping taps and contact mapping dragging.
  Long hover holds inject no page input in the normal distribution.
- Organize 23 settings into seven purpose-based groups, explain conditions and
  defaults in hover help, and align slider defaults with reset metadata.
- Rewrite installation, settings, build and release documentation; document the
  retained experiment and distinguish source versions from the older download.
- Pin existing CI action dependencies to verified v4 commits and keep the net6.0
  build/test targets explicit.
- All 191 Precision Control tests pass, including default-gate/UI coverage.

## [1.5.2] - 2026-10-22

- Added adjustable contact-page jitter coefficient in group 08: 0–10, default
  2, with zero restoring unfiltered displacement. Each unit is a one-touch-pixel
  hysteresis band per axis, applied after the existing direction/gain transform.
- Capture the coefficient at each contact and reset the guard on clutching.
  Small reversals/lateral wobble are suppressed; larger values require more
  movement at startup and direction changes. No temporal smoothing, velocity
  acceleration or catch-up movement is added to the stationary lift cleanup.
- All 185 tests pass, including 17 new jitter/stop/clutch/invalid-setting cases.
  The user confirmed 1.5.1 is usable; the new coefficient's physical feel is
  pending user adjustment and confirmation.

## [1.5.1] - 2026-10-22

- Fixed page dragging silently reverting to ink when virtual contacts reached
  the window edge. Clamp contacts to the page client instead; backend failures
  and focus changes pause page mode while retaining the pressure interlock.
- Resolve the target from mapped pen coordinates, skip only this daemon's exact
  border windows, and start contacts inside the page client. Mapping state no
  longer determines the initial touch anchor through stale mouse coordinates.
- At pen lift, keep contacts stationary for 120 ms (updates at 0/40/80 ms), then
  cancel. Rapid re-contact reuses/rebases contacts; stale callbacks cannot end
  the new stroke. This addresses observed lift-time fling; actual OneNote
  inertia and routing still need user confirmation.
- All 168 logic/native-boundary tests pass. Native surface discovery was also
  exercised read-only on this PC; it did not inject input or prove OneNote effects.

## [1.5.0] - 2026-10-22

- Added opt-in double-tap contact navigation: double-tap button 3 to enter or
  leave page mode without changing the writing mapping. In page mode, touching
  and sliding pans via the direct touch backend; lifting cancels contacts and
  allows repositioning. Hover movement and long hover holds do not pan.
- Defer a single tap for a configurable 300 ms double-tap interval. In ordinary
  mode it toggles mapping when the interval expires, or before the next first
  ink point; it is discarded on cancellation and never replayed during a stroke.
  A single tap in page mode leaves the mode and mapping unchanged.
- Suppress tip pressure and pin the cursor during page contacts. Focus changes,
  invalid input, backend failure, disabling or mapping actions exit page mode and
  clean up contacts. Leaving sensing range ends contacts but preserves page mode
  for clutching in the same app. Ordinary hold/contact mapping drag remains available.
- All 156 tests pass, including 32 new contact navigation cases. Real OneNote
  contact routing, ink isolation and stopping behavior require user confirmation.

## [1.4.0] - Development candidate, 2026-10-22

- Added opt-in Direct Touch Pan: two fixed-separation contacts move by total
  pen displacement times a fixed gain. No velocity curve, wheel fallback,
  smoothing timer or queued continuation. Legacy wheel remains the default.
- Cancel contacts at their last accepted positions on release or cancellation,
  then destroy the dedicated synthetic pointer device. Probe API availability
  and restrict the session to its foreground target without modifier keys.
- Preserve jitter-tolerant short taps and contact mapping dragging. All 124
  logic tests pass. Native delivery, OneNote pan routing and app-side inertia
  remain unverified; this candidate is not yet installed on the development PC.

## [1.3.1] - 2026-10-22

- Fixed a reproducible hover-tap rejection: movement exceeding 12 original
  output pixels cancelled a short tap, even though Scale=0.25 makes that only
  3 mapped pixels. Hover movement no longer vetoes a below-threshold button tap.
  Contact and long-press still suppress tap toggling; hover scrolling is retained.
- Added one diagnostic line per completed/cancelled button gesture with toggle
  result, resulting mapping state, duration, movement magnitude and rejection reason.
- Added open-then-close-with-jitter and ten alternating hover-tap regression
  cases. The former failed against 1.3.0; all 110 tests now pass. Real pen
  confirmation is still required to establish the cause of the user's session.

## [1.3.0] - 2026-10-22

- Added opt-in third-button navigation: release a short, stationary press to
  toggle precision mapping and its border together. Hidden mode restores the
  original full output mapping; enabling uses the existing activation rules.
- Hold for 300 ms (configurable 150–800) and move while hovering to scroll the
  page vertically/horizontally. Natural direction and speed are configurable.
  Contact dragging remains available when group 07 is enabled.
- Long holds, contact, movement beyond tap tolerance, cancelled gestures and
  out-of-range reports never trigger a short-tap toggle on release. Transitions
  between contact and hover reset their position baselines.
- Wheel output checks the foreground target and modifier keys, freezes the
  cursor, and stops on failure/context change without retry or backlog. Runtime
  API exports are probed; native packets contain wheel flags only.
- Added 26 navigation/native-packet cases; 108 tests pass. Actual OneNote wheel
  routing, physical pen gestures and scrolling feel still require manual tests.

## [1.2.0] - 2026-10-22

- Added optional third-pen-button contact dragging of the existing mapping area.
  Hold button 3 and touch/slide the tip; lift to reposition the pen without
  moving the area, then touch again to continue. Size and writing Scale stay fixed.
- Added Chinese settings under group 07, disabled by default, with independent
  drag sensitivity (0.1–5, default 1 relative to the original output area).
- Suppress tip pressure and consume button 3 before downstream bindings during
  dragging. Releasing the button while touching waits for tip lift before inking.
  A button pressed during an existing stroke does not interrupt that stroke;
  release/lift is required before dragging. Re-entry never resumes a held drag.
- Keep the cursor stationary while dragging; update the actual mapping and
  existing border together. Dials/reposition/nudge are excluded during capture.
- Added 25 regression cases; 82 Precision Control tests pass. Real pen button
  reports, Windows Ink/OneNote isolation, and physical drag feel await testing.
- Development packages now include newly added src/tests files in their source
  ZIP, so an uncommitted candidate contains the code used to build its DLL.

## [1.1.2] - 2026-10-14

- Hid keyboard nudge controls from the generated settings page and renumbered
  the remaining groups. Nudge behavior uses built-in defaults; old custom
  values remain on disk but are no longer loaded by the generated settings system.

## [1.1.1] - 2026-10-14

- Organized settings into numbered Chinese groups with descriptive labels,
  units, and contextual tooltips. Put the two dial distances near the top.

## [1.1.0] - 2026-10-14

- Added `Precision Control Move Area (Dial)` bindings with Left/Right/Up/Down.
- Added independent horizontal and vertical dial distances in screen pixels
  (default 40 px per tick), separate from the existing percentage-based hotkeys.
- Dial input moves the current area immediately, including with the pen out of
  range. Inactive and active-stroke input is discarded without later replay.

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
