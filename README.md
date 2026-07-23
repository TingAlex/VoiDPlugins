# VoiDPlugins [![.NET](https://github.com/X9VoiD/VoiDPlugins/actions/workflows/dotnet.yml/badge.svg)](https://github.com/X9VoiD/VoiDPlugins/actions/workflows/dotnet.yml)

VoiDPlugins is a collection of extensions for [OpenTabletDriver](https://github.com/InfinityGhost/OpenTabletDriver).

# Plugins

## Experimental

### [MeL](https://github.com/X9VoiD/VoiDPlugins/wiki/MeL)

      Machine Learning Plugin

### [Reconstructor](https://github.com/X9VoiD/VoiDPlugins/wiki/Reconstructor)

      Mathematically perfect "anti-hardware-smoothing"

## Filters

### [PrecisionControl](https://github.com/X9VoiD/VoiDPlugins/wiki/PrecisionControl)

      Dynamic sensitivity switching

#### Windows global hotkey

The PrecisionControl assembly also provides a `Precision Control Global Hotkey`
tool on Windows. It uses `RegisterHotKey`, not a low-level keyboard hook, and
only queues a precision toggle while a pen is in range on a tablet that has the
Precision Control filter enabled.

1. Enable the `Precision Control` filter and choose a precision multiplier.
2. Enable the `Precision Control Global Hotkey` tool.
3. Configure its key and modifiers. The default is `Ctrl+Alt+Shift+P`.
4. In Windows, open **Settings > Bluetooth & devices > Touchpad > Advanced
   gestures** and record the same custom shortcut for **Three-finger tap**.

The global hotkey is a toggle. A hotkey received while the pen is out of range
is ignored. If multiple tablets have an in-range pen, the most recently active
tablet is selected.

When precision mode is active on Windows, the filter displays a thin,
semi-transparent ice-blue border around the effective precision area. The
border is always on top, does not take focus, and lets mouse and pen input pass
through. Its visibility, thickness, and opacity can be configured on the
Precision Control filter.

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
