# Build and package Precision Control

Requirements: Git, an SDK able to build net6.0, and the .NET 6 runtime for
the current test target. These developer dependencies are separate from the
runtime required by your installed OpenTabletDriver version.

```powershell
git clone --recurse-submodules --branch agent/global-hotkey-precision-control https://github.com/TingAlex/VoiDPlugins.git
cd VoiDPlugins
dotnet build src/Filter/PrecisionControl/PrecisionControl.csproj -c Release
dotnet test tests/PrecisionControl.Tests/PrecisionControl.Tests.csproj -c Release
```

Existing checkout: switch to that branch, use `git pull --ff-only`, then
`git submodule update --init --recursive`.

The plugin is built at
`src/Filter/PrecisionControl/bin/Release/net6.0/PrecisionControl.dll`.
Follow [SETUP.md](SETUP.md) to install it with OTD stopped.

## Create a distribution candidate

From PowerShell:

```powershell
./scripts/Package-PrecisionControl.ps1
```

The script builds and tests the plugin, then creates a uniquely named directory
under `build/releases` containing a plugin ZIP, an accompanying source ZIP,
and SHA-256 checksums. Use `-OutputDirectory <path>` to choose another parent.
It neither installs the plugin nor publishes a GitHub release.

The plugin ZIP includes only the Precision Control DLL, metadata, docs, and
license. OTD, WindowsInk, VMulti, and their runtime dependencies are excluded.
The source ZIP includes this repository's tracked files (current working
contents) and tracked files from the OTD submodule, plus this packaging script.
Build info records the commit and whether tracked files differ from it.
Release from a clean tracked tree for unambiguous source provenance.

The automatic source archive links in GitHub Releases do not expand Git
submodules. Keep the explicit source ZIP alongside any published plugin ZIP.

## Validation boundary

The current Precision Control suite contains 57 tests covering mapping,
hotkeys, overlays via test doubles, and dial handling. Physical device input,
driver installation, real overlay appearance, and app behavior still need
manual checks. See the [release plan](RELEASE-PLAN.zh-CN.md).
