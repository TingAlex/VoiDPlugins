# Developer probes / 开发诊断工具

These are diagnostics kept for future development. None of them is part of the
plugin package or loaded by OpenTabletDriver; the plugin ZIP only contains
`PrecisionControl.dll` and `metadata.json`.

| Tool | Target | What it checks |
| --- | --- | --- |
| `TouchpadFrameProbe` | net6.0 console | Reuses the plugin's own Raw Input listener and prints precision-touchpad frames: contact count, contact IDs, X/Y, logical ranges and the centroid. Use this first when three-finger drag does nothing. |
| `TouchpadPenProbe` | net9.0-windows WinForms | Whether touchpad pointer/gesture events still arrive while the pen hovers, and whether a foreground app can capture three/four/five-finger global gestures. The optional checkbox pauses system gesture handling while enabled. |
| `BleHidAccessProbe` | net9.0-windows | Checks BLE HID access for the Magic Trackpad haptics experiments (see the project notes, not part of Precision Control). |

## Build and run

```powershell
dotnet build tools/TouchpadFrameProbe -c Release
dotnet run --project tools/TouchpadFrameProbe
dotnet run --project tools/TouchpadFrameProbe -- --seconds 15

dotnet run --project tools/TouchpadPenProbe
dotnet run --project tools/BleHidAccessProbe
```

`TouchpadFrameProbe` reuses `TouchpadRawInputListener` from the plugin (via
`InternalsVisibleTo`), so it fails or succeeds exactly like the installed plugin.
Each frame prints:

```text
[      1234 ms] count=3 range=4096x4096 | #1=(1200,800), #2=(1500,900), #3=(1800,1000) | centroid=(1500.0,900.0)
```

## Reading the output

| Observation | Meaning |
| --- | --- |
| `count=3` with changing coordinates | The input layer is healthy; a missing gesture is a plugin-side state or settings issue. |
| `count=0` when fingers lift | Expected; this frame ends the drag segment. |
| Registration failed (Win32 87/5) | Not an interactive desktop session, or the machine has no precision touchpad. |
| No frames while touching | Pen may be hovering/blocking the touchpad, the device is not a precision touchpad, or another session owns the input. |

## Requirements

- .NET 6 SDK for `TouchpadFrameProbe` (same target as the plugin); .NET 9 SDK and
  Windows SDK 10.0.26100 for the other two.
- An interactive desktop session. Raw Input registration and window creation fail
  in service/non-interactive sessions, which is an environment limit, not a bug.

## Notes

- These tools never modify the tablet, the plugin settings or the OTD profile.
- Do not record private screen content or note text when sharing probe output.
