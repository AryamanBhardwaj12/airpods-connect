$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Setup.Core.ps1')
$gate = New-Object Threading.Mutex($false, 'Local\AirPodsConnect-ShareSetup')
$held = $false
try {
    try { $held = $gate.WaitOne(0) } catch [Threading.AbandonedMutexException] { $held = $true }
    if (-not $held) { throw 'Another AirPods setup or uninstall is running.' }
    $expected = Join-Path $PSScriptRoot 'App\AirPodsConnect.exe'
    Assert-NoForeignTray -ExpectedExe $expected -Processes @(Get-Process AirPodsConnect -ErrorAction SilentlyContinue)
    & (Join-Path $PSScriptRoot 'Uninstall.ps1')
    Write-Host 'Startup and Start-menu shortcuts removed. Files and settings are retained in this folder.'
    exit 0
} catch { Write-Host $_.Exception.Message -ForegroundColor Red; exit 1 }
finally { if ($held) { $gate.ReleaseMutex() }; $gate.Dispose() }
