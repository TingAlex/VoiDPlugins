# Precision Control — TingAlex Enhanced

**Move your writing area. Keep your page in place.**

[简体中文](README.zh-CN.md) · [Install](docs/SETUP.md) · [Settings reference](docs/SETTINGS.md) · [Changelog](CHANGELOG.md) · [Build](docs/DEVELOPMENT.md)

An OpenTabletDriver plugin for handwriting on Windows. Map the tablet to a smaller screen area, then move that area between strokes using dials, pen button 3 or keyboard shortcuts. The document stays in place; subsequent pen input uses the new mapping.

The workflow comes from Wacom PTK-670 and OneNote use: write a line, lift, shift the mapping, and return your hand to a preferred position. Use the physical touchpad when you want to navigate the document.

## Version and downloads

Source version **1.6.0** on [agent/global-hotkey-precision-control](https://github.com/TingAlex/VoiDPlugins/tree/agent/global-hotkey-precision-control).

Checked on 2026-10-22: the public Latest asset is still **v1.0.0**, which predates the later dial, pen-button and settings updates. [Build the current source](docs/DEVELOPMENT.md) or use a matching maintainer-supplied package. Do not confuse the source version with an older [release download](https://github.com/TingAlex/VoiDPlugins/releases).

## What each control changes

| Control | Result |
| --- | --- |
| Physical touchpad | Move the application's document |
| Bound horizontal/vertical dials | Move the existing writing mapping, preserving size |
| Hold button 3 and slide in contact, when enabled | Move the mapping; lift to clutch |
| Tap button 3, when enabled | Toggle precision/original output mapping |
| Ctrl + Alt + Shift + P | Toggle precision mapping and its border |
| Ctrl + Alt + Shift + R | Enable or reposition the mapping at the mouse |

Pen-button features are off by default and require button 3 to be unbound in ordinary bindings. Dial bindings are configured separately. Active-stroke or inactive-mapping dial input is discarded, with no later replay.

Hiding the border alone leaves the mapping active. Exiting precision mapping restores the original output range. Mapping scale does not change OneNote zoom.

## Settings

The Chinese settings page has hover explanations and seven numbered groups:

| Group | Purpose |
| --- | --- |
| 01 Writing range | Scale and coordinate mapping method |
| 02 Initial position | Reference pointer and its position inside the frame |
| 03 Dial movement | Independent horizontal/vertical pixel steps |
| 04 Pen buttons | Tap toggle, tap duration, contact mapping drag and gain |
| 05 Border appearance | Visibility, color, opacity and width |
| 06 Keyboard shortcuts | Mapping toggle and mouse reposition |
| 07 Shared modifiers | Ctrl / Alt / Shift / Windows |

[The field reference](docs/SETTINGS.md) includes property IDs, defaults and conditions.

Suggested starting values: Pointer Anchored, scale 0.25, dial steps 40 pixels and opacity 0.4. Screen Relative (Legacy) remains the code default for compatibility; select Pointer Anchored explicitly.

## Retained page-pan experiment

The source includes double-tap contact page panning, stationary touch release and adjustable jitter suppression. The local user reported it usable, then chose to keep document navigation on the touchpad.

In **1.6.0 page navigation is disabled by default and its settings are hidden**. Old experimental JSON values cannot enable it. The implementation and tests remain available to developers; mapping tap/drag controls remain exposed. See [experimental page navigation](docs/EXPERIMENTAL-PAGE-PAN.md).

## Requirements and verification

OpenTabletDriver is required; the local workflow uses 0.6.7, PTK-670 and Windows Ink Absolute Mode. Configure WindowsInk, its VMulti prerequisite and the Pen Tip binding separately. This package installs none of those dependencies.

Enhanced borders and global hotkeys use Windows APIs. Other tablets, apps, mixed DPI and multiple monitors need their own checks. If pen proximity suppresses a physical touchpad, move the pen out of range before touchpad navigation.

The Precision Control suite has **191 tests** for mapping, input isolation, defaults and retained experimental behavior. Test doubles do not establish universal device support. Comfort claims describe the author's experience.

See the [setup guide](docs/SETUP.md) and [release status](docs/RELEASE-PLAN.zh-CN.md). Issues were disabled on this fork when checked on 2026-10-22; feedback should include versions, tablet, output mode, DPI and reproducible steps without private note content.

## Credits

Based on [X9VoiD/VoiDPlugins](https://github.com/X9VoiD/VoiDPlugins) and the [OpenTabletDriver](https://opentabletdriver.net/) plugin ecosystem. The fork retains [other upstream modules](docs/OTHER-PLUGINS.md); the focused ZIP contains Precision Control only.

Distributed under [GNU GPL v3](LICENSE).
