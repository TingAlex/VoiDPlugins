param(
    [Parameter(Mandatory=$true, ParameterSetName='Install')][string]$PackageDirectory,
    [Parameter(Mandatory=$true, ParameterSetName='Restore')][string]$RestoreBackup,
    [string]$DataDirectory = (Join-Path $env:LOCALAPPDATA 'OpenTabletDriver'),
    [string]$BackupRoot,
    [switch]$Restart,
    [string]$RuntimeDirectory
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
$env:PYTHONIOENCODING = 'utf-8'
$ErrorActionPreference = 'Stop'

# Only plugin binaries/metadata are replaced. Saved settings are evidence, never
# installation input: each computer keeps its own explicit profile values.
$dataRoot = [IO.Path]::GetFullPath($DataDirectory)
$pluginsRoot = Join-Path $dataRoot 'Plugins'
$settingsPath = Join-Path $dataRoot 'settings.json'
if (!(Test-Path -LiteralPath $settingsPath -PathType Leaf)) { throw 'Existing settings.json required.' }
$settingsHash = (Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash

function Assert-PluginPath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    $prefix = [IO.Path]::GetFullPath($pluginsRoot).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    if (!$full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Plugin path is outside the selected data directory.' }
    return $full
}

$original = $null
if ($PSCmdlet.ParameterSetName -eq 'Restore') {
    $restoreRoot = [IO.Path]::GetFullPath($RestoreBackup)
    $original = Get-Content -Encoding UTF8 -Raw -LiteralPath (Join-Path $restoreRoot 'manifest.json') | ConvertFrom-Json
    $targetDirectory = Assert-PluginPath $original.pluginDirectory
    $candidateDll = Join-Path $restoreRoot 'plugin-before/PrecisionControl.dll'
    $candidateMetadata = Join-Path $restoreRoot 'plugin-before/metadata.json'
    if ((Get-FileHash -LiteralPath $candidateDll).Hash -ne $original.beforeDllSha256) { throw 'Backup DLL integrity mismatch.' }
    if ($original.metadataPresent -and (Get-FileHash -LiteralPath $candidateMetadata).Hash -ne $original.beforeMetadataSha256) { throw 'Backup metadata integrity mismatch.' }
} else {
    $packageRoot = [IO.Path]::GetFullPath($PackageDirectory)
    if (Test-Path -LiteralPath (Join-Path $packageRoot 'plugin/PrecisionControl.dll')) { $packageRoot = Join-Path $packageRoot 'plugin' }
    $candidateDll = Join-Path $packageRoot 'PrecisionControl.dll'
    $candidateMetadata = Join-Path $packageRoot 'metadata.json'
    $metadata = Get-Content -Encoding UTF8 -Raw -LiteralPath $candidateMetadata | ConvertFrom-Json
    if ($metadata.Name -ne 'Precision Control (TingAlex Enhanced)') { throw 'Unexpected plugin package.' }
    $binaryVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($candidateDll).ProductVersion.Split('+')[0]
    if ($binaryVersion -ne $metadata.PluginVersion) { throw 'DLL version and package metadata differ.' }
    $installed = @(Get-ChildItem -LiteralPath $pluginsRoot -Filter PrecisionControl.dll -Recurse)
    if ($installed.Count -ne 1) { throw 'Expected exactly one installed PrecisionControl.dll; remove duplicate installations manually.' }
    $targetDirectory = Assert-PluginPath $installed[0].DirectoryName
}
if (!(Test-Path -LiteralPath $targetDirectory -PathType Container)) { throw 'Existing plugin directory required.' }

$oldUx = @()
$oldDaemon = @()
if ($Restart) {
    if ([Diagnostics.Process]::GetCurrentProcess().SessionId -eq 0) { throw 'Restart must run in the logged-in desktop session, not SSH Session 0.' }
    if (!$RuntimeDirectory) { throw 'Restart requires the existing OTD RuntimeDirectory.' }
    $daemonPath = Join-Path ([IO.Path]::GetFullPath($RuntimeDirectory)) 'OpenTabletDriver.Daemon.exe'
    $uxPath = Join-Path ([IO.Path]::GetFullPath($RuntimeDirectory)) 'OpenTabletDriver.UX.Wpf.exe'
    if (!(Test-Path -LiteralPath $daemonPath) -or !(Test-Path -LiteralPath $uxPath)) { throw 'OTD executables not found.' }
    $session = [Diagnostics.Process]::GetCurrentProcess().SessionId
    $oldDaemon = @(Get-Process -Name OpenTabletDriver.Daemon -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $daemonPath -and $_.SessionId -eq $session })
    $oldUx = @(Get-Process -Name OpenTabletDriver.UX.Wpf -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $uxPath -and $_.SessionId -eq $session })
}

if (!$BackupRoot) { $BackupRoot = Join-Path $dataRoot 'Backups/PrecisionControl' }
$backupDirectory = Join-Path ([IO.Path]::GetFullPath($BackupRoot)) ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8))
$beforeDirectory = Join-Path $backupDirectory 'plugin-before'
New-Item -ItemType Directory -Path $beforeDirectory | Out-Null
$targetDll = Join-Path $targetDirectory 'PrecisionControl.dll'
$targetMetadata = Join-Path $targetDirectory 'metadata.json'
Copy-Item -LiteralPath $targetDll -Destination $beforeDirectory
$hadMetadata = Test-Path -LiteralPath $targetMetadata
if ($hadMetadata) { Copy-Item -LiteralPath $targetMetadata -Destination $beforeDirectory }
Copy-Item -LiteralPath $settingsPath -Destination (Join-Path $backupDirectory 'settings.before.json')
$manifest = [ordered]@{
    pluginDirectory=$targetDirectory; metadataPresent=$hadMetadata;
    beforeDllSha256=(Get-FileHash -LiteralPath $targetDll).Hash;
    beforeMetadataSha256=$(if ($hadMetadata) { (Get-FileHash -LiteralPath $targetMetadata).Hash } else { $null });
    settingsSha256=$settingsHash
}
$manifest | ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $backupDirectory 'manifest.json')
$newProcesses = @()
function Start-PreviousProcesses {
    if ($oldDaemon.Count -gt 0) {
        $script:newProcesses += Start-Process -FilePath $daemonPath -WorkingDirectory $RuntimeDirectory -WindowStyle Hidden -PassThru
    }
    if ($oldUx.Count -gt 0) {
        $script:newProcesses += Start-Process -FilePath $uxPath -WorkingDirectory $RuntimeDirectory -WindowStyle Hidden -PassThru
    }
}

try {
    foreach ($process in @($oldUx) + @($oldDaemon)) {
        Stop-Process -Id $process.Id
        if (!$process.WaitForExit(5000)) { throw 'OTD did not exit.' }
    }
    Copy-Item -LiteralPath $candidateDll -Destination $targetDll -Force
    if ($PSCmdlet.ParameterSetName -eq 'Install' -or $original.metadataPresent) {
        Copy-Item -LiteralPath $candidateMetadata -Destination $targetMetadata -Force
    } elseif (Test-Path -LiteralPath $targetMetadata) {
        # Restore the original absence of metadata; this exact single file is
        # checked against the plugin directory above. No directory deletion.
        Remove-Item -LiteralPath $targetMetadata
    }
    if ((Get-FileHash -LiteralPath $targetDll).Hash -ne (Get-FileHash -LiteralPath $candidateDll).Hash) { throw 'Installed DLL hash mismatch.' }
    if ((Get-FileHash -LiteralPath $settingsPath).Hash -ne $settingsHash) { throw 'Settings changed during installation.' }
    if ($Restart) { Start-PreviousProcesses }
    $result = [ordered]@{
        operation=$PSCmdlet.ParameterSetName; pluginDirectory=$targetDirectory;
        version=[Diagnostics.FileVersionInfo]::GetVersionInfo($targetDll).ProductVersion;
        dllSha256=(Get-FileHash -LiteralPath $targetDll).Hash;
        backupDirectory=$backupDirectory; settingsByteIdentical=$true;
        processesRestarted=$newProcesses.Count; physicalInputVerified=$false
    }
    $result | ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $backupDirectory 'verification.json')
    $result | ConvertTo-Json
} catch {
    $failure = $_
    foreach ($process in $newProcesses) { if (!$process.HasExited) { Stop-Process -Id $process.Id; $null = $process.WaitForExit(5000) } }
    Copy-Item -LiteralPath (Join-Path $beforeDirectory 'PrecisionControl.dll') -Destination $targetDll -Force
    if ($hadMetadata) { Copy-Item -LiteralPath (Join-Path $beforeDirectory 'metadata.json') -Destination $targetMetadata -Force }
    elseif (Test-Path -LiteralPath $targetMetadata) { Remove-Item -LiteralPath $targetMetadata }
    if ($Restart) { Start-PreviousProcesses }
    throw $failure
}
