# Precision Touchpad / Pen Probe

This diagnostic utility checks whether Windows still delivers Precision
Touchpad events while a pen is hovering or moving over a tablet.

It has two modes:

- By default, it only observes pointer messages already delivered to its own
  window, including raw touchpad frames and any wheel, gesture, app-command, or
  keyboard messages Windows translated from input. It does not inject,
  redirect, suppress, or synthesize input.
- The optional checkbox enables `TouchpadGesturesController` so that three-,
  four-, and five-finger gestures can be diagnosed. Windows only exposes those
  events to a controller in the foreground process, so the original system
  action is temporarily paused while the box is checked and this window is in
  the foreground. Closing the probe restores normal handling.

Build and run:

```powershell
dotnet run --project .\tools\TouchpadPenProbe\TouchpadPenProbe.csproj
```

Test sequence:

1. Keep the probe in the foreground and move the pen so its cursor remains
   inside the probe window.
2. While the pen is hovering, perform a two-finger scroll or pinch.
3. Check the temporary global-gesture option, then perform three- and
   four-finger taps while continuing to hover.
4. Copy the diagnostic summary and compare which events arrived with the pen
   out of range versus hovering.
