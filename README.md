# VoiDPlugins [![.NET](https://github.com/X9VoiD/VoiDPlugins/actions/workflows/dotnet.yml/badge.svg)](https://github.com/X9VoiD/VoiDPlugins/actions/workflows/dotnet.yml)

VoiDPlugins is a collection of extensions for [OpenTabletDriver](https://github.com/InfinityGhost/OpenTabletDriver).

# Plugins

## Experimental

### [MeL](https://github.com/X9VoiD/VoiDPlugins/wiki/MeL)

      Machine Learning Plugin

### [Reconstructor](https://github.com/X9VoiD/VoiDPlugins/wiki/Reconstructor)

      Mathematically perfect "anti-hardware-smoothing"

## Filters

### [Precision Control (TingAlex Enhanced)](https://github.com/X9VoiD/VoiDPlugins/wiki/PrecisionControl)

      Dynamic sensitivity switching

#### Unified precision settings

The enhanced filter keeps its precision multiplier, border, and Windows global
hotkey settings together under `Filters > Precision Control (TingAlex
Enhanced)`. The hotkey uses `RegisterHotKey`, not a low-level keyboard hook,
and can toggle precision even when the pen is out of range. The activation
anchor can come from the last pen position, the current mouse position, or
whichever pointer moved most recently.

1. Enable the enhanced Precision Control filter.
2. Configure the multiplier, border, and hotkey in the same filter card. The
   default shortcut is `Ctrl+Alt+Shift+P`.
3. In Windows, open **Settings > Bluetooth & devices > Touchpad > Advanced
   gestures** and record the same custom shortcut for **Three-finger tap**.

The toggle works whether the pen is touching, hovering, or out of range.
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
