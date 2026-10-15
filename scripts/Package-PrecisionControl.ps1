param([string]$OutputDirectory)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
$env:PYTHONIOENCODING = 'utf-8'
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repo 'build/releases' }
$project = Join-Path $repo 'src/Filter/PrecisionControl/PrecisionControl.csproj'
$version = ([xml](Get-Content -Raw -Encoding UTF8 $project)).Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a numeric plugin version.' }
$run = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) ("PrecisionControl-$version-" + [guid]::NewGuid().ToString('N').Substring(0,8))
$package = Join-Path $run 'package'
$source = Join-Path $run 'source'
New-Item -ItemType Directory -Path (Join-Path $package 'plugin'),$source | Out-Null

Push-Location $repo
try {
    dotnet build $project -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet test tests/PrecisionControl.Tests/PrecisionControl.Tests.csproj -c Release --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

    $commit = git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve source commit.' }
    git diff --quiet HEAD --
    $dirty = $LASTEXITCODE -ne 0
    $submodule = Join-Path $repo '.modules/OpenTabletDriver'
    $submoduleCommit = git -C $submodule rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Initialize the OpenTabletDriver submodule.' }
    $info = [ordered]@{
        PluginVersion = $version
        SourceCommit = $commit
        TrackedSourceModified = $dirty
        OpenTabletDriverSubmoduleCommit = $submoduleCommit
        BuiltAtUtc = [DateTime]::UtcNow.ToString('o')
        SourceRepository = 'https://github.com/TingAlex/VoiDPlugins'
        PackageType = 'Manual installation candidate; dependencies not bundled'
    }
    $info | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $package 'BUILD-INFO.json')

    Copy-Item -LiteralPath (Join-Path $repo 'src/Filter/PrecisionControl/bin/Release/net6.0/PrecisionControl.dll') -Destination (Join-Path $package 'plugin')
    [ordered]@{
        Name = 'Precision Control (TingAlex Enhanced)'
        Owner = 'TingAlex'
        Description = 'Movable handwriting area with independent horizontal and vertical tablet dials'
        PluginVersion = $version
        SupportedDriverVersion = '0.6.7.0'
        RepositoryUrl = 'https://github.com/TingAlex/VoiDPlugins'
        DownloadUrl = ''
        CompressionFormat = 'zip'
        SHA256 = ''
        WikiUrl = 'https://github.com/TingAlex/VoiDPlugins/blob/agent/global-hotkey-precision-control/docs/SETUP.md'
        LicenseIdentifier = 'GPL-3.0-only'
    } | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $package 'plugin/metadata.json')
    foreach ($name in @('README.md','README.zh-CN.md','LICENSE','CHANGELOG.md','docs')) {
        Copy-Item -LiteralPath (Join-Path $repo $name) -Destination $package -Recurse
    }

    # Include tracked source, not local probes, logs, settings, or build output.
    $files = @(git -c core.quotepath=false ls-files)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate source files.' }
    # Include newly prepared public docs/script even before the first commit.
    $files += @('README.zh-CN.md')
    $files += @(Get-ChildItem (Join-Path $repo 'docs'),$PSScriptRoot -File -Recurse |
        ForEach-Object { $_.FullName.Substring($repo.Length + 1) })
    foreach ($relative in ($files | Sort-Object -Unique)) {
        $from = Join-Path $repo $relative
        if (Test-Path -LiteralPath $from -PathType Leaf) {
            $to = Join-Path $source $relative
            New-Item -ItemType Directory -Path (Split-Path $to -Parent) -Force | Out-Null
            Copy-Item -LiteralPath $from -Destination $to
        }
    }
    $moduleFiles = @(git -C $submodule -c core.quotepath=false ls-files)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate submodule sources.' }
    foreach ($relative in $moduleFiles) {
        $from = Join-Path $submodule $relative
        if (Test-Path -LiteralPath $from -PathType Leaf) {
            $to = Join-Path $source ('.modules/OpenTabletDriver/' + $relative)
            New-Item -ItemType Directory -Path (Split-Path $to -Parent) -Force | Out-Null
            Copy-Item -LiteralPath $from -Destination $to
        }
    }
    Copy-Item -LiteralPath (Join-Path $package 'BUILD-INFO.json') -Destination $source

    # .NET ZIP includes dotfiles (including the expanded .modules directory).
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $binaryZip = Join-Path $run "PrecisionControl-$version-windows.zip"
    $sourceZip = Join-Path $run "PrecisionControl-$version-source.zip"
    [IO.Compression.ZipFile]::CreateFromDirectory($package,$binaryZip)
    [IO.Compression.ZipFile]::CreateFromDirectory($source,$sourceZip)
    @($binaryZip,$sourceZip) | ForEach-Object {
        (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + (Split-Path $_ -Leaf)
    } | Set-Content -Encoding ASCII (Join-Path $run 'SHA256SUMS.txt')
    Write-Output "Distribution candidate: $run"
}
finally { Pop-Location }
