# Precision Control — TingAlex Enhanced

**Move your writing area. Keep your page in place.**

[简体中文](README.zh-CN.md) · [Setup guide](docs/SETUP.md) · [Changelog](CHANGELOG.md) · [Build from source](docs/DEVELOPMENT.md)

An **OpenTabletDriver plugin for handwriting on Windows**. Map your pen tablet
to a smaller area on the screen, then move that area with two tablet dials.
Use your touchpad to navigate the document when you want to move the page.

Developed and used in **OneNote with a Wacom PTK-670**, this workflow is useful
for handwritten notes, document annotations, and writing beside references
you want to keep in view.

## Why move the writing area?

A small mapping gives you finer control on a large display. But as you write
across or down the page, a fixed mapping can leave your hand working near
the edge of the tablet. Precision Control lets you shift the mapping between
strokes and return your hand to a preferred writing band.

- **Keep the document still.** Move the frame and pen mapping while existing
  text, diagrams, and references stay in place.
- **Reuse a comfortable writing position.** Shift the area down after a line,
  return your hand to the same band on the tablet, and continue.
- **Place the frame predictably.** In `Pointer Anchored` mode, specify where
  the activation point sits inside the frame.
- **Keep the surrounding page visible.** A thin, translucent, click-through
  border marks the area without dimming the rest of the screen.
- **Tune each dial independently.** Small steps for line-by-line writing;
  larger steps for moving to another part of the page.

Comfort and reduced interruption describe the author's experience, not
measured claims about fatigue, writing speed, or suitability for every user.

## One page, two kinds of movement

| What you want to move | What you use | What changes |
| --- | --- | --- |
| The document | Normal touchpad gestures | The application's page position |
| The writing area | Tablet dial 1 / dial 2 | The frame and mapping move horizontally / vertically |
| The area to a new starting point | Reposition shortcut | The area appears at the current mouse location |

Write a line, lift the tip, turn the vertical dial, return your hand to a
comfortable band, and continue. Use the touchpad when you want to navigate
elsewhere in the document.

The plugin does not implement touchpad scrolling or change OneNote's
scrolling. Some Windows devices suppress touchpad input while the pen is in
range; you may need to lift it out of sensing range to use touchpad gestures.

## Version and requirements

Source version: **1.1.2** on
[`agent/global-hotkey-precision-control`](https://github.com/TingAlex/VoiDPlugins/tree/agent/global-hotkey-precision-control).
As checked on 2026-10-15, the public [latest release](https://github.com/TingAlex/VoiDPlugins/releases)
is **1.0.0 and does not include dual-dial movement**. Build from source until
a matching release asset is published.

| Component | Status |
| --- | --- |
| Windows | Enhanced border and shortcuts use Windows APIs |
| OpenTabletDriver | Required; tested with 0.6.7 |
| Tablet | Author-tested: Wacom PTK-670; other models need verification |
| Two independent dials | OTD must expose both for this two-axis workflow |
| OneNote / Windows Ink | Author uses Windows Ink Absolute Mode; configure its prerequisites separately |
| Touchpad | Optional; useful for document navigation |
| Other apps, DPI settings, multiple monitors | Further compatibility testing needed |

This is an add-on, not a standalone driver or an official Wacom/Microsoft
product. Start with the [installation and configuration guide](docs/SETUP.md).

## Controls

| Control | Default action |
| --- | --- |
| Ctrl + Alt + Shift + P | Show / hide precision mapping |
| Ctrl + Alt + Shift + R | Show or reposition at the current mouse position |
| Dial 1 clockwise / counterclockwise | Right / left, after binding |
| Dial 2 clockwise / counterclockwise | Down / up, after binding |
| Horizontal / vertical dial step | 40 pixels per tick; independently adjustable from 1 to 1000 |

Dial movement starts from the current area and preserves its size. Inactive
mode and active strokes discard dial ticks without replaying them later.
Movement works with the pen out of range. The mapping updates on the next pen
report without synthesizing an immediate cursor move.

The settings UI uses Chinese labels and hover help; the English setup guide
provides a field reference. Keyboard nudge settings are hidden in 1.1.2, but
their built-in shortcuts remain active. Read the upgrade notes in the guide.

## Feedback and project status

Next priorities: a real-device demo, a versioned plugin download, a fresh-PC
installation test, and more device reports. See the
[release-readiness plan](docs/RELEASE-PLAN.zh-CN.md).

As checked on 2026-10-15, Issues are disabled on this fork. Enabling a feedback
channel is a release task. Reports should include OTD/plugin/Windows versions,
tablet model, output mode, app, monitor scaling, and reproduction steps.

## Credits and license

Based on Precision Control in [X9VoiD/VoiDPlugins](https://github.com/X9VoiD/VoiDPlugins)
and the [OpenTabletDriver](https://opentabletdriver.net/) plugin ecosystem.
This fork retains the upstream modules; this handwriting workflow focuses on
Precision Control. See the [other modules](docs/OTHER-PLUGINS.md).

Distributed under the repository's [GNU GPL v3 license](LICENSE).
