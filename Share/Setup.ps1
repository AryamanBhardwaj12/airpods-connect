param([switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
$committed = $false
$destination = $null
$gate = $null
$held = $false
$logging = $false
try {
    try {
        Start-Transcript -LiteralPath (Join-Path $env:TEMP 'AirPodsConnect-Setup.log') -Append -ErrorAction Stop | Out-Null
        $logging = $true
    } catch { Write-Host 'Setup could not save a log. Any error will remain visible in this window.' }
    . (Join-Path $PSScriptRoot 'Setup.Core.ps1')
    $destination = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'AirPodsConnect'
    $gate = New-Object Threading.Mutex($false, 'Local\AirPodsConnect-ShareSetup')
    try { $held = $gate.WaitOne(0) } catch [Threading.AbandonedMutexException] { $held = $true }
    if (-not $held) { throw 'Another AirPods setup or uninstall is already running.' }
    Write-Host 'AirPods Connect - personal setup' -ForegroundColor Cyan
    if (-not [Environment]::Is64BitProcess -or $env:PROCESSOR_ARCHITECTURE -ne 'AMD64' -or $env:PROCESSOR_ARCHITEW6432 -eq 'ARM64' -or [Environment]::OSVersion.Version.Major -lt 10) {
        throw 'This package is for Intel/AMD 64-bit Windows 10 or 11. It does not support Windows on ARM.'
    }
    $meta = Assert-PackageFiles -Root $PSScriptRoot
    if (-not $meta.BundledRuntime -and -not (Test-DesktopRuntime -DotNetRoot (Join-Path $env:ProgramFiles 'dotnet'))) {
        throw 'Install Microsoft .NET 8 Desktop Runtime (Windows x64) from https://dotnet.microsoft.com/en-us/download/dotnet/8.0 then run SETUP again. Or use the Runtime-Included ZIP instead.'
    }
    $shortcuts = @([Environment]::GetFolderPath('Startup'),[Environment]::GetFolderPath('Programs')) | ForEach-Object { Join-Path $_ 'AirPods Connect.lnk' }
    Assert-FirstInstall -Destination $destination -ShortcutPaths $shortcuts -Processes @(Get-Process AirPodsConnect -ErrorAction SilentlyContinue)
    $selected = Select-PairedAirPods -Candidates @(Get-PairedAirPods)
    if ($CheckOnly) { Write-Host 'Preflight passed. Nothing was installed or connected.'; exit 0 }
    $stage = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) ('AirPodsConnect-setup-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path (Join-Path $stage 'Data\Stage') -Force | Out-Null
    # Only copy manifest files, never the recipient's extracted folder recursively.
    foreach ($entry in $meta.Files) {
        $out = Join-Path $stage $entry.Path
        New-Item -ItemType Directory -Path (Split-Path $out -Parent) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $entry.Path) -Destination $out
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'package.json') -Destination (Join-Path $stage 'package.json')
    # Recheck immediately before the move; never overwrite an existing installation.
    Assert-FirstInstall -Destination $destination -ShortcutPaths $shortcuts -Processes @(Get-Process AirPodsConnect -ErrorAction SilentlyContinue)
    # Directory.Move fails if the destination appeared since the check; Move-Item
    # could instead nest the stage inside a competing directory.
    [IO.Directory]::Move($stage, $destination)
    $committed = $true
    & (Join-Path $destination 'Install.ps1') -SkipBuild -BluetoothInstanceId $selected.InstanceId
    $expected = Join-Path $destination 'App\AirPodsConnect.exe'
    Start-Sleep -Seconds 2
    $running = @(Get-Process AirPodsConnect -ErrorAction SilentlyContinue | Where-Object { $_.Path -and [string]::Equals($_.Path,$expected,[StringComparison]::OrdinalIgnoreCase) })
    if ($running.Count -ne 1) { throw 'Setup copied the app, but its tray process did not stay running. Keep this message and ask for help.' }
    Write-Host ''
    Write-Host 'Ready! Left-click the AirPods icon beside the Windows clock to connect.' -ForegroundColor Green
    Write-Host 'If hidden, click the small up-arrow by the clock and drag the icon out.'
    Write-Host 'It will appear at Windows sign-in. Starting Windows does not auto-connect.'
    Write-Host "Installed at: $destination"
    Write-Host 'You can delete the downloaded ZIP and extracted setup folder now.'
    exit 0
} catch {
    Write-Host ''
    Write-Host $_.Exception.Message -ForegroundColor Red
    if ($committed) {
        $message = 'Setup did not finish. Files and any shortcuts are retained. Run UNINSTALL.cmd in this folder to remove matching shortcuts, then move this AirPodsConnect folder aside before retrying. Nothing from another installation is deleted.'
        try { $message | Set-Content -LiteralPath (Join-Path $destination 'FAILED-SETUP.txt') -Encoding utf8 } catch { }
        Write-Host "$message`nFolder: $destination"
    }
    exit 1
} finally {
    if ($held) { $gate.ReleaseMutex() }
    if ($gate) { $gate.Dispose() }
    if ($logging) { Stop-Transcript | Out-Null }
}
