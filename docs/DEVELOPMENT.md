# Build, test and package Precision Control

Current source version: **1.6.0**. The plugin targets net6.0 for the retained OpenTabletDriver SDK. Developer SDK/runtime requirements are separate from those of your installed OTD.

## Build and tests

Use Git with submodules and a .NET SDK/runtime capable of building and running net6.0:

~~~powershell
git clone --recurse-submodules --branch agent/global-hotkey-precision-control https://github.com/TingAlex/VoiDPlugins.git
cd VoiDPlugins
dotnet build src/Filter/PrecisionControl/PrecisionControl.csproj -c Release
dotnet test tests/PrecisionControl.Tests/PrecisionControl.Tests.csproj -c Release
~~~

For an existing checkout, update your branch normally, then run:

~~~powershell
git submodule update --init --recursive
~~~

The DLL is produced at src/Filter/PrecisionControl/bin/Release/net6.0/PrecisionControl.dll. Follow [SETUP.md](SETUP.md) with OTD stopped.

The **191-test** Precision Control suite covers mapping, dial movement, hotkeys, report isolation, tap/contact controls, UI metadata/defaults and the retained page-pan experiment. Experiment fixtures explicitly opt in; normal filters cannot inject page input. Run the separate WindowsInk suite when changing that module:

~~~powershell
dotnet test tests/WindowsInk.Tests/WindowsInk.Tests.csproj -c Release
~~~

Physical pen input, real border appearance, fresh-machine installation and application compatibility still require manual checks.

## Code boundaries

| Area | Responsibility |
| --- | --- |
| PrecisionControl.cs | Existing mapping state, actions, coordinate transform and pipeline |
| PrecisionControlSettings.cs | All 23 visible settings declared in seven-group display order |
| PrecisionControlPenDrag.cs | Third-button contact mapping drag and pressure isolation |
| PrecisionControlPenNavigation.cs | Normal short-tap mapping control; experimental legacy hover route behind gate |
| PrecisionControlContactNavigation.cs | Retained double-tap contact page mode and stationary release |
| PenDirectPan.cs / NativePenPanPlatform.cs | Experimental synthetic touch lifecycle and target surface routing |
| PenPageJitterGuard.cs | Experimental spatial jitter suppression, no temporal catch-up |
| PrecisionControlOverlay.cs | Click-through mapping border |
| SettingsSurfaceTests.cs | Public settings, closed experiment gate and stable controls |

Preserve the existing Scale/aspect mapping, physical-pressure guards and discard-on-invalid-context behavior. Page navigation and mapping movement are separate actions.

## Public versus retained experimental settings

OTD's property metadata controls both generated UI and configuration loading. Removing a Property attribute hides the field and prevents old JSON from setting it.

The internal EnablePenPageNavigation gate defaults to false and is not an OTD property. Page fields have no Property attributes. To exercise the retained implementation, tests or a deliberate development build must set that gate in code; JSON alone does not enable it. See [EXPERIMENTAL-PAGE-PAN.md](EXPERIMENTAL-PAGE-PAN.md).

Public property IDs remain unchanged when labels/groups change. Keep the seven-group reference and tooltips synchronized with [SETTINGS.md](SETTINGS.md). Slider attribute defaults and DefaultPropertyValue must agree.

## Distribution package

~~~powershell
./scripts/Package-PrecisionControl.ps1
~~~

Optional: -OutputDirectory chooses another output parent. The script builds/tests and creates a uniquely named directory under build/releases containing:

- A plugin ZIP with DLL, metadata, docs and license.
- A matching source ZIP with repository sources and the expanded OTD submodule.
- BUILD-INFO.json recording source commit, dirty tracked state and submodule commit.
- SHA256SUMS.txt for the two ZIP files.

The script does not install or publish. It includes new src/tests development files before commit; local untracked probes, logs and device profiles are excluded. Build release packages from committed source for clear provenance.

GitHub's automatic source archive does not expand submodules, so keep the explicit source ZIP with any binary distribution. OTD, WindowsInk, VMulti and their runtimes are not bundled.

## Contribution and release checks

Keep changes focused on the Precision Control workflow. Before pushing, run the relevant tests, validate local Markdown links and review staged files for local settings/logs/probes. The normal package must keep page navigation off and hidden. CI builds retained upstream modules and runs the Precision Control/WindowsInk suites.

A source push, a PR merge and a downloadable GitHub Release are different publication steps. Track their actual status in [RELEASE-PLAN.zh-CN.md](RELEASE-PLAN.zh-CN.md); never label an untested device combination as verified.
