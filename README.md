# VoiDPlugins — TingAlex Enhanced [![.NET](https://github.com/TingAlex/VoiDPlugins/actions/workflows/dotnet.yml/badge.svg?branch=agent%2Fglobal-hotkey-precision-control)](https://github.com/TingAlex/VoiDPlugins/actions/workflows/dotnet.yml)

This fork of [VoiDPlugins](https://github.com/X9VoiD/VoiDPlugins) adds a
Windows-focused version of Precision Control for
[OpenTabletDriver](https://github.com/InfinityGhost/OpenTabletDriver). It is
designed for handwriting and drawing workflows driven by a precision touchpad:
show, reposition, and nudge a visible precision area without reaching for the
tablet buttons.

The enhanced plugin is displayed as **Precision Control (TingAlex Enhanced)**,
so it can be distinguished from the upstream filter in OpenTabletDriver.

## Precision Control v1.0.0 quick start

### Requirements

- Windows 10 or Windows 11
- OpenTabletDriver 0.6.x (tested with 0.6.7)
- A Windows Precision Touchpad is optional, but recommended for gesture control

### Installation

1. Download `PrecisionControl-v1.0.0.zip` from the
   [latest release](https://github.com/TingAlex/VoiDPlugins/releases/latest).
2. Exit OpenTabletDriver, including its daemon.
3. Extract `PrecisionControl.dll` into a dedicated plugin directory such as
   `%LOCALAPPDATA%\OpenTabletDriver\Plugins\Precision Control (TingAlex Enhanced)`.
4. Start OpenTabletDriver, add **Precision Control (TingAlex Enhanced)** under
   `Filters`, and save the configuration.

If an earlier review build is installed, replace its `PrecisionControl.dll`
while OpenTabletDriver is stopped. Keeping a backup of the plugin directory and
`%LOCALAPPDATA%\OpenTabletDriver\settings.json` is recommended before upgrading.

### Default shortcuts

All global actions share the modifier selection configured in the filter. The
default modifiers are `Ctrl+Alt+Shift`.

| Action | Default shortcut | Suggested touchpad gesture | Behavior |
| --- | --- | --- | --- |
| Show or hide | `Ctrl+Alt+Shift+P` | Three-finger tap | Toggles the precision area at the selected activation anchor. |
| Reposition | `Ctrl+Alt+Shift+R` | Four-finger tap | Shows a hidden area or immediately rebuilds the visible area at the current mouse position. |
| Nudge up | `Ctrl+Alt+Shift+Up` | Three-finger swipe up | Moves the visible area upward by the configured vertical step. |
| Nudge down | `Ctrl+Alt+Shift+Down` | Three-finger swipe down | Moves the visible area downward by the configured vertical step. |
| Nudge left | `Ctrl+Alt+Shift+Left` | Three-finger swipe left | Moves the visible area left by the configured horizontal step. |
| Nudge right | `Ctrl+Alt+Shift+Right` | Three-finger swipe right | Moves the visible area right by the configured horizontal step. |

Open **Windows Settings > Bluetooth & devices > Touchpad > Advanced gestures**
and assign each gesture to its matching custom shortcut. Repositioning and
nudging are ignored only while pen pressure is above zero, preventing an
accidental area move during a stroke. They remain available while the pen is
hovering or out of range.

# Plugins

## Experimental

### [MeL](https://github.com/X9VoiD/VoiDPlugins/wiki/MeL)

      Machine Learning Plugin

### [Reconstructor](https://github.com/X9VoiD/VoiDPlugins/wiki/Reconstructor)

      Mathematically perfect "anti-hardware-smoothing"

## Filters

### Precision Control (TingAlex Enhanced)

      Dynamic sensitivity switching

#### Unified settings

The enhanced filter keeps its precision multiplier, border, and Windows global
hotkey settings together under `Filters > Precision Control (TingAlex
Enhanced)`. The hotkey uses `RegisterHotKey`, not a low-level keyboard hook,
and can toggle precision even when the pen is out of range. The activation
anchor can come from the last pen position, the current mouse position, or
whichever pointer moved most recently.

The toggle works whether the pen is touching, hovering, or out of range.
The separate reposition action shows a hidden area at the current Windows
mouse position or directly rebuilds an already-visible area there without an
intermediate hide or border flicker.

Precision Control also registers `Ctrl+Alt+Shift` plus the four arrow keys for
moving an active precision area. Map those shortcuts to three-finger swipes in
Windows. Directional moves are ignored only while pen pressure is above zero;
hovering and out-of-range pens do not restrict them. Horizontal and vertical
move distances default to 20% of the precision-area width and height and can be
configured independently.

#### Precision area positioning

`Precision Area Positioning` provides two coordinate modes:

- `Screen Relative (Legacy)` preserves the original behavior. It scales the
  full monitor area around the activation point, so the pointer keeps the same
  relative position it had on the monitor.
- `Pointer Anchored` creates a fixed-size precision area around a configurable
  activation anchor. `Anchor Position X (%)` and `Anchor Position Y (%)`
  choose where that anchor sits inside the area. The defaults are 10% from the
  left and 10% from the top; 50%/50% centers the area on the anchor.

`Activation Anchor Source` selects that anchor:

- `Last Active Pointer` (default) uses whichever moved most recently: the
  OpenTabletDriver pen cursor or the physical mouse/touchpad cursor.
- `Last Pen Position` keeps using the last pen cursor, with a mouse fallback
  before the first pen report.
- `Current Mouse Position` always uses the Windows mouse cursor, with a pen
  fallback if the mouse position is unavailable.

The area keeps the exact configured relationship to its anchor. It is not
shifted back inside a display near an edge, and directional moves may also
place part of the area off-screen. Moving the area does not synthesize pointer
movement, so the current pen cursor stays still during the gesture. On the
next pen report, the full tablet/output area is mapped proportionally into the
translated precision area: tablet top-left maps to area top-left, center maps
to center, and bottom-right maps to bottom-right.

When precision mode is active on Windows, the filter displays a thin,
semi-transparent border around the effective precision area. The border is
always on top, does not take focus, and lets mouse and pen input pass through.
Its visibility, `#RRGGBB` color, thickness, and opacity can be configured on
the enhanced filter. The default is black at 40% opacity.

See Microsoft's
[touch gesture documentation](https://support.microsoft.com/windows/touch-gestures-for-windows-a9d28305-4818-a5df-4e2b-e5590f850741)
for Windows Precision Touchpad gesture configuration.

#### Configuration notes

- The reposition key and four nudge keys are independent, but reuse the same
  Ctrl/Alt/Shift/Windows modifier selection as the toggle key.
- Windows global shortcuts must be unique. If registration fails, choose a
  different key or modifier combination and restart OpenTabletDriver.
- Border color accepts `#RRGGBB`. The default border is black, 2 px thick, at
  40% opacity.
- `Pointer Anchored` intentionally allows the precision area to extend beyond
  a monitor edge so its location remains predictable.
- To uninstall, stop OpenTabletDriver, remove the enhanced plugin directory,
  then remove the enhanced filter from the saved configuration if necessary.

See [CHANGELOG.md](CHANGELOG.md) for release history.

## Output Modes

### [WindowsInk](https://github.com/X9VoiD/VoiDPlugins/wiki/WindowsInk)

      Pen pressure support
      100% compliance to Windows Ink
      Upto 8192 levels of pressure

#### Touchpad-friendly hover

Enable `Touchpad-friendly hover` on the Windows Ink output mode when Windows
suppresses precision-touchpad gestures while a pen is in range. Hover movement
is mirrored through the OS mouse cursor, while contact, pressure, tilt, and pen
buttons continue to use Windows Ink. This makes touchpad gestures available
while the pen is hovering, at the cost of native pen-hover semantics until the
tip touches the tablet.

### [VMultiMode](https://github.com/X9VoiD/VoiDPlugins/wiki/VMultiMode)

      Classic VMulti input emulation

### [TouchEmu](https://github.com/X9VoiD/VoiDPlugins/wiki/TouchEmu)

      Touch input emulation (RawInput incompatible)
      Natural Scrolling everywhere

## Bindings

### [ScriptRunner](https://github.com/X9VoiD/VoiDPlugins/wiki/ScriptRunner)

      Bind shell commands, scripts or programs to OTD
